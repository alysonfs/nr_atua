#!/usr/bin/env bash
# Biblioteca compartilhada: parseia $Postgres__ConnectionString (formato Npgsql,
# ex.: "Host=...;Port=...;Database=...;Username=...;Password=...;SSL Mode=Require")
# e exporta variáveis PG_HOST/PG_PORT/PG_DB/PG_USER/PGPASSWORD prontas para uso
# com psql. Também resolve o binário psql (libpq via Homebrew não entra no PATH
# por padrão no macOS).
#
# Uso: source lib-pg-conn.sh

set -euo pipefail

if [ -z "${Postgres__ConnectionString:-}" ]; then
  echo "Erro: variável de ambiente Postgres__ConnectionString não definida." >&2
  echo "Configure no seu ~/.zshrc (ex.: export Postgres__ConnectionString=\"Host=...;...\")." >&2
  exit 1
fi

# Resolve o binário psql (Homebrew libpq não é linkado no PATH por padrão)
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

# Parseia a connection string no formato Npgsql (chave=valor;chave=valor;...)
PG_HOST="$(echo "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Host=//p' | head -1)"
PG_PORT="$(echo "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Port=//p' | head -1)"
PG_DB="$(echo "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Database=//p' | head -1)"
PG_USER="$(echo "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Username=//p' | head -1)"
PGPASSWORD="$(echo "$Postgres__ConnectionString" | tr ';' '\n' | sed -n 's/^Password=//p' | head -1)"

PG_PORT="${PG_PORT:-5432}"

if [ -z "$PG_HOST" ] || [ -z "$PG_DB" ] || [ -z "$PG_USER" ] || [ -z "$PGPASSWORD" ]; then
  echo "Erro: não consegui extrair Host/Database/Username/Password de Postgres__ConnectionString." >&2
  exit 1
fi

export PGPASSWORD
export PGCONNECT_TIMEOUT="${PGCONNECT_TIMEOUT:-8}"

pg_run() {
  "$PSQL_BIN" -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" "$@"
}
