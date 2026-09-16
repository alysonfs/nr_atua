---
name: atua-mongo-inspect
description: Consulta somente leitura ao MongoDB Atlas do ATUA (banco `atua`) para validar rapidamente se os dados coletados (ex.: work_order_snapshots) chegaram e com a qualidade esperada, sem precisar gerar um comando novo a cada verificação. Use quando o usuário pedir para checar/validar dados no Mongo, ver amostras de uma coleção, ou listar o estado geral do banco.
---

# Inspeção rápida do MongoDB Atlas

Evita repetir `mongosh` manual a cada validação de coleta (ex.: depois de um
redeploy do Collector). Os scripts vivem em
`.github/skills/atua-mongo-inspect/scripts/` e também estão expostos como
targets do `infra/Makefile`.

## Pré-requisitos (ver `.env.example` na raiz do repo)

* `mongosh` instalado localmente (`brew install mongosh`).
* AWS CLI configurado com um profile (`AWS_PROFILE`) com permissão
  `secretsmanager:GetSecretValue` no secret `atua/mongodb-atlas`.
* Seu IP atual autorizado na **IP Access List** do MongoDB Atlas
  (Atlas → Security → Network Access). Sem isso, a conexão trava em timeout.
* `python3` disponível (usado só para parsear o JSON do secret).

Nenhuma credencial é impressa ou commitada — a connection string é lida
diretamente do Secrets Manager em memória, na hora da consulta.

## Comandos disponíveis (rode a partir de `infra/`)

```bash
AWS_PROFILE=moldato make mongo-peek COLLECTION=work_order_snapshots LIMIT=10
AWS_PROFILE=moldato make mongo-peek-all LIMIT=10
```

* `mongo-peek`: mostra o total de documentos da coleção e os `LIMIT`
  (default 10) mais recentes por `created_at` (quando o campo existir).
* `mongo-peek-all`: descobre todas as coleções existentes no banco `atua` e
  roda `mongo-peek` para cada uma — útil quando novas coleções forem
  criadas (ex.: um dia teremos mais além de `work_order_snapshots`) e você
  não quer manter uma lista manual.

Também é possível chamar os scripts diretamente, sem o Makefile:

```bash
AWS_PROFILE=moldato bash .github/skills/atua-mongo-inspect/scripts/mongo-peek.sh work_order_snapshots 5
AWS_PROFILE=moldato bash .github/skills/atua-mongo-inspect/scripts/mongo-peek-all.sh 5
```

## O que validar na resposta

Para `work_order_snapshots` (RF-016), confira:

* `provider_id` preenchido (não vazio) — é a chave D7 (`workOrderId`).
* `rawdata` com campos de negócio populados (`workOrderId`, `workOrderNo`,
  `woStatus`, `custAccountId`, etc.) — se vier vazio/nulo, é sinal de
  regressão no mapeamento `JsonElement → Dictionary` do Collector
  (ver `IServiceCollectorService.ConvertJsonElement`).
* `tenant_id` e `command_id` consistentes com o comando disparado por
  `make collector-trigger-cycle` (skill `atua-deploy`).

## Quando NÃO usar

* Para alterar/apagar dados no Mongo — esta skill é somente leitura por
  design, não crie comandos de escrita a partir daqui.
* Para métricas agregadas de custo — use a skill `aws-cost-monitoring`.

## Diagnóstico de falhas comuns

* `Connection attempt timed out` → seu IP não está na IP Access List do
  Atlas; adicione-o (ver console do MongoDB Atlas → Network Access).
* `FATAL: não foi possível obter a connection string` → profile AWS sem
  permissão `secretsmanager:GetSecretValue`, ou secret `atua/mongodb-atlas`
  renomeado/removido.
