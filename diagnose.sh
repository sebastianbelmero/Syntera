#!/usr/bin/env bash
#
# Syntera — Comprehensive Diagnostics
#
# Checks every layer of the stack and pinpoints failures.
# Auto-detects API port (5000 or 5113) and handles SQL Server
# case-sensitivity on uniqueidentifier columns.
#
set -uo pipefail

# ─── Configuration ──────────────────────────────────────────────────
CONTAINER="${CONTAINER:-sql-server}"
SA_PASSWORD="${SA_PASSWORD:-Passwordkuat123!}"
API_DIR="${API_DIR:-$(cd "$(dirname "$0")/Syntera.Api" && pwd)}"
REACT_DIR="${REACT_DIR:-$(cd "$(dirname "$0")/Syntera.React" && pwd)}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

PASS=0; FAIL=0; WARN=0
check_pass() { echo -e "  ${GREEN}✓${NC} $1"; ((PASS++)); }
check_fail() { echo -e "  ${RED}✗${NC} $1"; ((FAIL++)); }
check_warn() { echo -e "  ${YELLOW}⚠${NC} $1"; ((WARN++)); }
section()    { echo -e "\n${BLUE}━━━ $1 ━━━${NC}"; }

# ─── SQL helpers (strip "(X rows affected)" lines) ──────────────────
sql() {
  podman exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -d master -Q "$1" -h -1 -W 2>&1 \
    | grep -vE "^\([0-9]+ rows? affected\)$" | grep -v "^$"
}
sql_db() {
  local db="$1"; shift
  podman exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -d "$db" -Q "$1" -h -1 -W 2>&1 \
    | grep -vE "^\([0-9]+ rows? affected\)$" | grep -v "^$"
}
sql_scalar()      { sql "$1" 2>&1 | head -1 | tr -d '[:space:]'; }
sql_scalar_db()   { local db="$1"; shift; sql_db "$db" "$1" 2>&1 | head -1 | tr -d '[:space:]'; }

# ─── Detect API port ────────────────────────────────────────────────
detect_api_port() {
  for port in 5000 5113 5001 7000; do
    if curl -sf "http://localhost:$port/health" -o /dev/null 2>&1; then
      echo "$port"
      return 0
    fi
  done
  return 1
}

API_PORT=$(detect_api_port || echo "")
if [ -n "$API_PORT" ]; then
  API_URL="http://localhost:$API_PORT"
else
  API_URL="${API_URL:-http://localhost:5000}"
fi

echo "════════════════════════════════════════════════════════════════"
echo "  Syntera Diagnostics"
echo "  Container:  $CONTAINER"
echo "  SA Pass:    ${SA_PASSWORD:0:3}***"
echo "  API URL:    $API_URL (auto-detected)"
echo "  API Dir:    $API_DIR"
echo "  React Dir:  $REACT_DIR"
echo "════════════════════════════════════════════════════════════════"

# ─── 1. Podman ──────────────────────────────────────────────────────
section "1. Podman Container"

if command -v podman >/dev/null 2>&1; then
  check_pass "podman: $(podman --version)"
else
  check_fail "podman not installed"
  exit 1
fi

if podman ps --format '{{.Names}}' | grep -q "^${CONTAINER}$"; then
  check_pass "Container '$CONTAINER' is running"
else
  check_fail "Container '$CONTAINER' is NOT running"
  echo "      podman run -d --name $CONTAINER -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=$SA_PASSWORD -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest"
  exit 1
fi

# ─── 2. SQL Server ──────────────────────────────────────────────────
section "2. SQL Server"

SQL_TEST=$(sql_scalar "SELECT 1")
if [ "$SQL_TEST" = "1" ]; then
  check_pass "SA login works"
else
  check_fail "SA login failed: $SQL_TEST"
  exit 1
fi

COLLATION=$(sql_scalar "SELECT SERVERPROPERTY('Collation')")
check_pass "Collation: $COLLATION"
echo "$COLLATION" | grep -qi "CS_" && check_warn "Case-sensitive collation — GUID comparisons may break"

