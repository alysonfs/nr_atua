# infra/Makefile — ciclo de vida da infraestrutura ATUA MVP (Plano v3)
#
# NENHUM alvo aqui é executado automaticamente por este repositório.
# Cada comando abaixo cria/modifica/remove recursos REAIS e cobráveis
# na AWS quando invocado manualmente. Rode apenas após aprovação
# explícita, com credenciais AWS configuradas.
#
# Diferença semântica (ver README.md para detalhes):
#   up      -> provisiona/liga tudo (Network+Data se não existirem,
#              RDS restaurado do snapshot ou criado na 1a vez, Compute
#              sempre recriado).
#   down    -> destrói tudo que é EFÊMERO (Compute = EC2+EBS das DUAS
#              instâncias: API Master e Collector-base) e o RDS (com
#              snapshot final antes). NÃO afeta S3/Secrets/MongoDB Atlas.
#   destroy -> além do down, remove também Network e Data (S3/Secrets
#              ficam RETIDOS por RemovalPolicy, não são apagados).
#   destroy-all-data -> remove literalmente tudo, inclusive snapshots,
#              buckets S3 e secrets. IRREVERSÍVEL.

REGION ?= sa-east-1
export AWS_REGION := $(REGION)

STACK_NET=AtuaNetworkStack
STACK_DATA=AtuaDataStack
STACK_COMPUTE=AtuaComputeStack
STACK_DOMAIN=AtuaDomainStack
OFFICE_API_BASE_URL ?= https://api.atyno.com.br

ACCOUNT_ID := $(shell aws sts get-caller-identity --query Account --output text --region $(REGION) 2>/dev/null)

.PHONY: install synth diff bootstrap-cdk deploy-domain sync-api-domain up down destroy destroy-all-data status rds-bootstrap rds-up rds-down retrieve-keypair deploy-landing deploy-office deploy-manager deploy-tecnica deploy-api deploy-collector redeploy-api redeploy-collector collector-trigger-cycle logs-api logs-collector logs-api-follow logs-collector-follow mongo-peek mongo-peek-all pg-peek pg-peek-all

## Instala dependências (pnpm, alinhado ao monorepo)
install:
	pnpm install --frozen-lockfile

## Sintetiza CloudFormation localmente - NÃO cria nada na AWS
synth:
	pnpm --filter @atua/infra exec cdk synth

diff:
	pnpm --filter @atua/infra exec cdk diff

## Provisiona Route 53, ACM e CloudFront para atyno.com.br.
## Requer revisão dos registros existentes no GoDaddy antes da troca de NS.
## Este alvo altera recursos reais e não é executado por `make up`.
deploy-domain:
	pnpm --filter @atua/infra exec cdk deploy $(STACK_DOMAIN) --require-approval never

## Atualiza a origem operacional da API usada pelo CloudFront.
## Deve rodar após cada `make up`, pois a EC2 da API é efêmera e troca de IP.
sync-api-domain:
	@HOSTED_ZONE_ID=$$(aws route53 list-hosted-zones-by-name --dns-name atyno.com.br \
	  --query "HostedZones[?Name=='atyno.com.br.'].Id | [0]" --output text | cut -d/ -f3); \
	API_PUBLIC_IP=$$(aws cloudformation describe-stacks --region $(REGION) --stack-name $(STACK_COMPUTE) \
	  --query "Stacks[0].Outputs[?OutputKey=='ApiPublicIp'].OutputValue | [0]" --output text); \
	if [ -z "$$HOSTED_ZONE_ID" ] || [ "$$HOSTED_ZONE_ID" = "None" ]; then \
	  echo "Hosted zone atyno.com.br nao encontrada. Rode 'make deploy-domain' primeiro."; \
	  exit 1; \
	fi; \
	if [ -z "$$API_PUBLIC_IP" ] || [ "$$API_PUBLIC_IP" = "None" ]; then \
	  echo "IP publico da API nao encontrado. Rode 'make up' primeiro."; \
	  exit 1; \
	fi; \
	aws route53 change-resource-record-sets --hosted-zone-id "$$HOSTED_ZONE_ID" \
	  --change-batch "{\"Comment\":\"Atualiza origem HTTP da API efemera ATUA\",\"Changes\":[{\"Action\":\"UPSERT\",\"ResourceRecordSet\":{\"Name\":\"api-origin.atyno.com.br\",\"Type\":\"A\",\"TTL\":60,\"ResourceRecords\":[{\"Value\":\"$$API_PUBLIC_IP\"}]}}]}"; \
	echo "api-origin.atyno.com.br atualizado para $$API_PUBLIC_IP."

