# ADR-030 - Exclusão Imediata de `provider_interactions` Após Projeção Confirmada

## Status

Accepted

## Contexto

`provider_interactions` (RF-022, ADR-028) é uma coleção append-only no
MongoDB Atlas: um documento por chamada HTTP real ao provedor (login,
`list_query`, `detail_query`), sem upsert, sem TTL, sem processo de
arquivamento. O `ProviderInteractionConsumerWorker`
(`apps/collector/Atua.Collector/Consumer/ProviderInteractionConsumerWorker.cs`)
observa a coleção via Change Stream e projeta cada documento em
`work_orders`/`work_order_histories` no PostgreSQL, avançando o resume
token em `consumer_states` na mesma transação Postgres
(`WorkOrderPgRepository.ProcessInteractionAsync`/`AdvanceResumeTokenAsync`,
em `apps/collector/Atua.Collector/Consumer/WorkOrderPgRepository.cs`).

O usuário (product owner) reportou custo elevado de armazenamento no
MongoDB Atlas atribuído a essa coleção. Como ela nunca é podada, seu
tamanho cresce indefinidamente com o volume de coleta (uma interação por
chamada HTTP, todo ciclo, todo tenant), diferente do PostgreSQL, que
armazena apenas o estado projetado e o histórico de transições de status
(bem mais compacto).

