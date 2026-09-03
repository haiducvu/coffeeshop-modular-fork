#!/bin/sh

set -eu

api_url="${API_URL:-http://localhost:${API_PORT:-8080}}"
keycloak_url="${KEYCLOAK_URL:-http://localhost:${KEYCLOAK_PORT:-18080}}"
schema_registry_url="${SCHEMA_REGISTRY_URL:-http://localhost:${SCHEMA_REGISTRY_PORT:-8081}}"
dapr_http_url="${DAPR_HTTP_URL:-http://localhost:${DAPR_HTTP_PORT:-3500}}"
otel_metrics_url="${OTEL_METRICS_URL:-}"
jaeger_url="${JAEGER_URL:-}"
authentication_enabled="${AUTHENTICATION_ENABLED:-false}"
messaging_adapter="${MESSAGING_ADAPTER:-Kafka}"
timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-180}"
deadline=$(( $(date +%s) + timeout_seconds ))
timestamp_hex="$(printf '%08x' "$(date +%s)")"
process_hex="$(printf '%04x' "$(( $$ % 65536 ))")"
loyalty_member_id="11111111-2222-3333-4444-${timestamp_hex}${process_hex}"

run_diagnostic() {
  diagnostic_deadline=$(( $(date +%s) + 15 ))
  "$@" >&2 &
  diagnostic_pid=$!
  while kill -0 "$diagnostic_pid" 2>/dev/null; do
    if [ "$(date +%s)" -ge "$diagnostic_deadline" ]; then
      kill "$diagnostic_pid" 2>/dev/null || true
      wait "$diagnostic_pid" 2>/dev/null || true
      return
    fi
    sleep 0.1
  done
  wait "$diagnostic_pid" 2>/dev/null || true
}

fail() {
  echo "Phase 3 smoke test failed: $1" >&2
  run_diagnostic docker compose ps
  run_diagnostic docker compose logs --tail=150 \
    api kafka schema-registry dapr-sidecar postgres redis otel-collector jaeger
  exit 1
}

set_request_timeout() {
  maximum="$1"
  remaining=$(( deadline - $(date +%s) ))
  if [ "$remaining" -le 0 ]; then
    fail "The global smoke-test deadline was exceeded."
  fi
  request_timeout="$remaining"
  if [ "$request_timeout" -gt "$maximum" ]; then
    request_timeout="$maximum"
  fi
}

wait_before_retry() {
  remaining=$(( deadline - $(date +%s) ))
  if [ "$remaining" -le 0 ]; then
    fail "The global smoke-test deadline was exceeded."
  fi
  sleep 1
}

contains_text() {
  case "$1" in
    *"$2"*) return 0 ;;
    *) return 1 ;;
  esac
}

if ! command -v jq >/dev/null 2>&1; then
  fail "jq is required to validate JSON responses."
fi
case "$messaging_adapter" in
  Kafka|Dapr) ;;
  *) fail "MESSAGING_ADAPTER must be Kafka or Dapr." ;;
esac
if { [ -n "$otel_metrics_url" ] && [ -z "$jaeger_url" ]; } \
  || { [ -z "$otel_metrics_url" ] && [ -n "$jaeger_url" ]; }; then
  fail "OTEL_METRICS_URL and JAEGER_URL must be configured together."
fi

response_headers="$(mktemp)"
trap 'rm -f "$response_headers"' EXIT HUP INT TERM

echo "Waiting for the ${messaging_adapter} messaging path and data stores ..."
while :; do
  set_request_timeout 5
  readiness="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${api_url}/health/ready" 2>/dev/null || true)"
  if [ "$messaging_adapter" = Dapr ]; then
    if [ "$authentication_enabled" = true ]; then
      expected_checks='["dapr","identity-provider","postgresql","redis"]'
    else
      expected_checks='["dapr","postgresql","redis"]'
    fi
  elif [ "$authentication_enabled" = true ]; then
    expected_checks='["identity-provider","kafka","postgresql","redis","schema-registry"]'
  else
    expected_checks='["kafka","postgresql","redis","schema-registry"]'
  fi
  if printf '%s' "$readiness" | jq --exit-status \
      --argjson expected "$expected_checks" '
        .status == "Healthy"
        and ([.checks[].name] | sort) == $expected
        and all(.checks[]; .status == "Healthy")
      ' >/dev/null 2>&1; then
    break
  fi
  wait_before_retry
done