## cdk bootstrap: prepara a conta para receber assets do CDK
## (bucket interno do CDK). Só rodar 1x por conta/região, mediante aprovação.
bootstrap-cdk:
	pnpm --filter @atua/infra exec cdk bootstrap aws://$(ACCOUNT_ID)/$(REGION)

## O Key Pair 'atua-mvp-key' é criado automaticamente pelo CDK dentro
## de AtuaNetworkStack (recurso AWS::EC2::KeyPair) durante o primeiro
## `make up`. A AWS gera o par e guarda a chave PRIVADA automaticamente
## em SSM Parameter Store (SecureString) - nunca em código/repositório.
## Este alvo apenas RECUPERA essa chave e salva localmente para uso com SSH.
retrieve-keypair:
	@KEY_ID=$$(aws ec2 describe-key-pairs --key-names atua-mvp-key --region $(REGION) --query 'KeyPairs[0].KeyPairId' --output text); \
	aws ssm get-parameter --name "/ec2/keypair/$$KEY_ID" --with-decryption --region $(REGION) \
	  --query 'Parameter.Value' --output text > ~/.ssh/atua-mvp-key.pem; \
	chmod 400 ~/.ssh/atua-mvp-key.pem; \
	echo "Chave privada recuperada da SSM e salva em ~/.ssh/atua-mvp-key.pem (fora do repo)."

## Cria o RDS do zero (rodar 1x, apenas se não houver instância nem snapshot prévios)
rds-bootstrap:
	bash scripts/rds-bootstrap.sh

## Restaura o RDS a partir do snapshot mais recente
rds-up:
	bash scripts/rds-up.sh

## Snapshot final + destrói o RDS
rds-down:
	bash scripts/rds-down.sh

## Sobe tudo: Network+Data (idempotente) + RDS (bootstrap ou restore) + Compute
up:
	pnpm --filter @atua/infra exec cdk deploy $(STACK_NET) $(STACK_DATA) --require-approval never
	@if aws rds describe-db-instances --db-instance-identifier atua-postgres-mvp --region $(REGION) >/dev/null 2>&1; then \
		echo "RDS já está no ar."; \
	elif aws rds describe-db-snapshots --region $(REGION) --query "DBSnapshots[?starts_with(DBSnapshotIdentifier, 'atua-latest-snapshot')]" --output text | grep -q .; then \
		$(MAKE) rds-up; \
	else \
		echo "Nenhum RDS nem snapshot encontrado - rode 'make rds-bootstrap' explicitamente (1a vez)."; \
		exit 1; \
	fi
	pnpm --filter @atua/infra exec cdk deploy $(STACK_COMPUTE) --require-approval never
	$(MAKE) sync-api-domain
	@echo "up concluído. API Master E Collector-base (sem lógica) no ar."
	@echo "Rode 'make retrieve-keypair' se ainda não tiver a chave privada localmente."

## Desliga: destrói Compute (EC2+EBS das DUAS instâncias - API e
## Collector-base) e RDS (com snapshot final). Preserva Network, S3 e
## Secrets Manager.
down:
	pnpm --filter @atua/infra exec cdk destroy $(STACK_COMPUTE) --force
	$(MAKE) rds-down
	@echo "down concluído. Residual esperado: ~US\$$1,20/mês (Secrets Manager) + storage de snapshot (centavos)."

## Remove Network e Data também. S3/Secrets ficam RETIDOS (RemovalPolicy).
destroy: down
	pnpm --filter @atua/infra exec cdk destroy $(STACK_DATA) $(STACK_NET) --force
	@echo "Network e Data destruídos via CDK. S3/Secrets retidos - use 'make destroy-all-data' para remover tudo."

