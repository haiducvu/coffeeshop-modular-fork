#!/bin/sh

set -u

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"
fake_path="${script_directory}/phase-4-kitchen-fakes"
smoke_script="${repository_root}/scripts/phase-4-kitchen-smoke.sh"
failures=0

if [ ! -f "$smoke_script" ]; then
  echo "FAIL: Phase 4 Kitchen smoke script is missing." >&2
  exit 1
fi

run_smoke() {
  temporary_directory="$(mktemp -d)"
  trace_file="$(mktemp)"
  cp "$smoke_script" "${temporary_directory}/phase-4-kitchen-smoke.sh"
  cp "${fake_path}/phase-3-smoke.sh" "${temporary_directory}/phase-3-smoke.sh"
  chmod +x "${temporary_directory}/phase-4-kitchen-smoke.sh" \
    "${temporary_directory}/phase-3-smoke.sh"
  if output="$(PATH="${fake_path}:$PATH" \
    FAKE_PHASE4_TRACE="$trace_file" \
    FAKE_PHASE4_BARISTA_MISSING="${FAKE_PHASE4_BARISTA_MISSING:-false}" \
    FAKE_PHASE4_KITCHEN_MISSING="${FAKE_PHASE4_KITCHEN_MISSING:-false}" \
    FAKE_PHASE4_SCHEMAS_MISSING="${FAKE_PHASE4_SCHEMAS_MISSING:-false}" \
    FAKE_PHASE4_MESSAGING_TABLES_MISSING="${FAKE_PHASE4_MESSAGING_TABLES_MISSING:-false}" \
    FAKE_PHASE4_FINAL_MIGRATIONS_MISSING="${FAKE_PHASE4_FINAL_MIGRATIONS_MISSING:-false}" \
    SMOKE_TIMEOUT_SECONDS="${SMOKE_TIMEOUT_SECONDS:-5}" \
    "${temporary_directory}/phase-4-kitchen-smoke.sh" 2>&1)"; then
    status=0
  else
    status=$?
  fi
  trace="$(cat "$trace_file")"
  rm -rf "$temporary_directory"
  rm -f "$trace_file"
}

FAKE_PHASE4_BARISTA_MISSING=true run_smoke
case "$output" in
  *"barista-worker is not running"*) missing_message=true ;;
  *) missing_message=false ;;
esac
if [ "$status" -eq 0 ] || [ -n "$trace" ] || [ "$missing_message" != true ]; then
  echo "FAIL: a missing Barista Worker was accepted." >&2
  failures=$((failures + 1))
else
  echo "PASS: a missing Barista Worker was rejected before delegation."
fi
unset FAKE_PHASE4_BARISTA_MISSING

FAKE_PHASE4_KITCHEN_MISSING=true run_smoke
case "$output" in
  *"kitchen-worker is not running"*) missing_message=true ;;
  *) missing_message=false ;;
esac
if [ "$status" -eq 0 ] || [ -n "$trace" ] || [ "$missing_message" != true ]; then
  echo "FAIL: a missing Kitchen Worker was accepted." >&2
  failures=$((failures + 1))
else
  echo "PASS: a missing Kitchen Worker was rejected before delegation."
fi
unset FAKE_PHASE4_KITCHEN_MISSING

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 FAKE_PHASE4_SCHEMAS_MISSING=true run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
case "$output" in
  *"station schemas are not ready"*) missing_message=true ;;
  *) missing_message=false ;;
esac
if [ "$status" -eq 0 ] || [ -n "$trace" ] \
  || [ "$elapsed_seconds" -gt 2 ] || [ "$missing_message" != true ]; then
  echo "FAIL: missing station schemas were accepted or waited without a bound." >&2
  failures=$((failures + 1))
else
  echo "PASS: missing station schemas were rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS FAKE_PHASE4_SCHEMAS_MISSING

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 FAKE_PHASE4_MESSAGING_TABLES_MISSING=true run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
if [ "$status" -eq 0 ] || [ -n "$trace" ] || [ "$elapsed_seconds" -gt 2 ]; then
  echo "FAIL: partial station migrations were accepted or waited without a bound." >&2
  failures=$((failures + 1))
else
  echo "PASS: partial station migrations were rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS FAKE_PHASE4_MESSAGING_TABLES_MISSING

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 FAKE_PHASE4_FINAL_MIGRATIONS_MISSING=true run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
if [ "$status" -eq 0 ] || [ -n "$trace" ] || [ "$elapsed_seconds" -gt 2 ]; then
  echo "FAIL: station schemas without final migrations were accepted or waited without a bound." >&2
  failures=$((failures + 1))
else
  echo "PASS: station schemas without final migrations were rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS FAKE_PHASE4_FINAL_MIGRATIONS_MISSING

run_smoke
if [ "$status" -ne 0 ] || [ "$trace" != phase-3 ]; then
  echo "FAIL: running station Workers did not delegate exactly once." >&2
  failures=$((failures + 1))
else
  echo "PASS: running station Workers delegated exactly once."
fi

if [ "$failures" -ne 0 ]; then
  echo "Phase 4 Kitchen smoke behavior tests failed: ${failures} case(s)." >&2
  exit 1
fi

echo "Phase 4 Kitchen smoke behavior tests passed."