if [ "$messaging_adapter" = Dapr ]; then
  set_request_timeout 5
  dapr_metadata="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${dapr_http_url}/v1.0/metadata")" \
    || fail "Dapr metadata could not be loaded."
  if ! printf '%s' "$dapr_metadata" | jq --exit-status '
      .id == "coffeeshop-api"
      and any(.components[]?;
        .name == "coffeeshop-pubsub" and .type == "pubsub.kafka")
      and ([.subscriptions[]?.topic] | sort) == [
        "coffeeshop.orders.v1",
        "coffeeshop.preparation.v1"
      ]
    ' >/dev/null 2>&1; then
    fail "Dapr did not load the Kafka pub/sub component and both Version 1 subscriptions."
  fi
fi

if [ "$authentication_enabled" = true ]; then
  echo "Authenticating a customer and placing a protected mixed order ..."
  set_request_timeout 10
  token_response="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    --request POST \
    --data-urlencode 'client_id=coffeeshop-api' \
    --data-urlencode "username=${KEYCLOAK_CUSTOMER_USERNAME:-lesson18-customer}" \
    --data-urlencode "password=${KEYCLOAK_CUSTOMER_PASSWORD:-lesson18-customer-local}" \
    --data-urlencode 'scope=openid' \
    --data-urlencode 'grant_type=password' \
    "${keycloak_url}/realms/coffeeshop/protocol/openid-connect/token")" \
    || fail "The customer token request failed."
  access_token="$(printf '%s' "$token_response" | jq --raw-output '.access_token // empty')"
  if [ -z "$access_token" ]; then
    fail "The token response did not contain an access token."
  fi
  set_request_timeout 10
  userinfo_response="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    --header "Authorization: Bearer ${access_token}" \
    "${keycloak_url}/realms/coffeeshop/protocol/openid-connect/userinfo")" \
    || fail "The customer userinfo request failed."
  loyalty_member_id="$(printf '%s' "$userinfo_response" | jq --raw-output '.sub // empty')"
  if ! printf '%s' "$loyalty_member_id" | grep -Eq \
      '^[[:xdigit:]]{8}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{12}$'; then
    fail "The customer subject was not a loyalty-member GUID."
  fi
  set_request_timeout 10
  create_response="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    --request POST \
    --dump-header "$response_headers" \
    --header "Authorization: Bearer ${access_token}" \
    --header 'Content-Type: application/json' \
    --data "{\"orderSource\":0,\"location\":0,\"loyaltyMemberId\":\"${loyalty_member_id}\",\"baristaItems\":[5],\"kitchenItems\":[6]}" \
    "${api_url}/v2/orders")" || fail "The protected order request failed."
  order_id="$(printf '%s' "$create_response" | jq --raw-output '.orderId // empty')"
  if [ -z "$order_id" ]; then
    fail "The protected create response did not contain an order ID."
  fi

  while :; do
    set_request_timeout 5
    order_response="$(curl --fail --silent --show-error \
      --connect-timeout "$request_timeout" \
      --max-time "$request_timeout" \
      --header "Authorization: Bearer ${access_token}" \
      "${api_url}/v2/orders/${order_id}" 2>/dev/null || true)"
    if printf '%s' "$order_response" | jq --exit-status \
        '.status == "Fulfilled"' >/dev/null 2>&1; then
      break
    fi
    wait_before_retry
  done

  set_request_timeout 5
  fulfilled="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${api_url}/v1/api/fulfillment-orders")" \
    || fail "The fulfilled read model could not be loaded after protected fulfillment."
  if ! printf '%s' "$fulfilled" | jq --exit-status \
      --arg loyalty "$loyalty_member_id" '
        any(.[]; .loyaltyMemberId == $loyalty and .status == "Fulfilled")
      ' >/dev/null 2>&1; then
    fail "The protected order was missing from the fulfilled read model."
  fi
