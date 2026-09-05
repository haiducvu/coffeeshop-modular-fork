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
for owner in barista kitchen; do
  while :; do
    ready="$(docker compose exec -T postgres sh /opt/coffeeshop/query-service-database.sh "$owner" -At -c "SELECT
      to_regclass('$owner.items') IS NOT NULL
      AND to_regclass('$owner.inbox_messages') IS NOT NULL
      AND to_regclass('$owner.outbox_messages') IS NOT NULL
      AND EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = '$owner' AND table_name = 'outbox_messages'
          AND column_name = 'RejectedAtUtc');" 2>/dev/null || true)"
    if [ "$ready" = t ]; then break; fi
    if [ "$(date +%s)" -ge "$deadline" ]; then
      echo "Phase 4 Kitchen smoke test failed: station schemas are not ready." >&2
      exit 1
    fi
    sleep 1
  done
done

export EXPECT_BARISTA_WORKER_TELEMETRY=true
export EXPECT_KITCHEN_WORKER_TELEMETRY=true
exec "${script_directory}/phase-3-smoke.sh"
