#!/usr/bin/env bash
# .github/skills/atua-deploy/scripts/deploy-channel.sh
#
# Primeira instalação (idempotente) do Channel WhatsApp (Node/Baileys,
# ADR-035/038/049/050) na MESMA instância EC2 da API ("ApiPublicIp" via
# CloudFormation Outputs — não existe mais Elastic IP/instância dedicada).
#
# Idempotente por design: seguro de rodar de novo após `make up` (a
# instância é recriada do zero; o runtime Node/usuário/diretórios/unit já
# são restaurados pelo UserData da CDK — ver channelBootstrapCommands em
# infra/lib/atua-compute-stack.ts — mas o arquivo de credenciais
# /etc/atua-channel.env NUNCA é restaurado automaticamente, por decisão
# deliberada de não exigir nenhuma permissão IAM nova para o Channel. Este
# script sempre reaplica os passos de bootstrap remoto e reescreve o
# arquivo de credenciais, então funciona tanto numa instância "zero" quanto
# numa já preparada por um `make up` anterior a esta mudança de CDK.
#
# Pré-requisitos:
#   - Chave SSH local em ~/.ssh/atua-mvp-key.pem (rode 'make retrieve-keypair').
#   - Seu IP público autorizado no Security Group da instância (porta 22).
#   - Variáveis de ambiente locais com os tokens de serviço do Channel já
#     exportadas no shell (ver docs/guides/channel-node-deploy-ec2.md e
#     infra/scripts/seed-channel-credentials.sh /
#     add-channel-media-ingest-credential.sh /
#     add-channel-session-report-credential.sh):
#       ChannelWorker__ClaimServiceToken          (obrigatório)
#       ChannelWorker__CompleteServiceToken        (obrigatório)
#       ChannelWorker__MessageIngestServiceToken   (obrigatório)
#       ChannelWorker__MediaIngestServiceToken      (obrigatório)
#       ChannelWorker__SessionReportServiceToken    (opcional — ADR-049)
#   - pnpm instalado localmente (roda `pnpm --filter @atua/channel-node
#     build` e `pnpm --filter @atua/channel-node deploy --prod`).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.
# NÃO grava nada no banco (as credenciais já devem existir — ver scripts
# acima, rodados separadamente e sob aprovação prévia).

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

SSH_KEY="${SSH_KEY:-$HOME/.ssh/atua-mvp-key.pem}"
# URL que o Channel usa para falar com a API NA MESMA instância — nunca
# reaproveita o ChannelWorker__ApiBaseUrl do shell local (que normalmente
# aponta para o túnel SSH de teste local, ver channel-node-teste-local.md).
REMOTE_API_BASE_URL="${CHANNEL_API_BASE_URL:-http://127.0.0.1:80}"

for var in ChannelWorker__ClaimServiceToken ChannelWorker__CompleteServiceToken \
  ChannelWorker__MessageIngestServiceToken ChannelWorker__MediaIngestServiceToken; do
  if [ -z "${!var:-}" ]; then
    echo "FATAL: $var não está definida no ambiente local. Exporte os tokens antes de rodar (ver SKILL.md)." >&2
    exit 1
  fi
done

INSTANCE_IP="$(aws cloudformation describe-stacks --stack-name AtuaComputeStack --region "$REGION" \
  --query "Stacks[0].Outputs[?OutputKey=='ApiPublicIp'].OutputValue" --output text)"

if [ -z "$INSTANCE_IP" ] || [ "$INSTANCE_IP" = "None" ]; then
  echo "FATAL: não foi possível obter o IP público da API (ApiPublicIp) — AtuaComputeStack está no ar?" >&2
  exit 1
fi

if [ ! -f "$SSH_KEY" ]; then
  echo "FATAL: chave SSH não encontrada em $SSH_KEY. Rode 'make retrieve-keypair' primeiro." >&2
  exit 1
fi

BUNDLE_DIR="$(mktemp -d /tmp/atua-channel-bundle.XXXXXX)"
ENV_FILE_LOCAL="$(mktemp /tmp/atua-channel-env.XXXXXX)"
chmod 600 "$ENV_FILE_LOCAL"
cleanup() {
  rm -rf "$BUNDLE_DIR"
  rm -f "$ENV_FILE_LOCAL"
}
trap cleanup EXIT

echo "==> [1/6] Compilando @atua/channel-node (tsc) e gerando bundle de produção (pnpm deploy --prod)..."
pnpm --filter @atua/channel-node build
pnpm --filter @atua/channel-node deploy --prod "$BUNDLE_DIR"

echo "==> [2/6] Bootstrap remoto idempotente: runtime Node 22, usuário sem privilégio, diretórios, unit systemd..."
ssh -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" 'bash -s' <<'REMOTE_EOF'
set -euo pipefail

if ! command -v node >/dev/null 2>&1 || ! node -v | grep -q '^v22\.'; then
  sudo dnf install -y nodejs22 || sudo yum install -y nodejs22 || true