## Apaga literalmente tudo: snapshots RDS, buckets S3 e secrets. IRREVERSÍVEL.
destroy-all-data: destroy
	@echo "Removendo snapshots RDS..."
	@aws rds describe-db-snapshots --region $(REGION) \
	  --query "DBSnapshots[?starts_with(DBSnapshotIdentifier, 'atua-latest-snapshot')].DBSnapshotIdentifier" --output text | \
	  tr '\t' '\n' | xargs -r -n1 -I{} aws rds delete-db-snapshot --db-snapshot-identifier {} --region $(REGION)
	@echo "Removendo buckets S3..."
	-aws s3 rm s3://atua-$(ACCOUNT_ID)-frontends --recursive --region $(REGION)
	-aws s3api delete-bucket --bucket atua-$(ACCOUNT_ID)-frontends --region $(REGION)
	-aws s3 rm s3://atua-$(ACCOUNT_ID)-backups --recursive --region $(REGION)
	-aws s3api delete-bucket --bucket atua-$(ACCOUNT_ID)-backups --region $(REGION)
	-aws s3 rm s3://atua-$(ACCOUNT_ID)-releases --recursive --region $(REGION)
	-aws s3api delete-bucket --bucket atua-$(ACCOUNT_ID)-releases --region $(REGION)
	@echo "Removendo secrets..."
	-aws secretsmanager delete-secret --secret-id atua/rds-postgres --force-delete-without-recovery --region $(REGION)
	-aws secretsmanager delete-secret --secret-id atua/mongodb-atlas --force-delete-without-recovery --region $(REGION)
	-aws secretsmanager delete-secret --secret-id atua/app-secrets --force-delete-without-recovery --region $(REGION)
	@echo "Tudo removido na AWS. MongoDB Atlas (externo) NÃO é afetado - gerenciar separadamente."

## Build da landing (apps/landing) e sync para o S3 de frontends.
## Bucket: atua-$(ACCOUNT_ID)-frontends, prefixo /landing/
## URL resultante: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/landing/
deploy-landing:
	@echo "Building landing..."
	pnpm --filter landing build
	@echo "Sincronizando assets com cache longa duração..."
	aws s3 sync ../apps/landing/dist/ s3://atua-$(ACCOUNT_ID)-frontends/landing/ \
	  --region $(REGION) --delete \
	  --cache-control "public, max-age=31536000, immutable" \
	  --exclude "index.html"
	@echo "Sincronizando index.html sem cache..."
	aws s3 cp ../apps/landing/dist/index.html \
	  s3://atua-$(ACCOUNT_ID)-frontends/landing/index.html \
	  --region $(REGION) \
	  --cache-control "no-cache, no-store, must-revalidate" \
	  --content-type "text/html"
	@echo "Landing publicada em: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/landing/"

## Build do office (apps/office) e sync para o S3 de frontends.
## Bucket: atua-$(ACCOUNT_ID)-frontends, prefixo /office/
## URL resultante: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/office/
deploy-office:
	@echo "Building office..."
	VITE_API_BASE_URL=$(OFFICE_API_BASE_URL) pnpm --filter office build
	@echo "Sincronizando assets com cache longa duração..."
	aws s3 sync ../apps/office/dist/ s3://atua-$(ACCOUNT_ID)-frontends/office/ \
	  --region $(REGION) --delete \
	  --cache-control "public, max-age=31536000, immutable" \
	  --exclude "index.html"
	@echo "Sincronizando index.html sem cache..."
	aws s3 cp ../apps/office/dist/index.html \
	  s3://atua-$(ACCOUNT_ID)-frontends/office/index.html \
	  --region $(REGION) \
	  --cache-control "no-cache, no-store, must-revalidate" \
	  --content-type "text/html"
	@echo "Office publicado em: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/office/"

## Build do manager (apps/manager) e sync para o S3 de frontends.
## Bucket: atua-$(ACCOUNT_ID)-frontends, prefixo /manager/
## URL resultante: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/manager/
deploy-manager:
	@echo "Building manager..."
	pnpm --filter manager build
	@echo "Sincronizando assets com cache longa duração..."
	aws s3 sync ../apps/manager/dist/ s3://atua-$(ACCOUNT_ID)-frontends/manager/ \
	  --region $(REGION) --delete \
	  --cache-control "public, max-age=31536000, immutable" \
	  --exclude "index.html"
	@echo "Sincronizando index.html sem cache..."
	aws s3 cp ../apps/manager/dist/index.html \
	  s3://atua-$(ACCOUNT_ID)-frontends/manager/index.html \
	  --region $(REGION) \
	  --cache-control "no-cache, no-store, must-revalidate" \
	  --content-type "text/html"
	@echo "Manager publicado em: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/manager/"

