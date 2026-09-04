#!/bin/sh

set -u

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"
fake_path="${script_directory}/phase-4-barista-fakes"
smoke_script="${repository_root}/scripts/phase-4-barista-smoke.sh"
failures=0

run_smoke() {
  temporary_directory="$(mktemp -d)"
  trace_file="$(mktemp)"
  cp "$smoke_script" "${temporary_directory}/phase-4-barista-smoke.sh"
  cp "${fake_path}/phase-3-smoke.sh" "${temporary_directory}/phase-3-smoke.sh"
  chmod +x "${temporary_directory}/phase-4-barista-smoke.sh" \
    "${temporary_directory}/phase-3-smoke.sh"
  if output="$(PATH="${fake_path}:$PATH" \
    FAKE_PHASE4_TRACE="$trace_file" \
    FAKE_PHASE4_BARISTA_MISSING="${FAKE_PHASE4_BARISTA_MISSING:-false}" \
    "${temporary_directory}/phase-4-barista-smoke.sh" 2>&1)"; then
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

run_smoke
if [ "$status" -ne 0 ] || [ "$trace" != phase-3 ]; then
  echo "FAIL: a running Barista Worker did not delegate exactly once." >&2
  failures=$((failures + 1))
else
  echo "PASS: a running Barista Worker delegated exactly once."
fi

if [ "$failures" -ne 0 ]; then
  echo "Phase 4 Barista smoke behavior tests failed: ${failures} case(s)." >&2
  exit 1
fi

echo "Phase 4 Barista smoke behavior tests passed."
