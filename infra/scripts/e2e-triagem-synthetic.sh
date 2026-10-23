#!/usr/bin/env bash
# E2E repetível da triagem WhatsApp com OS sintética (prefixo "E2E-").
#
# Injeta uma interação fictícia em provider_interactions (MongoDB Atlas). O
# Consumer do Collector processa normalmente: grava a OS e a outbox; a API
# cria a triagem e o comando; o Channel envia ao destinatário fixo da trava.
# A consulta ao iService real fica fora do cenário.
#
# Uso:
#   e2e-triagem-synthetic.sh inject [ID]       cria uma OS E2E-<timestamp> (ou reapresenta ID, para testar idempotência)
#   e2e-triagem-synthetic.sh status [ID]       mostra o andamento (todas E2E- sem ID)
#   e2e-triagem-synthetic.sh release-to-pool ID  avança a triagem E2E para LiberadaAoPool (atalho sem LLM)
#   e2e-triagem-synthetic.sh cleanup [ID]      remove dados da OS e mídias no S3 (todas E2E- sem ID)
#
# Requisitos: Postgres__ConnectionString exportada, AWS_PROFILE com acesso ao
# secret atua/mongodb-atlas, mongosh e psql instalados. Nenhum segredo é impresso.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib-pg-conn.sh
source "$SCRIPT_DIR/lib-pg-conn.sh"

PREFIX="E2E-"
TENANT_ID="${E2E_TENANT_ID:-01a0888b-acd8-773b-93b7-7362973d7ea8}"
TEST_PHONE="${E2E_TEST_PHONE:-5584994241248}"
MONGO_SECRET_ID="${MONGO_SECRET_ID:-atua/mongodb-atlas}"
AWS_REGION="${AWS_REGION:-sa-east-1}"

usage() {
  sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'
}

require_synthetic_id() {
  case "$1" in
    "$PREFIX"*) ;;
    *) echo "Erro: o ID deve começar com $PREFIX." >&2; exit 1 ;;
  esac
  if ! printf '%s' "$1" | grep -Eq '^E2E-[A-Za-z0-9-]+$'; then
    echo "Erro: ID sintético inválido." >&2
    exit 1
  fi
}

mongo_uri() {
  aws secretsmanager get-secret-value \
    --secret-id "$MONGO_SECRET_ID" --region "$AWS_REGION" \
    --query SecretString --output text \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["uri"])'
}

cmd_inject() {
  local provider_id interaction_id command_id now_provider
  if [ -n "${1:-}" ]; then
    require_synthetic_id "$1"
    provider_id="$1"
  else
    provider_id="${PREFIX}$(date -u +%Y%m%d%H%M%S)"
  fi
  interaction_id="$(uuidgen | tr 'A-Z' 'a-z')"
  command_id="$(uuidgen | tr 'A-Z' 'a-z')"
  now_provider="$(date -u '+%Y-%m-%d %H:%M:%S')"

  MONGO_URI="$(mongo_uri)" \
  E2E_PROVIDER_ID="$provider_id" E2E_INTERACTION_ID="$interaction_id" \
  E2E_COMMAND_ID="$command_id" E2E_TENANT_ID="$TENANT_ID" \
  E2E_PHONE="$TEST_PHONE" E2E_NOW="$now_provider" \
  mongosh --nodb --quiet --eval '
    const env = process.env;
    const db = new Mongo(env.MONGO_URI).getDB("atua");
    const customerInfo = {
      customerType: "PF",
      name: "Cliente Teste E2E",
      contactName: "Cliente Teste E2E",
      phoneCountryCode1: env.E2E_PHONE.slice(0, 2),
      phoneNumber1: env.E2E_PHONE.slice(2),
      address: "Endereco ficticio de teste",
      cityName: "Natal",
      stateName: "RN",
      countryName: "Brasil"
    };
    const result = db.provider_interactions.insertOne({
      _id: env.E2E_INTERACTION_ID,
      tenant_id: env.E2E_TENANT_ID,
      provider_type: "iservice",
      command_id: env.E2E_COMMAND_ID,
      interaction_type: "detail_query",
      request: { source: "e2e-synthetic", workOrderId: env.E2E_PROVIDER_ID },
      orders: [{
        workOrderId: env.E2E_PROVIDER_ID,
        workOrderNo: env.E2E_PROVIDER_ID,
        woStatus: "assigned",
        productBrand: "MIDEA",
        productModel: "MODELO-TESTE-E2E",
        symptomDescription: "Teste E2E sintetico",
        creationDate: env.E2E_NOW,
        lastUpdateDate: env.E2E_NOW,
        orderDetail: { customerInfo: customerInfo }
      }],
      success: true,
      error_message: null,
      created_at: new Date()
    });
    if (!result.acknowledged) { throw new Error("insert não confirmado"); }
  '

  echo "OS sintética injetada: $provider_id"
  echo "Acompanhe com: $0 status $provider_id"
}

