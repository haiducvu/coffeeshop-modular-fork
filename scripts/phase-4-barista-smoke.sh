#!/bin/sh

set -eu

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"

if ! docker compose ps --status running --services \
  | grep -qx 'barista-worker'; then
  echo "Phase 4 Barista smoke test failed: barista-worker is not running." >&2
  exit 1
fi

export EXPECT_BARISTA_WORKER_TELEMETRY=true
exec "${script_directory}/phase-3-smoke.sh"
