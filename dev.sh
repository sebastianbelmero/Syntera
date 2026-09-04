#!/usr/bin/env bash
#
# Syntera — Development Script
#
# Starts backend (dotnet run) and frontend (bun dev) together.
# Automatically:
#   - Checks prerequisites
#   - Kills any process already on port 5000 or 5173
#   - Starts both processes in parallel
#   - Forwards Ctrl+C to kill both cleanly
#   - Colors output for easy distinction
#
# Usage:
#   ./dev.sh                  # start both
#   ./dev.sh backend          # backend only
#   ./dev.sh frontend         # frontend only
#
set -uo pipefail

# ─── Config ─────────────────────────────────────────────────────────
ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
BACKEND_DIR="$ROOT_DIR/Syntera.Backend"
REACT_DIR="$ROOT_DIR/Syntera.React"
BACKEND_PORT=5296
REACT_PORT=5173

# Colors (backend=cyan, frontend=magenta, system=yellow)
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m'

MODE="${1:-both}"
PIDS=()

# ─── Helpers ────────────────────────────────────────────────────────
log()    { echo -e "${YELLOW}[$(date +%H:%M:%S)]${NC} $*"; }
log_api()  { echo -e "${CYAN}[backend]${NC} $*"; }
log_web()  { echo -e "${MAGENTA}[web]${NC} $*"; }
log_err()  { echo -e "${RED}[err]${NC} $*" >&2; }
log_ok()   { echo -e "${GREEN}[ok]${NC} $*"; }

# Kill any process listening on a given port
kill_port() {
  local port="$1"
  local pids
  pids=$(lsof -ti :"$port" 2>/dev/null || ss -tlnp 2>/dev/null | grep ":$port " | grep -oP 'pid=\K[0-9]+' | sort -u)
  if [ -n "$pids" ]; then
    log "Killing process on port $port (pid: $(echo "$pids" | tr '\n' ' '))"
    echo "$pids" | xargs -r kill -9 2>/dev/null || true
    sleep 1
  fi
}

# Check if a command exists
check_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    log_err "$1 not found. $2"
    return 1
  fi
  return 0
}

# Cleanup on exit
cleanup() {
  log "Shutting down..."
  for pid in "${PIDS[@]}"; do
    if kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
    fi
  done
  # Force kill if still alive
  sleep 1
  for pid in "${PIDS[@]}"; do
    if kill -0 "$pid" 2>/dev/null; then
      kill -9 "$pid" 2>/dev/null || true
    fi
  done
  log_ok "All processes stopped."
  exit 0
}
trap cleanup SIGINT SIGTERM

# ─── Prerequisites ──────────────────────────────────────────────────
log "Syntera Dev — checking prerequisites..."

check_cmd dotnet "Install .NET 10 SDK: https://dotnet.microsoft.com/download" || exit 1
log_ok "dotnet $(dotnet --version)"

if [ "$MODE" = "both" ] || [ "$MODE" = "frontend" ]; then
  if check_cmd bun ""; then
    FRONTEND_CMD="bun"
    log_ok "bun $(bun --version)"
  elif check_cmd npm ""; then
    FRONTEND_CMD="npm"
    log_ok "npm $(npm --version)"
  else
    log_err "Neither bun nor npm found. Install one of them."
    exit 1
  fi
fi

# Verify project dirs exist
[ ! -d "$BACKEND_DIR" ] && log_err "Backend dir not found: $BACKEND_DIR" && exit 1
[ ! -d "$REACT_DIR" ] && log_err "React dir not found: $REACT_DIR" && exit 1

# ─── Pre-flight: check SQL Server ───────────────────────────────────
if podman ps --format '{{.Names}}' 2>/dev/null | grep -q "^sql-server$"; then
  log_ok "Podman container 'sql-server' is running"