SQL_VERSION=$(sql "SELECT @@VERSION" | head -1)
echo "      $(echo "$SQL_VERSION" | cut -c1-70)..."

# ─── 3. Databases ───────────────────────────────────────────────────
section "3. Databases (7 expected)"

EXPECTED_DBS=("syntera_master" "syntera_kalventis" "syntera_kalbe" "syntera_fima" "syntera_gof" "syntera_dankos" "syntera_hexpharm")
for db in "${EXPECTED_DBS[@]}"; do
  EXISTS=$(sql_scalar "SELECT COUNT(*) FROM sys.databases WHERE name = '$db'")
  [ "$EXISTS" = "1" ] && check_pass "$db" || check_fail "$db MISSING"
done

# ─── 4. Platform DB Schema ──────────────────────────────────────────
section "4. Platform DB Tables (10 expected)"

PLATFORM_TABLES=$(sql_db syntera_master "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME")
EXPECTED_TABLES=("AuditLogs" "PlatformSettings" "PlatformUsers" "RefreshTokens" "RoleTemplatePermissions" "RoleTemplates" "SiteLdapConfigs" "SiteLdapDomains" "SiteThemes" "Sites")
for t in "${EXPECTED_TABLES[@]}"; do
  echo "$PLATFORM_TABLES" | grep -qx "$t" && check_pass "$t" || check_fail "$t MISSING"
done

# ─── 4b. SiteLdapConfigs Schema ─────────────────────────────────────
section "4b. SiteLdapConfigs Schema (simplified)"

LDAP_COLS=$(sql_db syntera_master "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SiteLdapConfigs' ORDER BY ORDINAL_POSITION")
echo "      Columns: $(echo "$LDAP_COLS" | tr '\n' ' ')"

EXPECTED_LDAP_COLS=("Id" "SiteId" "Host" "Port" "UseStartTls" "BaseDn" "CreatedAt" "UpdatedAt")
for c in "${EXPECTED_LDAP_COLS[@]}"; do
  echo "$LDAP_COLS" | grep -qx "$c" && check_pass "$c" || check_fail "$c MISSING"
done

STALE_COLS=("EmailAttribute" "BindDn" "BindPasswordEncrypted" "UserFilterTemplate" "TimeoutSeconds" "SearchSubtree")
for c in "${STALE_COLS[@]}"; do
  echo "$LDAP_COLS" | grep -qx "$c" && check_warn "Stale column '$c' (should be dropped)"
done

# ─── 5. Seed Data ───────────────────────────────────────────────────
section "5. Seed Data"

ADMIN_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM PlatformUsers WHERE Email='admin@syntera.com'")
if [ "$ADMIN_COUNT" -ge 1 ] 2>/dev/null; then
  check_pass "Platform Admin exists"
  ADMIN_ENABLED=$(sql_scalar_db syntera_master "SELECT IsEnabled FROM PlatformUsers WHERE Email='admin@syntera.com'")
  [ "$ADMIN_ENABLED" = "1" ] && check_pass "Admin enabled" || check_fail "Admin DISABLED"
else
  check_fail "Platform Admin MISSING — run: cd Syntera.DbSetup && dotnet run"
fi

SITE_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM Sites")
[ "$SITE_COUNT" = "6" ] && check_pass "6 sites" || check_fail "Expected 6 sites, found '$SITE_COUNT'"

echo "      Sites:"
sql_db syntera_master "SELECT Code + ' | ' + DisplayName FROM Sites ORDER BY Code" | sed 's/^/        /'

RT_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM RoleTemplates")
[ "$RT_COUNT" -ge 2 ] 2>/dev/null && check_pass "$RT_COUNT role templates" || check_fail "Expected ≥2 templates, found '$RT_COUNT'"