**Decisão de produto já confirmada pelo usuário** (não é objeto de
reavaliação nesta ADR): cada documento de `provider_interactions` deve
ser apagado do MongoDB assim que for processado com sucesso pelo
`ProviderInteractionConsumerWorker` — sem janela de retenção, sem
arquivamento externo. O usuário está ciente de que isso reverte a
motivação original de ADR-028/RF-022 ("estudar a situação das OS através
do ATUA sem depender de acesso direto ao iService") em troca de custo de
armazenamento, e aceitou essa perda de capacidade de auditoria bruta.

Esta ADR define **apenas o mecanismo de exclusão** — quando, onde e com
que garantias de correção o documento é removido. Não reabre nenhuma
outra decisão de ADR-028 (schema de `provider_interactions`, existência
de `provider_sessions`, estratégia de busca por data, granularidade por
interação em vez de por OS).

### Análise do código atual relevante

* **`ProviderInteractionConsumerWorker.ProcessChangeAsync`** tem quatro
  caminhos de saída antecipada que **não** chamam `ProcessInteractionAsync`
  — apenas `AdvanceResumeTokenAsync` — e ainda assim avançam o resume
  token hoje, tratando o documento como concluído:
  1. `interaction_type == "login"` (não carrega OS);
  2. `success == false` (falha de interação, nada a projetar);
  3. adapter não encontrado para `provider_type`;
  4. `orders` vazio após `adapter.ExtractOrders(...)`.

  Todos os quatro casos já são, na prática, "processados" pelo
  consumer atual — o token nunca retrocede sobre eles. Logo, para efeito
  de elegibilidade de exclusão, são equivalentes ao caminho que chama
  `ProcessInteractionAsync` com sucesso.

* **`WorkOrderPgRepository.ProcessInteractionAsync`** abre uma transação
  Postgres, faz N upserts em `work_orders` + N inserts condicionais em
  `work_order_histories`, grava o resume token em `consumer_states` via
  `UpsertConsumerStateAsync`, e só então dá `COMMIT`. Se qualquer etapa
  falhar, há `ROLLBACK` e a exceção sobe — o resume token **não** avança
  e o documento Mongo permanece intacto para ser reprocessado (o Change
  Stream reabre do último token persistido). `AdvanceResumeTokenAsync`
  segue o mesmo padrão transacional, só que sem as etapas de upsert.

* **`ProviderInteractionRepository`/`IProviderInteractionRepository`**
  hoje só expõem `InsertInteractionAsync` (escrita) e `EnsureIndexesAsync`
  — não há nenhum método de exclusão. `InsertInteractionAsync` já trata
  falha de escrita como não-fatal (log + segue o ciclo de coleta), postura
  que deve ser preservada para a exclusão: falha ao apagar não pode
  derrubar o consumer nem bloquear a projeção.

* O documento `_id` (`ProviderInteractionDocument.Id`, `Guid.CreateVersion7()`)
  já é lido como `interactionId` em `ProcessChangeAsync` a partir de
  `change.FullDocument["_id"]`, disponível em todos os cinco caminhos
  (os quatro early-returns + o caminho completo).

* `change.FullDocument` é populado pelo próprio evento de **insert** do
  Change Stream — para inserts, o driver não precisa reconsultar o
  documento na coleção para preencher `FullDocument` (isso só é
  necessário para updates/deletes com `UpdateLookup`). Logo, o processamento
  em si (leitura de campos do documento) já é imune a uma exclusão que
  tenha ocorrido entre a inserção e a entrega do evento.

## Decisão

### 1. Exclusão inline, best-effort, sempre após o COMMIT da transação Postgres

`ProviderInteractionConsumerWorker.ProcessChangeAsync` passa a chamar um
novo método `IProviderInteractionRepository.DeleteProcessedAsync(Guid
interactionId, CancellationToken)` **imediatamente depois** de cada
chamada a `pgRepository.ProcessInteractionAsync(...)` ou
`pgRepository.AdvanceResumeTokenAsync(...)` retornar com sucesso — nos
cinco caminhos do método (os quatro early-returns e o caminho completo).
Como ambos os métodos do `WorkOrderPgRepository` só retornam
normalmente após `tx.CommitAsync`, a exclusão nunca ocorre antes da
confirmação da transação Postgres — se a transação falhar (exceção
propagada, sem commit), `DeleteProcessedAsync` nunca é chamado e o
documento Mongo permanece intacto para reprocessamento.

`DeleteProcessedAsync` é **best-effort**: falha ao apagar (ex.: timeout
de rede para o Atlas) é logada como aviso e não propagada — a mesma
postura já adotada em `InsertInteractionAsync` para falha de escrita.
Não bloquear o consumer é mais importante do que garantir a exclusão
imediata de um documento específico; a exclusão definitiva é garantida
pelo mecanismo de decisão 2.

`DeleteOne` por `_id` é naturalmente idempotente: se o documento já não
existir (excluído por uma execução anterior, ou pelo job de limpeza da
decisão 2), o driver retorna `DeletedCount = 0` sem lançar exceção. Não
há necessidade de tratamento especial de "documento não encontrado" —
apenas não assumir/validar `DeletedCount == 1`.

### 2. Job de limpeza por marca d'água (`ProviderInteractionCleanupJob`) como mecanismo autoritativo, desacoplado do Change Stream

A exclusão inline (decisão 1) reduz a maior parte do volume quase em
tempo real, mas **não é a garantia de correção** — se o processo do
Worker cair entre o `COMMIT` da transação Postgres e a chamada a
`DeleteProcessedAsync`, o documento já processado fica órfão no Mongo. O
Change Stream **não** reentrega esse evento no restart (o resume token já
foi avançado além dele na mesma transação que fez o commit), então a
exclusão inline nunca terá uma segunda chance para esse documento
específico.

Para cobrir esse caso, introduz-se uma marca d'água persistida em
`consumer_states`: `LastProcessedInteractionCreatedAt` (timestamp,
nullable), atualizada **na mesma transação e no mesmo método** que já
grava o resume token (`UpsertConsumerStateAsync`, chamado a partir de
`ProcessInteractionAsync` e `AdvanceResumeTokenAsync`). O valor gravado é
o `created_at` do documento Mongo sendo processado (já disponível em
`ProcessChangeAsync` via `doc["created_at"]`), não um timestamp gerado
pelo consumer.

Um novo `BackgroundService`, `ProviderInteractionCleanupJob` — mesmo
esqueleto de `ClaimTimeoutJob`/`RecurrentCollectionSchedulerJob` (ADR-029):
acorda em um intervalo fixo (proposto: 15 minutos, sem urgência de
negócio), lê `LastProcessedInteractionCreatedAt` para o
`consumerId = "provider-interaction-to-work-order"`, e executa
`DeleteMany({ CreatedAt: { $lte: watermark } })` em
`provider_interactions` via um novo método
`IProviderInteractionRepository.DeleteProcessedUpToAsync(DateTimeOffset
watermark, CancellationToken)`.

Esse mecanismo é **correto independentemente do sucesso da exclusão
inline**: qualquer documento com `created_at <= watermark` já foi
confirmado como processado no Postgres (a marca d'água só avança depois
do commit), então é sempre seguro apagá-lo — apagado ou não pela
exclusão inline. O job é idempotente (`DeleteMany` sobre um conjunto
que pode já estar parcialmente vazio não é erro) e não depende de
nenhum estado em memória — sobrevive a qualquer restart do processo.

**Por que `created_at` (timestamp) e não o `_id` (`Guid.CreateVersion7()`)
como base da consulta por intervalo:** embora UUIDv7 seja
cronologicamente ordenável por especificação, a ordenação binária de
`Guid` armazenada pelo driver do MongoDB não necessariamente preserva essa
ordem (representação de bytes do .NET `Guid` não é big-endian direta,
diferente do layout RFC da UUIDv7) — usar `_id` numa consulta de
intervalo (`$lte`) arriscaria comparar bytes fora de ordem e produzir
resultados incorretos. Comparação por igualdade (usada na exclusão
inline via `_id`) não sofre esse problema — só comparações de ordem
(`<=`, `>=`) o fazem. `created_at` é um campo `DateTimeOffset`/BSON date
sem ambiguidade de ordenação, e já existe no schema de RF-022
(RF-022.5) como o timestamp real de inserção pelo Worker — não introduz
campo novo no documento, apenas um novo uso dele.

### 3. Replay de resume token (`ChangeStreamHistoryLost`) permanece seguro

O tratamento existente de `ChangeStreamHistoryLost` (`IsResumeTokenExpired`
em `ProviderInteractionConsumerWorker`) já limpa o token e reabre o stream
a partir do ponto corrente — não há replay de documentos antigos nesse
caminho, então não há risco de o consumer tentar reprocessar um documento
já apagado. Nos demais caminhos de resume (reconexão normal do driver a
partir de um token persistido), o token só avança após commit, então o
Change Stream nunca reentrega um evento de inserção já confirmado —
mas, mesmo que uma reentrega ocorresse por alguma falha do driver fora do
controle desta ADR, o processamento em si não depende de reconsultar o
documento no Mongo (`change.FullDocument` já traz o conteúdo do insert),
e a exclusão subsequente seria apenas um no-op idempotente. Nenhuma
mudança de comportamento é necessária no tratamento de
`ChangeStreamHistoryLost` existente.

### 4. `provider_sessions` não é afetada

Esta decisão é restrita à coleção `provider_interactions` e ao consumer
que a observa. `provider_sessions` (RF-023, ADR-028 item 3) é uma coleção
mutável por chave natural `(tenant_id, provider_type)`, não observada por
Change Stream, sem relação com o pipeline de projeção — fora de escopo,
sem alteração.

### 5. Impacto em RF-022

RF-022 (schema e regras de gravação de `provider_interactions`) não é
alterado no que descreve — a coleção continua sendo escrita exatamente
como especificado. O que muda é o **ciclo de vida pós-gravação**: o
documento deixa de ser retenção permanente e passa a ser efêmero
(apagado após projeção confirmada). RF-022 não documenta hoje nenhuma
política de retenção — precisa de uma nota de escopo apontando para esta
ADR (ex.: em "Contexto e motivação" ou uma nova seção "Retenção"),
esclarecendo que a auditoria bruta via `provider_interactions` é
temporária (apenas durante a janela entre inserção e projeção bem-sucedida
+ o intervalo de varredura do job de limpeza), não permanente. A edição
do arquivo `docs/requirements/RF-022-registro-bruto-de-interacao-com-provedor.md`
em si fica a cargo do agente `documentation`/`backend-engineer` na
implementação desta ADR.

### 6. Emenda parcial ao ADR-028

Esta ADR **emenda ADR-028 apenas quanto à retenção de
`provider_interactions`**: ADR-028 (e RF-022) assumiam implicitamente
retenção indefinida como consequência natural de "unidade auditável"
(sem nunca declarar isso como requisito formal de retenção). Esta ADR
torna explícito que a retenção passa a ser efêmera por decisão de custo.
Todo o restante de ADR-028 permanece intacto e vigente: schema de
`provider_interactions`, existência e desenho de `provider_sessions`,
migração da estratégia de busca por status para busca por data, remoção
de `work_order_snapshots`, e o modelo de pipeline de projeção via Change
Stream em si (que continua existindo — apenas seguido, agora, de
exclusão).

## Alternativas consideradas

* **TTL index no MongoDB (`expireAfterSeconds`) sobre `created_at`.**
  Rejeitada como mecanismo principal: um TTL index expira por tempo
  decorrido, não por confirmação de processamento — um documento poderia
  ser apagado pelo TTL antes de o consumer processá-lo (ex.: consumer
  parado por manutenção, ou atrasado por reconexão de Change Stream),
  causando perda silenciosa de dado bruto sem projeção correspondente no
  Postgres. Isso viola diretamente a garantia pedida ("nunca apagar antes
  da confirmação da transação Postgres"). Poderia ser cogitado como
  camada adicional de segurança com um TTL muito longo (ex.: dias), mas
  não substitui a exclusão orientada a processamento — não adotado nesta
  fase por não haver necessidade adicional identificada além do job de
  limpeza da decisão 2, que já é orientado a processamento confirmado.

* **Exclusão dentro da mesma transação/atômica com o commit Postgres
  (ex.: registrar a exclusão como parte de um "outbox" transacional).**
  Rejeitada: MongoDB e PostgreSQL são bancos distintos sem transação
  distribuída disponível no ambiente atual — não é possível fazer commit
  atômico entre os dois. Qualquer desenho que tentasse simular isso
  (2PC manual, outbox pattern completo) introduziria complexidade
  desproporcional ao problema (redução de custo de armazenamento), quando
  a combinação "exclusão inline best-effort + job de limpeza por marca
  d'água" já entrega a garantia funcional necessária (nenhum dado
  apagado antes do commit; nenhum dado órfão permanece indefinidamente)
  sem transação distribuída.

* **Apagar apenas via o job de limpeza periódico, sem exclusão inline.**
  Considerada mais simples (um único mecanismo). Rejeitada como única
  solução porque adiaria a redução de armazenamento por até o intervalo
  do job (proposto 15 minutos) para todo documento, mesmo no caminho
  feliz — desperdiçando a oportunidade de reduzir custo quase em tempo
  real no caso comum (processo não cai). A exclusão inline é barata
  (uma chamada a mais por documento, best-effort) e cobre a maioria dos
  casos; o job de limpeza cobre apenas o resíduo dos casos de falha entre
  commit e exclusão inline.

* **Apagar dentro do próprio `WorkOrderPgRepository`, acoplando a exclusão
  Mongo ao código que fala com o Postgres.** Rejeitada: misturaria
  responsabilidade de acesso a dois bancos diferentes na mesma classe,
  quebrando a separação já estabelecida (`WorkOrderPgRepository` só fala
  Postgres; `IProviderInteractionRepository` só fala Mongo). A exclusão
  inline permanece no `ProviderInteractionConsumerWorker`, que já orquestra
  as duas dependências.

## Consequências

### Impactos de implementação (para backend-engineer)

* **`apps/collector/Atua.Collector/Persistence/IProviderInteractionRepository.cs`**:
  adicionar
  `Task DeleteProcessedAsync(Guid interactionId, CancellationToken cancellationToken = default)`
  e
  `Task<long> DeleteProcessedUpToAsync(DateTimeOffset watermark, CancellationToken cancellationToken = default)`.
* **`apps/collector/Atua.Collector/Persistence/ProviderInteractionRepository.cs`**:
  implementar os dois métodos acima.
  `DeleteProcessedAsync` faz `DeleteOneAsync` por `_id`, captura exceção e
  loga como `LogWarning` (mesma postura de `InsertInteractionAsync`, nunca
  propaga). `DeleteProcessedUpToAsync` faz
  `DeleteManyAsync(Builders<...>.Filter.Lte(d => d.CreatedAt, watermark))`,
  retorna `DeletedCount`, e **propaga** exceção (o chamador é o job de
  limpeza, que já trata exceção por execução sem derrubar o processo — ver
  abaixo).
* **`apps/collector/Atua.Collector/Consumer/ProviderInteractionConsumerWorker.cs`**:
  injetar `IProviderInteractionRepository providerInteractionRepository`
  no construtor; em `ProcessChangeAsync`, extrair `createdAt` de
  `doc["created_at"]` (`BsonDateTime` → `DateTimeOffset`); chamar
  `await providerInteractionRepository.DeleteProcessedAsync(interactionId, stoppingToken)`
  logo após cada um dos cinco retornos de sucesso de
  `pgRepository.ProcessInteractionAsync`/`AdvanceResumeTokenAsync`; passar
  `createdAt` para esses dois métodos do `WorkOrderPgRepository` (ver
  próximo item).
* **`apps/collector/Atua.Collector/Consumer/WorkOrderPgRepository.cs`**:
  * `ProcessInteractionAsync` e `AdvanceResumeTokenAsync` passam a receber
    um parâmetro adicional `DateTimeOffset interactionCreatedAt`.
  * `UpsertConsumerStateAsync` passa a gravar também
    `"LastProcessedInteractionCreatedAt" = @interactionCreatedAt` no mesmo
    `INSERT ... ON CONFLICT DO UPDATE`, na mesma transação do resume token.
  * Novo método `Task<DateTimeOffset?> GetLastProcessedInteractionCreatedAtAsync(string consumerId, CancellationToken cancellationToken = default)`,
    análogo a `GetResumeTokenAsync`, usado pelo job de limpeza.
* **`apps/api/Atua.Api/Domain/WorkOrders/ConsumerState.cs`**: adicionar
  propriedade `DateTimeOffset? LastProcessedInteractionCreatedAt` e um
  método de atualização (seguindo o padrão já usado pelo `ResumeToken`
  nesta entidade).
* **`apps/api/Atua.Api/Infrastructure/Persistence/AtuaDbContext.cs`**:
  em `ConfigureConsumerState`, mapear a nova coluna
  (`"LastProcessedInteractionCreatedAt"`, nullable).
* **Nova migration EF Core** em
  `apps/api/Atua.Api/Infrastructure/Persistence/Migrations/` adicionando a
  coluna `LastProcessedInteractionCreatedAt` (timestamptz, nullable) em
  `consumer_states` — gerada via `dotnet ef migrations add`, seguindo o
  padrão das migrations existentes (ex.:
  `20260917210447_AddRecurrentCollectionIntervalToIntegration`).
* **Novo arquivo**: `apps/collector/Atua.Collector/Consumer/ProviderInteractionCleanupJob.cs`
  — `BackgroundService`, mesmo esqueleto de `ClaimTimeoutJob`/
  `RecurrentCollectionSchedulerJob` (loop com `Task.Delay` de 15 minutos,
  `try/catch` que loga e segue sem derrubar o processo). A cada execução:
  abre escopo de DI, resolve `WorkOrderPgRepository` e
  `IProviderInteractionRepository`, chama
  `GetLastProcessedInteractionCreatedAtAsync("provider-interaction-to-work-order")`;
  se houver valor, chama `DeleteProcessedUpToAsync(watermark)` e loga
  quantos documentos foram removidos (mesmo padrão de observabilidade de
  `ClaimTimeoutJob`/`RecurrentCollectionSchedulerJob`).
* **Registro em `Program.cs` do Collector**: uma linha adicional,
  `builder.Services.AddHostedService<ProviderInteractionCleanupJob>();`,
  ao lado do registro de `ProviderInteractionConsumerWorker`.
* **`docs/requirements/RF-022-registro-bruto-de-interacao-com-provedor.md`**:
  precisa de uma nota de escopo/retenção apontando para esta ADR (ver
  decisão 5) — edição a cargo de `documentation`/`backend-engineer`, não
  incluída nesta ADR.

### Riscos e mitigação

* **Documento órfão entre commit Postgres e exclusão inline, se o processo
  cair nesse intervalo**: aceito como esperado e transitório — coberto
  pelo `ProviderInteractionCleanupJob` (decisão 2), que não depende do
  Change Stream para localizar e remover esses documentos. Janela máxima
  de resíduo: o intervalo do job de limpeza (proposto 15 minutos), não o
  tempo de vida do dado.
* **Crescimento momentâneo de `provider_interactions` se o
  `ProviderInteractionCleanupJob` ficar parado por um período (ex.: deploy,
  falha do processo do Collector)**: aceitável — o job é stateless e
  autossuficiente (lê a marca d'água do Postgres a cada execução), então
  retoma normalmente assim que o processo voltar, sem necessidade de
  reprocessar nada.
* **Falha isolada de uma execução do job de limpeza (ex.: timeout de rede
  com o Atlas)**: não deve derrubar o processo do Collector — mesmo
  padrão de tratamento de exceção de `ClaimTimeoutJob`/
  `RecurrentCollectionSchedulerJob` (log de erro, aguarda o próximo
  ciclo).
* **Concorrência entre exclusão inline e job de limpeza tentando apagar o
  mesmo documento**: sem risco de erro — `DeleteOneAsync`/`DeleteManyAsync`
  sobre um documento já removido apenas retornam contagem zero, não
  lançam exceção.
* **Perda de capacidade de auditoria bruta (motivação original de
  ADR-028/RF-022)**: risco de produto já assumido e aceito explicitamente
  pelo usuário — não é um risco técnico desta ADR, apenas registrado aqui
  para rastreabilidade da decisão.

### Compatibilidade

Não altera nenhum contrato público (Office, API, Manager/Tecnica) nem o
schema de gravação de `provider_interactions` (RF-022). É aditivo ao
pipeline existente: dois métodos novos de exclusão no repositório Mongo,
uma coluna nova em `consumer_states` (nullable, sem impacto em leituras
existentes de `ResumeToken`), e um novo `BackgroundService` no processo
do Collector, seguindo exatamente o padrão de `ClaimTimeoutJob`/
`RecurrentCollectionSchedulerJob` já em produção na API (ADR-029).

## Agentes envolvidos

* Usuário: identificação do custo elevado de armazenamento no MongoDB
  Atlas, decisão de produto de exclusão imediata pós-processamento sem
  janela de retenção, aceite explícito da perda de auditoria bruta.
* software-architect: mecanismo de exclusão (inline + job de limpeza por
  marca d'água), análise do código atual e justificativa desta ADR.
* backend-engineer: implementação dos itens listados em "Impactos de
  implementação", incluindo a migration EF Core e a nota de escopo em
  RF-022.
* qa-engineer: validação de que (a) nenhum documento é apagado antes do
  commit Postgres correspondente; (b) os quatro early-returns do consumer
  também resultam em exclusão; (c) o job de limpeza remove resíduos
  simulando uma falha entre commit e exclusão inline; (d) replay de
  resume token não falha sobre documento já apagado; (e) `provider_sessions`
  permanece intocada.

## Data

2026-09-21

## Altera

ADR-028 (emenda parcial — apenas quanto à retenção de
`provider_interactions`; schema, `provider_sessions`, estratégia de busca
por data e demais decisões de ADR-028 permanecem intactas).

## Substitui

Não aplicável.
