#!/usr/bin/env bash
#
# Syntera — Comprehensive Diagnostics
#
# Checks every layer of the stack to pinpoint where things break.
# Handles SQL Server case-sensitivity (GUIDs stored uppercase).
#
set -uo pipefail

# ─── Configuration ──────────────────────────────────────────────────
CONTAINER="${CONTAINER:-sql-server}"
SA_PASSWORD="${SA_PASSWORD:-Passwordkuat123!}"
API_URL="${API_URL:-http://localhost:5000}"
API_DIR="${API_DIR:-$(cd "$(dirname "$0")/Syntera.Api" && pwd)}"
LOG_FILE="${LOG_FILE:-/tmp/syntera-api-diag.log}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

PASS=0
FAIL=0
WARN=0

check_pass() { echo -e "  ${GREEN}✓${NC} $1"; ((PASS++)); }
check_fail() { echo -e "  ${RED}✗${NC} $1"; ((FAIL++)); }
check_warn() { echo -e "  ${YELLOW}⚠${NC} $1"; ((WARN++)); }
section()    { echo -e "\n${BLUE}━━━ $1 ━━━${NC}"; }

# Helper: run sqlcmd, strip "(X rows affected)" and empty lines.
# Returns clean output that can be safely compared.
sql() {
  podman exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C \
    -d master -Q "$1" -h -1 -W 2>&1 | grep -vE "^\([0-9]+ rows? affected\)$" | grep -v "^$"
}

sql_db() {
  local db="$1"; shift
  local query="$1"
  podman exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C \
    -d "$db" -Q "$query" -h -1 -W 2>&1 | grep -vE "^\([0-9]+ rows? affected\)$" | grep -v "^$"
}

# Helper: get a single scalar value (first non-empty line)
sql_scalar() {
  local result
  result=$(sql "$1" 2>&1 | head -1 | tr -d '[:space:]')
  echo "$result"
}

sql_scalar_db() {
  local db="$1"; shift
  local result
  result=$(sql_db "$db" "$1" 2>&1 | head -1 | tr -d '[:space:]')
  echo "$result"
}

echo "════════════════════════════════════════════════════════════════"
echo "  Syntera Diagnostics"
echo "  Container: $CONTAINER"
echo "  SA Password: ${SA_PASSWORD:0:3}***"
echo "  API URL: $API_URL"
echo "  API Dir: $API_DIR"
echo "════════════════════════════════════════════════════════════════"

# ─── 1. Podman container ────────────────────────────────────────────
section "1. Podman Container"

if ! command -v podman &>/dev/null; then
  check_fail "podman not installed"
else
  check_pass "podman installed: $(podman --version)"
fi

if podman ps --format '{{.Names}}' | grep -q "^${CONTAINER}$"; then
  check_pass "Container '$CONTAINER' is running"
else
  check_fail "Container '$CONTAINER' is NOT running"
  echo "      Start it with:"
  echo "      podman run -d --name $CONTAINER -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=$SA_PASSWORD -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest"
  exit 1
fi

# ─── 2. SQL Server reachable ────────────────────────────────────────
section "2. SQL Server Connection"

SQL_TEST=$(sql_scalar "SELECT 1")
if [ "$SQL_TEST" = "1" ]; then
  check_pass "SQL Server accepts SA login"
else
  check_fail "SQL Server SA login failed"
  echo "      Output: $SQL_TEST"
  exit 1
fi

SQL_VERSION=$(sql "SELECT @@VERSION" | head -1)
check_pass "SQL Server version: $(echo "$SQL_VERSION" | cut -c1-60)..."

# Check collation (case-sensitive vs insensitive)
COLLATION=$(sql_scalar "SELECT SERVERPROPERTY('Collation')")
check_pass "Server collation: $COLLATION"
if echo "$COLLATION" | grep -qi "CS_"; then
  check_warn "Case-sensitive collation — GUID comparisons may be case-sensitive"
