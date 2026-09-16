#!/usr/bin/env bash
# .github/skills/atua-deploy/scripts/tail-logs.sh <api|collector> [--follow]
#
# Padroniza o acesso SSH + journalctl às instâncias vivas, resolvendo o IP
# público via CloudFormation em vez de depender de IPs anotados manualmente
# (que mudam a cada `make up`/recriação da instância).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

SERVICE="${1:-}"
FOLLOW="${2:-}"
SSH_KEY="${SSH_KEY:-$HOME/.ssh/atua-mvp-key.pem}"

usage() {
  echo "Uso: $0 <api|collector> [--follow]" >&2
  exit 1
}

case "$SERVICE" in
  api) OUTPUT_KEY="ApiPublicIp"; SYSTEMD_UNIT="atua-api" ;;
  collector) OUTPUT_KEY="CollectorPublicIp"; SYSTEMD_UNIT="atua-collector" ;;
  *) usage ;;
esac

INSTANCE_IP="$(aws cloudformation describe-stacks --stack-name AtuaComputeStack --region "$REGION" \
  --query "Stacks[0].Outputs[?OutputKey=='$OUTPUT_KEY'].OutputValue" --output text)"

[ -n "$INSTANCE_IP" ] && [ "$INSTANCE_IP" != "None" ] || {
  echo "FATAL: não foi possível obter o IP público da instância ($OUTPUT_KEY)." >&2
  exit 1
}

if [ "$FOLLOW" = "--follow" ]; then
  exec ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
    "sudo journalctl -u $SYSTEMD_UNIT -f"
else
  exec ssh -o StrictHostKeyChecking=accept-new -i "$SSH_KEY" "ec2-user@$INSTANCE_IP" \
    "sudo journalctl -u $SYSTEMD_UNIT --no-pager -n 200"
fi
