#!/usr/bin/env bash
# .github/skills/atua-deploy/scripts/redeploy-live.sh <api|collector>
#
# Automatiza o ciclo completo de redeploy "a quente" de um serviço .NET
# (API ou Collector) já rodando numa instância EC2 viva, SEM recriar a
# instância (isso já é feito por `make up`, que reprocessa o user-data).
#
# Ciclo automatizado (antes feito manualmente, comando a comando):
#   1. dotnet publish (Release, RID correto por serviço)
#   2. aws s3 sync do artefato para o prefixo de release correspondente
#   3. SSH na instância: para o serviço, força resync (rm + sync, contorna
#      a heurística de tamanho/timestamp do `aws s3 sync` que pode pular
#      arquivos alterados), reinicia o serviço
#   4. Verifica por MD5 que o artefato remoto bate com o local (garante que
#      o passo 3 realmente atualizou o binário, não só o timestamp)
#   5. Mostra o status do serviço systemd após o restart
#
# Pré-requisitos:
#   - Chave SSH local em ~/.ssh/atua-mvp-key.pem (rode 'make retrieve-keypair'
#     uma vez se ainda não tiver).
#   - Seu IP público autorizado no Security Group da instância (regra
#     "SSH administrativo" ou equivalente na porta 22). Este script NÃO abre
#     regras de SG automaticamente — se a conexão falhar por timeout, abra
#     manualmente e revogue depois (ver README de infra/).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

SERVICE="${1:-}"
SSH_KEY="${SSH_KEY:-$HOME/.ssh/atua-mvp-key.pem}"

usage() {
  echo "Uso: $0 <api|collector>" >&2
  exit 1
}

[ -n "$SERVICE" ] || usage

case "$SERVICE" in
  api)
    PROJECT_PATH="../apps/api/Atua.Api/Atua.Api.csproj"
    PUBLISH_ARGS=(-c Release)
    PUBLISH_DIR="/tmp/atua-api-publish"
    S3_PREFIX="latest"
    REMOTE_DIR="/opt/atua-api"
    SYSTEMD_UNIT="atua-api"
    OUTPUT_KEY="ApiPublicIp"
    DLL_NAME="Atua.Api.dll"
    ;;
  collector)
    PROJECT_PATH="../apps/collector/Atua.Collector/Atua.Collector.csproj"
    PUBLISH_ARGS=(-c Release -r linux-x64 --self-contained false)
    PUBLISH_DIR="/tmp/atua-collector-publish"
    S3_PREFIX="collector-latest"
    REMOTE_DIR="/opt/atua-collector"
    SYSTEMD_UNIT="atua-collector"
    OUTPUT_KEY="CollectorPublicIp"
    DLL_NAME="Atua.Collector.dll"
    ;;
  *)
    usage
    ;;
esac

ACCOUNT_ID="$(account_id)"
INSTANCE_IP="$(aws cloudformation describe-stacks --stack-name AtuaComputeStack --region "$REGION" \
  --query "Stacks[0].Outputs[?OutputKey=='$OUTPUT_KEY'].OutputValue" --output text)"

if [ -z "$INSTANCE_IP" ] || [ "$INSTANCE_IP" = "None" ]; then
  echo "FATAL: não foi possível obter o IP público da instância ($OUTPUT_KEY) — AtuaComputeStack está no ar?" >&2
  exit 1
fi

if [ ! -f "$SSH_KEY" ]; then
  echo "FATAL: chave SSH não encontrada em $SSH_KEY. Rode 'make retrieve-keypair' primeiro." >&2
  exit 1
fi

echo "==> [1/5] Compilando $SERVICE (${PUBLISH_ARGS[*]})..."
dotnet publish "$PROJECT_PATH" "${PUBLISH_ARGS[@]}" -o "$PUBLISH_DIR"

LOCAL_MD5="$( (md5sum "$PUBLISH_DIR/$DLL_NAME" 2>/dev/null || true) | awk '{print $1}')"
if [ -z "$LOCAL_MD5" ]; then
  LOCAL_MD5="$(md5 -q "$PUBLISH_DIR/$DLL_NAME" 2>/dev/null || true)"
fi
echo "    MD5 local do $DLL_NAME: $LOCAL_MD5"

echo "==> [2/5] Publicando artefato em s3://atua-$ACCOUNT_ID-releases/$S3_PREFIX/ ..."
aws s3 sync "$PUBLISH_DIR/" "s3://atua-$ACCOUNT_ID-releases/$S3_PREFIX/" \
  --region "$REGION" --delete

echo "==> [3/5] Sincronizando e reiniciando $SYSTEMD_UNIT em $INSTANCE_IP ..."
# shellcheck disable=SC2087
ssh -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
  "REMOTE_DIR='$REMOTE_DIR' S3_PREFIX='$S3_PREFIX' REGION='$REGION' SYSTEMD_UNIT='$SYSTEMD_UNIT' bash -s" <<'REMOTE_EOF'
set -euo pipefail
sudo systemctl stop "$SYSTEMD_UNIT"
# Força resync completo (rm + sync) para contornar a heurística de
# tamanho/timestamp do 'aws s3 sync', que pode pular arquivos alterados
# quando tamanho e timestamp aparentam não ter mudado.
sudo find "$REMOTE_DIR" -maxdepth 1 -type f \( -name '*.dll' -o -name '*.pdb' -o -name '*.json' \) -delete
sudo aws s3 sync "s3://atua-$(aws sts get-caller-identity --query Account --output text --region $REGION)-releases/$S3_PREFIX/" "$REMOTE_DIR/" --region "$REGION"
sudo systemctl start "$SYSTEMD_UNIT"
sleep 2
sudo systemctl is-active "$SYSTEMD_UNIT"
REMOTE_EOF

echo "==> [4/5] Verificando MD5 remoto..."
REMOTE_MD5="$(ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" "md5sum '$REMOTE_DIR/$DLL_NAME' | awk '{print \$1}'")"
echo "    MD5 remoto do $DLL_NAME: $REMOTE_MD5"

if [ "$LOCAL_MD5" != "$REMOTE_MD5" ]; then
  echo "FATAL: MD5 local ($LOCAL_MD5) != MD5 remoto ($REMOTE_MD5) — o artefato NÃO foi atualizado corretamente." >&2
  exit 1
fi
echo "    OK: artefato remoto confere com o local."

echo "==> [5/5] Status final do serviço:"
ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" "sudo systemctl status $SYSTEMD_UNIT --no-pager | head -10"

echo "Redeploy de '$SERVICE' concluído com sucesso em $INSTANCE_IP."
