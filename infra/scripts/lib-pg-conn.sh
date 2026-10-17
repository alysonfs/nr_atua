#!/usr/bin/env bash
# Shared PostgreSQL connection helper for local operational scripts.
#
# Reads the Npgsql-style Postgres__ConnectionString environment variable,
# exports the libpq connection settings, and exposes pg_run().

set -euo pipefail

if [ -z "${Postgres__ConnectionString:-}" ]; then
  echo "Erro: variável de ambiente Postgres__ConnectionString não definida." >&2
  exit 1
fi

if command -v psql >/dev/null 2>&1; then
  PSQL_BIN="$(command -v psql)"
elif [ -x "/usr/local/opt/libpq/bin/psql" ]; then
  PSQL_BIN="/usr/local/opt/libpq/bin/psql"
elif [ -x "/opt/homebrew/opt/libpq/bin/psql" ]; then
  PSQL_BIN="/opt/homebrew/opt/libpq/bin/psql"
else
  echo "Erro: psql não encontrado. Instale com 'brew install libpq'." >&2
  exit 1
fi

PG_HOST="$(printf '%s' "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Host=//p' | head -1)"
PG_PORT="$(printf '%s' "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Port=//p' | head -1)"
PG_DB="$(printf '%s' "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Database=//p' | head -1)"
PG_USER="$(printf '%s' "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Username=//p' | head -1)"
PGPASSWORD="$(printf '%s' "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Password=//p' | head -1)"

PG_PORT="${PG_PORT:-5432}"

if [ -z "$PG_HOST" ] || [ -z "$PG_DB" ] || [ -z "$PG_USER" ] || [ -z "$PGPASSWORD" ]; then
  echo "Erro: não foi possível extrair Host/Database/Username/Password da connection string." >&2
  exit 1
fi

export PGPASSWORD
export PGCONNECT_TIMEOUT="${PGCONNECT_TIMEOUT:-8}"

pg_run() {
  "$PSQL_BIN" -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" "$@"
}
