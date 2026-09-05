#!/bin/sh
set -eu

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
api_url="${API_URL:-http://localhost:${API_PORT:-8080}}"
order_count="${DATAGEN_ORDER_COUNT:-3}"
seed="${DATAGEN_SEED:-34}"
timeout_seconds="${SMOKE_TIMEOUT_SECONDS:-240}"
recovery_seconds="${SMOKE_RECOVERY_SECONDS:-10}"
fault_worker="${PHASE4_FAULT_WORKER:-}"
topic_prefix="${KAFKA_TOPIC_PREFIX:-coffeeshop}"
group_prefix="${KAFKA_CONSUMER_GROUP_PREFIX:-coffeeshop}"
for value in "$order_count" "$seed" "$timeout_seconds" "$recovery_seconds"; do
  case "$value" in ''|*[!0-9]*|??????????*) echo 'Phase 4 requires bounded integer settings.' >&2; exit 1 ;; esac
done
# Shell arithmetic may interpret a leading zero as octal; settings are decimal.
decimal() { printf '%s' "$1" | sed 's/^0*//; s/^$/0/'; }
order_count="$(decimal "$order_count")"
seed="$(decimal "$seed")"
timeout_seconds="$(decimal "$timeout_seconds")"
recovery_seconds="$(decimal "$recovery_seconds")"
if [ "$order_count" -lt 1 ] || [ "$order_count" -gt 20 ] \
  || [ "$timeout_seconds" -lt 1 ] || [ "$timeout_seconds" -gt 900 ] \
  || [ "$recovery_seconds" -lt 1 ] || [ "$recovery_seconds" -gt 30 ]; then
  echo 'Phase 4 count must be 1..20, timeout 1..900, recovery timeout 1..30.' >&2
  exit 1
fi
case "$fault_worker" in ''|barista-worker|kitchen-worker) ;; *) echo 'Invalid fault worker.' >&2; exit 1 ;; esac
for name in "$topic_prefix" "$group_prefix"; do
  case "$name" in ''|*[!a-zA-Z0-9._-]*) echo 'Invalid Kafka prefix.' >&2; exit 1 ;; esac
done
command -v jq >/dev/null
command -v python3 >/dev/null
deadline=$(( $(date +%s) + timeout_seconds ))
temporary_directory="$(mktemp -d)"
worker_needs_recovery=false
stage=preflight

bounded() {
  cap="$1"; shift
  remaining=$(( deadline - $(date +%s) ))
  if [ "$remaining" -le 0 ]; then return 124; fi
  if [ "$remaining" -lt "$cap" ]; then cap="$remaining"; fi
  python3 "${script_directory}/phase-4-run-with-timeout.py" "$cap" "$@"
}