else
  log_err "Podman container 'sql-server' is NOT running!"
  echo ""
  echo "  Start it with:"
  echo "    podman run -d --name sql-server \\"
  echo "      -e ACCEPT_EULA=Y \\"
  echo "      -e MSSQL_SA_PASSWORD=Passwordkuat123! \\"
  echo "      -p 1433:1433 \\"
  echo "      mcr.microsoft.com/mssql/server:2022-latest"
  echo ""
  exit 1
fi

# ─── Kill existing processes on ports ───────────────────────────────
log "Cleaning up ports $BACKEND_PORT and $REACT_PORT..."
kill_port "$BACKEND_PORT"
kill_port "$REACT_PORT"

# ─── Start Backend ──────────────────────────────────────────────────
start_backend() {
  log_api "Building Backend..."
  (cd "$BACKEND_DIR" && dotnet build --nologo -v q 2>&1) | grep -E "error|Build succ" | sed 's/^/[backend] /' || true

  log_api "Starting Backend on http://localhost:$BACKEND_PORT..."
  (
    cd "$BACKEND_DIR"
    export ASPNETCORE_ENVIRONMENT=Development
    exec dotnet run --no-build --no-launch-profile --urls "http://localhost:$BACKEND_PORT" 2>&1
  ) | while IFS= read -r line; do
    echo -e "${CYAN}[backend]${NC} $line"
  done &
  PIDS+=($!)

  # Wait for Backend to be ready
  log_api "Waiting for Backend to start..."
  for i in {1..30}; do
    if curl -sf "http://localhost:$BACKEND_PORT/health" -o /dev/null 2>&1; then
      log_ok "Backend ready at http://localhost:$BACKEND_PORT (health: OK)"
      log_api "Swagger:  http://localhost:$BACKEND_PORT/docs"
      return 0
    fi
    sleep 1
  done
  log_err "Backend failed to start within 30s"
  return 1
}

# ─── Start Frontend ─────────────────────────────────────────────────
start_frontend() {
  log_web "Installing frontend deps (if needed)..."
  if [ ! -d "$REACT_DIR/node_modules" ]; then
    (cd "$REACT_DIR" && $FRONTEND_CMD install 2>&1) | sed 's/^/[web] /' || true
  fi

  log_web "Starting frontend on http://localhost:$REACT_PORT..."
  (
    cd "$REACT_DIR"
    if [ "$FRONTEND_CMD" = "bun" ]; then
      exec bun run dev 2>&1
    else
      exec npm run dev 2>&1
    fi
  ) | while IFS= read -r line; do
    echo -e "${MAGENTA}[web]${NC} $line"
  done &
  PIDS+=($!)

  # Wait for Vite to be ready
  log_web "Waiting for Vite to start..."
  for i in {1..20}; do
    if curl -sf "http://localhost:$REACT_PORT" -o /dev/null 2>&1; then
      log_ok "Frontend ready at http://localhost:$REACT_PORT"
      return 0
    fi
    sleep 1
  done
  log_err "Frontend failed to start within 20s"
  return 1
}

# ─── Run ────────────────────────────────────────────────────────────
echo ""
case "$MODE" in
  backend)
    start_backend || exit 1
    ;;
  frontend)
    start_frontend || exit 1
    ;;
  both|"")
    start_backend || exit 1
    echo ""
    start_frontend || exit 1
    ;;
  *)
    log_err "Unknown mode: $MODE"
    echo "Usage: $0 [backend|frontend|both]"
    exit 1
    ;;
esac

echo ""
echo -e "${GREEN}════════════════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}  ✓ Syntera dev stack is running!${NC}"
echo ""
echo "  Frontend:  http://localhost:$REACT_PORT"
echo "  Backend:   http://localhost:$BACKEND_PORT"
echo "  Swagger:   http://localhost:$BACKEND_PORT/docs"
echo "  Health:    http://localhost:$BACKEND_PORT/health"
echo ""
echo -e "${YELLOW}  Press Ctrl+C to stop both${NC}"
echo -e "${GREEN}════════════════════════════════════════════════════════════════${NC}"
echo ""

# Wait for both processes (will block until killed)
wait
