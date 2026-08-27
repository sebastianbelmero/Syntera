#!/usr/bin/env bash
# Syntera setup script — run this ONCE before starting development.
#
# Prerequisites:
#   - .NET 10 SDK installed
#   - SQL Server 2022 running (local or remote)
#
# Usage:
#   chmod +x setup.sh
#   ./setup.sh
#
# What this script does:
#   1. Sets up user-secrets (JWT key, admin password, connection string)
#   2. Applies EF Core migrations to the Platform DB
#   3. Seeds default data (settings, role templates, admin@syntera.com user)
#   4. Verifies the login endpoint works
#
set -euo pipefail

cd "$(dirname "$0")/Syntera.Api"

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

# ─── 2. Set user-secrets (Development only) ─────────────────────────
echo ""
echo "▶ Setting up user-secrets..."

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

dotnet user-secrets set "ConnectionStrings:Platform" "$CONN"
dotnet user-secrets set "Jwt:SigningKey" "$JWT_KEY"
dotnet user-secrets set "Seed:PlatformAdminEmail" "admin@syntera.com"
dotnet user-secrets set "Seed:PlatformAdminPassword" "$ADMIN_PASS"

echo "  ✓ User-secrets configured"

# ─── 3. Apply EF Core migrations ────────────────────────────────────
echo ""
echo "▶ Applying EF Core migrations to Platform DB..."

# Install EF tool if not present
if ! command -v dotnet-ef &>/dev/null; then
  echo "  Installing dotnet-ef tool..."
  dotnet tool install --global dotnet-ef --version 10.0.11
  export PATH="$PATH:$HOME/.dotnet/tools"
fi

dotnet ef database update --context PlatformDbContext
echo "  ✓ Platform DB migrated"

# ─── 4. Run the API briefly to trigger seeding ──────────────────────
echo ""
echo "▶ Seeding default data (starting API briefly)..."

# Run API in background, wait for it to seed, then kill
dotnet run --no-build --no-launch-profile --urls "http://localhost:5099" &
API_PID=$!

echo "  Waiting for API to start and seed..."
sleep 10

# Test login
echo ""
echo "▶ Testing login (admin@syntera.com)..."

LOGIN_RESPONSE=$(curl -s -X POST http://localhost:5099/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"admin@syntera.com\",\"password\":\"$ADMIN_PASS\"}")

if echo "$LOGIN_RESPONSE" | grep -q "accessToken"; then
  echo "  ✓ Login successful!"
  echo "  ✓ Seeding complete"
else
  echo "  ✗ Login failed. Response:"
  echo "$LOGIN_RESPONSE" | head -20
fi

# Kill the API
kill $API_PID 2>/dev/null || true
wait $API_PID 2>/dev/null || true

# ─── 5. Done ────────────────────────────────────────────────────────
echo ""
echo "════════════════════════════════════════════════════════════════"
echo "  ✓ Setup complete!"
echo ""
echo "  Platform Admin:  admin@syntera.com"
echo "  Password:        (the one you entered above)"
echo ""
echo "  Next steps:"
echo "    cd Syntera.Api && dotnet run        # Start API on :5000"
echo "    cd Syntera.React && bun run dev     # Start SPA on :5173"
echo ""
echo "  Then open http://localhost:5173 and log in."
echo "════════════════════════════════════════════════════════════════"
