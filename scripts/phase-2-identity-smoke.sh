#!/bin/sh

set -eu

api_url="${API_URL:-http://localhost:${API_PORT:-8080}}"
keycloak_url="${KEYCLOAK_URL:-http://localhost:${KEYCLOAK_PORT:-18080}}"
realm="coffeeshop"
client_id="coffeeshop-api"
username="${KEYCLOAK_TEST_USERNAME:-lesson17-user}"
password="${KEYCLOAK_TEST_PASSWORD:-lesson17-local}"
timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-120}"
deadline=$(( $(date +%s) + timeout_seconds ))

diagnose_and_exit() {
  echo "Phase 2 identity smoke test failed: $1" >&2
  docker compose --profile identity ps >&2 || true
  docker compose --profile identity logs --tail=150 keycloak api postgres >&2 || true
  exit 1
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

echo "Requesting a local development access token ..."
set_request_timeout 10
token_response="$(curl --fail --silent --show-error \
  --connect-timeout "$request_timeout" \
  --max-time "$request_timeout" \
  --data-urlencode "client_id=${client_id}" \
  --data-urlencode "username=${username}" \
  --data-urlencode "password=${password}" \
  --data-urlencode "grant_type=password" \
  "${keycloak_url}${token_path}")" \
  || diagnose_and_exit "The Keycloak token request failed."

access_token="$(printf '%s' "$token_response" \
  | sed -n 's/.*"access_token":"\([^"]*\)".*/\1/p')"
if [ -z "$access_token" ]; then
  diagnose_and_exit "The token response did not contain access_token."
fi

echo "Calling the authenticated diagnostic endpoint ..."
set_request_timeout 10
identity_response="$(curl --fail --silent --show-error \
  --connect-timeout "$request_timeout" \
  --max-time "$request_timeout" \
  --header "Authorization: Bearer ${access_token}" \
  "${api_url}/v2/authentication")" \
  || diagnose_and_exit "The API rejected the Keycloak access token."

if ! printf '%s' "$identity_response" \
  | jq --exit-status '
      (.subject? | select(type == "string")) as $subject
      | ($subject | test("\\S"))
    ' >/dev/null; then
  diagnose_and_exit "The diagnostic endpoint did not return an authenticated subject."
fi

echo "Phase 2 identity smoke test passed: Keycloak token authenticated the API call."
