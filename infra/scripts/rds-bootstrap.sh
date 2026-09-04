#!/usr/bin/env bash
# infra/scripts/rds-bootstrap.sh
#
# Cria o RDS PostgreSQL do ZERO (primeira vez apenas). Não deve ser
# chamado se já existir uma instância ou um snapshot prévio - nesse
# caso use rds-up.sh (restore).
#
# NÃO EXECUTADO AUTOMATICAMENTE. Requer aprovação explícita antes de rodar.

set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./common.sh
source "$DIR/common.sh"

if rds_exists; then
  echo "RDS '$DB_INSTANCE_ID' já existe. Use 'make up' normalmente (não é bootstrap)." >&2
  exit 1
fi

SG_ID="$(network_output RdsSecurityGroupId)"
SUBNET_IDS="$(network_output RdsSubnetIds)"
SUBNET_ID_1="$(echo "$SUBNET_IDS" | cut -d',' -f1)"
SUBNET_ID_2="$(echo "$SUBNET_IDS" | cut -d',' -f2)"

SUBNET_GROUP_NAME="atua-mvp-rds-subnet-group"

# Idempotente: o subnet group pode já existir de uma tentativa anterior.
if aws rds describe-db-subnet-groups --db-subnet-group-name "$SUBNET_GROUP_NAME" \
  --region "$REGION" >/dev/null 2>&1; then
  echo "DB Subnet Group ($SUBNET_GROUP_NAME) já existe - reutilizando."
else
  echo "Criando DB Subnet Group ($SUBNET_GROUP_NAME)..."
  aws rds create-db-subnet-group \
    --db-subnet-group-name "$SUBNET_GROUP_NAME" \
    --db-subnet-group-description "ATUA MVP - subnets privadas isoladas para RDS" \
    --subnet-ids "$SUBNET_ID_1" "$SUBNET_ID_2" \
    --region "$REGION" \
    --tags Key=Project,Value=atua Key=Environment,Value=dev-mvp Key=Component,Value=database
fi

# Senha gerada localmente apenas para esta chamada - nunca commitada,
# nunca exibida em log. Escrita diretamente no Secrets Manager.
DB_PASSWORD="$(aws secretsmanager get-secret-value --secret-id atua/rds-postgres --region "$REGION" \
  --query 'SecretString' --output text | python3 -c 'import json,sys;print(json.load(sys.stdin)["password"])')"

# backup-retention-period = 1: o Free Tier plan da conta limita a retenção a
# 1 dia (FreeTierRestrictionError com 7). O snapshot final gerado por
# 'make down' persiste independentemente desta retenção.
echo "Criando instância RDS PostgreSQL db.t3.micro ($DB_INSTANCE_ID)..."
aws rds create-db-instance \
  --db-instance-identifier "$DB_INSTANCE_ID" \
  --db-instance-class db.t3.micro \
  --engine postgres \
  --engine-version 16 \
  --master-username "$DB_USERNAME" \
  --master-user-password "$DB_PASSWORD" \
  --allocated-storage 20 \
  --storage-type gp2 \
  --db-name "$DB_NAME" \
  --db-subnet-group-name "$SUBNET_GROUP_NAME" \
  --vpc-security-group-ids "$SG_ID" \
  --backup-retention-period 1 \
  --no-multi-az \
  --publicly-accessible \
  --storage-encrypted \
  --region "$REGION" \
  --tags Key=Project,Value=atua Key=Environment,Value=dev-mvp Key=Component,Value=database

echo "Aguardando ficar 'available' (pode levar alguns minutos)..."
aws rds wait db-instance-available --db-instance-identifier "$DB_INSTANCE_ID" --region "$REGION"

ENDPOINT="$(aws rds describe-db-instances --db-instance-identifier "$DB_INSTANCE_ID" --region "$REGION" \
  --query 'DBInstances[0].Endpoint.Address' --output text)"

aws secretsmanager put-secret-value --secret-id atua/rds-postgres --region "$REGION" \
  --secret-string "{\"username\":\"$DB_USERNAME\",\"password\":\"$DB_PASSWORD\",\"host\":\"$ENDPOINT\",\"port\":5432,\"dbname\":\"$DB_NAME\"}"

echo "RDS criado e secret atualizado. Endpoint: $ENDPOINT"
