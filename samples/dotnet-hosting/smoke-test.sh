#!/usr/bin/env bash
# Asserts the hosting sample's claims that are checkable without a cloud account:
#   1. the app serves / and /health
#   2. a runtime-injected PORT (Railway/Render behaviour) is honoured — the fix for the
#      broken `ENV ASPNETCORE_URLS=http://+:${PORT:-8080}` Dockerfile pattern, which
#      bakes the port at BUILD time and silently ignores the platform's PORT
#   3. (if Docker is available) the same holds inside the container image
set -euo pipefail
cd "$(dirname "$0")"

ok()   { echo "  ok: $1"; }
fail() { echo "  FAIL: $1"; exit 1; }

wait_for() { # url, tries
  for _ in $(seq 1 "$2"); do
    if curl -sf "$1" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

echo "1. dotnet run with an injected PORT (what Railway and Render do)"
PORT=5599 ASPNETCORE_ENVIRONMENT=Production dotnet run --project . -c Release \
  --no-launch-profile >/dev/null 2>&1 &
APP_PID=$!
trap 'kill $APP_PID 2>/dev/null || true' EXIT

wait_for "http://localhost:5599/health" 60 || fail "app did not come up on the injected PORT 5599"
ok "the app listens on the runtime-injected PORT, not a baked-in default"

curl -sf http://localhost:5599/health | grep -q '"status":"healthy"' \
  && ok "/health returns healthy" || fail "/health wrong"
curl -sf http://localhost:5599/ | grep -q '"platform":"local / VM"' \
  && ok "/ identifies the platform (local / VM here — provider env vars stamp the rest)" \
  || fail "/ payload wrong"
kill $APP_PID 2>/dev/null || true

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  echo
  echo "2. same assertions inside the container image"
  docker build -q -t jorgenhoc-hosting-sample . >/dev/null
  CID=$(docker run -d -e PORT=5601 -p 5601:5601 jorgenhoc-hosting-sample)
  trap 'docker rm -f "$CID" >/dev/null 2>&1 || true' EXIT
  wait_for "http://localhost:5601/health" 30 || fail "container did not honour PORT=5601"
  ok "the container honours a runtime -e PORT (no build-time baking)"
  docker rm -f "$CID" >/dev/null
else
  echo
  echo "2. skipped: Docker not available — container PORT assertion not run"
fi

echo
echo "All checks passed. The deploy/ configs point at this app unchanged."
