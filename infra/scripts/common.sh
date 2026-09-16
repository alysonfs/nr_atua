#!/usr/bin/env bash
# infra/scripts/common.sh
# Funções e variáveis compartilhadas pelos scripts de RDS.
#
# O RDS PostgreSQL é gerenciado FORA do CDK (decisão de design, ver
# comentário em lib/atua-data-stack.ts): o ciclo destroy+snapshot / restore
# usa identificadores de snapshot variáveis a cada execução, o que não se
# encaixa bem no modelo declarativo do CloudFormation sem risco de "drift".
#
# Nenhum comando aqui é executado automaticamente - estes scripts só rodam
# quando chamados explicitamente pelo Makefile (`make up` / `make down`).

set -euo pipefail

REGION="${AWS_REGION:-sa-east-1}"
DB_INSTANCE_ID="atua-postgres-mvp"
DB_NAME="atua"
DB_USERNAME="atua_app"
SNAPSHOT_PREFIX="atua-latest-snapshot"

account_id() {
  aws sts get-caller-identity --query Account --output text --region "$REGION"
}

network_output() {
  # $1 = nome do CfnOutput (ex.: RdsSecurityGroupId)
  aws cloudformation describe-stacks --stack-name AtuaNetworkStack --region "$REGION" \
    --query "Stacks[0].Outputs[?OutputKey=='$1'].OutputValue" --output text
}

latest_snapshot_id() {
  aws rds describe-db-snapshots --region "$REGION" \
    --query "reverse(sort_by(DBSnapshots[?starts_with(DBSnapshotIdentifier, \`$SNAPSHOT_PREFIX\`)], &SnapshotCreateTime))[0].DBSnapshotIdentifier" \
    --output text
}

rds_exists() {
  aws rds describe-db-instances --db-instance-identifier "$DB_INSTANCE_ID" --region "$REGION" >/dev/null 2>&1
}
