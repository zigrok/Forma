#!/usr/bin/env bash
set -uo pipefail

# Purpose: Re-run a command only when it fails with a known transient MSBuild file-lock race
# (concurrent obj marker writes seen on CI, e.g. MSB3491/MSB3371). Real failures fail fast.
# Usage: `bash scripts/retry.sh <command> [args...]`. Override attempts with RETRY_ATTEMPTS.

attempts="${RETRY_ATTEMPTS:-2}"
# MSB3030/MSB3073 join the lock signatures: a parallel node can copy or invoke against an output
# another node is still writing, and MGCB (MSB3073) can be killed with 137 mid-write. All are the
# same race seen from different targets, and a real failure still fails fast.
transient='MSB3491|MSB3371|MSB4018|MSB3030|MSB3073|being used by another process|Could not write lines to file|Could not copy the file|exited with code 137'
log="$(mktemp "${TMPDIR:-/tmp}/forma-retry.XXXXXX")"
trap 'rm -f "$log"' EXIT

attempt=1
while true; do
  if "$@" 2>&1 | tee "$log"; then
    exit 0
  fi
  exit_code="${PIPESTATUS[0]}"
  if (( attempt < attempts )) && grep -qE "$transient" "$log"; then
    printf '::warning::retry.sh: transient MSBuild file lock detected (attempt %d/%d); retrying\n' "$attempt" "$attempts" >&2
    attempt=$((attempt + 1))
    continue
  fi
  exit "$exit_code"
done
