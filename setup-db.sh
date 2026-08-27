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
set -euo pipefail

ENVIRONMENT="${1:-Development}"
cd "$(dirname "$0")"

echo "Running Syntera.DbSetup in $ENVIRONMENT mode..."
echo ""

if [ "$ENVIRONMENT" = "Production" ]; then
  export ASPNETCORE_ENVIRONMENT=Production
else
  export ASPNETCORE_ENVIRONMENT=Development
fi

cd Syntera.DbSetup
dotnet run --no-build 2>&1 | tee -a ../setup-db.log
