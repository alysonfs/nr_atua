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

**Validação com dado real (2026-09-14) — achado inicial e causa raiz confirmada:**
Ciclo real (tenant `01a0888b-acd8-773b-93b7-7362973d7ea8`, comando
`01a0a1f9-1bc6-7d4b-af4a-e4adc9f124a5`) gerou 9 interações em
`provider_interactions` (1 `login` + 6 `list_query`, 1035 OS únicas + 2
`detail_query`). Uma primeira checagem encontrou apenas 563 de 1035 OS
projetadas em `work_orders`, levantando suspeita de bug no consumer
(processamento parcial silencioso).

Investigação local (Collector rodando via depurador do VS Code) mostrou o
`ProviderInteractionConsumerWorker` reabrindo o Change Stream com o
resume token persistido e processando o backlog restante normalmente,
sem nenhuma exceção. Reconferência direta em Postgres/Mongo após esse
run confirmou: `work_orders` passou a ter exatamente 1035 linhas (igual
ao total único de OS no Mongo) e 0 IDs faltando entre Mongo e Postgres.
**Não havia bug** — o estado de 563/1035 era simplesmente backlog ainda
não consumido (processo local havia sido interrompido antes de esgotar
o Change Stream), não uma falha de processamento. Ao deixar o consumer
rodar até drenar o backlog, o ciclo completo foi projetado corretamente,
incluindo upserts e históricos condizentes.

Mitigação aplicada durante a investigação (mantida, não é mais bloqueante
mas reduz volume por ciclo): `HistoryWindowMonths` reduzido de 3 para 1
mês e `StatusPageSize` reduzido de 200 para 50 (commit `2cf8e05`).

**Fase 4 validada ponta a ponta com dado real — liberada para a Fase 5.**

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

**Status: concluída (2026-09-14).** Removidos `WorkOrderRepository`,
`IWorkOrderRepository`, `WorkOrderSnapshotDocument` e `WorkOrderMapper`
(além dos testes `WorkOrderRepositoryTests`). `Worker` não recebe mais
`IWorkOrderRepository` nem chama `InsertSnapshotsAsync` — apenas loga o
resumo (`StatusCounts`) da coleta antes de `CompleteAsync`; a persistência
passa a ser feita integralmente por `IServiceCollectorService` em
`provider_interactions` (Fase 1) e projetada pelo
`ProviderInteractionConsumerWorker` (Fase 4). `Program.cs` não registra
mais o repositório antigo nem chama seu `EnsureIndexesAsync`. Build e
suíte de testes do Collector passando (45/45 após remover os 3 testes do
repositório removido). `work_order_snapshots` deixa de ser escrito a
partir deste commit — decisão de apagar os documentos antigos ou
deixá-los como resíduo histórico morto fica para decisão de produto
separada (nenhum dado foi apagado do Mongo por esta fase).

### Fase 6 — Validação QA ponta a ponta

**Responsável:** validação manual guiada (ver
[qa-manual-fase6-provider-interactions.md](/Users/alysonfs/workspace/Software/natal-refrigeracao/atua/docs/guides/qa-manual-fase6-provider-interactions.md)),
sem acionamento do agente `qa-engineer` — resultados analisados
diretamente na sessão para evitar custo/tempo de um agente QA completo.

Validar com iService real:

- ✅ Idempotência: reprocessar o mesmo `command_id` não duplica dado
  (`work_orders` estável, `work_order_histories` cresce só com mudanças
  reais de status).
- ⏳ Reuso de sessão funciona e evita login repetido — login CAS completo
  observado em vez de reuso; provável apenas TTL de sessão expirado
  (comportamento esperado), precisa de mais um ciclo para confirmar reuso
  real.