else
  echo "Placing a public mixed order ..."
  set_request_timeout 10
  curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    --request POST \
    --dump-header "$response_headers" \
    --header 'Content-Type: application/json' \
    --data "{\"commandType\":0,\"orderSource\":0,\"location\":0,\"loyaltyMemberId\":\"${loyalty_member_id}\",\"baristaItems\":[{\"itemType\":5}],\"kitchenItems\":[{\"itemType\":6}],\"timestamp\":\"2026-08-08T00:00:00Z\"}" \
    "${api_url}/v1/api/orders" >/dev/null \
    || fail "The public order request failed."

  while :; do
    set_request_timeout 5
    fulfilled="$(curl --fail --silent --show-error \
      --connect-timeout "$request_timeout" \
      --max-time "$request_timeout" \
      "${api_url}/v1/api/fulfillment-orders" 2>/dev/null || true)"
    if printf '%s' "$fulfilled" | jq --exit-status \
        --arg loyalty "$loyalty_member_id" '
          any(.[]; .loyaltyMemberId == $loyalty and .status == "Fulfilled")
        ' >/dev/null 2>&1; then
      break
    fi
    wait_before_retry
  done
fi

if [ "$messaging_adapter" = Kafka ]; then
  set_request_timeout 5
  schema_subjects="$(curl --fail --silent --show-error \
    --connect-timeout "$request_timeout" \
    --max-time "$request_timeout" \
    "${schema_registry_url}/subjects")" \
    || fail "Schema Registry subjects could not be loaded."
  if ! printf '%s' "$schema_subjects" | jq --exit-status '
      sort == [
        "CoffeeShop.Events.V1.OrderItemPreparedV1",
        "CoffeeShop.Events.V1.OrderPlacedV1"
      ]
    ' >/dev/null 2>&1; then
    fail "Schema Registry did not contain exactly the governed Version 1 record subjects."
  fi
fi

correlation_id="$(tr -d '\r' < "$response_headers" | awk -F ': *' '
  tolower($1) == "x-correlation-id" { value = $2 }
  END { print value }
')"
if ! printf '%s' "$correlation_id" | grep -Eq \
    '^[[:xdigit:]]{8}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{12}$'; then
  fail "The order response did not contain a canonical server correlation ID."
fi

if ! docker compose exec -T redis redis-cli --raw EXISTS fulfilled-orders:v1 \
  | grep -qx '1'; then
  fail "The fulfilled read model was not cached in Redis."
fi

effect_counts="$(docker compose exec -T postgres psql \
  -U "${POSTGRES_USER:-coffeeshop}" \
  -d "${POSTGRES_DB:-coffeeshop}" \
  -At -F '|' \
  -c '
    SELECT
      (SELECT COUNT(*) FROM barista.items),
      (SELECT COUNT(*) FROM kitchen.items),
      (SELECT COUNT(*) FROM barista.inbox_messages),
      (SELECT COUNT(*) FROM kitchen.inbox_messages),
      (SELECT COUNT(*) FROM counter.inbox_messages),
      (SELECT
        (SELECT COUNT(*) FROM counter.outbox_messages
         WHERE "PublishedAtUtc" IS NULL AND "RejectedAtUtc" IS NULL)
        + (SELECT COUNT(*) FROM barista.outbox_messages
           WHERE "PublishedAtUtc" IS NULL AND "RejectedAtUtc" IS NULL)
        + (SELECT COUNT(*) FROM kitchen.outbox_messages
           WHERE "PublishedAtUtc" IS NULL AND "RejectedAtUtc" IS NULL)),
      (SELECT
        (SELECT COUNT(*) FROM counter.outbox_messages WHERE "RejectedAtUtc" IS NOT NULL)
        + (SELECT COUNT(*) FROM barista.outbox_messages WHERE "RejectedAtUtc" IS NOT NULL)
        + (SELECT COUNT(*) FROM kitchen.outbox_messages WHERE "RejectedAtUtc" IS NOT NULL));
  ' | tr -d '\r')" || fail "Messaging persistence state could not be queried."
if [ "$effect_counts" != '1|1|1|1|2|0|0' ]; then
  fail "Inbox, station, pending Outbox, or rejected Outbox counts were unexpected."
fi

identity_state="$(docker compose exec -T postgres psql \
  -U "${POSTGRES_USER:-coffeeshop}" \
  -d "${POSTGRES_DB:-coffeeshop}" \
  -At -F '|' \
  -c '
    WITH root AS (
      SELECT "MessageId", "CorrelationId", "CausationId", "TraceParent", "TraceState"
      FROM counter.outbox_messages
    ), children AS (
      SELECT "CorrelationId", "CausationId", "TraceParent", "TraceState"
      FROM barista.outbox_messages
      UNION ALL
      SELECT "CorrelationId", "CausationId", "TraceParent", "TraceState"
      FROM kitchen.outbox_messages
    )
    SELECT root."CorrelationId",
           root."CausationId" IS NULL,
           root."TraceParent" IS NOT NULL,
           (SELECT COUNT(*)
            FROM children
            WHERE children."CorrelationId" = root."CorrelationId"
              AND children."CausationId" = root."MessageId"::text
              AND children."TraceParent" IS DISTINCT FROM root."TraceParent"
              AND substring(children."TraceParent" from 4 for 32)
                  = substring(root."TraceParent" from 4 for 32)
              AND children."TraceState" IS NOT DISTINCT FROM root."TraceState")
    FROM root;
  ' | tr -d '\r')" || fail "Workflow identity state could not be queried."
