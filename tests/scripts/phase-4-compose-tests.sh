#!/bin/sh

set -eu

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"

compose_json="$(
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
' >/dev/null

echo "Phase 4 worker Compose configuration tests passed."
