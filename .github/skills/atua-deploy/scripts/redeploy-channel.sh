#!/usr/bin/env bash
# .github/skills/atua-deploy/scripts/redeploy-channel.sh
#
# Redeploy "a quente" do Channel WhatsApp (Node/Baileys) já instalado e
# rodando na instância EC2 da API (ver deploy-channel.sh para a primeira
# instalação — este script pressupõe que o bootstrap idempotente, a unit
# systemd e o /etc/atua-channel.env JÁ existem na instância).
#
# Ciclo: (1) build local + `pnpm deploy --prod`; (2) sincroniza o bundle
# via SSH/rsync SEM tocar na sessão Baileys (/var/lib/atua-channel/session,
# fora da árvore do bundle — nunca é removida por este script); (3) para o
# serviço, troca o bundle, reinicia; (4) confere por um arquivo-sentinela
# (checksum do dist/main.js) que o bundle remoto realmente mudou;
# (5) mostra o status systemd.
#
# Pré-requisitos: os mesmos de deploy-channel.sh (chave SSH, IP liberado
# no SG). NÃO reescreve /etc/atua-channel.env nem o unit systemd — use
# deploy-channel.sh se os tokens precisarem mudar ou se o serviço ainda
# não existir (ex.: logo após um `make up`, instância recriada do zero).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

SSH_KEY="${SSH_KEY:-$HOME/.ssh/atua-mvp-key.pem}"

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

# Pré-checagem: se o serviço ainda não existir, oriente a usar deploy-channel.sh
# (primeira instalação) em vez de falhar de forma confusa no meio do processo.
if ! ssh -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
  "sudo test -f /etc/systemd/system/atua-channel.service"; then
  echo "FATAL: atua-channel.service não existe na instância. Rode 'make deploy-channel' (primeira instalação) antes." >&2
  exit 1
fi

BUNDLE_DIR="$(mktemp -d /tmp/atua-channel-bundle.XXXXXX)"
trap 'rm -rf "$BUNDLE_DIR"' EXIT

echo "==> [1/5] Compilando @atua/channel-node (tsc) e gerando bundle de produção (pnpm deploy --prod)..."
pnpm --filter @atua/channel-node build
pnpm --filter @atua/channel-node deploy --prod "$BUNDLE_DIR"

LOCAL_SUM="$( (md5sum "$BUNDLE_DIR/dist/main.js" 2>/dev/null || true) | awk '{print $1}')"
if [ -z "$LOCAL_SUM" ]; then
  LOCAL_SUM="$(md5 -q "$BUNDLE_DIR/dist/main.js" 2>/dev/null || true)"
fi
echo "    MD5 local de dist/main.js: $LOCAL_SUM"

echo "==> [1b/5] Publicando o bundle em s3://atua-\$(ACCOUNT_ID)-releases/channel-latest/ (restauração automática no próximo 'make up')..."
ACCOUNT_ID="$(account_id)"
aws s3 sync "$BUNDLE_DIR/" "s3://atua-${ACCOUNT_ID}-releases/channel-latest/" --region "$REGION" --delete

echo "==> [2/5] Parando atua-channel antes de sincronizar (evita process rodando sobre arquivos sendo trocados)..."
ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" "sudo systemctl stop atua-channel"

echo "==> [3/5] Sincronizando dist/ + node_modules/ para /opt/atua-channel/app (sessão Baileys preservada, fora dessa árvore)..."
if command -v rsync >/dev/null 2>&1; then
  rsync -az --delete \
    --exclude 'dist/.gitkeep' \
    "$BUNDLE_DIR"/dist "$BUNDLE_DIR"/node_modules "$BUNDLE_DIR"/package.json \
    -e "ssh -o StrictHostKeyChecking=accept-new -i $SSH_KEY" \
    "ec2-user@$INSTANCE_IP:/opt/atua-channel/app/"
else
  ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" "rm -rf /opt/atua-channel/app/dist /opt/atua-channel/app/node_modules"
  scp -o StrictHostKeyChecking=accept-new -r -i "$SSH_KEY" "$BUNDLE_DIR"/dist "$BUNDLE_DIR"/node_modules "$BUNDLE_DIR"/package.json \
    "ec2-user@$INSTANCE_IP:/opt/atua-channel/app/"
fi

echo "==> [4/5] Verificando MD5 remoto de dist/main.js e reiniciando o serviço..."
REMOTE_SUM="$(ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
  "md5sum /opt/atua-channel/app/dist/main.js 2>/dev/null | awk '{print \$1}' || md5 -q /opt/atua-channel/app/dist/main.js")"
echo "    MD5 remoto de dist/main.js: $REMOTE_SUM"

if [ -n "$LOCAL_SUM" ] && [ "$LOCAL_SUM" != "$REMOTE_SUM" ]; then
  echo "FATAL: MD5 local ($LOCAL_SUM) != MD5 remoto ($REMOTE_SUM) — o bundle NÃO foi atualizado corretamente." >&2
  ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" "sudo systemctl start atua-channel" || true
  exit 1
fi
echo "    OK: bundle remoto confere com o local."

ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
  "sudo systemctl start atua-channel && sleep 2 && sudo systemctl is-active atua-channel"

echo "==> [5/5] Status final do serviço:"
ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" "sudo systemctl status atua-channel --no-pager | head -10"

echo "Redeploy do Channel concluído em $INSTANCE_IP."
