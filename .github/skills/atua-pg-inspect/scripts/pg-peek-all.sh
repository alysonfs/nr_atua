#!/usr/bin/env bash
# .github/skills/atua-pg-inspect/scripts/pg-peek-all.sh [limit]
#
# Descobre todas as tabelas do schema `public` no RDS PostgreSQL do ATUA e
# roda pg-peek.sh para cada uma — útil para validar rapidamente o estado
# geral do banco sem manter uma lista manual de tabelas.
#
# Abre um único túnel SSH e reaproveita para todas as consultas (mais rápido
# que abrir um túnel por tabela).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

REGION="${AWS_REGION:-sa-east-1}"
PG_SECRET_ID="${PG_SECRET_ID:-atua/rds-postgres}"
SSH_KEY="${SSH_KEY:-$HOME/.ssh/atua-mvp-key.pem}"
TUNNEL_EC2_OUTPUT_KEY="${TUNNEL_EC2_OUTPUT_KEY:-ApiPublicIp}"
LOCAL_TUNNEL_PORT="${LOCAL_TUNNEL_PORT:-15432}"
PSQL_BIN="${PSQL_BIN:-$(command -v psql || echo /usr/local/Cellar/libpq/*/bin/psql)}"
LIMIT="${1:-10}"

# shellcheck disable=SC2086
PSQL_BIN="$(ls -1 $PSQL_BIN 2>/dev/null | head -1)"
[ -n "$PSQL_BIN" ] && [ -x "$PSQL_BIN" ] || {
  echo "FATAL: psql não encontrado. Instale com 'brew install libpq' (exporte PSQL_BIN se necessário)." >&2
  exit 1
}

[ -f "$SSH_KEY" ] || {
  echo "FATAL: chave SSH não encontrada em $SSH_KEY." >&2
  exit 1
}

PG_SECRET_JSON="$(aws secretsmanager get-secret-value \
  --secret-id "$PG_SECRET_ID" \
  --query 'SecretString' --output text --region "$REGION")"

[ -n "$PG_SECRET_JSON" ] && [ "$PG_SECRET_JSON" != "None" ] || {
  echo "FATAL: não foi possível obter o secret '$PG_SECRET_ID'." >&2
  exit 1
}

read -r PG_USER PG_PASSWORD PG_HOST PG_PORT PG_DB <<EOF_VARS
$(echo "$PG_SECRET_JSON" | python3 -c '
import json, sys
d = json.loads(sys.stdin.read())
print(d["username"], d["password"], d["host"], d["port"], d["dbname"])
')
EOF_VARS

TUNNEL_IP="$(aws cloudformation describe-stacks --stack-name AtuaComputeStack --region "$REGION" \
  --query "Stacks[0].Outputs[?OutputKey=='$TUNNEL_EC2_OUTPUT_KEY'].OutputValue" --output text)"

[ -n "$TUNNEL_IP" ] && [ "$TUNNEL_IP" != "None" ] || {
  echo "FATAL: não foi possível obter o IP da instância-túnel ($TUNNEL_EC2_OUTPUT_KEY)." >&2
  exit 1
}

SOCKET="/tmp/atua-pg-tunnel-all-$$.sock"
cleanup() {
  ssh -S "$SOCKET" -O exit "ec2-user@$TUNNEL_IP" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Abrindo túnel SSH via $TUNNEL_IP (porta local $LOCAL_TUNNEL_PORT -> $PG_HOST:$PG_PORT)..."
ssh -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -i "$SSH_KEY" \
  -M -S "$SOCKET" -fN -L "$LOCAL_TUNNEL_PORT:$PG_HOST:$PG_PORT" "ec2-user@$TUNNEL_IP"

TABLES="$(PGPASSWORD="$PG_PASSWORD" "$PSQL_BIN" -h localhost -p "$LOCAL_TUNNEL_PORT" -U "$PG_USER" -d "$PG_DB" -tA -c "
  SELECT table_name FROM information_schema.tables
  WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
  ORDER BY table_name;
")"

echo "==> Tabelas encontradas no schema 'public': $TABLES"
echo

for TABLE in $TABLES; do
  ORDER_COL="$(PGPASSWORD="$PG_PASSWORD" "$PSQL_BIN" -h localhost -p "$LOCAL_TUNNEL_PORT" -U "$PG_USER" -d "$PG_DB" -tA -c "
    SELECT column_name FROM information_schema.columns
    WHERE table_name = '$TABLE' AND column_name IN ('CreatedAt', 'created_at')
    ORDER BY column_name LIMIT 1;
  ")"

  echo "==> Tabela '$TABLE' — últimas $LIMIT linha(s):"
  if [ -n "$ORDER_COL" ]; then
    PGPASSWORD="$PG_PASSWORD" "$PSQL_BIN" -h localhost -p "$LOCAL_TUNNEL_PORT" -U "$PG_USER" -d "$PG_DB" \
      -c "SELECT count(*) AS total FROM \"$TABLE\";" \
      -c "SELECT * FROM \"$TABLE\" ORDER BY \"$ORDER_COL\" DESC LIMIT $LIMIT;"
  else
    PGPASSWORD="$PG_PASSWORD" "$PSQL_BIN" -h localhost -p "$LOCAL_TUNNEL_PORT" -U "$PG_USER" -d "$PG_DB" \
      -c "SELECT count(*) AS total FROM \"$TABLE\";" \
      -c "SELECT * FROM \"$TABLE\" LIMIT $LIMIT;"
  fi
  echo
done
