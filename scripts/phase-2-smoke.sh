#!/bin/sh

set -eu

api_url="${API_URL:-http://localhost:${API_PORT:-8080}}"
timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-90}"
deadline=$(( $(date +%s) + timeout_seconds ))
log_file="$(mktemp)"
trap 'rm -f "$log_file"' EXIT HUP INT TERM

fail() {
  echo "Phase 2 operational smoke test failed: $1" >&2
  docker compose ps >&2 || true
  docker compose logs --tail=100 api postgres redis >&2 || true
  exit 1
}

remaining_timeout() {
  remaining_seconds=$(( deadline - $(date +%s) ))
  if [ "$remaining_seconds" -le 0 ]; then
    fail "The global smoke-test deadline was exceeded."
  fi
  request_timeout="$remaining_seconds"
  if [ "$request_timeout" -gt 5 ]; then
    request_timeout=5
  fi
}

if ! command -v jq >/dev/null 2>&1; then
  fail "jq is required to validate health responses and JSON logs."
fi

echo "Waiting for operational readiness at ${api_url}/health/ready ..."
while :; do
  remaining_timeout
  if ready_response="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${api_url}/health/ready" 2>/dev/null)"; then
    break
  fi
  sleep 1
done

remaining_timeout
live_response="$(curl --fail --silent --show-error \
  --connect-timeout "$request_timeout" \
  --max-time "$request_timeout" \
  "${api_url}/health/live")" || fail "Liveness endpoint was unavailable."

printf '%s' "$live_response" | jq --exit-status '
  .status == "Healthy" and .checks == []
' >/dev/null || fail "Liveness did not report process-only health."

printf '%s' "$ready_response" | jq --exit-status '
  .status == "Healthy"
  and ([.checks[].name] | sort) == ["kafka", "postgresql", "redis"]
  and all(.checks[]; .status == "Healthy" and (.durationMilliseconds | type) == "number")
' >/dev/null || fail "Readiness did not report healthy PostgreSQL and Redis checks."

docker compose logs --no-color --no-log-prefix api >"$log_file" \
  || fail "API logs could not be captured."
if ! jq --slurp --exit-status '
  any(.[].Properties?;
    .RequestPath == "/health/ready"
    and .StatusCode == 200
    and (.TraceId | type) == "string"
    and (.TraceId | length) > 0)
  and all(.[];
    (.Timestamp | type) == "string"
    and (.Level | type) == "string"
    and (.RenderedMessage | type) == "string")
' "$log_file" >/dev/null; then
  fail "API logs were not newline-delimited JSON with request correlation fields."
fi

if grep -Eqi 'Authorization|access_token|Password=|coffeeshop-local' "$log_file"; then
  fail "API logs contained a sensitive field or local credential."
fi

echo "Phase 2 operational smoke test passed: health semantics and structured logs are observable."
