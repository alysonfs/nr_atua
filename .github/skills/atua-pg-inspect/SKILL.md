---
name: atua-pg-inspect
description: Consulta somente leitura ao RDS PostgreSQL do ATUA (via túnel SSH, o banco é privado) para validar rapidamente se os dados chegaram nas tabelas relacionais (ex.: work_orders, work_order_histories) e com a qualidade esperada, sem precisar gerar um comando novo a cada verificação. Use quando o usuário pedir para checar/validar dados no Postgres/RDS, ver amostras de uma tabela, ou listar o estado geral do banco relacional.
---

# Inspeção rápida do RDS PostgreSQL

Evita repetir `psql`/túnel SSH manual a cada validação (ex.: depois de um
redeploy do Collector, confirmar que o `SnapshotConsumerWorker` está
escrevendo em `work_orders`/`work_order_histories` — RF-017/ADR-023). Os
scripts vivem em `.github/skills/atua-pg-inspect/scripts/` e também estão
expostos como targets do `infra/Makefile`.

## Por que precisa de túnel SSH

O RDS (`atua-postgres-mvp`) é **privado** (`PubliclyAccessible=false`,
subnet privada) — não é possível conectar direto da sua máquina (é por
isso que o DBeaver local dá `Connection attempt timed out`). Os scripts
abrem um túnel SSH via uma das EC2 já autorizadas na VPC (por padrão, a
API — `ApiPublicIp`) e rodam `psql` localmente através do túnel.

## Pré-requisitos (ver `.env.example` na raiz do repo)

* `psql` disponível localmente. No macOS, `brew install libpq` não expõe
  `psql` no PATH por padrão — descubra o caminho com
  `brew --prefix libpq` e exporte `PSQL_BIN=<prefix>/bin/psql`, ou rode
  `brew link --force libpq`.
* AWS CLI configurado com um profile (`AWS_PROFILE`) com permissão
  `secretsmanager:GetSecretValue` no secret `atua/rds-postgres` e
  `cloudformation:DescribeStacks`.
* Chave SSH local em `~/.ssh/atua-mvp-key.pem` (`make retrieve-keypair`).
* Seu IP atual autorizado no Security Group da instância-túnel (porta 22).

Nenhuma credencial é impressa ou commitada — usuário/senha/host são lidos
do Secrets Manager em memória, na hora da consulta.

## Comandos disponíveis (rode a partir de `infra/`)

```bash
AWS_PROFILE=moldato make pg-peek TABLE=work_orders LIMIT=10
AWS_PROFILE=moldato make pg-peek-all LIMIT=10
```

* `pg-peek`: mostra o total de linhas da tabela e as `LIMIT` (default 10)
  mais recentes por `CreatedAt`/`created_at` (quando a coluna existir —
  cobre tanto a convenção PascalCase do EF Core quanto snake_case).
* `pg-peek-all`: descobre todas as tabelas do schema `public` via
  `information_schema.tables` e roda `pg-peek` para cada uma — reaproveita
  um único túnel SSH para todas as consultas.

Chamada direta, sem o Makefile:

```bash
AWS_PROFILE=moldato PSQL_BIN=/usr/local/Cellar/libpq/17.4/bin/psql \
  bash .github/skills/atua-pg-inspect/scripts/pg-peek.sh work_orders 5
```

## O que validar na resposta

Para `work_orders`/`work_order_histories` (RF-017/ADR-023), confira:

* `work_orders.status` consistente com o último snapshot coletado (comparar
  com a skill `atua-mongo-inspect`, coleção `work_order_snapshots`).
* `work_order_histories` só ganha nova linha quando o status muda
  (RF-017.2/DP-017.1) — não uma linha por snapshot.
* `work_order_histories.WorkOrderSnapshotId` referenciando um `_id`
  existente em `work_order_snapshots` (rastreabilidade, RF-017.4).
* `consumer_states` com `ResumeToken` não nulo — confirma que o
  `SnapshotConsumerWorker` está avançando no Change Stream.

## Quando NÃO usar

* Para alterar/apagar dados no Postgres — esta skill é somente leitura por
  design, não crie comandos de escrita a partir daqui.
* Para dados brutos de OS (snapshots) — use a skill `atua-mongo-inspect`.
* Para métricas agregadas de custo — use a skill `aws-cost-monitoring`.

## Diagnóstico de falhas comuns

* `Connection attempt timed out` no túnel SSH → seu IP mudou; adicione a
  regra no Security Group da instância-túnel (porta 22) e revogue depois.
* `psql não encontrado` → exporte `PSQL_BIN` com o caminho completo (ver
  pré-requisitos).
* Túnel "preso" após uma execução interrompida → o script usa
  `ssh -S <socket> -O exit` no `trap EXIT` para fechar o túnel; se restar
  um processo `ssh -fN` órfão, mate manualmente com `kill <PID>`
  (nunca `pkill`/`killall`).
