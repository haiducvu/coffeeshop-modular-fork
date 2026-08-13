#!/bin/sh

set -eu

api_url="${API_URL:-http://localhost:${API_PORT:-8080}}"
client_url="${CLIENT_URL:-http://localhost:${CLIENT_PORT:-5173}}"
timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-90}"
deadline=$(( $(date +%s) + timeout_seconds ))
timestamp_hex="$(printf '%08x' "$(date +%s)")"
process_hex="$(printf '%04x' "$(( $$ % 65536 ))")"
loyalty_member_id="11111111-2222-3333-4444-${timestamp_hex}${process_hex}"

diagnose_and_exit() {
  echo "Phase 1 smoke test failed: $1" >&2
  run_diagnostic docker compose ps
  run_diagnostic docker compose logs --tail=100 postgres redis api signalr-client
  exit 1
}

run_diagnostic() {
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

echo "Waiting for API readiness at ${api_url}/health/ready ..."
while :; do
  set_request_timeout 5
  if curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${api_url}/health/ready" >/dev/null 2>&1; then
    break
  fi
  wait_before_retry
done

echo "Waiting for the browser client at ${client_url} ..."
while :; do
  set_request_timeout 5
  if curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "$client_url" >/dev/null 2>&1; then
    break
  fi
  wait_before_retry
done

echo "Verifying SignalR negotiation ..."
set_request_timeout 5
negotiate_response="$(curl --fail --silent --show-error \
  --connect-timeout "$request_timeout" \
  --max-time "$request_timeout" \
  --dump-header - \
  --request POST \
  --header "Origin: ${client_url}" \
  "${api_url}/message/negotiate?negotiateVersion=1")" \
  || diagnose_and_exit "SignalR negotiation failed."
normalized_negotiate_response="$(printf '%s' "$negotiate_response" | tr -d '\r')"
if ! printf '%s' "$normalized_negotiate_response" | grep -q '"connectionId"'; then
  diagnose_and_exit "SignalR negotiation returned an unexpected response."
fi
if ! printf '%s' "$normalized_negotiate_response" \
  | grep -Fqi "Access-Control-Allow-Origin: ${client_url}"; then
  diagnose_and_exit "SignalR CORS did not allow the browser client origin."
fi
if ! printf '%s' "$normalized_negotiate_response" \
  | grep -Fqi "Access-Control-Allow-Credentials: true"; then
  diagnose_and_exit "SignalR CORS did not allow browser credentials."
fi

echo "Placing a deterministic order ..."
set_request_timeout "$timeout_seconds"
if ! curl --fail --silent --show-error \
  --connect-timeout "$request_timeout" \
  --max-time "$request_timeout" \
  --header "Content-Type: application/json" \
  --data "{\"commandType\":0,\"orderSource\":0,\"location\":0,\"loyaltyMemberId\":\"${loyalty_member_id}\",\"baristaItems\":[{\"itemType\":5}],\"kitchenItems\":[{\"itemType\":6}],\"timestamp\":\"2026-08-08T00:00:00Z\"}" \
  "${api_url}/v1/api/orders" >/dev/null; then
  diagnose_and_exit "The place-order request failed."
fi

echo "Polling fulfilled orders ..."
while :; do
  set_request_timeout 5
  fulfilled_orders="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${api_url}/v1/api/fulfillment-orders" 2>/dev/null || true)"

  if printf '%s' "$fulfilled_orders" | grep -q "$loyalty_member_id" \
    && printf '%s' "$fulfilled_orders" | grep -q '"status":"Fulfilled"'; then
    if ! docker compose exec -T redis redis-cli --raw EXISTS fulfilled-orders:v1 \
      | grep -qx '1'; then
      diagnose_and_exit "The Redis fulfillment cache did not contain the read model."
    fi
    echo "Phase 1 smoke test passed: deterministic order was fulfilled."
    exit 0
  fi

  wait_before_retry
done