## Build do tecnica (apps/tecnica) e sync para o S3 de frontends.
## Bucket: atua-$(ACCOUNT_ID)-frontends, prefixo /tecnica/
## URL resultante: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/tecnica/
deploy-tecnica:
	@echo "Building tecnica..."
	pnpm --filter tecnica build
	@echo "Sincronizando assets com cache longa duração..."
	aws s3 sync ../apps/tecnica/dist/ s3://atua-$(ACCOUNT_ID)-frontends/tecnica/ \
	  --region $(REGION) --delete \
	  --cache-control "public, max-age=31536000, immutable" \
	  --exclude "index.html"
	@echo "Sincronizando index.html sem cache..."
	aws s3 cp ../apps/tecnica/dist/index.html \
	  s3://atua-$(ACCOUNT_ID)-frontends/tecnica/index.html \
	  --region $(REGION) \
	  --cache-control "no-cache, no-store, must-revalidate" \
	  --content-type "text/html"
	@echo "Tecnica publicado em: http://atua-$(ACCOUNT_ID)-frontends.s3-website-$(REGION).amazonaws.com/tecnica/"

## Publica o artefato da API Master no bucket de releases.
## Fluxo completo de deploy da API:
##   1. make deploy-api         -> compila (Release) e sobe para S3
##   2. make up                 -> sobe/recria a EC2 (user-data sincroniza o artefato e inicia atua-api.service)
##      OU, com a EC2 já no ar: ssh na instância e rode:
##        aws s3 sync s3://atua-<ACCOUNT_ID>-releases/latest/ /opt/atua-api/
##        systemctl restart atua-api
deploy-api:
	@echo "Compilando Atua.Api (Release)..."
	dotnet publish ../apps/api/Atua.Api/Atua.Api.csproj -c Release -o /tmp/atua-api-publish
	@echo "Sincronizando artefato para s3://atua-$(ACCOUNT_ID)-releases/latest/ ..."
	aws s3 sync /tmp/atua-api-publish/ s3://atua-$(ACCOUNT_ID)-releases/latest/ \
	  --region $(REGION) --delete
	@echo "Artefato publicado. Rode 'make up' para subir/recriar a EC2 com o novo artefato."
	@echo "Ou, com a EC2 já no ar, sincronize e reinicie manualmente o serviço (ver comentário acima)."

## Publica o artefato do Agente Coletor no bucket de releases.
## Prefixo dedicado (collector-latest/) para não colidir com o artefato da API.
## CRÍTICO: usa -r linux-x64 --self-contained false, NÃO só '-c Release', porque
## sem o RID explícito o MSBuild embute o driver Node do Playwright da plataforma
## de BUILD (ex.: darwin-x64 num Mac), inutilizável na EC2 Linux. Com -r linux-x64
## o pacote Microsoft.Playwright embute corretamente .playwright/node/linux-x64/node
## (ELF x86-64), permitindo instalar os browsers no boot sem depender de PowerShell.
## Fluxo completo de deploy do Collector:
##   1. make deploy-collector   -> compila (Release, linux-x64) e sobe para S3
##   2. make up                 -> sobe/recria a EC2 (user-data sincroniza o artefato,
##                                  instala o Chromium via driver Node embutido e inicia
##                                  atua-collector.service)
##      OU, com a EC2 já no ar: ssh na instância e rode:
##        aws s3 sync s3://atua-<ACCOUNT_ID>-releases/collector-latest/ /opt/atua-collector/
##        /opt/atua-collector/.playwright/node/linux-x64/node \
##          /opt/atua-collector/.playwright/package/cli.js install chromium
##        systemctl restart atua-collector
deploy-collector:
	@echo "Compilando Atua.Collector (Release, linux-x64, self-contained=false)..."
	dotnet publish ../apps/collector/Atua.Collector/Atua.Collector.csproj \
	  -c Release -r linux-x64 --self-contained false \
	  -o /tmp/atua-collector-publish
	@echo "Sincronizando artefato para s3://atua-$(ACCOUNT_ID)-releases/collector-latest/ ..."
	aws s3 sync /tmp/atua-collector-publish/ s3://atua-$(ACCOUNT_ID)-releases/collector-latest/ \
	  --region $(REGION) --delete
	@echo "Artefato publicado. Rode 'make up' para subir/recriar a EC2 com o novo artefato."
	@echo "Ou, com a EC2 já no ar, sincronize e reinicie manualmente o serviço (ver comentário acima)."