cmd_status() {
  local filter
  if [ -n "${1:-}" ]; then
    require_synthetic_id "$1"
    filter="= :'pid'"
  else
    filter="LIKE '${PREFIX}%'"
  fi

  pg_run -v ON_ERROR_STOP=1 -v pid="${1:-}" -P pager=off <<SQL
SELECT w."WorkOrderProviderId" AS os,
       w."Status" AS status_os,
       o."Status" AS outbox,
       o."LastErrorCode" AS erro_outbox,
       t."Status" AS triagem,
       c."Status" AS comando,
       c."AttemptCount" AS tentativas,
       c."LastErrorCode" AS erro_comando,
       COALESCE(messages.total, 0) AS mensagens_recebidas,
       messages.resultados AS resultado_mensagens,
       COALESCE(media.total, 0) AS midias_recebidas,
       COALESCE(media.armazenadas, 0) AS midias_armazenadas,
       media.tipos AS tipos_midias,
       media.resultados AS resultado_midias
FROM work_orders w
LEFT JOIN triagem_trigger_outbox o ON o."WorkOrderId" = w."Id"
LEFT JOIN triagens t ON t."WorkOrderId" = w."Id"
LEFT JOIN triagem_channel_commands c ON c."TriagemId" = t."Id"
LEFT JOIN LATERAL (
  SELECT count(*) AS total, string_agg(DISTINCT message."Outcome", ',') AS resultados
  FROM triagem_channel_messages AS message
  WHERE message."TriagemId" = t."Id"
     OR t."Id" = ANY(message."CandidateTriagemIds")
) AS messages ON true
LEFT JOIN LATERAL (
  SELECT count(*) AS total,
         count(*) FILTER (WHERE item."Outcome" = 'Armazenada') AS armazenadas,
         string_agg(DISTINCT item."MediaKind", ',') AS tipos,
         string_agg(DISTINCT item."Outcome", ',') AS resultados
  FROM triagem_channel_media AS item
  WHERE item."TriagemId" = t."Id"
     OR t."Id" = ANY(item."CandidateTriagemIds")
) AS media ON true
WHERE w."TenantId" = '$TENANT_ID'
  AND w."WorkOrderProviderId" $filter
ORDER BY w."CreatedAt";
SQL
}

media_bucket() {
  if [ -n "${E2E_MEDIA_BUCKET:-}" ]; then
    printf '%s' "$E2E_MEDIA_BUCKET"
    return
  fi
  printf 'atua-%s-media-triagem' "$(aws sts get-caller-identity --query Account --output text)"
}

cmd_cleanup() {
  local filter media_keys bucket key
  if [ -n "${1:-}" ]; then
    require_synthetic_id "$1"
    filter="= :'pid'"
  else
    filter="LIKE '${PREFIX}%'"
  fi

  # Objetos no S3 das mídias recebidas; removidos só após o COMMIT.
  media_keys="$(pg_run -At -v ON_ERROR_STOP=1 -v pid="${1:-}" <<SQL
SELECT m."StorageKey" FROM triagem_channel_media m
JOIN triagens t ON t."Id" = m."TriagemId"
JOIN work_orders w ON w."Id" = t."WorkOrderId"
WHERE w."TenantId" = '$TENANT_ID'
  AND starts_with(w."WorkOrderProviderId", '$PREFIX')
  AND w."WorkOrderProviderId" $filter
  AND m."StorageKey" IS NOT NULL;
SQL
)"

  pg_run -v ON_ERROR_STOP=1 -v pid="${1:-}" -P pager=off <<SQL