fi

# ─── 3. Databases exist ─────────────────────────────────────────────
section "3. Databases"

EXPECTED_DBS=("syntera_master" "syntera_kalventis" "syntera_kalbe" "syntera_fima" "syntera_gof" "syntera_dankos" "syntera_hexpharm")

for db in "${EXPECTED_DBS[@]}"; do
  EXISTS=$(sql_scalar "SELECT COUNT(*) FROM sys.databases WHERE name = '$db'")
  if [ "$EXISTS" = "1" ]; then
    check_pass "Database '$db' exists"
  else
    check_fail "Database '$db' MISSING"
  fi
done

# ─── 4. Platform DB Schema ──────────────────────────────────────────
section "4. Platform DB Schema (syntera_master)"

PLATFORM_TABLES=$(sql_db syntera_master "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME")

EXPECTED_PLATFORM_TABLES=("AuditLogs" "PlatformSettings" "PlatformUsers" "RefreshTokens" "RoleTemplatePermissions" "RoleTemplates" "SiteLdapConfigs" "SiteLdapDomains" "SiteThemes" "Sites")

for t in "${EXPECTED_PLATFORM_TABLES[@]}"; do
  if echo "$PLATFORM_TABLES" | grep -qx "$t"; then
    check_pass "Table '$t' exists"
  else
    check_fail "Table '$t' MISSING — run migrations"
  fi
done

# Critical: SiteLdapConfigs columns
section "4b. SiteLdapConfigs Schema (critical — was simplified)"

LDAP_COLS=$(sql_db syntera_master "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SiteLdapConfigs' ORDER BY ORDINAL_POSITION")
echo "      Current columns: $(echo "$LDAP_COLS" | tr '\n' ' ')"

EXPECTED_LDAP_COLS=("Id" "SiteId" "Host" "Port" "UseStartTls" "BaseDn" "CreatedAt" "UpdatedAt")
for c in "${EXPECTED_LDAP_COLS[@]}"; do
  if echo "$LDAP_COLS" | grep -qx "$c"; then
    check_pass "Column '$c' present"
  else
    check_fail "Column '$c' MISSING — schema is stale, re-run migrations"
  fi
done

STALE_COLS=("EmailAttribute" "BindDn" "BindPasswordEncrypted" "UserFilterTemplate" "TimeoutSeconds" "SearchSubtree")
for c in "${STALE_COLS[@]}"; do
  if echo "$LDAP_COLS" | grep -qx "$c"; then
    check_warn "Stale column '$c' still present — schema needs DROP COLUMN"
  fi
done

# ─── 5. Seed Data ───────────────────────────────────────────────────
section "5. Seed Data"

# Platform admin — use COUNT(*) and check if >= 1
ADMIN_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM PlatformUsers WHERE Email='admin@syntera.com'")
if [ "$ADMIN_COUNT" -ge 1 ] 2>/dev/null; then
  check_pass "Platform Admin (admin@syntera.com) exists"
  ADMIN_ENABLED=$(sql_scalar_db syntera_master "SELECT IsEnabled FROM PlatformUsers WHERE Email='admin@syntera.com'")
  if [ "$ADMIN_ENABLED" = "1" ]; then
    check_pass "Platform Admin is enabled"
  else
    check_fail "Platform Admin is DISABLED"
  fi
else
  check_fail "Platform Admin MISSING — run Syntera.DbSetup"
fi

# 6 Sites
SITE_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM Sites")
if [ "$SITE_COUNT" = "6" ]; then
  check_pass "6 sites seeded"
else
  check_fail "Expected 6 sites, found '$SITE_COUNT'"
fi

echo "      Sites:"
sql_db syntera_master "SELECT Code + ' | ' + DisplayName FROM Sites ORDER BY Code" | sed 's/^/        /'

# Role templates — escape [Key] because it's a reserved word
RT_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM RoleTemplates")
if [ "$RT_COUNT" -ge 2 ] 2>/dev/null; then
  check_pass "$RT_COUNT role templates exist"
