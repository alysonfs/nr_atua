#!/usr/bin/env bash
# .github/skills/atua-mongo-inspect/scripts/mongo-peek-all.sh [limit]
#
# Descobre todas as coleções existentes no banco `atua` (MongoDB Atlas) e
# roda mongo-peek.sh para cada uma — útil para validar rapidamente o estado
# geral do banco sem precisar saber de antemão quais coleções existem.
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

REGION="${AWS_REGION:-sa-east-1}"
MONGO_SECRET_ID="${MONGO_SECRET_ID:-atua/mongodb-atlas}"
MONGO_DB_NAME="${MONGO_DB_NAME:-atua}"
LIMIT="${1:-10}"

command -v mongosh >/dev/null 2>&1 || {
  echo "FATAL: mongosh não encontrado. Instale com 'brew install mongosh'." >&2
  exit 1
}

MONGO_URI="$(aws secretsmanager get-secret-value \
  --secret-id "$MONGO_SECRET_ID" \
  --query 'SecretString' --output text --region "$REGION" \
  | python3 -c 'import json,sys; print(json.loads(sys.stdin.read())["uri"])')"

[ -n "$MONGO_URI" ] && [ "$MONGO_URI" != "None" ] || {
  echo "FATAL: não foi possível obter a connection string do secret '$MONGO_SECRET_ID'." >&2
  exit 1
}

COLLECTIONS="$(mongosh "$MONGO_URI" --quiet --eval \
  "JSON.stringify(db.getSiblingDB('$MONGO_DB_NAME').getCollectionNames())")"

echo "==> Coleções encontradas no banco '$MONGO_DB_NAME': $COLLECTIONS"
echo

echo "$COLLECTIONS" | python3 -c "
import json, sys
for name in json.loads(sys.stdin.read()):
    print(name)
" | while read -r COLLECTION; do
  [ -n "$COLLECTION" ] || continue
  bash "$DIR/mongo-peek.sh" "$COLLECTION" "$LIMIT"
  echo
done
