#!/usr/bin/env bash
# .github/skills/atua-deploy/scripts/collector-trigger-cycle.sh
#
# Automatiza o ciclo de teste manual do Collector que vínhamos repetindo
# via curl a cada verificação:
#   1. Renova o JWT do usuário de teste (POST /auth/signin)
#   2. Desativa o collector-activation (gera Idempotency-Key)
#   3. Reativa o collector-activation (gera novo ImmediateCollectionCommand
#      'Pending', que o Worker vai reivindicar no próximo polling)
#
# Não expõe credenciais reais do iService — usa apenas o usuário de teste
# da API (login/senha de aplicação), nunca a credencial do provedor.
#
# Variáveis de ambiente esperadas (todas com defaults dos valores conhecidos
# do ambiente MVP atual; sobrescreva se o tenant/integration mudarem):
#   ATUA_TEST_EMAIL      (default: teste.mvp@atua-mvp.local)
#   ATUA_TEST_PASSWORD   (default: SenhaForte@123)
#   ATUA_TENANT_ID       (default: 01a05f21-92c6-7188-b785-1ebda5c839ba)
#   ATUA_INTEGRATION_ID  (default: 01a05f21-92ef-7fe6-af0d-428c112fa402)
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

ATUA_TEST_EMAIL="${ATUA_TEST_EMAIL:-alysonforever@gmail.com}"
ATUA_TEST_PASSWORD="${ATUA_TEST_PASSWORD:-asdqwe123}"
ATUA_TENANT_ID="${ATUA_TENANT_ID:-01a0888b-acd8-773b-93b7-7362973d7ea8}"
ATUA_INTEGRATION_ID="${ATUA_INTEGRATION_ID:-01a0888b-ad0d-760a-9327-bf7dfa5fd97d}"

API_IP="$(aws cloudformation describe-stacks --stack-name AtuaComputeStack --region "$REGION" \
  --query "Stacks[0].Outputs[?OutputKey=='ApiPublicIp'].OutputValue" --output text)"

if [ -z "$API_IP" ] || [ "$API_IP" = "None" ]; then
  echo "FATAL: não foi possível obter o IP público da API — AtuaComputeStack está no ar?" >&2
  exit 1
fi

echo "==> [1/3] Renovando JWT do usuário de teste..."
SIGNIN_RESP="$(curl -s -m 15 -X POST "http://$API_IP/auth/signin" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$ATUA_TEST_EMAIL\",\"password\":\"$ATUA_TEST_PASSWORD\"}")"

JWT="$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['accessToken'])" "$SIGNIN_RESP" 2>/dev/null || true)"
if [ -z "$JWT" ]; then
  echo "FATAL: signin falhou ou não retornou accessToken. Resposta: $SIGNIN_RESP" >&2
  exit 1
fi
echo "    OK: JWT obtido (não exibido)."

echo "==> [2/3] Desativando collector-activation..."
DEACTIVATE_RESP="$(curl -s -m 15 -X DELETE \
  "http://$API_IP/api/tenants/$ATUA_TENANT_ID/integrations/$ATUA_INTEGRATION_ID/collector-activation" \
  -H "Authorization: Bearer $JWT" \
  -H "Idempotency-Key: $(python3 -c 'import uuid; print(uuid.uuid4())')")"
echo "    $DEACTIVATE_RESP"

echo "==> [3/3] Reativando collector-activation (gera novo comando Pending)..."
ACTIVATE_RESP="$(curl -s -m 15 -X PUT \
  "http://$API_IP/api/tenants/$ATUA_TENANT_ID/integrations/$ATUA_INTEGRATION_ID/collector-activation" \
  -H "Authorization: Bearer $JWT" \
  -H "Idempotency-Key: $(python3 -c 'import uuid; print(uuid.uuid4())')")"
echo "    $ACTIVATE_RESP"

echo ""
echo "Novo ciclo disparado. Acompanhe os logs do Worker com:"
echo "  ssh -i ~/.ssh/atua-mvp-key.pem ec2-user@<CollectorPublicIp> 'sudo journalctl -u atua-collector -f'"
