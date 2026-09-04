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
    MESSAGING_ADAPTER="${MESSAGING_ADAPTER:-Kafka}" \
    FAKE_PHASE3_NEVER_FULFILLED="${FAKE_PHASE3_NEVER_FULFILLED:-false}" \
    FAKE_PHASE3_NO_KAFKA="${FAKE_PHASE3_NO_KAFKA:-false}" \
    FAKE_PHASE3_NO_DAPR="${FAKE_PHASE3_NO_DAPR:-false}" \
    FAKE_PHASE3_NO_DLT="${FAKE_PHASE3_NO_DLT:-false}" \
    FAKE_PHASE3_NO_SCHEMAS="${FAKE_PHASE3_NO_SCHEMAS:-false}" \
    FAKE_PHASE3_NO_TELEMETRY="${FAKE_PHASE3_NO_TELEMETRY:-false}" \
    FAKE_PHASE3_NO_BARISTA_TELEMETRY="${FAKE_PHASE3_NO_BARISTA_TELEMETRY:-false}" \
    FAKE_PHASE3_EXISTING_EFFECTS="${FAKE_PHASE3_EXISTING_EFFECTS:-false}" \
    FAKE_PHASE3_EXISTING_IDENTITIES="${FAKE_PHASE3_EXISTING_IDENTITIES:-false}" \
    EXPECT_BARISTA_WORKER_TELEMETRY="${EXPECT_BARISTA_WORKER_TELEMETRY:-false}" \
    OTEL_METRICS_URL="${OTEL_METRICS_URL:-}" \
    JAEGER_URL="${JAEGER_URL:-}" \
    "$smoke_script" 2>&1)"; then
    status=0
  else
    status=$?
  fi
  trace="$(cat "$trace_file")"
  rm -f "$trace_file" "$state_file" "$state_file.dlt" "$state_file.effects"
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

FAKE_PHASE3_EXISTING_EFFECTS=true run_smoke
if [ "$status" -ne 0 ]; then
  echo "FAIL: pre-existing workflow effects made the Phase 3 smoke fail." >&2
  failures=$((failures + 1))
else
  echo "PASS: Phase 3 smoke isolated the workflow effects it created."
fi
unset FAKE_PHASE3_EXISTING_EFFECTS

FAKE_PHASE3_EXISTING_IDENTITIES=true run_smoke
if [ "$status" -ne 0 ]; then
  echo "FAIL: pre-existing workflow identities made the Phase 3 smoke fail." >&2
  failures=$((failures + 1))
else
  echo "PASS: Phase 3 smoke selected the identity of the workflow it created."
fi
unset FAKE_PHASE3_EXISTING_IDENTITIES

MESSAGING_ADAPTER=Dapr run_smoke
expected_dapr_trace='GET readiness
GET dapr metadata
POST order-v1
GET fulfilled-v1'
if [ "$status" -ne 0 ] || [ "$trace" != "$expected_dapr_trace" ]; then
  echo "FAIL: Dapr Phase 3 workflow did not use sidecar discovery and fulfillment." >&2
  failures=$((failures + 1))
else
  echo "PASS: Dapr Phase 3 workflow reached fulfillment through the sidecar."
fi
unset MESSAGING_ADAPTER

OTEL_METRICS_URL=http://collector.test/metrics \
JAEGER_URL=http://jaeger.test \
run_smoke
expected_observability_trace='GET readiness
POST order-v1
GET fulfilled-v1
GET schema subjects
GET telemetry metrics
GET jaeger services
GET jaeger traces'
if [ "$status" -ne 0 ] || [ "$trace" != "$expected_observability_trace" ]; then
  echo "FAIL: observability smoke did not prove exported metrics and traces." >&2
  failures=$((failures + 1))
else
  echo "PASS: observability smoke proved exported metrics and traces."
fi
unset OTEL_METRICS_URL JAEGER_URL

EXPECT_BARISTA_WORKER_TELEMETRY=true \
OTEL_METRICS_URL=http://collector.test/metrics \
JAEGER_URL=http://jaeger.test \
run_smoke
if [ "$status" -ne 0 ]; then
  echo "FAIL: exported Barista Worker telemetry was not accepted." >&2
  failures=$((failures + 1))
else
  echo "PASS: exported Barista Worker telemetry was accepted."
fi
unset EXPECT_BARISTA_WORKER_TELEMETRY OTEL_METRICS_URL JAEGER_URL

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 \
EXPECT_BARISTA_WORKER_TELEMETRY=true \
FAKE_PHASE3_NO_BARISTA_TELEMETRY=true \
OTEL_METRICS_URL=http://collector.test/metrics \
JAEGER_URL=http://jaeger.test \
run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
if [ "$status" -eq 0 ] || [ "$elapsed_seconds" -gt 2 ]; then
  echo "FAIL: missing Barista Worker telemetry was accepted or unbounded." >&2
  failures=$((failures + 1))
else
  echo "PASS: missing Barista Worker telemetry was rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS EXPECT_BARISTA_WORKER_TELEMETRY \
  FAKE_PHASE3_NO_BARISTA_TELEMETRY OTEL_METRICS_URL JAEGER_URL

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 \
FAKE_PHASE3_NO_TELEMETRY=true \
OTEL_METRICS_URL=http://collector.test/metrics \
JAEGER_URL=http://jaeger.test \
run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
if [ "$status" -eq 0 ] || [ "$elapsed_seconds" -gt 2 ]; then
  echo "FAIL: missing exported telemetry was accepted or unbounded." >&2
  failures=$((failures + 1))
else
  echo "PASS: missing exported telemetry was rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS FAKE_PHASE3_NO_TELEMETRY OTEL_METRICS_URL JAEGER_URL

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

started_at="$(date +%s)"
SMOKE_TIMEOUT_SECONDS=1 \
MESSAGING_ADAPTER=Dapr \
FAKE_PHASE3_NO_DAPR=true \
run_smoke
elapsed_seconds=$(( $(date +%s) - started_at ))
if [ "$status" -eq 0 ] || [ "$elapsed_seconds" -gt 2 ]; then
  echo "FAIL: readiness without Dapr was accepted or unbounded." >&2
  failures=$((failures + 1))
else
  echo "PASS: readiness without Dapr was rejected within the deadline."
fi
unset SMOKE_TIMEOUT_SECONDS MESSAGING_ADAPTER FAKE_PHASE3_NO_DAPR

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