cleanup() {
  result=$?
  trap - EXIT HUP INT TERM
  if [ "$worker_needs_recovery" = true ]; then
    if ! python3 "${script_directory}/phase-4-run-with-timeout.py" "$recovery_seconds" \
      docker compose start "$fault_worker" >/dev/null 2>&1; then
      echo "Recovery failed: run docker compose start $fault_worker manually." >&2
      result=1
    fi
  fi
  rm -r "$temporary_directory"
  exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM HUP

fail() {
  echo "Phase 4 smoke failed [$stage]: $1" >&2
  # Only service names: never dump environment, HTTP bodies, logs or credentials.
  bounded 2 docker compose ps --status running --services >&2 2>/dev/null || true
  exit 1
}
retry() {
  if [ "$(date +%s)" -ge "$deadline" ]; then fail 'Global deadline exceeded.'; fi
  sleep 0.1
}
query_owner() {
  bounded 8 docker compose exec -T postgres sh /opt/coffeeshop/query-service-database.sh "$@"
}
stats() {
  owner="$1"
  if [ "$owner" = counter ]; then
    effect='orders'; unique='"Id"'
    line_sql='(SELECT COUNT(*) FROM counter.line_items WHERE "Status" = '\''Fulfilled'\'')'
  else
    effect='items'; unique='"LineItemId"'
    line_sql="(SELECT COUNT(*) FROM $owner.items WHERE \"TimeUp\" IS NOT NULL)"
  fi
  query_owner "$owner" -At -c "/* phase4:stats */ SELECT json_build_object(
    'effects', (SELECT COUNT(*) FROM $owner.$effect),
    'unique', (SELECT COUNT(DISTINCT $unique) FROM $owner.$effect),
    'completed', $line_sql,
    'inbox', (SELECT COUNT(*) FROM $owner.inbox_messages WHERE \"ProcessedAtUtc\" IS NOT NULL),
    'outbox', (SELECT COUNT(*) FROM $owner.outbox_messages),
    'pending', (SELECT COUNT(*) FROM $owner.outbox_messages WHERE \"PublishedAtUtc\" IS NULL AND \"RejectedAtUtc\" IS NULL),
    'rejected', (SELECT COUNT(*) FROM $owner.outbox_messages WHERE \"RejectedAtUtc\" IS NOT NULL));"
}
snapshot() {
  counter="$(stats counter)" && barista="$(stats barista)" && kitchen="$(stats kitchen)" || return 1
  printf '%s\n%s\n%s\n' "$counter" "$barista" "$kitchen" | jq -ces '
    if length == 3 and all(.[]; keys == ["completed","effects","inbox","outbox","pending","rejected","unique"]
      and all(.[]; type == "number" and . >= 0)) then . else error("Invalid stats") end'
}
counts_match() {
  printf '%s' "$1" | jq -e --argjson before "$baseline" --argjson n "$order_count" '
    [range(0;3) as $i | .[$i] as $after | $before[$i] as $start
      | ($after.effects - $start.effects == $n)
        and ($after.unique - $start.unique == $n)
        and ($after.completed - $start.completed == ($n * (if $i == 0 then 2 else 1 end)))
        and ($after.inbox - $start.inbox == ($n * (if $i == 0 then 2 else 1 end)))
        and ($after.outbox - $start.outbox == $n)
        and ($after.pending == 0) and ($after.rejected - $start.rejected == 0)] | all' >/dev/null
}
group_lag() {
  offsets="$(bounded 12 docker compose exec -T kafka /opt/kafka/bin/kafka-consumer-groups.sh \
    --bootstrap-server localhost:19092 --describe --group "${group_prefix}.$1" 2>/dev/null)" || return 1
  printf '%s\n' "$offsets" |
    awk -v topic="${topic_prefix}.orders.v1" '
      $2 == topic && $5 ~ /^[0-9]+$/ {
        if ($4 == "-") lag += $5
        else if ($4 ~ /^[0-9]+$/) lag += $5 - $4
        else invalid = 1
        found++
      }
      END { if (!found || invalid) exit 1; print lag + 0 }'
}
topic_end() {
  offsets="$(bounded 12 docker compose exec -T kafka /opt/kafka/bin/kafka-get-offsets.sh \
    --bootstrap-server localhost:19092 --topic "${topic_prefix}.orders.v1" 2>/dev/null)" || return 1
  printf '%s\n' "$offsets" |
    awk -F: 'NF == 3 && $3 ~ /^[0-9]+$/ { total += $3; found++ } END { if (!found) exit 1; print total + 0 }'
}
failure_end() {
  # Query all topics: filtering a not-yet-created retry/DLT topic exits nonzero.
  # Missing failure topics mean zero; an actual CLI failure must still fail closed.
  offsets="$(bounded 12 docker compose exec -T kafka /opt/kafka/bin/kafka-get-offsets.sh \
    --bootstrap-server localhost:19092 2>/dev/null)" || return 1
  printf '%s\n' "$offsets" | awk -F: -v topic="${topic_prefix}.orders.v1" '
    NF == 3 && $3 ~ /^[0-9]+$/ {
      if ($1 == topic ".retry.1" || $1 == topic ".retry.2" || $1 == topic ".dlt") total += $3
      next
    }
    NF > 0 && $0 != "" { invalid = 1 }
    END { if (invalid) exit 1; print total + 0 }'
}

running="$(bounded 5 docker compose ps --status running --services)" || fail 'Cannot inspect topology.'
for service in api barista-worker kitchen-worker; do
  printf '%s\n' "$running" | grep -qx "$service" || fail "Missing service: $service."
done
while :; do
  readiness="$(bounded 5 curl --fail --silent --max-time 5 "$api_url/health/ready" 2>/dev/null || true)"
  if printf '%s' "$readiness" | jq -e '.status == "Healthy" and any(.checks[]; .name == "kafka" and .status == "Healthy")' >/dev/null 2>&1 \
    && baseline="$(snapshot 2>/dev/null)"; then break; fi
  retry
done
printf '%s' "$baseline" | jq -e 'all(.[]; .pending == 0)' >/dev/null || fail 'Start from a drained Outbox.'
run_hex="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"
run_id="$(printf '%s' "$run_hex" | sed 's/^\(........\)\(....\)\(....\)\(....\)\(............\)$/\1-\2-\3-\4-\5/')"

stage=batch
echo "Submitting $order_count deterministic mixed order(s), seed=$seed."
index=1
while [ "$index" -le "$order_count" ]; do
  # A reproducible menu sequence with one drink and one food per order.
  drink=$(( (seed + index) % 6 )); food=$(( 6 + (seed + index) % 4 ))
  bounded 10 curl --fail --silent --max-time 10 --request POST \
    --dump-header "$temporary_directory/headers-$index" --header 'Content-Type: application/json' \
    --data "{\"commandType\":0,\"orderSource\":0,\"location\":0,\"loyaltyMemberId\":\"$run_id\",\"baristaItems\":[{\"itemType\":$drink}],\"kitchenItems\":[{\"itemType\":$food}],\"timestamp\":\"2026-08-08T00:00:00Z\"}" \
    "$api_url/v1/api/orders" >/dev/null 2>&1 || fail 'Order request failed.'
  if [ "$index" -eq 1 ] && [ -n "$fault_worker" ]; then
    stage=commit-before-stop
    committed="$(query_owner counter -At -c "/* phase4:committed */ SELECT COUNT(*) FROM counter.orders WHERE \"LoyaltyMemberId\" = '$run_id';")" || fail 'Cannot verify Counter commit.'
    [ "$committed" = 1 ] || fail 'Counter did not commit exactly one order.'
    worker_needs_recovery=true
    bounded 8 docker compose stop --timeout 2 "$fault_worker" >/dev/null 2>&1 || fail 'Worker stop failed.'
    stage=backlog
    fault_role="${fault_worker%-worker}"
    while :; do
      lag="$(group_lag "$fault_role" || true)"
      if [ -n "$lag" ] && [ "$lag" -gt 0 ]; then break; fi
      retry
    done
    echo "Observed committed order and Kafka backlog for $fault_worker."
    stage=recovery
    bounded 12 docker compose start "$fault_worker" >/dev/null 2>&1 || fail 'Worker restart failed.'
    worker_needs_recovery=false
  fi
  index=$((index + 1))
done

stage=fulfillment
while :; do
  fulfilled="$(bounded 5 curl --fail --silent --max-time 5 "$api_url/v1/api/fulfillment-orders" 2>/dev/null || true)"
  if printf '%s' "$fulfilled" | jq -e --arg run "$run_id" --argjson n "$order_count" '
      [.[] | select(.loyaltyMemberId == $run and .status == "Fulfilled")]
      | length == $n and (map(.id) | unique | length) == $n
        and all(.[]; (.lineItems | length) == 2
          and all(.lineItems[]; .status == "Fulfilled")
          and (.lineItems | map(.station) | sort) == ["Barista","Kitchen"])' >/dev/null 2>&1 \
    && current="$(snapshot 2>/dev/null)" && counts_match "$current"; then break; fi
  retry
done

# Replay the first original envelope as JSON. The dual-format reader accepts it even
# when normal producers use Avro; all business identity fields remain unchanged.
stage=duplicate-replay
first_correlation="$(tr -d '\r' < "$temporary_directory/headers-1" | awk -F ': *' 'tolower($1) == "x-correlation-id" {print $2}')"
printf '%s' "$first_correlation" | grep -Eq '^[0-9a-fA-F-]{36}$' || fail 'Invalid correlation header.'
envelope="$(query_owner counter -At -c "/* phase4:envelope */ SELECT \"EnvelopeJson\" FROM counter.outbox_messages WHERE \"CorrelationId\" = '$first_correlation';")" || fail 'Cannot load original envelope.'
record="$(printf '%s' "$envelope" | jq -er '
  . as $event
  | (.occurredAtUtc | capture("^(?<date>[0-9T:-]{19})(?:\\.(?<fraction>[0-9]+))?(?<zone>Z|[+-][0-9:]{5})$")) as $time
  | ($time.date + "." + (($time.fraction // "") + "0000000")[0:7]
    + (if $time.zone == "Z" then "+00:00" else $time.zone end)) as $occurred
  | [["message-id:" + .messageId, "event-type:" + .eventType, "event-version:" + (.eventVersion|tostring),
      "occurred-at:" + $occurred, "correlation-id:" + .correlationId,
      "causation-id:" + (.causationId // ""), "content-type:application/json"] | join(","),
      $event.payload.orderId, ($event|tojson)] | join("\t")')" || fail 'Cannot encode replay.'
before_replay="$(topic_end)" || fail 'Cannot inspect original topic.'
before_failure="$(failure_end)" || fail 'Cannot inspect retry/DLT topics.'
printf '%s\n' "$record" | bounded 15 docker compose exec -T kafka /opt/kafka/bin/kafka-console-producer.sh \
  --bootstrap-server localhost:19092 --topic "${topic_prefix}.orders.v1" \
  --producer-property acks=all --producer-property delivery.timeout.ms=10000 --producer-property request.timeout.ms=5000 \
  --property parse.headers=true --property parse.key=true >/dev/null 2>&1 || fail 'Replay publish failed.'
while :; do
  after_replay="$(topic_end || true)"
  barista_lag="$(group_lag barista || true)"; kitchen_lag="$(group_lag kitchen || true)"
  if [ -n "$after_replay" ] && [ "$after_replay" -gt "$before_replay" ] \
    && [ "$barista_lag" = 0 ] && [ "$kitchen_lag" = 0 ]; then break; fi
  retry
done
after_failure="$(failure_end)" || fail 'Cannot inspect retry/DLT after replay.'
[ "$after_failure" = "$before_failure" ] || fail 'Replay was forwarded to retry/DLT, not accepted as a duplicate.'
current="$(snapshot)" || fail 'Cannot inspect duplicate effects.'
counts_match "$current" || fail 'Duplicate delivery changed business effects.'
echo "Phase 4 smoke passed: $order_count orders fulfilled, exact owner effects, drained Outboxes, duplicate consumed by both stations."
