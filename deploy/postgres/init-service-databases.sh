#!/bin/sh

set -eu

: "${POSTGRES_USER:?POSTGRES_USER is required}"
: "${POSTGRES_DB:?POSTGRES_DB is required}"
: "${COUNTER_DB_PASSWORD:?COUNTER_DB_PASSWORD is required}"
: "${BARISTA_DB_PASSWORD:?BARISTA_DB_PASSWORD is required}"
: "${KITCHEN_DB_PASSWORD:?KITCHEN_DB_PASSWORD is required}"

# psql reads secrets from its environment: no passwords in command arguments or output.
for owner in counter barista kitchen; do
  SERVICE_DB="coffeeshop_${owner}"
  case "$owner" in
    counter) SERVICE_PASSWORD="$COUNTER_DB_PASSWORD" ;;
    barista) SERVICE_PASSWORD="$BARISTA_DB_PASSWORD" ;;
    kitchen) SERVICE_PASSWORD="$KITCHEN_DB_PASSWORD" ;;
  esac
  export SERVICE_DB SERVICE_PASSWORD
  psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --no-psqlrc --quiet --set=ON_ERROR_STOP=1 <<'SQL'
\getenv service_db SERVICE_DB
\getenv service_password SERVICE_PASSWORD
SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
              :'service_db', :'service_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'service_db')
\gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'service_db', :'service_db')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'service_db')
\gexec
REVOKE ALL ON DATABASE :"service_db" FROM PUBLIC;
GRANT CONNECT, CREATE, TEMPORARY ON DATABASE :"service_db" TO :"service_db";
SQL
done
unset SERVICE_PASSWORD
