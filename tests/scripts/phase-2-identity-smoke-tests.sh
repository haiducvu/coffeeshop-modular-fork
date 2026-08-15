#!/bin/sh

set -u

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"
fake_path="${script_directory}/fakes"
smoke_script="${repository_root}/scripts/phase-2-identity-smoke.sh"
failures=0

run_smoke() {
  response="$1"
  trace_file="${2:-}"
  if [ "$response" = "__dynamic__" ]; then
    response=""
  fi
  if output="$(PATH="${fake_path}:$PATH" \
    FAKE_IDENTITY_RESPONSE="$response" \
    FAKE_CURL_TRACE="$trace_file" \
    API_URL="http://api.test" \
    KEYCLOAK_URL="http://identity.test" \
    SMOKE_TIMEOUT_SECONDS=5 \
    "$smoke_script" 2>&1)"; then
    status=0
  else
    status=$?
  fi
}

assert_trace_matrix() {
  trace_file="$(mktemp)"
  trap 'rm -f "$trace_file"' EXIT HUP INT TERM
  run_smoke '__dynamic__' "$trace_file"

  expected_trace='GET discovery anonymous
GET readiness anonymous
POST token customer
GET authentication customer
POST orders customer
GET order customer
POST token fulfillment-reader
GET authentication fulfillment-reader
GET fulfillment-orders fulfillment-reader
POST token operator
GET authentication operator
GET order operator
GET operations-order operator'
  actual_trace="$(cat "$trace_file")"
  rm -f "$trace_file"
  trap - EXIT HUP INT TERM

  if [ "$status" -ne 0 ] || [ "$actual_trace" != "$expected_trace" ]; then
    echo "FAIL: authorization request trace did not match the role-route-method matrix." >&2
    return 1
  fi

  echo "PASS: authorization request trace matched the role-route-method matrix."
}

assert_fake_rejects_invalid_matrix_call() {
  name="$1"
  method="$2"
  path="$3"
  if output="$(PATH="${fake_path}:$PATH" \
    curl --silent --show-error --request "$method" \
    --header 'Authorization: Bearer controlled-lesson18-customer' \
    "http://api.test${path}" 2>&1)"; then
    echo "FAIL: ${name} invalid authorization matrix call was accepted." >&2
    return 1
  fi
  case "$output" in
    *"Fake authorization matrix rejected request."*) ;;
    *)
      echo "FAIL: ${name} invalid call failed for the wrong reason." >&2
      return 1
      ;;
  esac
  case "$output" in
    *controlled-lesson18-customer*)
      echo "FAIL: ${name} invalid call leaked its bearer value." >&2
      return 1
      ;;
  esac
  echo "PASS: ${name} invalid authorization matrix call was rejected safely."
}

assert_deadline_covers_failure_diagnostics() {
  started_at="$(date +%s)"
  if output="$(PATH="${fake_path}:$PATH" \
    FAKE_CURL_FAIL_DISCOVERY=true \
    FAKE_DOCKER_SLEEP_SECONDS=3 \
    API_URL="http://api.test" \
    KEYCLOAK_URL="http://identity.test" \
    SMOKE_TIMEOUT_SECONDS=1 \
    "$smoke_script" 2>&1)"; then
    status=0
  else
    status=$?
  fi
  elapsed_seconds=$(( $(date +%s) - started_at ))

  if [ "$status" -eq 0 ] || [ "$elapsed_seconds" -gt 2 ]; then
    echo "FAIL: deadline did not bound failure diagnostics (${elapsed_seconds}s)." >&2
    return 1
  fi
  case "$output" in
    *"The global smoke-test deadline was exceeded."*) ;;
    *)
      echo "FAIL: deadline failure had the wrong outcome." >&2
      return 1
      ;;
  esac
  echo "PASS: deadline bounded failure diagnostics in ${elapsed_seconds}s."
}

assert_rejected() {
  name="$1"
  response="$2"
  run_smoke "$response"

  if [ "$status" -eq 0 ]; then
    echo "FAIL: ${name} subject was accepted." >&2
    return 1
  fi
  case "$output" in
    *"The diagnostic endpoint did not return a meaningful subject and expected role."*) ;;
    *)
      echo "FAIL: ${name} subject failed for the wrong reason." >&2
      return 1
      ;;
  esac
  case "$output" in
    *"$response"*)
      echo "FAIL: ${name} response was printed by the smoke script." >&2
      return 1
      ;;
  esac

  echo "PASS: ${name} subject was rejected without printing the response."
}

assert_accepted() {
  response="$1"
  run_smoke "$response"

  if [ "$status" -ne 0 ]; then
    echo "FAIL: meaningful subject was rejected." >&2
    return 1
  fi
  case "$output" in
    *"Phase 2 identity smoke test passed"*) ;;
    *)
      echo "FAIL: meaningful subject did not reach the success outcome." >&2
      return 1
      ;;
  esac

  echo "PASS: meaningful subject was accepted."
}

assert_rejected "missing" '{"roles":["customer"]}' || failures=$((failures + 1))
assert_rejected "non-string" '{"subject":17,"roles":["customer"]}' || failures=$((failures + 1))
assert_rejected "empty" '{"subject":"","roles":["customer"]}' || failures=$((failures + 1))
assert_rejected "whitespace-only" '{"subject":" \t\n","roles":["customer"]}' || failures=$((failures + 1))
assert_accepted '__dynamic__' || failures=$((failures + 1))
assert_trace_matrix || failures=$((failures + 1))
assert_fake_rejects_invalid_matrix_call "customer fulfillment POST" POST "/v2/fulfillment-orders" || failures=$((failures + 1))
assert_fake_rejects_invalid_matrix_call "customer operations GET" GET "/v2/operations/orders/7a97d7bf-f7e3-4294-9d5c-b95b957342c5" || failures=$((failures + 1))
assert_deadline_covers_failure_diagnostics || failures=$((failures + 1))

run_smoke '__dynamic__'
if [ "$status" -eq 0 ] && case "$output" in *"customer, fulfillment-reader, and operator"*) true;; *) false;; esac; then
  echo "PASS: smoke proves all authorization roles."
else
  echo "FAIL: smoke did not prove customer, fulfillment-reader, and operator." >&2
  failures=$((failures + 1))
fi

if [ "$failures" -ne 0 ]; then
  echo "Identity smoke behavior tests failed: ${failures} case(s)." >&2
  exit 1
fi

echo "Identity smoke behavior tests passed."
