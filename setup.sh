#!/usr/bin/env bash
# Syntera setup script — run this ONCE before starting development.
#
# Prerequisites:
#   - .NET 10 SDK installed
#   - SQL Server 2022 running (local or remote) — see dev.sh for podman example
#
# Usage:
#   chmod +x setup.sh
#   ./setup.sh
#
# What this script does:
#   1. Validates prerequisites
#   2. Sets up user-secrets on Syntera.Backend (JWT key, admin password, conn string)
#   3. Applies EF Core migrations to all 7 databases via Syntera.DbSetup
#   4. Seeds default data (role templates, 6 sites, themes, admin@syntera.com user)
#   5. Verifies the login endpoint works
#
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
BACKEND_DIR="$ROOT_DIR/Syntera.Backend"
DBSETUP_DIR="$ROOT_DIR/Syntera.DbSetup"
BACKEND_PORT=5296

echo "════════════════════════════════════════════════════════════════"
echo "  Syntera IAM — Initial Setup"
echo "════════════════════════════════════════════════════════════════"

# ─── 1. Validate prerequisites ─────────────────────────────────────
echo ""
echo "▶ Checking prerequisites..."

if ! command -v dotnet &>/dev/null; then
  echo "✗ .NET SDK not found. Install from https://dotnet.microsoft.com/download"
  exit 1
fi

DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "0")
echo "  ✓ .NET SDK $DOTNET_VERSION"

[ ! -d "$BACKEND_DIR" ] && echo "✗ Backend dir not found: $BACKEND_DIR" && exit 1
[ ! -d "$DBSETUP_DIR" ] && echo "✗ DbSetup dir not found: $DBSETUP_DIR" && exit 1

# ─── 2. Set user-secrets (Development only) ─────────────────────────
echo ""
echo "▶ Setting up user-secrets on Syntera.Backend..."

cd "$BACKEND_DIR"

# Initialize user-secrets store for this project (idempotent)
dotnet user-secrets init >/dev/null 2>&1 || true

# Prompt for SQL Server connection string (with default for local dev)
read -rp "  SQL Server connection string [Server=localhost,1433;Database=syntera_master;User Id=sa;Password=YourSaPassword;TrustServerCertificate=True;MultipleActiveResultSets=True]: " CONN
CONN=${CONN:-"Server=localhost,1433;Database=syntera_master;User Id=sa;Password=YourSaPassword;TrustServerCertificate=True;MultipleActiveResultSets=True"}

# Generate a random JWT signing key (32+ chars)
JWT_KEY=$(openssl rand -base64 48 | tr -d '/+=' | head -c 48)
echo "  Generated JWT signing key (saved to user-secrets)"

# Prompt for admin password (with default for dev)
read -rsp "  Platform Admin password (admin@syntera.com) [ChangeMe!Strong#1]: " ADMIN_PASS
ADMIN_PASS=${ADMIN_PASS:-"ChangeMe!Strong#1"}
echo ""

dotnet user-secrets set "ConnectionStrings:Platform" "$CONN" >/dev/null
# Also expose all 6 site DB conn strings via user-secrets so DbSetup can pick them up.
SITE_CONN_BASE="Server=localhost,1433;User Id=sa;Password=$(echo "$CONN" | grep -oP 'Password=\K[^;]+');TrustServerCertificate=True;MultipleActiveResultSets=True"
for code in kalventis kalbe fima gof dankos hexpharm; do
  dotnet user-secrets set "ConnectionStrings:Sites:$code" "${SITE_CONN_BASE};Database=syntera_${code}" >/dev/null
done
dotnet user-secrets set "Jwt:SigningKey" "$JWT_KEY" >/dev/null
dotnet user-secrets set "Seed:PlatformAdminEmail" "admin@syntera.com" >/dev/null
dotnet user-secrets set "Seed:PlatformAdminPassword" "$ADMIN_PASS" >/dev/null

echo "  ✓ User-secrets configured (Platform + 6 sites + JWT + Seed)"

# ─── 3. Apply migrations + seed via Syntera.DbSetup ─────────────────
echo ""
echo "▶ Running Syntera.DbSetup (creates 7 DBs, applies migrations, seeds platform data)..."
cd "$DBSETUP_DIR"
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --no-build 2>&1 | tee -a "$ROOT_DIR/setup-db.log"

# ─── 4. Run the Backend briefly to verify startup + test login ───────
echo ""
echo "▶ Starting Backend briefly to verify startup..."
cd "$BACKEND_DIR"

# Build first so dotnet run is fast
dotnet build --nologo -v q 2>&1 | grep -E "error|Build succ" || true

dotnet run --no-build --no-launch-profile --urls "http://localhost:$BACKEND_PORT" &
API_PID=$!

echo "  Waiting for Backend to start..."
for i in {1..30}; do
  if curl -sf "http://localhost:$BACKEND_PORT/health" -o /dev/null 2>&1; then
    echo "  ✓ Backend ready"
    break
  fi
  sleep 1
done

# Test login
echo ""
echo "▶ Testing login (admin@syntera.com)..."
LOGIN_RESPONSE=$(curl -s -X POST "http://localhost:$BACKEND_PORT/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"admin@syntera.com\",\"password\":\"$ADMIN_PASS\"}")

if echo "$LOGIN_RESPONSE" | grep -q "accessToken"; then
  echo "  ✓ Login successful!"
  echo "  ✓ Seeding complete"
else
  echo "  ✗ Login failed. Response:"
  echo "$LOGIN_RESPONSE" | head -20
fi

# Kill the Backend
kill "$API_PID" 2>/dev/null || true
wait "$API_PID" 2>/dev/null || true

# ─── 5. Done ────────────────────────────────────────────────────────
echo ""
echo "════════════════════════════════════════════════════════════════"
echo "  ✓ Setup complete!"
echo ""
echo "  Platform Admin:  admin@syntera.com"
echo "  Password:        (the one you entered above)"
echo ""
echo "  Next steps:"
echo "    ./dev.sh              # Start backend (:5296) + frontend (:5173)"
echo "    ./diagnose.sh         # Run comprehensive diagnostics"
echo ""
echo "  Then open http://localhost:5173 and log in."
echo "════════════════════════════════════════════════════════════════"
