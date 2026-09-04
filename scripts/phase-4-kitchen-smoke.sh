#!/bin/sh

set -eu

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"

if ! docker compose ps --status running --services \
  | grep -qx 'barista-worker'; then
  echo "Phase 4 Kitchen smoke test failed: barista-worker is not running." >&2
  exit 1
fi

if ! docker compose ps --status running --services \
  | grep -qx 'kitchen-worker'; then
  echo "Phase 4 Kitchen smoke test failed: kitchen-worker is not running." >&2
  exit 1
fi

timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-180}"
deadline=$(( $(date +%s) + timeout_seconds ))
while :; do
  station_tables="$(docker compose exec -T postgres psql \
    -U "${POSTGRES_USER:-coffeeshop}" \
    -d "${POSTGRES_DB:-coffeeshop}" \
    -At -F '|' \
    -c "SELECT
      to_regclass('barista.items'),
      to_regclass('barista.inbox_messages'),
      to_regclass('barista.outbox_messages'),
      to_regclass('kitchen.items'),
      to_regclass('kitchen.inbox_messages'),
      to_regclass('kitchen.outbox_messages'),
      EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'barista'
          AND table_name = 'outbox_messages'
          AND column_name = 'RejectedAtUtc'
      ),
      EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'kitchen'
          AND table_name = 'outbox_messages'
          AND column_name = 'RejectedAtUtc'
      );" \
    2>/dev/null | tr -d '\r' || true)"
  if [ "$station_tables" = 'barista.items|barista.inbox_messages|barista.outbox_messages|kitchen.items|kitchen.inbox_messages|kitchen.outbox_messages|t|t' ]; then
    break
  fi

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "Phase 4 Kitchen smoke test failed: station schemas are not ready." >&2
    exit 1
  fi
  sleep 1
done

export EXPECT_BARISTA_WORKER_TELEMETRY=true
export EXPECT_KITCHEN_WORKER_TELEMETRY=true
exec "${script_directory}/phase-3-smoke.sh"
