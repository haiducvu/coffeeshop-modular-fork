#!/bin/sh
set -eu
case "${1:-}" in
  barista-worker|kitchen-worker) ;;
  *) echo 'Usage: phase-4-fault-demo.sh barista-worker|kitchen-worker' >&2; exit 1 ;;
esac
script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
export PHASE4_FAULT_WORKER="$1"
export DATAGEN_ORDER_COUNT=1
exec "${script_directory}/phase-4-smoke.sh"
