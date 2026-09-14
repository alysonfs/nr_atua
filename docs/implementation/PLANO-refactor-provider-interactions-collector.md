# Plano de Implementação — Refactor de Persistência do Collector (provider_interactions / provider_sessions)

## Status

`FASE_0_CONCLUIDA` — ADR-028 escrita e aprovada pelo usuário em
2026-09-12 (`docs/decisions/ADR-028-registro-bruto-de-interacao-com-provedor-e-sessao-persistida.md`),
junto com RF-022 e RF-023.

`FASE_1_CONCLUIDA` — `provider_interactions` aditivo implementado e
commitado (`f860e60`).

`FASE_2_CONCLUIDA` — `provider_sessions` implementado e commitado
(`f00cfb1`): repositório com
índice único `(tenant_id, provider_type)`, `CollectAsync` reestruturado
em `ExecuteCollectionCycleAsync` para reaproveitar `storage_state` salvo
via `BrowserContext` explícito, checagem barata de validade (redirect
para `signin.midea.com`), persistência da sessão após login CAS bem-sucedido,
e detecção de HTTP 401 em pleno ciclo (`list_query`/`detail_query`) via
`ProviderSessionInvalidException` com invalidação + retry único de login
dentro do mesmo comando. TTL configurável via
`CollectorWorkerOptions.SessionTtlHours` (default 4h, DP-023.1 — ajustar
por observação real).

`FASE_3_CONCLUIDA` — busca de OS por data implementada e commitada
(`3a137e7`): `FetchWorkOrdersByDateRangeAsync` substitui as 5 chamadas
fixas por status por uma única sequência paginada a `queryWorkOrder` com
`woStatus=""`/`woStatusCond="me"` e `creationDateFrom`/`creationDateTo`
derivados de `HistoryWindowMonths`, agrupando o resultado por `woStatus`
via `GroupOrdersByStatus`. Estratégia legada mantida atrás de
`CollectorWorkerOptions.UseDateBasedWorkOrderQuery` (default `true`,
rollback sem redeploy). Build limpo. Pendente: validação lado a lado
contra o iService real (comparar OS retornadas pelas duas estratégias)
antes de desligar a legada.

`FASE_4_CONCLUIDA` — consumer decompõe array de N OS por documento e
commitada (`f7c2dec`): `ProviderInteractionConsumerWorker` substitui
`SnapshotConsumerWorker`, com Change Stream sobre `provider_interactions`
em vez de `work_order_snapshots`; interações `login` e interações com
`success=false` apenas avançam o resume token, sem escrita em
`work_orders`/`work_order_histories`.
`IProviderInteractionOrderAdapter`/`IServiceProviderInteractionOrderAdapter`
substituem `ISnapshotAdapter`/`IServiceSnapshotAdapter`, decompondo o
array `orders` em N pares `(providerId, status)` (chave `workOrderId`
com fallback `id`; status em `woStatus`).
`WorkOrderPgRepository.ProcessInteractionAsync` processa N upserts em
`work_orders` + N appends condicionais em `work_order_histories` + 1
upsert de `consumer_states`, tudo em uma única transação Postgres — um
resume token por interação, não por OS. Novo consumer id
`provider-interaction-to-work-order` (distinto de
`snapshot-to-work-order`, cuja linha do tempo de resume tokens não é
compatível com a nova fonte). Resume token expirado (código 286/
`ChangeStreamHistoryLost`, quando o Atlas já reciclou o oplog) é
detectado especificamente e tratado limpando apenas o token persistido
(`ClearResumeTokenAsync`) — o stream reabre a partir do ponto corrente,
sem apagar nenhum dado de negócio no RDS ou no Mongo. `work_order_snapshots`
segue sendo escrito por `WorkOrderRepository` (Fase 1, em paralelo) até o
cutover da Fase 5 — só deixou de ter consumer próprio nesta fase. Build
limpo. Pendente: validação real contra o iService (novo consumer
processando interações reais ponta a ponta) antes da Fase 5.

