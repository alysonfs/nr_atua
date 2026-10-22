#!/usr/bin/env bash
set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TENANT_ID=""
INTEGRATION_ID=""
DRY_RUN=false
CONFIRM_WRITE=false
DB_ATTEMPTED=false
PENDING_FILE=""
SESSION_REPORT_TOKEN=""
SESSION_REPORT_HASH=""

usage() {
  cat <<'USAGE'
Uso:
  add-channel-session-report-credential.sh \
    --tenant-id <uuid> --integration-id <uuid> --dry-run
  add-channel-session-report-credential.sh \
    --tenant-id <uuid> --integration-id <uuid> --confirm-write

Adiciona somente o escopo channel.session.report à integração informada
(ADR-049 — status da sessão Baileys/QR exibido nas Configurações do
Office). Exige que ela já possua exatamente uma credencial ativa de
channel.command.claim e channel.command.complete. Não revoga nem altera
credenciais existentes. Este escopo é OPCIONAL para o Channel operar (ver
apps/channel-node/src/config.ts, SessionReportServiceToken) — sem ele o
Worker ainda publica mensagens e mídia normalmente, apenas não reporta o
estado da sessão (qr_pending/connected/etc.) para o Office.

O token plaintext é salvo em
~/.config/atua/channel/session-report.env com permissão 0600; o banco
recebe somente o SHA-256. --dry-run valida os argumentos e não acessa o
banco, gera token ou cria arquivos. --confirm-write é obrigatório para
gravar no banco indicado por Postgres__ConnectionString.
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --tenant-id)
      [ "$#" -ge 2 ] || { echo "Falta valor para --tenant-id." >&2; exit 2; }
      TENANT_ID="$2"
      shift 2
      ;;
    --integration-id)
      [ "$#" -ge 2 ] || { echo "Falta valor para --integration-id." >&2; exit 2; }
      INTEGRATION_ID="$2"
      shift 2
      ;;
    --dry-run)
      DRY_RUN=true
      shift
      ;;
    --confirm-write)
      CONFIRM_WRITE=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Argumento desconhecido: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if ! [[ "$TENANT_ID" =~ ^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$ ]]; then
  echo "Informe --tenant-id com um UUID válido." >&2
  exit 2
fi

if ! [[ "$INTEGRATION_ID" =~ ^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$ ]]; then
  echo "Informe --integration-id com um UUID válido." >&2
  exit 2
fi

if [ "$DRY_RUN" = true ] && [ "$CONFIRM_WRITE" = true ]; then
  echo "Use --dry-run ou --confirm-write, não ambos." >&2
  exit 2
fi

ENV_DIR="${HOME:?HOME precisa estar definida}/.config/atua/channel"
ENV_FILE="$ENV_DIR/session-report.env"
PENDING_FILE="$ENV_FILE.pending"

if [ "$DRY_RUN" = true ]; then
  echo "Dry-run: adicionaria somente channel.session.report."
  echo "Tenant: $TENANT_ID"
  echo "Integration: $INTEGRATION_ID"
  echo "Arquivo privado: $ENV_FILE"
  echo "Nenhum acesso ao banco, token ou arquivo foi criado."
  exit 0
fi

if [ "$CONFIRM_WRITE" != true ]; then
  echo "Operação recusada: use --dry-run ou informe --confirm-write." >&2
  exit 2
fi

if [ -L "$HOME" ] || [ -L "$HOME/.config" ] || [ -L "$HOME/.config/atua" ] || [ -L "$ENV_DIR" ]; then
  echo "O caminho do diretório de segredos contém um link simbólico; operação recusada." >&2
  exit 1
fi

if [ -e "$ENV_FILE" ] || [ -L "$ENV_FILE" ] || [ -e "$PENDING_FILE" ] || [ -L "$PENDING_FILE" ]; then
  echo "O arquivo de segredo ou uma tentativa pendente já existe; nada será sobrescrito." >&2
  exit 1
fi

# shellcheck source=lib-pg-conn.sh
source "$SCRIPT_DIR/lib-pg-conn.sh"

mkdir -p "$ENV_DIR"
if [ -L "$ENV_DIR" ] || [ ! -d "$ENV_DIR" ] || [ ! -O "$ENV_DIR" ]; then
  echo "O diretório de segredos não é um diretório local pertencente ao usuário." >&2
  exit 1
fi
chmod 700 "$ENV_DIR"

cleanup() {
  local exit_code=$?
  trap - EXIT
  if [ "$DB_ATTEMPTED" != true ]; then
    rm -f -- "$PENDING_FILE"
  fi
  unset SESSION_REPORT_TOKEN SESSION_REPORT_HASH
  unset ATUA_CHANNEL_TENANT_ID ATUA_CHANNEL_INTEGRATION_ID
  unset ATUA_CHANNEL_CREDENTIAL_ID ATUA_CHANNEL_TOKEN_HASH
  exit "$exit_code"
}
trap cleanup EXIT