- ⚠️ Busca por data retorna o mesmo conjunto de OS que a busca por status
  trazia antes — **divergência real encontrada**: nossa agregação por
  `woStatus` (via `provider_interactions`) somou 150 OS, enquanto uma
  busca manual no portal iService (tela "Pesquisa de Ordem de Serviço")
  no mesmo período retornou 374. Explicação mais provável: as duas telas
  do portal têm escopos diferentes — a tela "Visão por Status" (de onde
  vem o template/sessão usado pelo Collector, com `woStatusCond: "me"`)
  parece restringir ao escopo do usuário/credencial logada, enquanto
  "Pesquisa de Ordem de Serviço" é uma busca mais ampla, sem essa
  restrição. **Ainda não confirmado 1:1** — falta comparar o total da
  própria tela "Visão por Status" (soma das 5 abas: Designado/Em
  Processamento/Pendente/Concluído/Cancelado) contra os 150 que
  coletamos, para isolar se a diferença é só de escopo de tela ou se há
  perda real de OS na paginação por data. Enquanto não confirmado,
  **risco em aberto**: se `woStatusCond: "me"` de fato limitar a coleta
  às OS atribuídas à credencial de serviço (e não a todas as OS do
  tenant/conta), isso é uma lacuna de completude real, não um artefato de
  comparação — requer confirmação com o usuário/negócio sobre o que a
  credencial de coleta deveria enxergar.
- ✅ Resume token do Change Stream sobrevive a restart do Worker — log
  confirma reabertura com `ResumeToken=sim`, processamento continua sem
  duplicar (`work_orders`/`work_order_histories` estáveis entre o corte e
  a retomada).

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

## Backlog / débitos técnicos identificados na Fase 6

Itens encontrados durante a validação manual que **não bloqueiam** o
encerramento desta refatoração, mas ficam registrados para não se
perderem:

### 1. Datas do iService não são persistidas (`work_orders`/`work_order_histories`)

Hoje `IServiceProviderInteractionOrderAdapter.ExtractOrders` (ver
[IServiceProviderInteractionOrderAdapter.cs](/Users/alysonfs/workspace/Software/natal-refrigeracao/atua/apps/collector/Atua.Collector/Consumer/IServiceProviderInteractionOrderAdapter.cs))
só extrai `workOrderId`/`id` e `woStatus` de cada OS bruta em
`provider_interactions.orders[]`. Nenhuma data de negócio do iService é
extraída ou persistida — `work_orders.CreatedAt`/`UpdatedAt` refletem
apenas o momento da nossa ingestão, não datas reais da OS na origem.

O payload bruto do `list_query` (`orders[]`) confirmado em produção expõe,
entre outros, os campos `creationDate` e `lastUpdateDate` (nomes exatos
capturados via Compass em 2026-09-15). Proposta (a implementar em uma
iteração futura, fora do escopo desta refatoração):

- `creationDate` → novo campo `ProviderCreatedAt` em `work_orders`.
- `lastUpdateDate` → novo campo `ProviderUpdatedAt` em `work_orders`.

Os demais ~50 campos de data do payload (`requestDate`, `promisedDate`,
`nextVisitDate`, `closeDate`, `visitDate`, `expectedDate`,
`purchaseDate`, etc. — ver payload completo capturado) não têm uso
identificado hoje e ficam fora deste backlog até surgir necessidade
concreta (ex.: SLA de visita técnica).

### 2. Escopo de `woStatusCond: "me"` na busca por data — precisa confirmação

Ver nota na Fase 6 acima: a comparação com o portal indicou uma possível
diferença de escopo (credencial/usuário logado vs. conta inteira do
tenant) entre a tela usada pelo Collector ("Visão por Status") e a tela
usada na validação manual ("Pesquisa de Ordem de Serviço"). Antes de
considerar este item fechado, é preciso confirmar — comparando o total
da própria tela "Visão por Status" (soma das 5 abas) contra os 150 OS
coletados — se `woStatusCond: "me"` cobre todas as OS do tenant ou apenas
as atribuídas à credencial de serviço usada na coleta.

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
