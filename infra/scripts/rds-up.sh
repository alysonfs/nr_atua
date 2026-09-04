#!/usr/bin/env bash
# infra/scripts/rds-up.sh
#
# Restaura o RDS a partir do snapshot mais recente (Opção 2 aprovada).
# Se não existir nenhum snapshot ainda, orienta a rodar rds-bootstrap.sh
# (primeira vez).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

if rds_exists; then
  echo "RDS '$DB_INSTANCE_ID' já está no ar. Nada a fazer."
  exit 0
fi

SNAPSHOT_ID="$(latest_snapshot_id)"
if [ -z "$SNAPSHOT_ID" ] || [ "$SNAPSHOT_ID" = "None" ]; then
  echo "Nenhum snapshot encontrado. Rode 'make rds-bootstrap' primeiro (primeira vez apenas)." >&2
  exit 1
fi

SG_ID="$(network_output RdsSecurityGroupId)"
SUBNET_GROUP_NAME="atua-mvp-rds-subnet-group"

echo "Restaurando RDS a partir do snapshot: $SNAPSHOT_ID ..."
aws rds restore-db-instance-from-db-snapshot \
  --db-instance-identifier "$DB_INSTANCE_ID" \
  --db-snapshot-identifier "$SNAPSHOT_ID" \
  --db-instance-class db.t3.micro \
  --db-subnet-group-name "$SUBNET_GROUP_NAME" \
  --vpc-security-group-ids "$SG_ID" \
  --no-multi-az \
  --publicly-accessible \
  --region "$REGION" \
  --tags Key=Project,Value=atua Key=Environment,Value=dev-mvp Key=Component,Value=database

echo "Aguardando ficar 'available' (pode levar 10-20 minutos)..."
aws rds wait db-instance-available --db-instance-identifier "$DB_INSTANCE_ID" --region "$REGION"

ENDPOINT="$(aws rds describe-db-instances --db-instance-identifier "$DB_INSTANCE_ID" --region "$REGION" \
  --query 'DBInstances[0].Endpoint.Address' --output text)"

# Atualiza o secret com o novo endpoint (muda a cada restore).
CURRENT_SECRET="$(aws secretsmanager get-secret-value --secret-id atua/rds-postgres --region "$REGION" --query 'SecretString' --output text)"
NEW_SECRET="$(python3 -c "
import json,sys
d = json.loads(sys.argv[1])
d['host'] = sys.argv[2]
d['port'] = 5432
print(json.dumps(d))
" "$CURRENT_SECRET" "$ENDPOINT")"

aws secretsmanager put-secret-value --secret-id atua/rds-postgres --region "$REGION" --secret-string "$NEW_SECRET"

echo "RDS restaurado. Novo endpoint: $ENDPOINT. Secret atua/rds-postgres atualizado."
