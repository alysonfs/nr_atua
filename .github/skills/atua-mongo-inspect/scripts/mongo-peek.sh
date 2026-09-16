#!/usr/bin/env bash
# .github/skills/atua-mongo-inspect/scripts/mongo-peek.sh <collection> [limit]
#
# Consulta somente leitura: retorna as N últimas amostras (default 10) de uma
# coleção do MongoDB Atlas (banco `atua`, ADR-021), ordenadas por
# `created_at` desc quando o campo existir, para validar rapidamente se os
# dados coletados estão chegando e com a qualidade esperada — sem precisar
# pedir ao agente para "gerar mais um comando de validação" a cada vez.
#
# Credenciais: lê a connection string do secret `atua/mongodb-atlas` no AWS
# Secrets Manager (nunca commitada, nunca impressa). Requer:
#   - AWS CLI configurado com um profile que tenha `secretsmanager:GetSecretValue`
#   - mongosh instalado localmente (brew install mongosh)
#   - Seu IP atual autorizado na IP Access List do Atlas
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail

REGION="${AWS_REGION:-sa-east-1}"
MONGO_SECRET_ID="${MONGO_SECRET_ID:-atua/mongodb-atlas}"
MONGO_DB_NAME="${MONGO_DB_NAME:-atua}"

COLLECTION="${1:-}"
LIMIT="${2:-10}"

usage() {
  echo "Uso: $0 <collection> [limit=10]" >&2
  echo "Exemplo: $0 work_order_snapshots 10" >&2
  exit 1
}

[ -n "$COLLECTION" ] || usage

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

echo "==> Coleção '$COLLECTION' (banco '$MONGO_DB_NAME') — últimos $LIMIT documento(s):"
mongosh "$MONGO_URI" --quiet --eval "
  const c = db.getSiblingDB('$MONGO_DB_NAME').getCollection('$COLLECTION');
  const total = c.countDocuments();
  print('Total de documentos na coleção: ' + total);
  const hasCreatedAt = c.findOne({ created_at: { \$exists: true } }) !== null;
  const cursor = hasCreatedAt
    ? c.find().sort({ created_at: -1 }).limit($LIMIT)
    : c.find().limit($LIMIT);
  cursor.forEach(doc => printjson(doc));
"
