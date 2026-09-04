#!/usr/bin/env bash
#
# Syntera — Database Setup Script
#
# Creates all 7 databases (1 platform + 6 sites) and applies migrations
# in one command. Idempotent — safe to run multiple times.
#
# Usage:
#   ./setup-db.sh                    # use Development config
#   ./setup-db.sh Production         # use Production config (requires env vars)
#   SYNTERA_Seed__PlatformAdminPassword="MyPass123!" ./setup-db.sh
#
# What this script does (in order):
#   1. Build Syntera.DbSetup (so the latest migration code is included)
#   2. For each database (1 platform + 6 sites):
#      a. CREATE DATABASE if not exists
#      b. Apply ALL pending EF Core migrations (including any new ones
#         you pulled from main, e.g. AddUserTitle)
#   3. Seed platform data (role templates, 6 sites, themes, admin@syntera.com)
#
# Run this AFTER every `git pull` to ensure the DB schema matches the code.
#
set -euo pipefail

ENVIRONMENT="${1:-Development}"
ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
DBSETUP_DIR="$ROOT_DIR/Syntera.DbSetup"

echo "════════════════════════════════════════════════════════════════"
echo "  Syntera DbSetup — Environment: $ENVIRONMENT"
echo "════════════════════════════════════════════════════════════════"
echo ""

if [ "$ENVIRONMENT" = "Production" ]; then
  export ASPNETCORE_ENVIRONMENT=Production
else
  export ASPNETCORE_ENVIRONMENT=Development
fi

# ─── 1. Build Syntera.DbSetup (and Syntera.Backend as a transitive dep) ─
echo "▶ Building Syntera.DbSetup..."
cd "$DBSETUP_DIR"
if ! dotnet build --nologo -v q 2>&1 | grep -E "error|Build succeeded" | tail -3; then
  echo "✗ Build failed. See above for details."
  exit 1
fi
echo "  ✓ Build OK"
echo ""

# ─── 2. Run DbSetup (creates DBs + applies migrations + seeds platform) ─
echo "▶ Running Syntera.DbSetup..."
echo ""
dotnet run --no-build 2>&1 | tee -a "$ROOT_DIR/setup-db.log"

echo ""
echo "════════════════════════════════════════════════════════════════"
echo "  ✓ Done. Next: ./dev.sh   (start backend :5296 + frontend :5173)"
echo "════════════════════════════════════════════════════════════════"