if [ "$identity_state" != "${correlation_id}|t|t|2" ]; then
  fail "Correlation, causation, or trace identity did not stay continuous."
fi

read_dead_letter_count() {
  dead_letter_offsets="$(docker compose exec -T kafka \
    /opt/kafka/bin/kafka-get-offsets.sh \
    --bootstrap-server localhost:19092 \
    --topic coffeeshop.orders.v1.dlt 2>/dev/null || true)"
  printf '%s\n' "$dead_letter_offsets" | awk -F: '
    NF == 3 { total += $3 }
    END { print total + 0 }
  '
}

if [ "$messaging_adapter" = Kafka ]; then
  dead_letter_count_before="$(read_dead_letter_count)"
  expected_dead_letter_count=$(( dead_letter_count_before + 2 ))
  echo "Sending one poison record and waiting for both station consumers to dead-letter it ..."
  if ! printf '%s\n' 'poison-order|{"broken":' | docker compose exec -T kafka \
      /opt/kafka/bin/kafka-console-producer.sh \
      --bootstrap-server localhost:19092 \
      --topic coffeeshop.orders.v1 \
      --property parse.key=true \
      --property 'key.separator=|' >/dev/null; then
    fail "The poison Kafka record could not be produced."
  fi

  while :; do
    dead_letter_count="$(read_dead_letter_count)"
    if [ "$dead_letter_count" -ge "$expected_dead_letter_count" ]; then
      break
    fi
    wait_before_retry
  done
fi

if [ -n "$otel_metrics_url" ]; then
  echo "Waiting for low-cardinality messaging metrics ..."
  while :; do
    set_request_timeout 5
    metrics="$(curl --fail --silent --show-error \
      --connect-timeout "$request_timeout" \
      --max-time "$request_timeout" \
      "$otel_metrics_url" 2>/dev/null || true)"
    if contains_text "$metrics" 'coffeeshop_messaging_publish_count' \
      && contains_text "$metrics" 'coffeeshop_messaging_consume_count' \
      && contains_text "$metrics" 'coffeeshop_messaging_outbox_publish_attempts' \
      && { [ "$messaging_adapter" = Dapr ] \
        || contains_text "$metrics" 'coffeeshop_messaging_deadletter_forwarded'; }; then
      break
    fi
    wait_before_retry
  done

  echo "Waiting for the distributed workflow trace in Jaeger ..."
  while :; do
    set_request_timeout 5
    services="$(curl --fail --silent --show-error \
      --connect-timeout "$request_timeout" \
      --max-time "$request_timeout" \
      "${jaeger_url}/api/services" 2>/dev/null || true)"
    if printf '%s' "$services" | jq --exit-status \
        '.data | index("coffeeshop-api") != null' >/dev/null 2>&1; then
      break
    fi
    wait_before_retry
  done
  while :; do
    set_request_timeout 5
    traces="$(curl --fail --silent --show-error \
      --connect-timeout "$request_timeout" \
      --max-time "$request_timeout" \
      "${jaeger_url}/api/traces?service=coffeeshop-api&limit=20&lookback=1h" \
      2>/dev/null || true)"
    if printf '%s' "$traces" | jq --exit-status '
        any(.data[]?;
          [.spans[]?.operationName] as $operations
          | ($operations | index("coffeeshop.order-placed publish") != null)
            and ($operations | index("coffeeshop.order-placed process") != null))
      ' >/dev/null 2>&1; then
      break
    fi
    wait_before_retry
  done
fi

if [ "$messaging_adapter" = Dapr ]; then
  echo "Phase 3 smoke test passed: Dapr drove the complete Kafka-backed workflow with continuous identity."
else
  echo "Phase 3 smoke test passed: workflow trace stayed continuous, poison input reached DLT, and configured telemetry was observable."
fi
