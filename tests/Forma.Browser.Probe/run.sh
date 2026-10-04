#!/usr/bin/env bash
set -euo pipefail

# Purpose: serve the built browser probe and verify it in the requested browsers. This is the
# runner Forma CI uses; it takes an already installed Playwright module so the probe itself
# never installs tools. Usage:
#
#   PLAYWRIGHT_MODULE=<path to @playwright/test/index.mjs> \
#   [FORMA_PROBE_BROWSERS=chromium,firefox,webkit] [FORMA_PROBE_PORT=5196] \
#   bash tests/Forma.Browser.Probe/run.sh
#
# A headless Linux runner has no WebGL for Firefox or WebKit, so CI passes chromium; the
# default still runs all three, which is what a desktop machine does.

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output="$root/tests/Forma.Browser.Probe/bin/MonoGame/Browser/Release/net10.0/publish/wwwroot"
port="${FORMA_PROBE_PORT:-5196}"
browsers="${FORMA_PROBE_BROWSERS:-chromium,firefox,webkit}"
: "${PLAYWRIGHT_MODULE:?Set PLAYWRIGHT_MODULE to an installed @playwright/test entry point.}"
test -d "$output" || { printf 'The probe is not built; run tests/Forma.Browser.Probe/prepare.sh first.\n' >&2; exit 2; }

node "$root/tests/Forma.Browser.Probe/serve.mjs" "$output" "$port" &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
for _ in $(seq 1 100); do
  curl -sf "http://127.0.0.1:$port/" >/dev/null && break
  sleep 0.2
done
node "$root/tests/Forma.Browser.Probe/verify.mjs" \
  "$PLAYWRIGHT_MODULE" "http://127.0.0.1:$port/" "$browsers"