BEGIN;
CREATE TEMP TABLE e2e_ids ON COMMIT DROP AS
  SELECT "Id" FROM work_orders
  WHERE "TenantId" = '$TENANT_ID'
    AND starts_with("WorkOrderProviderId", '$PREFIX')
    AND "WorkOrderProviderId" $filter;
CREATE TEMP TABLE e2e_triagem_ids ON COMMIT DROP AS
  SELECT "Id" FROM triagens
  WHERE "WorkOrderId" IN (SELECT "Id" FROM e2e_ids);
DELETE FROM triagem_channel_messages
WHERE "TriagemId" IN (SELECT "Id" FROM e2e_triagem_ids)
   OR "CandidateTriagemIds" && ARRAY(SELECT "Id" FROM e2e_triagem_ids)::uuid[];
DELETE FROM triagem_channel_media
WHERE "TriagemId" IN (SELECT "Id" FROM e2e_triagem_ids)
   OR "CandidateTriagemIds" && ARRAY(SELECT "Id" FROM e2e_triagem_ids)::uuid[];
DELETE FROM triagem_channel_commands WHERE "WorkOrderId" IN (SELECT "Id" FROM e2e_ids);
DELETE FROM triagens WHERE "Id" IN (SELECT "Id" FROM e2e_triagem_ids);
DELETE FROM triagem_trigger_outbox WHERE "WorkOrderId" IN (SELECT "Id" FROM e2e_ids);
DELETE FROM work_order_histories WHERE "WorkOrderId" IN (SELECT "Id" FROM e2e_ids);
DELETE FROM work_orders WHERE "Id" IN (SELECT "Id" FROM e2e_ids);
COMMIT;
SQL

  if [ -n "$media_keys" ]; then
    bucket="$(media_bucket)"
    while IFS= read -r key; do
      [ -n "$key" ] && aws s3 rm "s3://$bucket/$key" --only-show-errors
    done <<<"$media_keys"
    echo "Mídias removidas do S3: $(printf '%s\n' "$media_keys" | grep -c .)"
  fi
  echo "Limpeza concluída."
}

cmd_release_to_pool() {
  # Sem a LLM, nenhum fluxo automático leva a triagem a LiberadaAoPool.
  # Atalho só para o E2E: exige ID sintético explícito e não toca OS reais.
  if [ -z "${1:-}" ]; then
    echo "Erro: release-to-pool exige o ID E2E- da OS." >&2
    exit 1
  fi
  require_synthetic_id "$1"

  pg_run -v ON_ERROR_STOP=1 -v pid="$1" -P pager=off <<SQL
UPDATE triagens t
SET "Status" = 'LiberadaAoPool',
    "UpdatedAtUtc" = now(),
    "ConcurrencyToken" = gen_random_uuid()
FROM work_orders w
WHERE w."Id" = t."WorkOrderId"
  AND w."TenantId" = '$TENANT_ID'
  AND starts_with(w."WorkOrderProviderId", '$PREFIX')
  AND w."WorkOrderProviderId" = :'pid'
  AND t."Status" IN ('ContatoIniciado', 'EmColeta')
RETURNING w."WorkOrderProviderId" AS os, t."Status" AS triagem;
SQL
}

case "${1:-}" in
  inject) cmd_inject "${2:-}" ;;
  status) cmd_status "${2:-}" ;;
  release-to-pool) cmd_release_to_pool "${2:-}" ;;
  cleanup) cmd_cleanup "${2:-}" ;;
  *) usage; exit 1 ;;
esac
