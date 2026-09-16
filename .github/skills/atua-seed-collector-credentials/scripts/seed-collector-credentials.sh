#!/usr/bin/env bash
# Gera novas credenciais de serviço (Collector -> API) e insere na tabela
# service_credentials do Postgres real (RDS), usando a mesma variável de
# ambiente Postgres__ConnectionString já configurada no shell do usuário.
#
# ATENÇÃO: este script GRAVA no banco (INSERT, e opcionalmente UPDATE para
# revogar credenciais antigas). NÃO É EXECUTADO AUTOMATICAMENTE. Requer
# aprovação explícita antes de rodar, e deve ser usado apenas em ambiente de
# desenvolvimento/staging.
#
# O algoritmo de token/hash replica EXATAMENTE
# apps/api/Atua.Api/Application/Identity/TokenHashService.cs:
#   token = Base64(48 bytes aleatórios)
#   hash  = SHA256(UTF8(token)) em hex MAIÚSCULO
# Qualquer mudança nesse arquivo C# exige atualizar este script também.
#
# Uso:
#   ./seed-collector-credentials.sh --tenant-id <uuid> --integration-id <uuid> [--provider-id <uuid>] [--revoke-existing]
#
# Se --tenant-id/--integration-id forem omitidos e houver exatamente UM
# tenant e UMA integration no banco, o script os usa automaticamente
# (conveniente para o ambiente de dev/MVP com um único tenant).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib-pg-conn.sh
source "$SCRIPT_DIR/lib-pg-conn.sh"

TENANT_ID=""
INTEGRATION_ID=""
PROVIDER_ID=""
REVOKE_EXISTING=false

while [ $# -gt 0 ]; do
  case "$1" in
    --tenant-id) TENANT_ID="$2"; shift 2 ;;
    --integration-id) INTEGRATION_ID="$2"; shift 2 ;;
    --provider-id) PROVIDER_ID="$2"; shift 2 ;;
    --revoke-existing) REVOKE_EXISTING=true; shift ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) echo "Argumento desconhecido: $1" >&2; exit 1 ;;
  esac
done

# Resolve tenant/integration automaticamente se houver exatamente um cada
if [ -z "$TENANT_ID" ]; then
  TENANT_ID="$(pg_run -t -A -c 'SELECT "Id" FROM tenants;' | sed '/^$/d')"
  TENANT_COUNT=$(echo "$TENANT_ID" | grep -c . || true)
  if [ "$TENANT_COUNT" -ne 1 ]; then
    echo "Erro: existe(m) $TENANT_COUNT tenant(s). Informe --tenant-id explicitamente." >&2
    exit 1
  fi
fi

if [ -z "$INTEGRATION_ID" ]; then
  INTEGRATION_ID="$(pg_run -t -A -c "SELECT \"Id\" FROM integrations WHERE \"TenantId\" = '$TENANT_ID';" | sed '/^$/d')"
  INTEGRATION_COUNT=$(echo "$INTEGRATION_ID" | grep -c . || true)
  if [ "$INTEGRATION_COUNT" -ne 1 ]; then
    echo "Erro: existe(m) $INTEGRATION_COUNT integration(s) para o tenant $TENANT_ID. Informe --integration-id explicitamente." >&2
    exit 1
  fi
fi

if [ -z "$PROVIDER_ID" ]; then
  PROVIDER_ID="$(pg_run -t -A -c "SELECT \"ProviderId\" FROM integrations WHERE \"Id\" = '$INTEGRATION_ID';" | sed '/^$/d')"
fi

if [ -z "$TENANT_ID" ] || [ -z "$INTEGRATION_ID" ] || [ -z "$PROVIDER_ID" ]; then
  echo "Erro: não foi possível resolver TenantId/IntegrationId/ProviderId." >&2
  exit 1
fi

echo "TenantId:      $TENANT_ID"
echo "IntegrationId: $INTEGRATION_ID"
echo "ProviderId:    $PROVIDER_ID"
echo

# scope -> nome da variável de ambiente lida pelo Collector
# (ver apps/collector/Atua.Collector/Configuration/CollectorWorkerOptions.cs)
declare -a SCOPES=(
  "collector.command.claim:CollectorWorker__ClaimServiceToken"
  "collector.command.complete:CollectorWorker__CompleteServiceToken"
  "collector.eligibility.read:CollectorWorker__EligibilityServiceToken"
)

if [ "$REVOKE_EXISTING" = true ]; then
  echo "Revogando credenciais ativas existentes para este Tenant/Integration..."
  pg_run -q -c "
    UPDATE service_credentials
    SET \"RevokedAt\" = now()
    WHERE \"TenantId\" = '$TENANT_ID'
      AND \"IntegrationId\" = '$INTEGRATION_ID'
      AND \"RevokedAt\" IS NULL;
  "
  echo
fi

EXPORT_LINES=""

for entry in "${SCOPES[@]}"; do
  scope="${entry%%:*}"
  env_var="${entry##*:}"

  token="$(openssl rand -base64 48)"
  hash="$(printf '%s' "$token" | shasum -a 256 | awk '{print toupper($1)}')"
  id="$(python3 -c 'import uuid; print(uuid.uuid4())')"

  pg_run -q -c "
    INSERT INTO service_credentials (\"Id\", \"TenantId\", \"IntegrationId\", \"ProviderId\", \"TokenHash\", \"Scope\", \"RevokedAt\")
    VALUES ('$id', '$TENANT_ID', '$INTEGRATION_ID', '$PROVIDER_ID', '$hash', '$scope', NULL);
  "

  echo "OK  scope=$scope  id=$id"
  EXPORT_LINES="${EXPORT_LINES}export ${env_var}=\"${token}\"
"
done

echo
echo "=================================================================="
echo "Credenciais criadas. Adicione as linhas abaixo ao seu ~/.zshrc:"
echo "=================================================================="
printf '%s' "$EXPORT_LINES"
echo "=================================================================="
echo "Depois: source ~/.zshrc  (ou abra um novo terminal) e reinicie o Collector."
