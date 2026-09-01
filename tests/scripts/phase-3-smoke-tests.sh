#!/bin/sh

set -u

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"
fake_path="${script_directory}/phase-3-fakes"
smoke_script="${repository_root}/scripts/phase-3-smoke.sh"
failures=0

run_smoke() {
  trace_file="$(mktemp)"
  state_file="$(mktemp)"
  if output="$(PATH="${fake_path}:$PATH" \
    FAKE_PHASE3_TRACE="$trace_file" \
    FAKE_PHASE3_STATE="$state_file" \
    SMOKE_TIMEOUT_SECONDS="${SMOKE_TIMEOUT_SECONDS:-5}" \
    AUTHENTICATION_ENABLED="${AUTHENTICATION_ENABLED:-false}" \
    FAKE_PHASE3_NEVER_FULFILLED="${FAKE_PHASE3_NEVER_FULFILLED:-false}" \
    FAKE_PHASE3_NO_KAFKA="${FAKE_PHASE3_NO_KAFKA:-false}" \
    FAKE_PHASE3_NO_DLT="${FAKE_PHASE3_NO_DLT:-false}" \
    FAKE_PHASE3_NO_SCHEMAS="${FAKE_PHASE3_NO_SCHEMAS:-false}" \
    "$smoke_script" 2>&1)"; then
    status=0
  else
    status=$?
  fi
  trace="$(cat "$trace_file")"
  rm -f "$trace_file" "$state_file" "$state_file.dlt"
}

run_smoke
expected_trace='GET readiness
POST order-v1
GET fulfilled-v1
GET schema subjects'
if [ "$status" -ne 0 ] || [ "$trace" != "$expected_trace" ]; then
  echo "FAIL: public Phase 3 workflow did not follow the expected request trace." >&2
  failures=$((failures + 1))
else
  echo "PASS: public Phase 3 workflow reached fulfillment."
fi

AUTHENTICATION_ENABLED=true run_smoke
expected_auth_trace='GET readiness
POST token
GET userinfo authenticated
POST order-v2 authenticated
GET order-v2 authenticated
GET fulfilled-v1
GET schema subjects'
if [ "$status" -ne 0 ] || [ "$trace" != "$expected_auth_trace" ]; then
  echo "FAIL: authenticated Phase 3 workflow did not use the protected order API." >&2
  failures=$((failures + 1))
else
  echo "PASS: authenticated Phase 3 workflow used a bearer token."
fi
unset AUTHENTICATION_ENABLED

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 FAKE_PHASE3_NEVER_FULFILLED=true run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
case "$output" in
  *"The global smoke-test deadline was exceeded."*) deadline_message=true ;;
  *) deadline_message=false ;;
esac
if [ "$status" -eq 0 ] || [ "$elapsed_seconds" -gt 2 ] || [ "$deadline_message" != true ]; then
  echo "FAIL: fulfillment timeout was not bounded." >&2
  failures=$((failures + 1))
else
  echo "PASS: fulfillment timeout was bounded."
fi
unset SMOKE_TIMEOUT_SECONDS FAKE_PHASE3_NEVER_FULFILLED

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 FAKE_PHASE3_NO_KAFKA=true run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
if [ "$status" -eq 0 ] || [ "$elapsed_seconds" -gt 2 ]; then
  echo "FAIL: readiness without Kafka was accepted or unbounded." >&2
  failures=$((failures + 1))
else
  echo "PASS: readiness without Kafka was rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS FAKE_PHASE3_NO_KAFKA

FAKE_PHASE3_NO_SCHEMAS=true run_smoke
case "$output" in
  *"did not contain exactly the governed Version 1 record subjects"*) schema_message=true ;;
  *) schema_message=false ;;
esac
if [ "$status" -eq 0 ] || [ "$schema_message" != true ]; then
  echo "FAIL: incomplete Schema Registry subjects were accepted." >&2
  failures=$((failures + 1))
else
  echo "PASS: incomplete Schema Registry subjects were rejected."
fi
unset FAKE_PHASE3_NO_SCHEMAS

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 FAKE_PHASE3_NO_DLT=true run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
case "$output" in
  *"The global smoke-test deadline was exceeded."*) deadline_message=true ;;
  *) deadline_message=false ;;
esac
if [ "$status" -eq 0 ] || [ "$elapsed_seconds" -gt 2 ] || [ "$deadline_message" != true ]; then
  echo "FAIL: missing DLT routing was accepted or unbounded." >&2
  failures=$((failures + 1))
else
  echo "PASS: missing DLT routing was rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS FAKE_PHASE3_NO_DLT

if [ "$failures" -ne 0 ]; then
  echo "Phase 3 smoke behavior tests failed: ${failures} case(s)." >&2
  exit 1
fi

echo "Phase 3 smoke behavior tests passed."
