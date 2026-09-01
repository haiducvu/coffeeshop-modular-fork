#!/bin/sh

set -eu

api_url="${API_URL:-http://localhost:${API_PORT:-8080}}"
keycloak_url="${KEYCLOAK_URL:-http://localhost:${KEYCLOAK_PORT:-18080}}"
realm="coffeeshop"
client_id="coffeeshop-api"
customer_username="${KEYCLOAK_CUSTOMER_USERNAME:-lesson18-customer}"
customer_password="${KEYCLOAK_CUSTOMER_PASSWORD:-lesson18-customer-local}"
fulfillment_reader_username="${KEYCLOAK_FULFILLMENT_READER_USERNAME:-lesson18-fulfillment-reader}"
fulfillment_reader_password="${KEYCLOAK_FULFILLMENT_READER_PASSWORD:-lesson18-fulfillment-reader-local}"
operator_username="${KEYCLOAK_OPERATOR_USERNAME:-lesson18-operator}"
operator_password="${KEYCLOAK_OPERATOR_PASSWORD:-lesson18-operator-local}"
customer_loyalty_member_id="3fa85f64-5717-4562-b3fc-2c963f66afa6"
timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-120}"
deadline=$(( $(date +%s) + timeout_seconds ))

diagnose_and_exit() {
  echo "Phase 2 identity smoke test failed: $1" >&2
  run_diagnostic docker compose --profile identity ps
  run_diagnostic docker compose --profile identity logs --tail=150 keycloak api postgres
  exit 1
}

run_diagnostic() {
  diagnostic_remaining_seconds=$(( deadline - $(date +%s) ))
  if [ "$diagnostic_remaining_seconds" -le 0 ]; then
    return
  fi

  "$@" >&2 &
  diagnostic_pid=$!
  while kill -0 "$diagnostic_pid" 2>/dev/null; do
    diagnostic_remaining_seconds=$(( deadline - $(date +%s) ))
    if [ "$diagnostic_remaining_seconds" -le 0 ]; then
      kill "$diagnostic_pid" 2>/dev/null || true
      wait "$diagnostic_pid" 2>/dev/null || true
      return
    fi
    sleep 1
  done
  wait "$diagnostic_pid" 2>/dev/null || true
}

if ! command -v jq >/dev/null 2>&1; then
  diagnose_and_exit "jq is required to validate identity responses."
fi

set_request_timeout() {
  maximum_seconds="$1"
  remaining_seconds=$(( deadline - $(date +%s) ))
  if [ "$remaining_seconds" -le 0 ]; then
    diagnose_and_exit "The global smoke-test deadline was exceeded."
  fi

  request_timeout="$remaining_seconds"
  if [ "$request_timeout" -gt "$maximum_seconds" ]; then
    request_timeout="$maximum_seconds"
  fi
}

wait_before_retry() {
  remaining_seconds=$(( deadline - $(date +%s) ))
  if [ "$remaining_seconds" -le 0 ]; then
    diagnose_and_exit "The global smoke-test deadline was exceeded."
  fi
  retry_delay=2
  if [ "$remaining_seconds" -lt "$retry_delay" ]; then
    retry_delay="$remaining_seconds"
  fi
  sleep "$retry_delay"
}

discovery_url="${keycloak_url}/realms/${realm}/.well-known/openid-configuration"
echo "Waiting for Keycloak discovery at ${discovery_url} ..."
while :; do
  set_request_timeout 5
  if discovery="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "$discovery_url" 2>/dev/null)"; then
    break
  fi
  wait_before_retry
done

token_path="/realms/${realm}/protocol/openid-connect/token"
if ! printf '%s' "$discovery" | grep -Fq "$token_path"; then
  diagnose_and_exit "Discovery did not advertise the expected token endpoint."
fi

echo "Waiting for API readiness with identity discovery enabled ..."
while :; do
  set_request_timeout 5
  if readiness="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${api_url}/health/ready" 2>/dev/null)" \
    && printf '%s' "$readiness" | jq --exit-status '
      .status == "Healthy"
      and ([.checks[].name] | sort) == ["identity-provider", "kafka", "postgresql", "redis", "schema-registry"]
      and all(.checks[]; .status == "Healthy")
    ' >/dev/null; then
    break
  fi
  wait_before_retry
