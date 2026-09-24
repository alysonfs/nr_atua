# ADR-031 - Decisão de Enriquecimento de Detalhe de OS no Consumer, Sinalizada ao Collector via Claim

## Status

Accepted

## Contexto

RF-026 (Enriquecimento e Exibição de Detalhes de Ordem de Serviço) formaliza
três regras de negócio sobre **quando** buscar detalhe de uma OS via
`queryOneWorkOrder` — nova (RF-026.1), mudança de status (RF-026.2), e skip
se já enriquecida e em status terminal confirmado (`"Payment Approved"`,
`"cancelled"`, `"closed"` — RF-026.3) — generalizando o mecanismo hoje
restrito a OS `"assigned"` (RF-026.4) para todas as OS coletadas. RF-026
deixa deliberadamente em aberto **onde** essa decisão deve rodar (DP-026.1),
remetendo a decisão a esta ADR.

### Contexto técnico verificado (código atual)

* **`IServiceCollectorService.EnrichAssignedOrdersAsync`**
  (`apps/collector/Atua.Collector/IService/IServiceCollectorService.cs`,
  linha ~1113) roda **dentro do mesmo ciclo de coleta**, logo após
  `ordersByStatus["assigned"]` ser obtido via `list_query`, usando a mesma
  sessão de browser Playwright já autenticada. É incondicional para toda OS
  `"assigned"` — não há nenhuma consulta a estado anterior. Cada chamada a
  `queryOneWorkOrder` gera um documento `provider_interactions` próprio
  (`interaction_type="detail_query"`, um por OS) via
  `LogProviderInteractionSafeAsync`.
* **O Collector já possui um canal HTTP síncrono e autenticado com a Master
  API**: `ICollectorApiClient`/`CollectorApiClient`
  (`apps/collector/Atua.Collector/Api/CollectorApiClient.cs`) chama
  `POST /api/internal/collector/commands/claim` e
  `POST /api/internal/collector/commands/{id}/complete` a cada ciclo
  (ADR-020/ADR-021), autenticado via `ServiceCredential` com escopo
  `collector.command.claim`. Este canal **já existe e já é usado a cada
  ciclo** — não é uma dependência nova a ser introduzida.