else
  check_fail "Expected ≥2 role templates, found '$RT_COUNT'"
fi

echo "      Role templates:"
sql_db syntera_master "SELECT [Key] + ' (v' + CAST(Version AS VARCHAR) + ', published=' + CAST(IsPublished AS VARCHAR) + ')' FROM RoleTemplates ORDER BY [Key]" | sed 's/^/        /'

# ─── 6. Site DBs Schema ─────────────────────────────────────────────
section "6. Site DBs Schema"

EXPECTED_SITE_TABLES=("AuditLogs" "Permissions" "RefreshTokens" "RolePermissions" "Roles" "UserPermissions" "UserRoles" "UserSyncHistory" "Users")

for db in "syntera_kalventis" "syntera_kalbe" "syntera_fima" "syntera_gof" "syntera_dankos" "syntera_hexpharm"; do
  # Check DB exists first
  DB_EXISTS=$(sql_scalar "SELECT COUNT(*) FROM sys.databases WHERE name = '$db'")
  if [ "$DB_EXISTS" != "1" ]; then
    check_fail "$db: database MISSING"
    continue
  fi

  SITE_TABLES=$(sql_db "$db" "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME")
  TABLE_COUNT=$(echo "$SITE_TABLES" | grep -c .)

  if [ "$TABLE_COUNT" -lt 8 ]; then
    check_fail "$db: only $TABLE_COUNT tables (expected ≥9)"
  else
    check_pass "$db: $TABLE_COUNT tables"

    SBA_COUNT=$(sql_scalar_db "$db" "SELECT COUNT(*) FROM Roles WHERE [Key]='site-business-admin'")
    if [ "$SBA_COUNT" -ge 1 ] 2>/dev/null; then
      check_pass "  $db: site-business-admin role cloned"
    else
      check_warn "  $db: site-business-admin role NOT cloned — publish template first"
    fi
  fi
done

# ─── 7. API reachable ───────────────────────────────────────────────
section "7. API Health"

if ! curl -sf "${API_URL}/health" -o /dev/null 2>&1; then
  check_fail "API not reachable at $API_URL"
  echo "      Start it with: cd Syntera.Api && ASPNETCORE_ENVIRONMENT=Development dotnet run"
else
  check_pass "API reachable at $API_URL"
  HEALTH=$(curl -s "${API_URL}/health" 2>&1)
  echo "      Health response: $HEALTH"
fi

# ─── 8. Login Test ──────────────────────────────────────────────────
section "8. Login Test (API → DB)"

if ! curl -sf "${API_URL}/health" -o /dev/null 2>&1; then
  check_warn "Skipping login test — API not running"
