#!/usr/bin/env bash
# .github/skills/atua-deploy/scripts/common.sh
# Funções e variáveis mínimas compartilhadas pelos scripts de deploy/logs
# do Collector e da API (RF-009/016/017). Cópia deliberadamente enxuta do
# infra/scripts/common.sh (que também cobre o ciclo de vida do RDS, fora do
# escopo desta skill) — mantenha os dois em sincronia se `REGION`/`account_id`
# mudarem.
#
# Nenhum comando aqui é executado automaticamente - roda apenas quando
# chamado explicitamente pelos scripts desta pasta.

set -euo pipefail

REGION="${AWS_REGION:-sa-east-1}"

account_id() {
  aws sts get-caller-identity --query Account --output text --region "$REGION"
}
