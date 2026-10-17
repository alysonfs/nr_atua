#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../atua-deploy/scripts/common.sh
source "$DIR/../../atua-deploy/scripts/common.sh"

SSH_KEY="${SSH_KEY:-$HOME/.ssh/atua-mvp-key.pem}"
STACK_NAME="${STACK_NAME:-AtuaComputeStack}"
OUTPUT_KEY="${EC2_OUTPUT_KEY:-ApiPublicIp}"
SSH_USER="${SSH_USER:-ec2-user}"

usage() {
  echo "Uso: $0 <comando-remoto>" >&2
  echo 'Exemplo: '"$0"' "echo alyson"' >&2
}

if [ "$#" -eq 0 ]; then
  usage
  exit 1
fi

INSTANCE_IP="$(aws cloudformation describe-stacks   --stack-name "$STACK_NAME"   --region "$REGION"   --query "Stacks[0].Outputs[?OutputKey=='$OUTPUT_KEY'].OutputValue | [0]"   --output text)"

if [ -z "$INSTANCE_IP" ] || [ "$INSTANCE_IP" = "None" ]; then
  echo "FATAL: não foi possível obter o IP público da instância ($OUTPUT_KEY) no stack $STACK_NAME." >&2
  exit 1
fi

REMOTE_CMD="$*"
set +e
ssh -i "$SSH_KEY"   -o StrictHostKeyChecking=accept-new   -o ConnectTimeout=10   "$SSH_USER@$INSTANCE_IP"   "$REMOTE_CMD"
ssh_exit=$?
set -e

if [ "$ssh_exit" -eq 255 ]; then
  echo "ERRO: timeout ou falha de conexão SSH com $SSH_USER@$INSTANCE_IP." >&2
  echo "Verifique se o IP da instância mudou e autorize seu IP atual no Security Group na porta 22. Este script não altera o Security Group." >&2
fi

exit "$ssh_exit"