else
  LOGIN_RESP=$(curl -s -w "\n%{http_code}" -X POST "${API_URL}/api/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"email":"admin@syntera.com","password":"ChangeMe!Strong#1"}')

  HTTP_CODE=$(echo "$LOGIN_RESP" | tail -1)
  BODY=$(echo "$LOGIN_RESP" | head -n -1)

  if [ "$HTTP_CODE" = "200" ] && echo "$BODY" | grep -q "accessToken"; then
    check_pass "Platform Admin login successful"
    TOKEN=$(echo "$BODY" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)
    check_pass "Got JWT token (${#TOKEN} chars)"
  else
    check_fail "Login failed (HTTP $HTTP_CODE)"
    echo "      Response: $(echo "$BODY" | head -c 300)"
  fi
fi

# ─── 9. Role Templates endpoint ─────────────────────────────────────
section "9. Role Templates Endpoint"

if [ -z "${TOKEN:-}" ]; then
  check_warn "Skipping role templates test — no auth token"
else
  RT_RESP=$(curl -s -w "\n%{http_code}" "${API_URL}/api/platform/role-templates" \
    -H "Authorization: Bearer $TOKEN")

  HTTP_CODE=$(echo "$RT_RESP" | tail -1)
  BODY=$(echo "$RT_RESP" | head -n -1)

  if [ "$HTTP_CODE" = "200" ]; then
    RT_COUNT_API=$(echo "$BODY" | grep -o '"id"' | wc -l)
    check_pass "GET /role-templates OK ($RT_COUNT_API templates)"
  else
    check_fail "GET /role-templates failed (HTTP $HTTP_CODE)"
    echo "      Response: $(echo "$BODY" | head -c 300)"
  fi

  # Test PUT — use the first template ID (case-insensitive)
  FIRST_RT_ID=$(echo "$BODY" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
  if [ -n "$FIRST_RT_ID" ]; then
    echo "      Testing PUT on role template: $FIRST_RT_ID"

    PUT_RESP=$(curl -s -w "\n%{http_code}" -X PUT "${API_URL}/api/platform/role-templates/${FIRST_RT_ID}" \
      -H "Authorization: Bearer $TOKEN" \
      -H "Content-Type: application/json" \
      -d '{
        "key": "site-business-admin",
        "displayName": "Site Business Admin",
        "description": "Manages users, roles, and permissions within own site.",
        "isSiteAdminRole": true,
        "permissionKeys": ["user.read","user.write","user.disable","user.sync","role.read","user_role.assign","user_role.revoke","permission.read","permission.grant","permission.revoke","audit.read","report.read"]
      }')

    HTTP_CODE=$(echo "$PUT_RESP" | tail -1)
    BODY=$(echo "$PUT_RESP" | head -n -1)

    if [ "$HTTP_CODE" = "200" ]; then
      check_pass "PUT /role-templates OK"
    else
      check_fail "PUT /role-templates failed (HTTP $HTTP_CODE)"
      echo "      Response body:"
      echo "$BODY" | head -c 500 | sed 's/^/        /'
      echo ""
    fi
  fi
fi

# ─── 10. API Logs ───────────────────────────────────────────────────
section "10. Recent API Logs"

LATEST_LOG=$(ls -t "$API_DIR"/logs/syntera-api-*.log 2>/dev/null | head -1)
if [ -n "$LATEST_LOG" ]; then
  check_pass "API log file: $LATEST_LOG"

  echo "      Last 15 lines:"
  tail -15 "$LATEST_LOG" | sed 's/^/        /'

  echo ""
  echo "      Errors in last 100 lines:"
  ERRORS=$(tail -100 "$LATEST_LOG" | grep -i "error\|exception\|fail" | tail -5)
  if [ -n "$ERRORS" ]; then
    echo "$ERRORS" | sed 's/^/        /'
  else
    echo "        (none)"
  fi
else
  check_warn "No API log files found in $API_DIR/logs/"
fi

# ─── 11. Role Template by ID (case-insensitive) ─────────────────────
section "11. Role Template Lookup by ID"

RT_DATA=$(sql_db syntera_master "SELECT UPPER(CAST(Id AS CHAR(36))), [Key], IsPublished, Version FROM RoleTemplates")
echo "      Role templates in DB:"
echo "$RT_DATA" | sed 's/^/        /'

RTP_COUNT=$(sql_scalar_db syntera_master "SELECT COUNT(*) FROM RoleTemplatePermissions")
check_pass "RoleTemplatePermissions: $RTP_COUNT rows"

# ─── Summary ────────────────────────────────────────────────────────
echo ""
echo "════════════════════════════════════════════════════════════════"
echo -e "  ${GREEN}Passed: $PASS${NC}  ${RED}Failed: $FAIL${NC}  ${YELLOW}Warnings: $WARN${NC}"
echo "════════════════════════════════════════════════════════════════"

if [ "$FAIL" -gt 0 ]; then
  echo ""
  echo "  Next steps:"
  echo "    1. Fix all ✗ failures above"
  echo "    2. Re-run: ./diagnose.sh"
  exit 1
fi
