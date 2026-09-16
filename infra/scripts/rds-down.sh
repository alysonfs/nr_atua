#!/usr/bin/env bash
# infra/scripts/rds-down.sh
#
# Implementa a Opção 2 (aprovada): tira um snapshot final do RDS e
# destrói a instância, eliminando o custo de storage/compute do RDS
# quando ocioso, preservando os dados no snapshot (custo residual
# muito baixo - poucos centavos/mês).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

if ! rds_exists; then
  echo "RDS '$DB_INSTANCE_ID' não existe (já está 'down'). Nada a fazer."
  exit 0
fi

SNAPSHOT_ID="${SNAPSHOT_PREFIX}-$(date +%Y%m%d%H%M%S)"

echo "Criando snapshot final: $SNAPSHOT_ID ..."
aws rds create-db-snapshot \
  --db-instance-identifier "$DB_INSTANCE_ID" \
  --db-snapshot-identifier "$SNAPSHOT_ID" \
  --region "$REGION" \
  --tags Key=Project,Value=atua Key=Environment,Value=dev-mvp Key=Component,Value=database

echo "Aguardando snapshot ficar disponível..."
aws rds wait db-snapshot-available --db-snapshot-identifier "$SNAPSHOT_ID" --region "$REGION"

echo "Destruindo a instância RDS (dados preservados no snapshot $SNAPSHOT_ID)..."
aws rds delete-db-instance \
  --db-instance-identifier "$DB_INSTANCE_ID" \
  --skip-final-snapshot \
  --region "$REGION"

echo "RDS destruído. Snapshot preservado: $SNAPSHOT_ID."
echo "Custo de computação do RDS agora é \$0. Custo residual = storage do snapshot (poucos centavos/mês)."