echo "      Templates:"
sql_db syntera_master "SELECT [Key] + ' (v' + CAST(Version AS VARCHAR) + ', pub=' + CAST(IsPublished AS VARCHAR) + ')' FROM RoleTemplates ORDER BY [Key]" | sed 's/^/        /'

# ─── 6. Site DBs ────────────────────────────────────────────────────
section "6. Site DBs Schema + Roles"

for db in "syntera_kalventis" "syntera_kalbe" "syntera_fima" "syntera_gof" "syntera_dankos" "syntera_hexpharm"; do
  DB_EXISTS=$(sql_scalar "SELECT COUNT(*) FROM sys.databases WHERE name = '$db'")
  if [ "$DB_EXISTS" != "1" ]; then
    check_fail "$db: MISSING"
    continue
  fi

  SITE_TABLES=$(sql_db "$db" "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME")
  TABLE_COUNT=$(echo "$SITE_TABLES" | grep -c .)
  [ "$TABLE_COUNT" -ge 9 ] && check_pass "$db: $TABLE_COUNT tables" || check_fail "$db: only $TABLE_COUNT tables"

  SBA_COUNT=$(sql_scalar_db "$db" "SELECT COUNT(*) FROM Roles WHERE [Key]='site-business-admin'")
  if [ "$SBA_COUNT" -ge 1 ] 2>/dev/null; then
    check_pass "  $db: site-business-admin role cloned"
  else
    check_warn "  $db: site-business-admin NOT cloned — publish template via UI"
  fi
done

# ─── 7. API ────────────────────────────────────────────────────────
section "7. API Health"

if [ -z "$API_PORT" ]; then
  check_fail "API not reachable (tried ports 5000, 5113, 5001, 7000)"
  echo "      Start with: ./dev.sh backend"
else
  check_pass "API reachable at $API_URL (port $API_PORT)"
  HEALTH=$(curl -s "${API_URL}/health" 2>&1)
  echo "      Health: $HEALTH"
fi

# ─── 8. Frontend ────────────────────────────────────────────────────
section "8. Frontend (Vite)"

if curl -sf "http://localhost:5173" -o /dev/null 2>&1; then
  check_pass "Frontend reachable at http://localhost:5173"
else
  check_warn "Frontend not running on 5173"
  echo "      Start with: ./dev.sh frontend"
fi

# Check vite.config.ts proxy target
if [ -f "$REACT_DIR/vite.config.ts" ]; then
  PROXY_TARGET=$(grep -oE "target:\s*'[^']+'" "$REACT_DIR/vite.config.ts" | head -1 | cut -d"'" -f2)
  echo "      Vite proxy target: $PROXY_TARGET"
  if [ -n "$API_PORT" ]; then
    if echo "$PROXY_TARGET" | grep -q ":$API_PORT"; then
      check_pass "Vite proxy target matches API port ($API_PORT)"
    else
      check_fail "Vite proxy target ($PROXY_TARGET) != API port ($API_PORT)"
      echo "      Fix: edit Syntera.React/vite.config.ts → target: 'http://localhost:$API_PORT'"
    fi
  fi
fi

# ─── 9. Login Test ──────────────────────────────────────────────────
section "9. Login Test"

if [ -z "$API_PORT" ]; then
  check_warn "Skipping — API not running"