* **`WorkOrderPgRepository.UpsertWorkOrderAsync`**
  (`apps/collector/Atua.Collector/Consumer/WorkOrderPgRepository.cs`) — que
  roda no `ProviderInteractionConsumerWorker`, reagindo a
  `provider_interactions` via Change Stream (ADR-023/ADR-028) — **já lê o
  status anterior de `work_orders` e já calcula `(existingId,
  previousStatus, isNew)`** antes de decidir se cria um `work_order_history`
  (RF-017.2). Ou seja, a comparação exigida por DP-026.1 ("esta OS é nova ou
  mudou de status?") **já é feita neste método, hoje, para outro fim**.
  Estender essa mesma comparação para decidir "preciso buscar detalhe?" não
  introduz nenhuma consulta nova ao Postgres — reaproveita a que já existe.
* **Risco identificado e relevante para esta decisão**: `UpsertWorkOrderAsync`
  hoje faz `UPDATE work_orders SET ... {DetailAssignmentList()}` de forma
  incondicional, sobrescrevendo **todas** as colunas de detalhe
  (`CustomerName`, `Address`, `ProductBrand` etc. — ver `DetailColumns`) com
  o valor do `ProviderWorkOrderData` recebido, convertendo `null` para
  `DBNull`. Uma interação `list_query` (sem detalhe) que hoje já contém uma
  OS que só será enriquecida por um documento `detail_query` (com
  `orders`/`orderDetail`) já pode, sob os dados de "assigned", correr o
  risco de zerar campos de detalhe já capturados. Ao generalizar o
  enriquecimento para todas as OS (RF-026.4), esse risco deixa de ser
  restrito a "assigned" e passa a afetar toda OS reconhecida — precisa ser
  corrigido como parte desta decisão (ver "Decisão", item 4).
* **`ImmediateCollectionCommand`/`RecurrentCollectionSchedulerJob`**
  (ADR-029) já criam periodicamente comandos `Pending` por integração,
  reivindicados via `claim`. Não existe hoje nenhuma distinção de "tipo" de
  ciclo — todo comando dispara o mesmo ciclo completo (login/sessão →
  `list_query` de todos os status → enriquecimento condicional).

## Decisão

### Opção escolhida: **Opção B — a decisão roda no Consumer**, comunicada ao Collector como um dado adicional no `claim` já existente (sem novo tipo de comando)

A comparação "esta OS é nova, mudou de status, ou está em terminal já
enriquecida?" roda no `ProviderInteractionConsumerWorker`
(`WorkOrderPgRepository.UpsertWorkOrderAsync`), no mesmo ponto em que hoje já
calcula `previousStatus`/`isNew` para `work_order_history`. O resultado dessa
decisão é persistido como um **flag por OS em `work_orders`**
(`NeedsDetailFetch`), e não como um novo tipo de comando assíncrono: o
Collector passa a receber, **dentro da resposta do `claim` já existente**, a
lista de `WorkOrderProviderId` pendentes de detalhe para a integração
reivindicada, e busca detalhe para exatamente essas OS no ciclo em curso,
generalizando `EnrichAssignedOrdersAsync` para todos os status (RF-026.4).

Isso combina a simplicidade de reaproveitar o `claim`/ciclo recorrente já
existente (sem inventar um novo `ECollectionCommandType` de "somente
detalhe") com a correção de manter a decisão perto de onde o estado
anterior já é conhecido (Postgres, no Consumer) — sem exigir que o Collector
consulte o Postgres, direta ou indiretamente por um novo endpoint dedicado.

### 1. Nova coluna `NeedsDetailFetch` (bool) e `DetailsFetchedAt` (timestamptz, nullable) em `work_orders`

* `DetailsFetchedAt`: marca a última vez em que um `detail_query`
  bem-sucedido foi aplicado a esta OS. É a resposta definitiva à ambiguidade
  técnica deixada em aberto por RF-026.3 ("nota técnica") — **decidida
  aqui**: nem heurística sobre campos populados, nem consulta a
  `provider_interactions` (que é efêmera pós-ADR-030 e não pode ser fonte de
  verdade para isso). `DetailsFetchedAt != null` é o critério de "já
  enriquecida" usado pela regra de skip terminal (RF-026.3).
* `NeedsDetailFetch`: fila implícita de "preciso buscar detalhe desta OS na
  próxima oportunidade". `true` quando a regra de negócio (abaixo) decide
  que detalhe é necessário; `false` quando não há pendência. É a estrutura
  de dados que substitui um novo tipo de comando assíncrono — o Collector
  não precisa saber "por que" precisa buscar detalhe, apenas "quais IDs".

### 2. Regra de decisão em `UpsertWorkOrderAsync` (Consumer)

Ao processar cada OS de uma interação (`list_query` ou `detail_query`):

```text
isTerminal(status) := status ∈ { "Payment Approved", "cancelled", "closed" }  // RN-026.3

needsDetail :=
    isNew
    OR (statusChanged AND NOT (existing.DetailsFetchedAt != null AND isTerminal(existingStatus)))
```

* OS nova → `NeedsDetailFetch = true` (RF-026.1).
* OS existente, status mudou, e **não** era terminal-já-enriquecida →
  `NeedsDetailFetch = true` (RF-026.2).
* OS existente, status mudou, mas já era terminal e já tinha
  `DetailsFetchedAt` preenchido → `NeedsDetailFetch` permanece como estava
  (não é setado; defensivo contra a hipótese rara de um status terminal
  "mudar" para outro terminal — RF-026.3, nota sobre imutabilidade de
  terminais).
* OS existente, status **não** mudou → `NeedsDetailFetch` não é tocado
  (preserva um `true` pendente de um ciclo anterior que ainda não foi
  atendido pelo Collector; não reintroduz um `false` indevido).
* Quando a interação sendo processada é, ela própria, um `detail_query`
  bem-sucedido para aquela OS: `DetailsFetchedAt = interactionCreatedAt` e
  `NeedsDetailFetch = false`, incondicionalmente — é a confirmação de que a
  pendência foi atendida.

### 3. `claim` passa a devolver a lista de OS pendentes de detalhe da integração

`ImmediateCollectionCommandService.ClaimAsync`
(`apps/api/Atua.Api/Application/Integrations/CollectorControl/ImmediateCollectionCommandService.cs`)
adiciona, à mesma consulta que já resolve a integração/credenciais no claim,
uma leitura adicional:

```csharp
var pendingDetailIds = await dbContext.WorkOrders
    .Where(w => w.IntegrationId == integrationId && w.NeedsDetailFetch)
    .Select(w => w.WorkOrderProviderId)
    .ToListAsync(cancellationToken);
```

`ClaimCommandResult`/`ClaimResponse`
(`apps/collector/Atua.Collector/Contracts/ClaimResponse.cs`) ganha um novo
campo `IReadOnlyList<string> PendingDetailWorkOrderIds`. Não é criado nenhum
endpoint novo, nenhum novo tipo de comando, nenhuma nova tabela de fila — é
uma projeção adicional sobre uma tabela já existente, devolvida no mesmo
payload que o Collector já lê a cada ciclo.

### 4. Correção do `UPDATE` de colunas de detalhe: `COALESCE` em vez de sobrescrita incondicional

Como parte desta decisão (necessária para RF-026.4 não regredir dados),
`UpsertWorkOrderAsync` passa a montar o `UPDATE` das colunas de
`DetailColumns` com `COALESCE(@dX, "ColX")` em vez de `"ColX" = @dX` puro —
uma interação sem aqueles campos (ex.: `list_query` puro, sem
`orderDetail`) nunca mais apaga um valor de detalhe já capturado por um
`detail_query` anterior. O `INSERT` (OS nova) continua gravando os valores
recebidos diretamente (não há valor anterior a preservar).

### 5. Collector: generalização de `EnrichAssignedOrdersAsync`

`EnrichAssignedOrdersAsync` é renomeado para `EnrichPendingDetailOrdersAsync`
e deixa de operar apenas sobre `ordersByStatus["assigned"]`: passa a receber
o conjunto de `PendingDetailWorkOrderIds` do `ClaimResponse` do ciclo
corrente e filtra, dentre **todas** as OS já obtidas via `list_query` nesse
ciclo (independente de status), aquelas cujo id do provedor está na lista
recebida — chamando `queryOneWorkOrder` apenas para essas. O restante do
método (chamada Playwright, log de `provider_interactions` com
`interaction_type="detail_query"`, tratamento de sessão expirada) permanece
inalterado.

### Por que esta opção (e não A ou uma C totalmente nova)

* **Não introduz acoplamento síncrono novo entre Collector e
  Postgres/API.** O Collector nunca consulta o Postgres, direta ou
  indiretamente por um endpoint dedicado — apenas lê um campo a mais numa
  resposta HTTP que ele **já** consome a cada ciclo (`claim`). Isso
  preserva integralmente o princípio "Collector só fala com o provedor e
  com o Mongo" quanto a **novas** dependências: a única exceção existente
  (canal `claim`/`complete`) já era uma exceção aceita e em produção antes
  desta ADR, não uma criada por ela.
* **Não introduz um novo tipo de `ImmediateCollectionCommand`.** A opção B,
  como enunciada no problema original, previa "pedir ao Collector para
  buscar detalhe em um ciclo futuro via um novo tipo de comando". Esta
  decisão evita até essa complexidade adicional: a informação "quais OS
  precisam de detalhe" trafega como um campo simples na resposta de um
  comando que já existe (o ciclo recorrente completo, ADR-029), em vez de
  um novo tipo de comando com seu próprio ciclo de vida
  (`Pending/Claimed/Succeeded/Failed`) a ser modelado, testado e operado.
* **Reaproveita computação já existente no Consumer.** `previousStatus`/
  `isNew` já são calculados em `UpsertWorkOrderAsync` para popular
  `work_order_history` (RF-017.2) — a decisão de "preciso de detalhe?" é
  literalmente a mesma comparação, sem nenhuma consulta adicional ao banco.
* **Mantém a separação de responsabilidades vigente.** Collector continua
  só falando com o provedor (scraping) e o Mongo (grava eventos brutos);
  Consumer continua só falando com Mongo (lê eventos) e Postgres (projeta
  estado e agora também decide/sinaliza pendência de detalhe, que é uma
  extensão natural de "projetar estado").
* **Auditabilidade preservada.** Toda chamada real a `queryOneWorkOrder`
  continua gerando um documento `provider_interactions` com
  `interaction_type="detail_query"` (RF-022/ADR-028), sem alteração de
  schema.
* **Latência adicional é aceitável e já era esperada pela Opção B original.**
  Uma OS marcada `NeedsDetailFetch=true` pelo processamento assíncrono de um
  `list_query` só é atendida no **próximo** `claim` da integração — não no
  mesmo ciclo em que a mudança de status foi vista. Como o intervalo mínimo
  de coleta recorrente é 5 minutos (RF-025.4/ADR-029) e o Change Stream do
  Mongo processa interações em segundos, esse atraso é desprezível frente ao
  próprio ciclo de coleta e não é um requisito de tempo real em RF-026.

## Alternativas consideradas

* **Opção A — decisão no Collector, durante o `list_query`.** Rejeitada.
  Exigiria que o Collector, durante o scraping, consultasse o Postgres
  (direta ou via um novo endpoint HTTP síncrono) **para cada OS** antes de
  decidir se busca detalhe — uma dependência síncrona nova e potencialmente
  cara (N consultas por ciclo, N = OS coletadas, que pode ser grande sob
  RF-024) em vez de uma única leitura em lote já embutida no `claim`
  (decisão desta ADR). Romperia a separação onde só o Consumer fala com
  Postgres, sem ganho real: o Consumer já tem o estado anterior disponível
  sem custo adicional, então mover a decisão para o Collector apenas
  duplicaria a necessidade de acesso a esse estado em outro lugar.
* **Opção B "pura" (com novo tipo de comando assíncrono
  `DetailFetchCommand` reivindicado separadamente do ciclo recorrente).**
  Considerada e rejeitada em favor da variante adotada (B via `claim`
  estendido). Um novo tipo de comando exigiria: novo enum de tipo de
  comando, novo endpoint ou branch de `claim`, novo ciclo de vida
  (`Pending/Claimed/...`), e uma nova sessão de browser dedicada apenas
  para detalhe (o detalhe só pode ser buscado com uma sessão autenticada
  ativa — a mesma que já existe durante o ciclo recorrente). Isso
  duplicaria infraestrutura já existente (o ciclo recorrente já abre sessão
  e já teria, de qualquer forma, que rodar para manter `list_query`
  atualizado) sem nenhum benefício sobre simplesmente devolver a lista de
  pendências no mesmo `claim`.
* **Opção C explorada — endpoint HTTP dedicado e síncrono
  (`GET /internal/collector/.../pending-details`) chamado pelo Collector à
  parte do `claim`.** Rejeitada por redundância: introduziria uma segunda
  chamada HTTP por ciclo quando a mesma informação cabe, sem custo
  arquitetural adicional, dentro da resposta do `claim` já existente. Um
  endpoint novo só se justificaria se a lista de pendências precisasse ser
  consultada em um momento diferente do claim (não é o caso: o Collector só
  pode agir sobre ela durante o mesmo ciclo de coleta que já começa com um
  claim).
* **Heurística "campo populado" ou consulta a `provider_interactions` para
  decidir "já enriquecida" (RF-026.3, nota técnica).** Rejeitadas como
  mecanismo de marca de "já busquei detalhe". A heurística de campo
  populado é frágil (um campo genuinamente vazio no provedor seria
  indistinguível de "nunca buscado"); consultar `provider_interactions` é
  inviável porque a coleção é efêmera por decisão de produto (ADR-030,
  exclusão logo após projeção) — não pode ser fonte de verdade para nada
  além do curtíssimo prazo entre inserção e exclusão. A coluna
  `DetailsFetchedAt` em `work_orders` (decisão desta ADR) é a única fonte
  estável e correta.

## Consequências

### Impactos de implementação (para backend-engineer)

**API (`apps/api/Atua.Api`)**

* `Domain/WorkOrders/WorkOrder.cs`: adicionar propriedades
  `bool NeedsDetailFetch` e `DateTimeOffset? DetailsFetchedAt`, com métodos
  de atualização seguindo o padrão já usado por `UpdateDetails`/`Status`
  nesta entidade (ex.: `MarkDetailsFetched(DateTimeOffset at)`,
  `MarkNeedsDetailFetch()`).
* `Infrastructure/Persistence/AtuaDbContext.cs`: mapear as duas novas
  colunas na configuração de `WorkOrder` (nullable para
  `DetailsFetchedAt`, `NOT NULL DEFAULT false` para `NeedsDetailFetch`).
* Nova migration EF Core em
  `Infrastructure/Persistence/Migrations/` adicionando as duas colunas em
  `work_orders`, seguindo o padrão das migrations existentes (ex.:
  `20260917210447_AddRecurrentCollectionIntervalToIntegration`). Considerar
  índice parcial `WHERE "NeedsDetailFetch" = true` por `IntegrationId`, dado
  que o `claim` passa a consultar essa condição a cada ciclo.
* `Application/Integrations/CollectorControl/ImmediateCollectionCommandService.cs`:
  em `ClaimAsync`, após resolver a integração/credenciais, adicionar a
  consulta de `WorkOrderProviderId`s com `NeedsDetailFetch = true` para
  `integrationId`, e incluir o resultado em `ClaimCommandResult`.
* `Contracts/ClaimCommandResult.cs`/equivalente (o record retornado por
  `ClaimAsync` na API, análogo ao `ClaimResponse` do lado Collector):
  adicionar `IReadOnlyList<string> PendingDetailWorkOrderIds`.
* `Endpoints/CollectorCommandEndpoints.cs`: mapear o novo campo do
  `ClaimCommandResult` para o corpo JSON de resposta do endpoint
  `POST /api/internal/collector/commands/claim` (sem alterar a rota nem o
  contrato de autenticação existente).

**Collector (`apps/collector/Atua.Collector`)**

* `Contracts/ClaimResponse.cs`: adicionar
  `IReadOnlyList<string> PendingDetailWorkOrderIds`.
* `IService/IServiceCollectorService.cs`:
  * Renomear `EnrichAssignedOrdersAsync` para
    `EnrichPendingDetailOrdersAsync`, alterando a assinatura para receber
    `IReadOnlyList<string> pendingDetailWorkOrderIds` em vez de operar
    implicitamente sobre `ordersByStatus["assigned"]`.
  * O chamador (`ExecuteCollectionCycleAsync`) passa a filtrar, sobre o
    conjunto completo de OS obtidas no ciclo (todas as chaves de
    `ordersByStatus`, ou o resultado unificado de
    `FetchWorkOrdersByDateRangeAsync` sob RF-024), apenas as OS cujo id
    esteja em `pendingDetailWorkOrderIds`, chamando
    `EnrichPendingDetailOrdersAsync` uma única vez sobre esse subconjunto —
    em vez de uma vez por status.
  * O restante do corpo do método (chamada `queryOneWorkOrder` via
    Playwright, log de `provider_interactions` com
    `interaction_type="detail_query"`, tratamento de
    `ProviderSessionInvalidException`) permanece sem alteração de
    comportamento.
* `Worker.cs`: nenhuma alteração de fluxo — o `ClaimResponse` já é
  repassado ao `IIServiceCollector`/`IServiceCollectorService`; apenas o
  novo campo passa a ser lido.

**Consumer (`apps/collector/Atua.Collector/Consumer`)**

* Novo arquivo `Consumer/WorkOrderTerminalStatuses.cs` (classe estática,
  não enum — os valores são strings exatas do provedor, não um conjunto
  fechado controlado pelo ATUA): expõe
  `IReadOnlySet<string> Values` com `"Payment Approved"`, `"cancelled"`,
  `"closed"` (comparação `StringComparison.Ordinal`, mesma convenção já
  usada em `UpsertWorkOrderAsync` para comparar status) e um método
  `bool IsTerminal(string status)`. Centraliza a lista para não duplicá-la
  entre `UpsertWorkOrderAsync` e qualquer teste unitário.
* `WorkOrderPgRepository.UpsertWorkOrderAsync`:
  * No `SELECT` que lê o estado atual, incluir também
    `"DetailsFetchedAt"` (além de `"Id"`, `"Status"`).
  * Aplicar a regra de decisão da seção "Decisão, item 2" para calcular
    `needsDetail` e, quando a interação sendo processada for
    `detail_query`, calcular a atualização de `DetailsFetchedAt`/
    `NeedsDetailFetch = false`.
  * Alterar `DetailAssignmentList()` (usado no `UPDATE`) para gerar
    `"ColX" = COALESCE(@dX, "ColX")` em vez de `"ColX" = @dX` (decisão
    "Decisão", item 4). O `INSERT` (`DetailColumnList()`/`DetailParamList()`)
    permanece inalterado.
  * `UPDATE`/`INSERT` passam a incluir `"NeedsDetailFetch"` e
    `"DetailsFetchedAt"` na lista de colunas afetadas.
* `ProviderInteractionConsumerWorker.ProcessDocumentAsync`: ao chamar
  `pgRepository.ProcessInteractionAsync`, passar também o
  `interactionType` (já lido nesse método) para que
  `WorkOrderPgRepository` saiba diferenciar `detail_query` de `list_query`
  ao decidir a atualização de `DetailsFetchedAt`/`NeedsDetailFetch` — hoje
  `ProcessInteractionAsync` não recebe esse dado; a assinatura de
  `IWorkOrderPgRepository.ProcessInteractionAsync` precisa de um parâmetro
  adicional `string interactionType`.
* `IWorkOrderPgRepository.cs`: atualizar a assinatura de
  `ProcessInteractionAsync` conforme acima.

### Riscos e mitigação

* **Janela de atraso entre `list_query` marcar `NeedsDetailFetch=true` e o
  próximo `claim` da integração**: aceita, ver "Por que esta opção" —
  limitada ao intervalo de coleta recorrente já configurado (mínimo 5
  minutos, RF-025.4), sem requisito de tempo real em RF-026.
* **Falha ao aplicar `detail_query` (ex.: sessão expirada em pleno voo,
  `ProviderSessionInvalidException`)**: o comportamento de retry já
  existente em `ExecuteCollectionCycleAsync` (novo login + retomada do
  ciclo) se aplica sem alteração; se o ciclo inteiro falhar antes de
  processar o detalhe, `NeedsDetailFetch` permanece `true` para aquela OS
  e será retomado no próximo `claim` — nenhuma pendência é perdida
  silenciosamente.
* **Crescimento do número de OS com `NeedsDetailFetch=true` simultâneas**:
  o índice parcial recomendado (`WHERE "NeedsDetailFetch" = true`) mantém a
  consulta do `claim` barata mesmo com muitas OS pendentes; o volume
  esperado no MVP (dezenas de tenants, coleta a cada poucos minutos) não
  representa risco de performance identificável hoje.
* **Regressão do `UPDATE` incondicional zerando campos de detalhe (risco
  pré-existente, ver "Contexto")**: mitigada diretamente por esta ADR via
  `COALESCE` no `UPDATE` (decisão, item 4) — deixa de ser um risco após a
  implementação.
* **Concorrência entre duas leituras de `NeedsDetailFetch=true` no `claim`
  e uma atualização simultânea pelo Consumer**: sem risco de dado
  inconsistente grave — na pior hipótese, uma OS que teve
  `NeedsDetailFetch` setado para `true` bem depois do `claim` já ter lido a
  lista simplesmente aguarda o próximo `claim` (mesmo comportamento de
  "atraso aceitável" acima); não há duplicação nem perda de detalhe.

### Compatibilidade

Não altera o contrato de autenticação, o schema de `provider_interactions`
(RF-022/ADR-028), nem o ciclo de vida de `ImmediateCollectionCommand`
(ADR-020/ADR-029). É aditiva: dois campos novos em `work_orders`, um campo
novo na resposta de `claim` já existente (`ClaimResponse`/
`ClaimCommandResult`), e uma correção pontual (`COALESCE`) na lógica de
`UPDATE` já existente do Consumer. Nenhum endpoint novo é criado; nenhum
tipo de comando novo é criado.

## Agentes envolvidos

* software-architect: análise de DP-026.1, decisão do mecanismo (Consumer
  decide + sinalização via `claim` existente) e justificativa desta ADR.
* backend-engineer: implementação dos itens listados em "Impactos de
  implementação" (API, Collector, Consumer), incluindo a migration EF Core.
* qa-engineer: validação dos 8 critérios de aceite de RF-026 (novo,
  mudança de status, terminal já enriquecida, transição para terminal,
  terminal como status inicial, navegação/exibição da página de detalhes),
  com atenção especial a: (a) `NeedsDetailFetch` nunca perdido entre
  ciclos; (b) `COALESCE` preservando detalhe já capturado após um
  `list_query` subsequente sem detalhe; (c) nenhuma chamada a
  `queryOneWorkOrder` para OS terminal já enriquecida.
* aws-architect: nenhuma ação necessária — sem impacto de infraestrutura.

## Data

2026-09-22

## Substitui

Não aplicável. Resolve DP-026.1, deixada em aberto por RF-026.

## Referências

* RF-026 — Enriquecimento e Exibição de Detalhes de Ordem de Serviço
  (`docs/requirements/RF-026-enriquecimento-exibicao-detalhes-ordem-servico.md`),
  em especial DP-026.1 e a nota técnica de RF-026.3.
* ADR-023 — Fila e Consumer de Snapshot para Work Order (pipeline de
  projeção original, superseded por ADR-028 quanto à fonte de dados, mas
  com o modelo geral de Change Stream + projeção Postgres mantido).
* ADR-028 — Registro Bruto de Interação com Provedor e Sessão Persistida
  (schema de `provider_interactions`, adapters de extração de OS).
* ADR-029 — Agendador Recorrente de Coleta: `BackgroundService` interno na
  Master API (mecanismo de `ImmediateCollectionCommand` recorrente,
  reaproveitado aqui sem alteração de tipo).
* ADR-030 — Exclusão Imediata de `provider_interactions` Após Projeção
  Confirmada (motivo pelo qual `provider_interactions` não pode ser fonte
  de verdade para "já busquei detalhe desta OS?").
