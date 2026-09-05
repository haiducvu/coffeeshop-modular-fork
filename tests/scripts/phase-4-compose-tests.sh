#!/bin/sh

set -eu

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"

compose_json="$(
  COFFEESHOP_CONNECTION_STRING= \
  COUNTER_DB_PASSWORD=counter-compose-test \
  BARISTA_DB_PASSWORD=barista-compose-test \
  KITCHEN_DB_PASSWORD=kitchen-compose-test \
  BARISTA_OUTBOX_BATCH_SIZE=31 \
  BARISTA_OUTBOX_POLL_INTERVAL=00:00:00.031 \
  BARISTA_OUTBOX_LEASE_DURATION=00:00:31 \
  BARISTA_OUTBOX_RETRY_DELAY=00:00:03 \
  KITCHEN_OUTBOX_BATCH_SIZE=32 \
  KITCHEN_OUTBOX_POLL_INTERVAL=00:00:00.032 \
  KITCHEN_OUTBOX_LEASE_DURATION=00:00:32 \
  KITCHEN_OUTBOX_RETRY_DELAY=00:00:04 \
  KAFKA_RETRY_FIRST_DELAY=00:00:02 \
  KAFKA_RETRY_SECOND_DELAY=00:00:06 \
  KAFKA_MAX_POLL_INTERVAL=00:06:00 \
    docker compose -f "${repository_root}/compose.yaml" config --format json
)"

printf '%s' "$compose_json" | jq -e '
  .services.api.environment as $api
  | .services["barista-worker"].environment as $barista
  | .services["kitchen-worker"].environment as $kitchen
  | ($api["Messaging__Kafka__Retry__FirstDelay"] == "00:00:02")
    and ($api["Messaging__BaristaOutbox__BatchSize"] == "31")
    and ($api["Messaging__KitchenOutbox__BatchSize"] == "32")
    and ($barista["Messaging__Kafka__Retry__FirstDelay"] == "00:00:02")
    and ($barista["Messaging__Kafka__Retry__SecondDelay"] == "00:00:06")
    and ($barista["Messaging__Kafka__Retry__MaxPollInterval"] == "00:06:00")
    and ($barista["Messaging__BaristaOutbox__BatchSize"] == "31")
    and ($barista["Messaging__BaristaOutbox__PollInterval"] == "00:00:00.031")
    and ($barista["Messaging__BaristaOutbox__LeaseDuration"] == "00:00:31")
    and ($barista["Messaging__BaristaOutbox__RetryDelay"] == "00:00:03")
    and ($kitchen["Messaging__Kafka__Retry__FirstDelay"] == "00:00:02")
    and ($kitchen["Messaging__Kafka__Retry__SecondDelay"] == "00:00:06")
    and ($kitchen["Messaging__Kafka__Retry__MaxPollInterval"] == "00:06:00")
    and ($kitchen["Messaging__KitchenOutbox__BatchSize"] == "32")
    and ($kitchen["Messaging__KitchenOutbox__PollInterval"] == "00:00:00.032")
    and ($kitchen["Messaging__KitchenOutbox__LeaseDuration"] == "00:00:32")
    and ($kitchen["Messaging__KitchenOutbox__RetryDelay"] == "00:00:04")
    and ($api["ConnectionStrings__CoffeeShop"] == "Host=postgres;Port=5432;Database=coffeeshop_counter;Username=coffeeshop_counter;Password=counter-compose-test")
    and ($barista["ConnectionStrings__Barista"] == "Host=postgres;Port=5432;Database=coffeeshop_barista;Username=coffeeshop_barista;Password=barista-compose-test")
    and ($kitchen["ConnectionStrings__Kitchen"] == "Host=postgres;Port=5432;Database=coffeeshop_kitchen;Username=coffeeshop_kitchen;Password=kitchen-compose-test")
    and ($api | has("ConnectionStrings__Barista") or has("ConnectionStrings__Kitchen") | not)
    and ($barista | has("ConnectionStrings__CoffeeShop") or has("ConnectionStrings__Kitchen") | not)
    and ($kitchen | has("ConnectionStrings__CoffeeShop") or has("ConnectionStrings__Barista") | not)
' >/dev/null

docker compose -f "${repository_root}/compose.yaml" -f "${repository_root}/compose.dapr.yaml" \
  config --format json | jq -e '
    .services.api.environment
    | .Messaging__Adapter == "Dapr"
      and .Modules__Barista__Hosting == "Embedded"
      and .Modules__Kitchen__Hosting == "Embedded"
      and (.ConnectionStrings__CoffeeShop | contains("Database=coffeeshop_counter;") | not)
  ' >/dev/null

echo "Phase 4 worker Compose configuration tests passed."
