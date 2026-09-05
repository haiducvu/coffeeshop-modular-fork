#!/bin/sh
set -eu

# Operator smoke helper: use the same credentials as the queried service over TCP.
owner="${1:?service owner is required}"
shift
case "$owner" in
  counter) PGPASSWORD="${COUNTER_DB_PASSWORD:?}" ;;
  barista) PGPASSWORD="${BARISTA_DB_PASSWORD:?}" ;;
  kitchen) PGPASSWORD="${KITCHEN_DB_PASSWORD:?}" ;;
  *) echo "Unknown service database owner." >&2; exit 1 ;;
esac
export PGPASSWORD
export PGCONNECT_TIMEOUT=3
export PGOPTIONS='-c statement_timeout=5000'
exec psql --no-psqlrc -h 127.0.0.1 -U "coffeeshop_${owner}" -d "coffeeshop_${owner}" \
  --set=ON_ERROR_STOP=1 "$@"