# Scripts de deploy/logs vivem na skill Batuta `atua-deploy` (não em
# infra/scripts/), para serem reutilizáveis e documentados junto ao
# framework multi-agente. Ver .github/skills/atua-deploy/SKILL.md.
DEPLOY_SKILL_SCRIPTS := ../.github/skills/atua-deploy/scripts

## Redeploy "a quente" da API já rodando (sem recriar a EC2): compila,
## publica no S3, sincroniza via SSH, reinicia o systemd e verifica por MD5
## que o binário remoto realmente mudou. Automatiza o ciclo que antes era
## feito manualmente comando a comando (ver skill atua-deploy).
redeploy-api:
	bash $(DEPLOY_SKILL_SCRIPTS)/redeploy-live.sh api

## Idem para o Collector (usa RID linux-x64 --self-contained false, igual ao deploy-collector).
redeploy-collector:
	bash $(DEPLOY_SKILL_SCRIPTS)/redeploy-live.sh collector

## Dispara um novo ciclo de teste do Collector: renova o JWT do usuário de
## teste e desativa/reativa o collector-activation, gerando um novo
## ImmediateCollectionCommand 'Pending' para o Worker reivindicar.
## Sobrescreva ATUA_TEST_EMAIL/ATUA_TEST_PASSWORD/ATUA_TENANT_ID/ATUA_INTEGRATION_ID
## se o tenant de teste mudar.
collector-trigger-cycle:
	bash $(DEPLOY_SKILL_SCRIPTS)/collector-trigger-cycle.sh

## Mostra as últimas 200 linhas de log do serviço (resolve o IP via CloudFormation).
logs-api:
	bash $(DEPLOY_SKILL_SCRIPTS)/tail-logs.sh api

logs-collector:
	bash $(DEPLOY_SKILL_SCRIPTS)/tail-logs.sh collector

## Acompanha os logs em tempo real (Ctrl+C para sair).
logs-api-follow:
	bash $(DEPLOY_SKILL_SCRIPTS)/tail-logs.sh api --follow

logs-collector-follow:
	bash $(DEPLOY_SKILL_SCRIPTS)/tail-logs.sh collector --follow

## Consultas somente leitura ao MongoDB Atlas (skill atua-mongo-inspect):
## últimos N documentos de uma coleção, ou de todas as coleções conhecidas.
## Uso: make mongo-peek COLLECTION=work_order_snapshots [LIMIT=10]
MONGO_SKILL_SCRIPTS := ../.github/skills/atua-mongo-inspect/scripts
LIMIT ?= 10

mongo-peek:
	bash $(MONGO_SKILL_SCRIPTS)/mongo-peek.sh $(COLLECTION) $(LIMIT)

mongo-peek-all:
	bash $(MONGO_SKILL_SCRIPTS)/mongo-peek-all.sh $(LIMIT)

## Consultas somente leitura ao RDS PostgreSQL (skill atua-pg-inspect, via
## túnel SSH — o RDS é privado): últimos N registros de uma tabela, ou de
## todas as tabelas do schema public. Uso: make pg-peek TABLE=work_orders [LIMIT=10]
PG_SKILL_SCRIPTS := ../.github/skills/atua-pg-inspect/scripts

pg-peek:
	bash $(PG_SKILL_SCRIPTS)/pg-peek.sh $(TABLE) $(LIMIT)

pg-peek-all:
	bash $(PG_SKILL_SCRIPTS)/pg-peek-all.sh $(LIMIT)


status:
	@echo "--- Compute stack ---"
	@aws cloudformation describe-stacks --stack-name $(STACK_COMPUTE) --region $(REGION) \
	  --query 'Stacks[0].StackStatus' --output text 2>/dev/null || echo "não provisionado (down)"
	@echo "--- RDS ---"
	@aws rds describe-db-instances --db-instance-identifier atua-postgres-mvp --region $(REGION) \
	  --query 'DBInstances[0].DBInstanceStatus' --output text 2>/dev/null || echo "não existe (destruído/nunca criado)"
	@echo "--- Snapshots RDS disponíveis ---"
	@aws rds describe-db-snapshots --region $(REGION) \
	  --query "DBSnapshots[?starts_with(DBSnapshotIdentifier, 'atua-latest-snapshot')].DBSnapshotIdentifier" --output table 2>/dev/null || true