**Validação com dado real (2026-09-14) — achado pendente de investigação:**
Ciclo real (tenant `01a0888b-acd8-773b-93b7-7362973d7ea8`, comando
`01a0a1f9-1bc6-7d4b-af4a-e4adc9f124a5`) gerou 9 interações em
`provider_interactions` (1 `login` + 6 `list_query`, 1035 OS únicas + 2
`detail_query`). Confirmado via consulta direta a Postgres/Mongo que o
`ProviderInteractionConsumerWorker` processou corretamente apenas as 2
primeiras páginas (400 de 1035 OS projetadas em `work_orders`/
`work_order_histories`, com upsert e histórico condizentes) — as 4 páginas
seguintes e as 2 consultas de detalhe não foram projetadas nesse ciclo.
`consumer_states` mostra token atualizado logo após o fim do ciclo, então o
consumer não travou incondicionalmente, mas parou de avançar antes do fim
do stream desse ciclo especificamente. Causa raiz ainda não identificada —
requer acesso aos logs reais do Worker (journalctl via `atua-deploy`) para
confirmar se houve exceção/restart; investigação adiada a pedido do
usuário. Mitigação aplicada enquanto isso: `HistoryWindowMonths` reduzido
de 3 para 1 mês e `StatusPageSize` reduzido de 200 para 50 (commit
`2cf8e05`), reduzindo o volume por ciclo. **Fase 4 segue não validada
ponta a ponta — não prosseguir para a Fase 5 até a causa raiz ser
identificada e um ciclo completo (todas as páginas) ser confirmado como
projetado corretamente.**

## Objetivo

Executar o redesenho da persistência Mongo do Collector em fases pequenas
e independentes, evitando um "big bang" de implementação. Cada fase é
isolada, testável e não bloqueia o pipeline atual (`work_order_snapshots`
→ Change Stream → `SnapshotConsumerWorker`, superseded por
`provider_interactions` → Change Stream → `ProviderInteractionConsumerWorker`
→ Postgres) até que a substituição esteja validada com dado real.

Escopo geral da mudança (decisão do usuário, detalhada pela ADR em
formalização):

- Deixar de gravar `work_order_snapshots` (1 documento por OS) e passar a
  gravar o registro bruto da **interação** do Collector com o provedor
  (request + response, podendo conter N ordens de serviço por documento).
- Persistir e reutilizar a sessão do provedor (login CAS) entre ciclos de
  coleta, evitando login repetido a cada execução.
- Avaliar migrar a estratégia de busca de "por status" (N chamadas) para
  "por data" (1 chamada paginada, sem filtro de status), com base em
  endpoint real confirmado (`queryWorkOrderList` aceitando
  `woStatus: ""` + `woStatusCond: "me"` + intervalo de datas).

## Por que faseado

A reescrita completa em um único passo reescreveria um pipeline já
implementado e validado em produção (RF-016/RF-017/ADR-023), com risco de
regressão real. O plano abaixo separa **captura aditiva** (baixo risco)
de **mudança do consumer/Postgres** (alto risco) e só remove o pipeline
antigo depois de validação ponta a ponta com dado real.

## Fases

### Fase 0 — ADR formal (bloqueante)

**Responsável:** `software-architect`

Formalizar em `docs/decisions/` a ADR que substitui parcialmente o
ADR-023 e o RF-016, cobrindo:

- Schema de `provider_interactions` (coleção única, fonte do Change
  Stream, documento por request/página de listagem, por enriquecimento
  de OS, e evento de auditoria de login).
- Schema de `provider_sessions` (documento mutável por
  `tenant_id + provider_type`, storage state do Playwright, TTL
  conservador, não observado por Change Stream).
- Novo índice de idempotência em `provider_interactions` (por identidade
  de interação, não mais por `provider_id`).
- Atualização/substituição de RF-016 e RF-017.
- Novo requisito de persistência/reuso de sessão do provedor.
- Definição formal do termo "Pipeline de projeção".

Nenhuma fase de implementação abaixo começa antes desta ADR estar escrita
e revisada com o usuário.

### Fase 1 — `provider_interactions` aditivo

**Responsável:** `backend-engineer`

- Criar a coleção Mongo `provider_interactions`.
- Adicionar pontos de escrita dentro do loop de coleta em
  `IServiceCollectorService` (login, cada `list_query` por
  status/página, cada `detail_query` de enriquecimento).
- **Não remover** `work_order_snapshots` / `WorkOrderRepository` nesta
  fase — os dois pipelines rodam em paralelo, permitindo comparar o dado
  real gravado pelo novo formato sem risco para o que já está em
  produção.
- Sem consumer associado ainda (a coleção não é observada por Change
  Stream nesta fase).

### Fase 2 — `provider_sessions` (reuso de sessão)