done

request_access_token() {
  token_username="$1"
  token_password="$2"
  set_request_timeout 10
  token_response="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    --data-urlencode "client_id=${client_id}" \
    --data-urlencode "username=${token_username}" \
    --data-urlencode "password=${token_password}" \
    --data-urlencode "grant_type=password" \
    "${keycloak_url}${token_path}")" \
    || diagnose_and_exit "A Keycloak token request failed."

  access_token="$(printf '%s' "$token_response" | jq --raw-output '.access_token // empty')"
  if [ -z "$access_token" ]; then
    diagnose_and_exit "A token response did not contain access_token."
  fi
  printf '%s' "$access_token"
}

require_identity_role() {
  access_token="$1"
  expected_role="$2"
  set_request_timeout 10
  identity_response="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    --header "Authorization: Bearer ${access_token}" \
    "${api_url}/v2/authentication")" \
    || diagnose_and_exit "The API rejected a Keycloak access token."

  if ! printf '%s' "$identity_response" | jq --exit-status --arg role "$expected_role" '
      (.subject? | select(type == "string") | test("\\S"))
      and ([.roles[]? | select(type == "string")] | index($role) != null)
    ' >/dev/null; then
    diagnose_and_exit "The diagnostic endpoint did not return a meaningful subject and expected role."
  fi
}

call_api() {
  access_token="$1"
  method="$2"
  path="$3"
  body="${4:-}"
  set_request_timeout 10
  if [ -n "$body" ]; then
    curl --fail --silent --show-error \
      --connect-timeout "$request_timeout" \
      --max-time "$request_timeout" \
      --request "$method" \
      --header "Authorization: Bearer ${access_token}" \
      --header "Content-Type: application/json" \
      --data "$body" \
      "${api_url}${path}"
  else
    curl --fail --silent --show-error \
      --connect-timeout "$request_timeout" \
      --max-time "$request_timeout" \
      --request "$method" \
      --header "Authorization: Bearer ${access_token}" \
      "${api_url}${path}"
  fi
}

echo "Requesting and validating customer identity ..."
customer_token="$(request_access_token "$customer_username" "$customer_password")"
require_identity_role "$customer_token" "customer"
customer_order_response="$(call_api "$customer_token" POST "/v2/orders" "{\"orderSource\":0,\"location\":0,\"loyaltyMemberId\":\"${customer_loyalty_member_id}\",\"baristaItems\":[],\"kitchenItems\":[6]}")" \
  || diagnose_and_exit "The customer could not create an order."
order_id="$(printf '%s' "$customer_order_response" | jq --raw-output '.orderId // empty')"
if ! printf '%s' "$order_id" | grep -Eq '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'; then
  diagnose_and_exit "The customer create response did not contain an order ID."
fi
call_api "$customer_token" GET "/v2/orders/${order_id}" >/dev/null \
  || diagnose_and_exit "The customer could not read their own order."

echo "Requesting and validating fulfillment-reader identity ..."
fulfillment_reader_token="$(request_access_token "$fulfillment_reader_username" "$fulfillment_reader_password")"
require_identity_role "$fulfillment_reader_token" "fulfillment-reader"
call_api "$fulfillment_reader_token" GET "/v2/fulfillment-orders" >/dev/null \
  || diagnose_and_exit "The fulfillment reader could not read the queue."

echo "Requesting and validating operator identity ..."
operator_token="$(request_access_token "$operator_username" "$operator_password")"
require_identity_role "$operator_token" "operator"
call_api "$operator_token" GET "/v2/orders/${order_id}" >/dev/null \
  || diagnose_and_exit "The operator could not override order ownership."
call_api "$operator_token" GET "/v2/operations/orders/${order_id}" >/dev/null \
  || diagnose_and_exit "The operator could not read the operational order route."

echo "Phase 2 identity smoke test passed: customer, fulfillment-reader, and operator authorization flows succeeded."
