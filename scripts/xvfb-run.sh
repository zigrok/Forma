#!/usr/bin/env bash
# Runs a command under a fresh X server, and returns only once the server accepts connections.
#
# `xvfb-run -a` hands the display number to the payload as soon as Xvfb creates its socket; Xvfb 2:21.1.12-1ubuntu1.9 then still needs a
# moment before it listens, so a client that connects immediately dies with
#     XCB failed to connect to the X server due to error:1
# (vulkaninfo, and SDL/GL programs in general). Waiting for a successful X query before starting the payload removes the race.
# Usage mirrors the part of xvfb-run this repository uses:
#
#     scripts/xvfb-run.sh -s '-screen 0 1440x900x24 +extension GLX' COMMAND [ARGS...]
set -euo pipefail

server_args=""
if [ "${1:-}" = "-s" ]; then
  server_args=${2:?xvfb-run.sh: -s needs a value}
  shift 2
fi
if [ $# -eq 0 ]; then
  echo "usage: xvfb-run.sh [-s SERVER_ARGS] COMMAND [ARGS...]" >&2
  exit 2
fi
if ! command -v xdpyinfo >/dev/null 2>&1; then
  echo "xvfb-run.sh: xdpyinfo is required (package x11-utils)" >&2
  exit 1
fi

# Xvfb starts in milliseconds on an idle machine and in a second or two under load; 15 s is the "something is actually wrong" limit.
wait_seconds=${XVFB_WAIT_SECONDS:-15}
args=(xvfb-run -a)
if [ -n "$server_args" ]; then
  args+=(-s "$server_args")
fi

exec "${args[@]}" bash -c '
  wait=$1
  shift
  deadline=$((SECONDS + wait))
  while ! xdpyinfo >/dev/null 2>&1; do
    if [ "$SECONDS" -ge "$deadline" ]; then
      echo "xvfb-run.sh: the X server on ${DISPLAY:-?} did not accept connections within ${wait}s" >&2
      exit 1
    fi
    sleep 0.1
  done
  exec "$@"
' _ "$wait_seconds" "$@"