**Responsável:** `backend-engineer`

- Criar a coleção `provider_sessions`.
- Adaptar `IServiceCollectorService.CollectAsync`:
  1. buscar sessão salva e válida antes de logar;
  2. reaproveitar via `storage_state` do Playwright quando válida;
  3. fazer login normal e persistir nova sessão quando não houver
     sessão válida;
  4. marcar sessão inválida e refazer login se uma request autenticada
     falhar em pleno ciclo (ex.: 401/redirect para tela de login).
- Depende da Fase 1 (mesmo padrão de conexão Mongo já estabelecido).

### Fase 3 — Migração de busca por status para busca por data

**Responsável:** `backend-engineer`

- Substituir `FetchWorkOrdersForStatusAsync` / `FetchWorkOrdersPageAsync`
  por uma única chamada paginada a `queryWorkOrderList` com
  `woStatus: ""`, `woStatusCond: "me"` e `creationDateFrom`/
  `creationDateTo` do período desejado.
- Validar, lado a lado, que a nova estratégia retorna o mesmo conjunto de
  OS que a estratégia antiga (por status) antes de desligar a antiga.
- Pode ser feita em paralelo à Fase 2 (mudança isolada no fluxo de
  coleta, não na persistência).

### Fase 4 — Consumer decompõe array de N OS por documento

**Responsável:** `backend-engineer`

- Trocar `ISnapshotAdapter.ExtractStatus` (1 OS) por `ExtractOrders`
  (N OS).
- Adaptar `WorkOrderPgRepository` para processar um documento inteiro de
  `provider_interactions` numa única transação (N upserts + N históricos
  condicionais + 1 resume token).
- Trocar a fonte do Change Stream de `work_order_snapshots` para
  `provider_interactions`, filtrando `interaction_type` diferente de
  `login`.
- Criar o novo índice de idempotência por identidade de interação.
- Depende das Fases 1 e 3 estarem concluídas e com dado real suficiente
  para validar o novo schema.

### Fase 5 — Cutover (remoção do pipeline antigo)

**Responsável:** `backend-engineer`

- Só após a Fase 4 validada ponta a ponta (produção ou ambiente
  equivalente) por um período de observação.
- Remover `WorkOrderRepository`, `IWorkOrderRepository`,
  `WorkOrderSnapshotDocument` e parar de escrever em
  `work_order_snapshots`.
- Decisão separada (produto, não arquitetural): apagar os documentos
  antigos ou deixá-los como resíduo histórico morto.

### Fase 6 — Validação QA ponta a ponta

**Responsável:** `qa-engineer`

Validar com iService real:

- Idempotência: reprocessar o mesmo `command_id` não duplica dado.
- Reuso de sessão funciona e evita login repetido.
- Busca por data retorna o mesmo conjunto de OS que a busca por status
  trazia antes.
- Resume token do Change Stream sobrevive a restart do Worker.

### Fase 7 — Documentação viva

**Responsável:** `documentation`

- Confirmar que RF-016/RF-017/novo RF de sessão (escritos na Fase 0)
  refletem o que foi de fato implementado.
- Atualizar `docs/features/` se aplicável.
- Atualizar `docs/architecture/` com a definição de "Pipeline de
  projeção".

### Fase 8 — Deploy AWS e observação real

**Responsável:** orquestração via skill `atua-deploy`

- `make redeploy-collector` para publicar a versão final.
- `make collector-trigger-cycle` contra o iService real da Natal
  Refrigeração.
- `make logs-collector-follow` para confirmar comportamento em produção
  real.

## Dependências entre fases

```text
Fase 0 (ADR)
  |
  +--> Fase 1 (provider_interactions aditivo)
  |      |
  |      +--> Fase 2 (provider_sessions)
  |      |
  +--> Fase 3 (busca por data)
  |
  (Fase 1 + Fase 3) --> Fase 4 (consumer decompõe array)
                            |
                            +--> Fase 6 (QA)
                            |
  (Fase 4 + Fase 2) --> Fase 5 (cutover)

Fase 6 --> Fase 7 (documentação) --> Fase 8 (deploy AWS)
```

## Rastreamento

O progresso fase a fase é acompanhado na sessão de trabalho corrente
(tabela de todos), com dependências espelhando a estrutura acima. Este
documento é a referência formal para consulta por qualquer agente ou
humano que retome o trabalho posteriormente.
