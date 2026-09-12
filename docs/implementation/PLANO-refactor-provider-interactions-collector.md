# Plano de Implementação — Refactor de Persistência do Collector (provider_interactions / provider_sessions)

## Status

`FASE_0_CONCLUIDA` — ADR-028 escrita e aprovada pelo usuário em
2026-09-12 (`docs/decisions/ADR-028-registro-bruto-de-interacao-com-provedor-e-sessao-persistida.md`),
junto com RF-022 e RF-023. Fase 1 acionada para `backend-engineer`.

## Objetivo

Executar o redesenho da persistência Mongo do Collector em fases pequenas
e independentes, evitando um "big bang" de implementação. Cada fase é
isolada, testável e não bloqueia o pipeline atual (`work_order_snapshots`
→ Change Stream → `SnapshotConsumerWorker` → Postgres) até que a
substituição esteja validada com dado real.

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
