#!/usr/bin/env bash
# Creates the three scoped service credentials required by the Channel.
# The database receives SHA-256 hashes only; plaintext tokens are saved to a
# private environment file outside the repository.

set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

TENANT_ID=""
INTEGRATION_ID=""
DRY_RUN=false
CONFIRM_WRITE=false
DB_ATTEMPTED=false

usage() {
  cat <<'USAGE'
Uso:
  seed-channel-credentials.sh --tenant-id <uuid> --integration-id <uuid> --dry-run
  seed-channel-credentials.sh --tenant-id <uuid> --integration-id <uuid> --confirm-write

Insere credenciais ativas para channel.command.claim,
channel.command.complete e channel.message.ingest em uma integração sem
nenhuma credencial Channel ativa. Não altera nem revoga credenciais
existentes. Para integração que já possui claim/complete, use
add-channel-message-ingest-credential.sh.

Os tokens plaintext são salvos em ~/.config/atua/channel/channel.env com
permissão 0600; o banco recebe somente o SHA-256. --dry-run valida os
argumentos sem acessar o banco, gerar tokens ou criar arquivos.
--confirm-write é obrigatório para gravar no banco indicado por
Postgres__ConnectionString.
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
ENV_FILE="$ENV_DIR/channel.env"
PENDING_FILE="$ENV_FILE.pending"

if [ "$DRY_RUN" = true ]; then
  echo "Dry-run: criaria channel.command.claim, channel.command.complete e channel.message.ingest."
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
  echo "O arquivo de segredos ou uma tentativa pendente já existe; nada será sobrescrito." >&2
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

CLAIM_TOKEN="$(openssl rand -base64 48 | tr -d '\n')"
COMPLETE_TOKEN="$(openssl rand -base64 48 | tr -d '\n')"
MESSAGE_INGEST_TOKEN="$(openssl rand -base64 48 | tr -d '\n')"
hash_token() {
  if command -v shasum >/dev/null 2>&1; then
    printf '%s' "$1" | shasum -a 256 | awk '{print toupper($1)}'
  elif command -v sha256sum >/dev/null 2>&1; then
    printf '%s' "$1" | sha256sum | awk '{print toupper($1)}'
  else
    echo "Erro: shasum ou sha256sum é necessário para gerar hashes." >&2
    return 1
  fi
}

new_uuid() {
  if command -v uuidgen >/dev/null 2>&1; then
    uuidgen | tr '[:upper:]' '[:lower:]'
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import uuid; print(uuid.uuid4())'
  else
    local value
    value="$(openssl rand -hex 16)"
    printf '%s-%s-%s-%s-%s\n' \
      "${value:0:8}" "${value:8:4}" "${value:12:4}" "${value:16:4}" "${value:20:12}"
  fi
}

CLAIM_HASH="$(hash_token "$CLAIM_TOKEN")"
COMPLETE_HASH="$(hash_token "$COMPLETE_TOKEN")"
MESSAGE_INGEST_HASH="$(hash_token "$MESSAGE_INGEST_TOKEN")"
CLAIM_CREDENTIAL_ID="$(new_uuid)"
COMPLETE_CREDENTIAL_ID="$(new_uuid)"
MESSAGE_INGEST_CREDENTIAL_ID="$(new_uuid)"

cleanup() {
  local exit_code=$?
  trap - EXIT
  if [ "$DB_ATTEMPTED" != true ]; then
    rm -f -- "$PENDING_FILE"
  fi
  unset CLAIM_TOKEN COMPLETE_TOKEN MESSAGE_INGEST_TOKEN CLAIM_HASH COMPLETE_HASH MESSAGE_INGEST_HASH
  exit "$exit_code"
}
trap cleanup EXIT

set -o noclobber
printf "export ChannelWorker__ClaimServiceToken='%s'\nexport ChannelWorker__CompleteServiceToken='%s'\nexport ChannelWorker__MessageIngestServiceToken='%s'\n" \
  "$CLAIM_TOKEN" "$COMPLETE_TOKEN" "$MESSAGE_INGEST_TOKEN" > "$PENDING_FILE"
set +o noclobber
chmod 600 "$PENDING_FILE"

DB_ATTEMPTED=true
if ! pg_run -X -q -v ON_ERROR_STOP=1 \
  -v tenant_id="$TENANT_ID" \
  -v integration_id="$INTEGRATION_ID" \
  -v claim_id="$CLAIM_CREDENTIAL_ID" \
  -v complete_id="$COMPLETE_CREDENTIAL_ID" \
  -v message_ingest_id="$MESSAGE_INGEST_CREDENTIAL_ID" \
  -v claim_hash="$CLAIM_HASH" \
  -v complete_hash="$COMPLETE_HASH" \
  -v message_ingest_hash="$MESSAGE_INGEST_HASH" <<'SQL'
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

    IF EXISTS (
        SELECT 1
        FROM service_credentials
        WHERE "TenantId" = target_tenant
          AND "IntegrationId" = target_integration
          AND "Scope" IN ('channel.command.claim', 'channel.command.complete', 'channel.message.ingest')
          AND "RevokedAt" IS NULL
    ) THEN
        RAISE EXCEPTION 'Já existe uma credencial Channel ativa para esta integração.';
    END IF;
END
$guard$;

INSERT INTO service_credentials
    ("Id", "TenantId", "IntegrationId", "ProviderId", "TokenHash", "Scope", "RevokedAt")
SELECT :'claim_id'::uuid, i."TenantId", i."Id", i."ProviderId", :'claim_hash',
       'channel.command.claim', NULL
FROM integrations AS i
WHERE i."Id" = :'integration_id'::uuid
  AND i."TenantId" = :'tenant_id'::uuid;

INSERT INTO service_credentials
    ("Id", "TenantId", "IntegrationId", "ProviderId", "TokenHash", "Scope", "RevokedAt")
SELECT :'complete_id'::uuid, i."TenantId", i."Id", i."ProviderId", :'complete_hash',
       'channel.command.complete', NULL
FROM integrations AS i
WHERE i."Id" = :'integration_id'::uuid
  AND i."TenantId" = :'tenant_id'::uuid;

INSERT INTO service_credentials
    ("Id", "TenantId", "IntegrationId", "ProviderId", "TokenHash", "Scope", "RevokedAt")
SELECT :'message_ingest_id'::uuid, i."TenantId", i."Id", i."ProviderId", :'message_ingest_hash',
       'channel.message.ingest', NULL
FROM integrations AS i
WHERE i."Id" = :'integration_id'::uuid
  AND i."TenantId" = :'tenant_id'::uuid;

COMMIT;
SQL
then
  echo "A gravação no banco falhou ou ficou sem confirmação." >&2
  echo "Mantenha o arquivo pendente: $PENDING_FILE" >&2
  echo "Verifique os escopos no banco antes de tentar novamente; o resultado pode ser indeterminado." >&2
  exit 1
fi

mv -n "$PENDING_FILE" "$ENV_FILE"
if [ -e "$PENDING_FILE" ]; then
  echo "As credenciais foram criadas, mas o arquivo final já existe." >&2
  echo "Não repita a operação; preserve o arquivo pendente: $PENDING_FILE" >&2
  exit 1
fi

unset CLAIM_TOKEN COMPLETE_TOKEN MESSAGE_INGEST_TOKEN CLAIM_HASH COMPLETE_HASH MESSAGE_INGEST_HASH
trap - EXIT
echo "Credenciais Channel criadas para tenant=$TENANT_ID integration=$INTEGRATION_ID."
echo "Scopes: channel.command.claim, channel.command.complete, channel.message.ingest."
echo "Segredos salvos fora do repositório com permissões 0600: $ENV_FILE"
echo "Para carregar nesta sessão: source '$ENV_FILE'"