else
  LOGIN_RESP=$(curl -s -w "\n%{http_code}" -X POST "${API_URL}/api/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"email":"admin@syntera.com","password":"ChangeMe!Strong#1"}')
  HTTP_CODE=$(echo "$LOGIN_RESP" | tail -1)
  BODY=$(echo "$LOGIN_RESP" | head -n -1)

  if [ "$HTTP_CODE" = "200" ] && echo "$BODY" | grep -q "accessToken"; then
    check_pass "Login OK (admin@syntera.com)"
    TOKEN=$(echo "$BODY" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)
    check_pass "JWT token: ${#TOKEN} chars"
  else
    check_fail "Login failed (HTTP $HTTP_CODE)"
    echo "      Response: $(echo "$BODY" | head -c 200)"
  fi
fi

# ─── 10. Role Templates CRUD ────────────────────────────────────────
section "10. Role Templates CRUD"

if [ -z "${TOKEN:-}" ]; then
  check_warn "Skipping — no auth token"
else
  # GET
  RT_RESP=$(curl -s -w "\n%{http_code}" "${API_URL}/api/platform/role-templates" -H "Authorization: Bearer $TOKEN")
  HTTP_CODE=$(echo "$RT_RESP" | tail -1)
  BODY=$(echo "$RT_RESP" | head -n -1)

  if [ "$HTTP_CODE" = "200" ]; then
    RT_COUNT_API=$(echo "$BODY" | grep -o '"id"' | wc -l)
    check_pass "GET /role-templates: $RT_COUNT_API templates"
  else
    check_fail "GET /role-templates: HTTP $HTTP_CODE"
  fi

  # PUT
  FIRST_RT_ID=$(echo "$BODY" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
  if [ -n "$FIRST_RT_ID" ]; then
    PUT_RESP=$(curl -s -w "\n%{http_code}" -X PUT "${API_URL}/api/platform/role-templates/${FIRST_RT_ID}" \
      -H "Authorization: Bearer $TOKEN" \
      -H "Content-Type: application/json" \
      -d '{"key":"site-business-admin","displayName":"Site Business Admin","description":"Manages users, roles, and permissions within own site.","isSiteAdminRole":true,"permissionKeys":["user.read","user.write","user.disable","user.sync","role.read","user_role.assign","user_role.revoke","permission.read","permission.grant","permission.revoke","audit.read","report.read"]}')
    HTTP_CODE=$(echo "$PUT_RESP" | tail -1)
    BODY=$(echo "$PUT_RESP" | head -n -1)

    [ "$HTTP_CODE" = "200" ] && check_pass "PUT /role-templates/{id}: OK" || {
      check_fail "PUT /role-templates/{id}: HTTP $HTTP_CODE"
      echo "      Body: $(echo "$BODY" | head -c 300)"
    }
  fi
fi

# ─── 11. LDAP Configs ──────────────────────────────────────────────
section "11. LDAP Configs per Site"

LDAP_CONFIGS=$(sql_db syntera_master "SELECT s.Code + ': ' + COALESCE(c.Host + ':' + CAST(c.Port AS VARCHAR), 'NOT CONFIGURED') FROM Sites s LEFT JOIN SiteLdapConfigs c ON c.SiteId = s.Id ORDER BY s.Code")
echo "$LDAP_CONFIGS" | sed 's/^/        /'

LDAP_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM SiteLdapConfigs WHERE Host IS NOT NULL AND Host <> ''")
[ "$LDAP_COUNT" -ge 1 ] && check_pass "$LDAP_COUNT site(s) have LDAP configured" || check_warn "No LDAP configs yet — configure via UI"

# ─── 12. Recent API Errors ─────────────────────────────────────────
section "12. Recent API Errors (last 5)"

LATEST_LOG=$(ls -t "$API_DIR"/logs/syntera-api-*.log 2>/dev/null | head -1)
if [ -n "$LATEST_LOG" ]; then
  ERRORS=$(tail -200 "$LATEST_LOG" | grep -iE "\[ERR\]|\[FTL\]|exception" | tail -5)
  if [ -n "$ERRORS" ]; then
    echo "$ERRORS" | sed 's/^/        /'
    check_warn "Found recent errors — see above"
  else
    check_pass "No errors in recent logs"
  fi
else
  check_warn "No log files in $API_DIR/logs/"
fi

# ─── Summary ────────────────────────────────────────────────────────
echo ""
echo "════════════════════════════════════════════════════════════════"
echo -e "  ${GREEN}Passed: $PASS${NC}  ${RED}Failed: $FAIL${NC}  ${YELLOW}Warnings: $WARN${NC}"
echo "════════════════════════════════════════════════════════════════"

[ "$FAIL" -gt 0 ] && exit 1 || exit 0