SESSION_REPORT_TOKEN="$(openssl rand -base64 48 | tr -d '\n')"
if command -v shasum >/dev/null 2>&1; then
  SESSION_REPORT_HASH="$(printf '%s' "$SESSION_REPORT_TOKEN" | shasum -a 256 | awk '{print toupper($1)}')"
elif command -v sha256sum >/dev/null 2>&1; then
  SESSION_REPORT_HASH="$(printf '%s' "$SESSION_REPORT_TOKEN" | sha256sum | awk '{print toupper($1)}')"
else
  echo "Erro: shasum ou sha256sum é necessário para gerar o hash." >&2
  exit 1
fi

if command -v uuidgen >/dev/null 2>&1; then
  CREDENTIAL_ID="$(uuidgen | tr '[:upper:]' '[:lower:]')"
elif command -v python3 >/dev/null 2>&1; then
  CREDENTIAL_ID="$(python3 -c 'import uuid; print(uuid.uuid4())')"
else
  echo "Erro: uuidgen ou python3 é necessário para gerar o identificador." >&2
  exit 1
fi

set -o noclobber
printf "export ChannelWorker__SessionReportServiceToken='%s'\n" \
  "$SESSION_REPORT_TOKEN" > "$PENDING_FILE"
set +o noclobber
chmod 600 "$PENDING_FILE"

export ATUA_CHANNEL_TENANT_ID="$TENANT_ID"
export ATUA_CHANNEL_INTEGRATION_ID="$INTEGRATION_ID"
export ATUA_CHANNEL_CREDENTIAL_ID="$CREDENTIAL_ID"
export ATUA_CHANNEL_TOKEN_HASH="$SESSION_REPORT_HASH"

DB_ATTEMPTED=true
if ! pg_run -X -q -v ON_ERROR_STOP=1 <<'SQL'
\getenv tenant_id ATUA_CHANNEL_TENANT_ID
\getenv integration_id ATUA_CHANNEL_INTEGRATION_ID
\getenv credential_id ATUA_CHANNEL_CREDENTIAL_ID
\getenv token_hash ATUA_CHANNEL_TOKEN_HASH

BEGIN;

SELECT pg_advisory_xact_lock(hashtextextended(
    'atua.channel.credentials:' || :'tenant_id' || ':' || :'integration_id', 0));

SET LOCAL atua.tenant_id = :'tenant_id';
SET LOCAL atua.integration_id = :'integration_id';

DO $guard$
DECLARE
    target_tenant uuid := current_setting('atua.tenant_id')::uuid;
    target_integration uuid := current_setting('atua.integration_id')::uuid;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM integrations
        WHERE "Id" = target_integration
          AND "TenantId" = target_tenant
    ) THEN
        RAISE EXCEPTION 'A integração não pertence ao tenant informado.';
    END IF;

    IF (
        SELECT count(*)
        FROM service_credentials
        WHERE "TenantId" = target_tenant
          AND "IntegrationId" = target_integration
          AND "Scope" = 'channel.command.claim'
          AND "RevokedAt" IS NULL
    ) <> 1 OR (
        SELECT count(*)
        FROM service_credentials
        WHERE "TenantId" = target_tenant
          AND "IntegrationId" = target_integration
          AND "Scope" = 'channel.command.complete'
          AND "RevokedAt" IS NULL
    ) <> 1 THEN
        RAISE EXCEPTION 'A integração deve possuir exatamente uma credencial ativa de claim e complete.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM service_credentials
        WHERE "TenantId" = target_tenant
          AND "IntegrationId" = target_integration
          AND "Scope" = 'channel.session.report'
          AND "RevokedAt" IS NULL
    ) THEN
        RAISE EXCEPTION 'Já existe uma credencial ativa de channel.session.report para esta integração.';
    END IF;
END
$guard$;

INSERT INTO service_credentials
    ("Id", "TenantId", "IntegrationId", "ProviderId", "TokenHash", "Scope", "RevokedAt")
SELECT :'credential_id'::uuid, i."TenantId", i."Id", i."ProviderId", :'token_hash',
       'channel.session.report', NULL
FROM integrations AS i
WHERE i."Id" = :'integration_id'::uuid
  AND i."TenantId" = :'tenant_id'::uuid;

COMMIT;
SQL
then
  echo "A gravação no banco falhou ou ficou sem confirmação." >&2
  echo "Mantenha o arquivo pendente: $PENDING_FILE" >&2
  echo "Verifique o escopo no banco antes de tentar novamente; o resultado pode ser indeterminado." >&2
  exit 1
fi

mv -n "$PENDING_FILE" "$ENV_FILE"
if [ -e "$PENDING_FILE" ]; then
  echo "A credencial foi criada, mas o arquivo final já existe." >&2
  echo "Não repita a operação; preserve o arquivo pendente: $PENDING_FILE" >&2
  exit 1
fi

unset SESSION_REPORT_TOKEN SESSION_REPORT_HASH
trap - EXIT
echo "Credencial channel.session.report criada para a integração informada."
echo "Token salvo fora do repositório com permissão 0600: $ENV_FILE"
echo "Para carregar nesta sessão: source '$ENV_FILE'"
