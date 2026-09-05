#!/bin/sh
set -eu
script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"
smoke_script="${repository_root}/scripts/phase-4-smoke.sh"
if [ ! -x "$smoke_script" ]; then
  echo 'FAIL: distributed batch smoke is missing.' >&2
  exit 1
fi

failures=0
run_case() {
  scenario="$1"
  expected="$2"
  test_seed=34
  if [ "$scenario" = leading-zero-seed ]; then test_seed=08; fi
  temporary_directory="$(mktemp -d)"
  started_at="$(date +%s)"
  if output="$(PATH="${script_directory}/phase-4-fakes:$PATH" \
    FAKE_PHASE4_STATE="$temporary_directory" FAKE_PHASE4_SCENARIO="$scenario" \
    DATAGEN_ORDER_COUNT=2 DATAGEN_SEED="$test_seed" SMOKE_TIMEOUT_SECONDS=6 SMOKE_RECOVERY_SECONDS=1 \
    PHASE4_FAULT_WORKER="${3:-}" "$smoke_script" 2>&1)"; then result=0; else result=$?; fi
  elapsed=$(( $(date +%s) - started_at ))
  expected_stage=''
  case "$scenario" in
    lost-order|pending-outbox|rejected-outbox) expected_stage=fulfillment ;;
    missing-worker|unavailable-api|hung-command) expected_stage=preflight ;;
    duplicate-effect|duplicate-not-consumed|replay-dead-lettered) expected_stage=duplicate-replay ;;
    restart-failed) expected_stage=recovery ;;
    no-backlog) expected_stage=backlog ;;
  esac
  wrong_stage=false
  if [ -n "$expected_stage" ] && ! printf '%s' "$output" | grep -Fq "failed [$expected_stage]"; then
    wrong_stage=true
  fi
  if { [ "$expected" = pass ] && [ "$result" -ne 0 ]; } \
    || { [ "$expected" = fail ] && [ "$result" -eq 0 ]; } \
    || [ "$wrong_stage" = true ] \
    || [ "$elapsed" -gt 10 ]; then
    echo "FAIL: $scenario (result=$result, elapsed=$elapsed)." >&2
    printf '%s\n' "$output" >&2
    failures=$((failures + 1))
  else
    echo "PASS: $scenario ($expected, bounded)."
  fi
  if [ -n "${3:-}" ] && [ -f "$temporary_directory/stopped" ] \
    && [ ! -f "$temporary_directory/restart-attempted" ]; then
    echo 'FAIL: interrupted worker was not restored on exit.' >&2
    failures=$((failures + 1))
  fi
  rm -r "$temporary_directory"
}

run_case normal pass
run_case fresh-topics pass
run_case leading-zero-seed pass
run_case existing-data pass
run_case lost-order fail
run_case duplicate-effect fail
run_case pending-outbox fail
run_case rejected-outbox fail
run_case missing-worker fail
run_case unavailable-api fail
run_case hung-command fail
run_case duplicate-not-consumed fail
run_case replay-dead-lettered fail
run_case recovery pass barista-worker
run_case recovery pass kitchen-worker
run_case restart-failed fail kitchen-worker
run_case no-backlog fail barista-worker
run_case invalid-target fail postgres
test "$failures" -eq 0
echo 'Phase 4 distributed smoke behavior tests passed.'