fi
node -v | grep -q '^v22\.' || { echo "FATAL: node 22.x não instalado corretamente (dnf/yum falhou)." >&2; exit 1; }

id -u atua-channel >/dev/null 2>&1 || sudo useradd --system --no-create-home --shell /sbin/nologin atua-channel

sudo mkdir -p /opt/atua-channel/app
sudo chown ec2-user:ec2-user /opt/atua-channel/app
sudo mkdir -p /var/lib/atua-channel/session
sudo chown atua-channel:atua-channel /var/lib/atua-channel/session
sudo chmod 700 /var/lib/atua-channel/session

echo "Bootstrap remoto OK."
REMOTE_EOF

echo "==> [3/6] Enviando unit systemd (infra/systemd/atua-channel.service, fonte de verdade)..."
scp -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" \
  "$DIR/../../../../infra/systemd/atua-channel.service" \
  "ec2-user@$INSTANCE_IP:/tmp/atua-channel.service"
ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
  "sudo mv /tmp/atua-channel.service /etc/systemd/system/atua-channel.service && sudo systemctl daemon-reload && sudo systemctl enable atua-channel"

echo "==> [4/6] Escrevendo /etc/atua-channel.env (permissão 600, nunca versionado)..."
{
  echo "# Gerado por deploy-channel.sh em $(date -u +%Y-%m-%dT%H:%M:%SZ). Não editar manualmente."
  echo "ChannelWorker__ApiBaseUrl=$REMOTE_API_BASE_URL"
  echo "ChannelWorker__ClaimServiceToken=$ChannelWorker__ClaimServiceToken"
  echo "ChannelWorker__CompleteServiceToken=$ChannelWorker__CompleteServiceToken"
  echo "ChannelWorker__MessageIngestServiceToken=$ChannelWorker__MessageIngestServiceToken"
  echo "ChannelWorker__MediaIngestServiceToken=$ChannelWorker__MediaIngestServiceToken"
  if [ -n "${ChannelWorker__SessionReportServiceToken:-}" ]; then
    echo "ChannelWorker__SessionReportServiceToken=$ChannelWorker__SessionReportServiceToken"
  fi
  echo "ChannelWorker__SessionPath=/var/lib/atua-channel/session"
  echo "ChannelWorker__PrintQr=false"
} > "$ENV_FILE_LOCAL"

scp -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "$ENV_FILE_LOCAL" "ec2-user@$INSTANCE_IP:/tmp/atua-channel.env"
ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
  "sudo mv /tmp/atua-channel.env /etc/atua-channel.env && sudo chown root:root /etc/atua-channel.env && sudo chmod 600 /etc/atua-channel.env"

echo "==> [5a/6] Publicando o bundle em s3://atua-\$(ACCOUNT_ID)-releases/channel-latest/ (restauração automática no próximo 'make up' via UserData/channelBootstrapCommands)..."
ACCOUNT_ID="$(account_id)"
aws s3 sync "$BUNDLE_DIR/" "s3://atua-${ACCOUNT_ID}-releases/channel-latest/" --region "$REGION" --delete

echo "==> [5b/6] Enviando bundle (dist + node_modules de produção) para /opt/atua-channel/app ..."
# Layout com profundidade suficiente (/opt/atua-channel/app/dist) é
# OBRIGATÓRIO: ChannelConfig valida que ChannelWorker__SessionPath fique
# fora do "repositório" calculado a partir de 3 níveis acima de dist/ (ver
# apps/channel-node/src/config.ts). Com WorkingDirectory=/opt/atua-channel/app,
# a raiz calculada é /opt — e /var/lib/atua-channel/session fica fora dela.
# NÃO achate essa estrutura (ex.: /opt/atua-channel/dist direto), ou todo
# ChannelWorker__SessionPath passará a ser rejeitado como "dentro do repo".
if command -v rsync >/dev/null 2>&1; then
  rsync -az --delete "$BUNDLE_DIR"/ -e "ssh -o StrictHostKeyChecking=accept-new -i $SSH_KEY" "ec2-user@$INSTANCE_IP:/opt/atua-channel/app/"
else
  ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" "rm -rf /opt/atua-channel/app/dist /opt/atua-channel/app/node_modules"
  scp -o StrictHostKeyChecking=accept-new -r -i "$SSH_KEY" "$BUNDLE_DIR"/dist "$BUNDLE_DIR"/node_modules "$BUNDLE_DIR"/package.json \
    "ec2-user@$INSTANCE_IP:/opt/atua-channel/app/"
fi

echo "==> [6/6] Reiniciando atua-channel e verificando status..."
ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
  "sudo systemctl restart atua-channel && sleep 2 && sudo systemctl is-active atua-channel && sudo systemctl status atua-channel --no-pager | head -10"

echo
echo "Deploy do Channel concluído em $INSTANCE_IP."
echo "Acompanhe os logs com 'make logs-channel-follow' e pareie o WhatsApp pelo"
echo "Office (Configurações) em até ~60s após o estado 'qr_pending' aparecer (ADR-049)."
