# Arquitetura: SubStatus, Prazos de Atendimento e Situação Crítica (RF-028 a RF-033)

Status: `Proposto` (aguardando implementação pelo `backend-engineer`/`frontend-engineer`)

Referências: RF-017 (`WorkOrder`/`WorkOrderHistory`), RF-026/ADR-031
(enriquecimento de detalhe), RF-027 (aging), ADR-032 (duplicação de
constantes de domínio entre Collector/API), ADR-033 (lista explícita "Em
Aberto"), `docs/architecture/dashboard-work-order-queries.md` (padrão de
contratos de endpoint já em produção).

Este documento define a arquitetura para RF-028 a RF-033. RF-034 está
fora do planejamento atual e não é tratado aqui. Todas as "Pendências
para validação" bloqueantes dos RFs referenciados já foram resolvidas
(ver seção "Decisões" de cada RF); os itens ainda marcados como "não
bloqueante" (ex.: classificação final de 4 status raw em RF-029, janela
de risco de RF-033) são parâmetros de negócio de baixo custo de mudança,
não afetam esta arquitetura.

---

## 1. Visão geral das mudanças

| RF | Mudança de domínio | Camada principal |
|---|---|---|
| RF-028 | Novo campo `SymptomRaw`; `Symptom` só de `symptomDescription` | Collector (adapter) + API (leitura) |
| RF-029 | Nova lista `WorkOrderOpenStatuses`; substitui exclusão-por-terminal em RF-027 | Collector + API (Domain) |
| RF-030 | Novo campo `NextAppointmentAt`; 3 indicadores de prazo | Collector (captura) + API (cálculo) |
| RF-031 | Novo campo `SubStatus`; catálogo substatus→responsável; gatilho de histórico estendido | Collector (captura + gatilho) + API (catálogo/leitura) |
| RF-032 | Indicador de OS por substatus/responsável | API (novo endpoint) + `apps/office` (novo card) |
| RF-033 | Indicadores de Situação Crítica e Risco | API (novo endpoint) + `apps/office` (novos cards) |

Nenhuma dessas mudanças introduz um novo componente arquitetural (serviço,
fila, banco). Todas seguem o padrão já estabelecido: colunas adicionais em
`work_orders`/`work_order_histories` (EF Core migration na `Atua.Api`,
consumidas via SQL cru pelo Collector — mesmo modelo de hoje), novos
`QueryHandler`s de leitura na API, novos cards no `apps/office`.

---

## 2. RF-028 - `SymptomRaw` e correção de `Symptom`

### Domínio

* `WorkOrder.SymptomRaw` (novo, `string?`) e `WorkOrderHistory.SymptomRaw`
  (novo, `string?`) — mesmo padrão de `Symptom` hoje (nullable, sem
  enum, texto livre do provedor).
* Migration EF Core (`Atua.Api`): `AddColumn<string>("SymptomRaw",
  table: "work_orders", type: "text", nullable: true)` e o mesmo para
  `work_order_histories` (duas tabelas, seguir o padrão de
  `20260916000144_AddWorkOrderProviderFields.cs`, que já adiciona a
  mesma coluna nas duas tabelas em uma única migration).

### Collector

* `ProviderWorkOrderData` (record) ganha o campo `string? SymptomRaw`.
* `IServiceProviderInteractionOrderAdapter`:
  * Remover `SymptomCandidateFields` e o método `ExtractSymptom` (o
    fallback deixa de existir — RF-028.2/RF-028.3).
  * `Symptom` passa a ser `ExtractString(order, "symptomDescription")`
    diretamente em `BuildOrderData`.
  * `SymptomRaw` passa a ser `ExtractString(order, "symptom")`.
  * `pendingReason` não é mais lido aqui — vira `SubStatus` (RF-031).
* `WorkOrderPgRepository`: adicionar `"SymptomRaw"` a `DetailColumns`
  (array compartilhado entre UPDATE/INSERT de `work_orders` e INSERT de
  `work_order_histories` — nenhuma outra mudança de método é necessária,
  a coluna se propaga automaticamente pelos três lugares que já
  consomem `DetailColumns`/`AddDetailParameters`). **Não** adicionar a
  `ContactDetailColumns` (não é dado de contato mascarável).

### API (leitura)

* `WorkOrder.cs`/`WorkOrderHistory.cs`: adicionar propriedade
  `SymptomRaw` e mapear em `Update(WorkOrderDetails details)` /
  construtor, igual a `Symptom`.
* `IWorkOrderDetailQuery`/`WorkOrderDetailQueryHandler`: incluir
  `SymptomRaw` no contrato de leitura (disponível para consumidores
  futuros da API, mesmo sem tela associada — RF-028 só exclui *UI*, não
  a leitura via API). Não expor em `WorkOrderEndpoints`/lista (Endpoint
  2) — mantém escopo mínimo (só o detalhe, não a listagem).

Sem reprocessamento retroativo (RF-028, decisão já registrada no RF).

---

## 3. RF-029 - `WorkOrderOpenStatuses`

**Decisão arquitetural (ponto 1 da tarefa):** lista central como
**classe estática de código** (`HashSet<string>`), duplicada entre
Collector e API, seguindo *exatamente* o mesmo padrão e justificativa já
usados para `WorkOrderTerminalStatuses` (ADR-032) — não uma tabela de
configuração no banco, e não um enum `E*` (os valores são strings livres
do provedor, DP-017.2, não um domínio fechado do ATUA).

Motivo de não usar tabela de configuração: o custo de uma tabela
(migration, seed, CRUD/admin, cache) é desproporcional para 5 valores que
mudam raramente e cuja alteração já exige deploy de código de qualquer
forma (a lógica de classificação de status não é dado de usuário
final). Motivo de não usar enum: os valores são a string exata devolvida
pelo provedor (`"pending"`, `"Exchange Proposal"`, etc.), não um conceito
fechado controlado pelo ATUA — igual ao motivo já documentado em
`WorkOrderTerminalStatuses`.

### Novos arquivos

* `apps/collector/Atua.Collector/Consumer/WorkOrderOpenStatuses.cs`
* `apps/api/Atua.Api/Domain/WorkOrders/WorkOrderOpenStatuses.cs`

Ambos com o mesmo conteúdo:

```csharp
public static class WorkOrderOpenStatuses
{
    public static IReadOnlySet<string> Values { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "pending", "assigned", "accepted", "Exchange Proposal", "Request to Explain",
    };

    public static bool IsOpen(string status) => Values.Contains(status);
}
```

Comentário XML de cada arquivo deve referenciar RF-029 e ADR-033 (mesmo
padrão de `WorkOrderTerminalStatuses`, que referencia ADR-032).

### Integração com o agrupamento existente (ponto 1, parte 2)

Ver ADR-033 para a decisão completa. Resumo: `WorkOrderAgingSummaryQueryHandler`
e `WorkOrderListByStatusQueryHandler.ApplyAgingFilter` trocam
`!WorkOrderTerminalStatuses.Contains(status)` por
`WorkOrderOpenStatuses.Contains(status)`. `WorkOrderTerminalStatuses`
continua existindo e sendo usada onde "concluída" é o conceito correto
(Collector: `NeedsDetailFetch`; API:
`WorkOrderMonthlyTrendQueryHandler`).

### Status não classificado (RF-029.3)

Responsabilidade do **Collector**, no momento da ingestão (não da API a
cada leitura — evita log repetido a cada carregamento do Dashboard).

Em `WorkOrderPgRepository.UpsertWorkOrderAsync`, após resolver
`newStatus`, adicionar uma checagem (antes ou depois do UPSERT, sem
afetar a transação):

```csharp
if (!WorkOrderOpenStatuses.IsOpen(newStatus) && !WorkOrderTerminalStatuses.IsTerminal(newStatus))
{
    logger.LogWarning(
        "[CONSUMER] woStatus não classificado (nem 'Em Aberto' nem terminal): {Status} WorkOrderProviderId={ProviderId}",
        newStatus, workOrderProviderId);
}
```

Mesmo padrão de severidade/formato já usado em `IServiceCollectorService`
para `woStatus` fora de `StatusConfigs`. Não é necessário armazenar o
alerta em uma tabela — log estruturado é suficiente para operação
consultar (mesmo padrão do resto do sistema).

---

## 4. RF-030 - `NextAppointmentAt` e indicadores de prazo

### Domínio

* `WorkOrder.NextAppointmentAt` (novo, `DateTimeOffset?`) e
  `WorkOrderHistory.NextAppointmentAt` (novo, `DateTimeOffset?`) —
  mesmo tratamento de `ProviderCreatedAt`/`ProviderUpdatedAt`.
* Migration EF Core: `AddColumn<DateTimeOffset>("NextAppointmentAt", ...,
  nullable: true)` nas duas tabelas, mesma migration de RF-028/RF-031
  (uma única migration pode agrupar as três colunas novas —
  `SymptomRaw`, `SubStatus`, `NextAppointmentAt` — já que são aditivas e
  não conflitantes; ver seção 6, "Migrations").

### Collector

* `ProviderWorkOrderData` ganha `DateTimeOffset? NextAppointmentAt`.
* `IServiceProviderInteractionOrderAdapter.BuildOrderData`:
  `NextAppointmentAt: ExtractDate(order, "visitStartTime")` — mesmo
  método `ExtractDate` já usado para `creationDate`/`lastUpdateDate`
  (formato `"yyyy-MM-dd HH:mm:ss"`, `ProviderDateFormat`).
* `WorkOrderPgRepository`: adicionar `"NextAppointmentAt"` a
  `DetailColumns` (**não** a `ContactDetailColumns`). Isso já produz a
  semântica pedida em RF-030 ("sobrescrita simples, sem COALESCE de
  preservação exclusiva de contato"): o `SET` gerado é
  `"NextAppointmentAt" = COALESCE(@d, "NextAppointmentAt")`, ou seja,
  todo `provider_interactions` que trouxer um `visitStartTime` não nulo
  sobrescreve o valor anterior; se o provedor não enviar o campo naquela
  interação, o valor antigo é preservado (mesmo comportamento já usado
  para `ProviderCreatedAt`/`ProviderUpdatedAt` — não é um tratamento
  novo, é reaproveitar o existente).

### API - cálculo dos 3 indicadores (RF-030.3/RF-030.4)

**Decisão (ponto 2 da tarefa):** cálculo em **query handler** (não em
view de banco, nem campo computado persistido). Justificativa: o
critério depende do fuso do tenant (`Tenant.TimeZoneId`) e de "hoje"
(instante de execução), portanto não pode ser um valor persistido
(mudaria sem nenhuma escrita); e o volume esperado (OS abertas por
tenant) é pequeno o suficiente para materializar em memória, seguindo o
precedente já aceito em `WorkOrderMonthlyTrendQueryHandler` (que também
traz timestamps para memória porque `TimeZoneInfo.ConvertTime` não é
traduzível para SQL pelo EF Core).

Novo endpoint:

```
GET /api/tenants/{tenantId:guid}/work-orders/next-appointment-summary
```

Contrato:

```
Application/WorkOrders/Contracts/IWorkOrderNextAppointmentSummaryQuery.cs
```

```csharp
public interface IWorkOrderNextAppointmentSummaryQuery
{
    Task<WorkOrderNextAppointmentSummaryResult> ExecuteAsync(
        Guid tenantId, string timeZoneId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record WorkOrderNextAppointmentSummaryResult(int Overdue, int DueToday, int DueTomorrow);
```

Implementação (`WorkOrderNextAppointmentSummaryQueryHandler`):

1. `SELECT NextAppointmentAt` de `WorkOrder` onde `TenantId = tenantId`,
   `WorkOrderOpenStatuses.Contains(Status)` (RF-030.4) e
   `NextAppointmentAt IS NOT NULL` — `AsNoTracking().Select(...)
   .ToListAsync`.
2. Em memória: converter `NextAppointmentAt` para `DateOnly` no fuso do
   tenant (`TimeZoneInfo.ConvertTime` + `DateOnly.FromDateTime`), e
   `hoje` = `DateOnly` de `now` convertido para o mesmo fuso.
3. Classificar cada `DateOnly`: `< hoje` → `Overdue`; `== hoje` →
   `DueToday`; `== hoje.AddDays(1)` → `DueTomorrow`; qualquer outro
   valor não conta em nenhum dos 3 (RF-030, critério de aceite 4 é sobre
   `null`, mas o mesmo raciocínio vale para datas fora da janela — elas
   simplesmente não incrementam nenhum contador).

Response:

```json
{ "overdue": 12, "dueToday": 3, "dueTomorrow": 5 }
```

### Extensão do Endpoint 2 (lista) para os 3 indicadores (RF-030.5)

Cada card do Dashboard precisa navegar para uma lista filtrada. Estender
`GET /api/tenants/{tenantId}/work-orders` com um terceiro filtro,
mutuamente exclusivo com `status` e `minAgeDays`:

```
GET .../work-orders?nextAppointmentFilter={overdue|due-today|due-tomorrow}&page=&pageSize=
```

Novo enum (conceito do ATUA, não valor cru do provedor — por isso `E*`):

```csharp
// Domain/WorkOrders/EWorkOrderNextAppointmentFilter.cs
public enum EWorkOrderNextAppointmentFilter { Overdue, DueToday, DueTomorrow }
```

`WorkOrderListByStatusQueryHandler` ganha um parâmetro
`EWorkOrderNextAppointmentFilter? nextAppointmentFilter` e um terceiro
ramo de filtro análogo a `ApplyAgingFilter`, reaproveitando a mesma
lógica de conversão de fuso da seção anterior (aplicada sobre a query
paginada, não sobre todos os registros do tenant). A validação de
"exatamente um filtro informado" (hoje `status`/`minAgeDays`) deve ser
generalizada para os três (contar quantos dos três parâmetros não são
nulos; != 1 → `400 invalid_filter`) — mudança pequena na validação já
existente no endpoint, não uma reestruturação.

---

## 5. RF-031 - `SubStatus` e catálogo de responsáveis

### Domínio

* `WorkOrder.SubStatus` (novo, `string?`) e `WorkOrderHistory.SubStatus`
  (novo, `string?`) — texto livre do provedor (`pendingReason`), mesmo
  tratamento de `Symptom`/`Status`: sem enum, porque é valor emitido
  pelo provedor, não um catálogo fechado controlado pelo ATUA.
* Migration: coluna nova nas duas tabelas (agrupável com RF-028/RF-030,
  ver seção 6).

### Collector (captura)

* `ProviderWorkOrderData` ganha `string? SubStatus`.
* `IServiceProviderInteractionOrderAdapter.BuildOrderData`:
  `SubStatus: ExtractString(order, "pendingReason")`.
* `WorkOrderPgRepository`: adicionar `"SubStatus"` a `DetailColumns`
  (mesmo mecanismo de RF-028/RF-030 — nenhum método novo necessário para
  persistência básica).

### Catálogo substatus → responsável (ponto 3 da tarefa)

**Decisão:** dicionário estático em código (`Dictionary<string,
EWorkOrderSubStatusResponsible>`), **apenas na API**, não no Collector e
não em tabela de banco.

Justificativa:

* O Collector **não precisa** resolver o responsável — ele só persiste
  `pendingReason` como está (RF-031.1). Resolver o responsável é
  necessidade exclusiva de leitura/indicador (RF-032), então o catálogo
  só precisa existir onde é consumido — evita duplicação desnecessária
  entre Collector e API (ao contrário de `WorkOrderOpenStatuses`, que
  *precisa* existir nos dois processos).
  * Único ponto de atenção: RF-031.4 (extensão do gatilho de histórico)
    só compara os valores brutos de `SubStatus` (mudou ou não mudou),
    não precisa saber o responsável — não quebra esta decisão.
* Não é tabela de banco: os 36 valores são um catálogo curado
  manualmente pela cliente, atualizado com pouca frequência, e sua
  alteração já implica uma decisão de produto/negócio (não é dado que um
  usuário final edita em runtime). Uma tabela de lookup adicionaria
  migration + seed + necessidade de tela de administração para um
  ganho nulo neste momento (princípio de evolução: não antecipar).
* **É um dicionário, não hardcoded em múltiplos lugares** (atende
  RF-031.2 literalmente): um único arquivo, uma única fonte de verdade,
  consultado por todo indicador que precisar do responsável.

O **responsável** é um conceito fechado e controlado pelo ATUA (7
valores fixos + "não mapeado") — diferente do `SubStatus` cru, este é
modelado como enum `E*`:

```csharp
// Domain/WorkOrders/EWorkOrderSubStatusResponsible.cs
public enum EWorkOrderSubStatusResponsible
{
    PostoAutorizado,
    MideaCallcenter,
    MideaPlanejamento,
    MideaCorreios,
    MideaJira,
    MideaJuridico,
    MideaSuporteTecnico,
    NaoMapeado,
}
```

Catálogo:

```csharp
// Domain/WorkOrders/WorkOrderSubStatusCatalog.cs
public static class WorkOrderSubStatusCatalog
{
    private static readonly IReadOnlyDictionary<string, EWorkOrderSubStatusResponsible> Responsibles =
        new Dictionary<string, EWorkOrderSubStatusResponsible>(StringComparer.Ordinal)
        {
            ["Acordo de ressarcimento - pendente de coleta"] = EWorkOrderSubStatusResponsible.PostoAutorizado,
            // ... os 36 valores de RF-031.2 (tabela do RF é a fonte de verdade a copiar)
        };

    public static EWorkOrderSubStatusResponsible Resolve(string? subStatus) =>
        subStatus is not null && Responsibles.TryGetValue(subStatus, out var responsible)
            ? responsible
            : EWorkOrderSubStatusResponsible.NaoMapeado;
}
```

Substatus não mapeado (RF-031.3): `Resolve` já devolve `NaoMapeado` sem
descartar o valor bruto (`WorkOrder.SubStatus` continua com o texto
original). O log de acompanhamento para revisão de catálogo é
responsabilidade do **Collector**, no mesmo ponto de ingestão do RF-029
(evita repetir o alerta a cada leitura do Dashboard):

```csharp
// dentro de UpsertWorkOrderAsync, após resolver order.SubStatus
if (order.SubStatus is not null && !WorkOrderSubStatusCatalog.IsKnown(order.SubStatus))
{
    logger.LogWarning(
        "[CONSUMER] SubStatus não mapeado no catálogo (RF-031.3): {SubStatus} WorkOrderProviderId={ProviderId}",
        order.SubStatus, workOrderProviderId);
}
```

Isso exige uma cópia mínima e somente-leitura do **conjunto de chaves**
do catálogo no Collector (não o mapeamento completo para responsável,
que só a API usa) — proposta: `WorkOrderSubStatusCatalog.IsKnown(string)`
como método estático simples no próprio Collector, com a mesma lista de
36 chaves (sem os responsáveis, que não são necessários ali). Isso é uma
duplicação parcial e deliberada, no mesmo espírito de ADR-032 — se este
conjunto de chaves precisar ser idêntico em três ou mais lugares, ou
divergir, reavaliar a extração para um projeto compartilhado (mesmo
critério objetivo do ADR-032).

### RF-031.4 - Extensão do gatilho de `work_order_histories`

Em `WorkOrderPgRepository`:

1. `UpsertWorkOrderAsync`: o `SELECT` que lê o estado atual (`"Id",
   "Status", "NeedsDetailFetch", "DetailsFetchedAt"`) passa a também
   selecionar `"SubStatus"` e `"NextAppointmentAt"`, e o método passa a
   retornar também `PreviousSubStatus` e `PreviousNextAppointmentAt`
   (tupla ampliada: `(Guid WorkOrderId, string? PreviousStatus, string?
   PreviousSubStatus, DateTimeOffset? PreviousNextAppointmentAt, bool
   IsNew)`).
2. `ProcessInteractionAsync`, condição de disparo do histórico, hoje:

   ```csharp
   if (isNew || !string.Equals(previousStatus, order.Status, StringComparison.Ordinal))
   ```

   passa a ser:

   ```csharp
   var subStatusChanged = !string.Equals(previousSubStatus, order.SubStatus, StringComparison.Ordinal);
   var nextAppointmentChanged = previousNextAppointmentAt != order.NextAppointmentAt;

   if (isNew || statusChanged || subStatusChanged || nextAppointmentChanged)
   {
       await InsertWorkOrderHistoryAsync(...);
   }
   ```

   (`statusChanged` já existia como variável local dentro de
   `UpsertWorkOrderAsync`; propagar esse booleano para fora — junto com
   os outros dois — em vez de recalcular `previousStatus != order.Status`
   no chamador, para não duplicar a comparação.)

Nenhuma mudança estrutural em `InsertWorkOrderHistoryAsync`: `SubStatus`
e `NextAppointmentAt` já chegam por `DetailColumns`/`AddDetailParameters`
(seção 2/4), então o `INSERT` em `work_order_histories` já grava os
valores atuais automaticamente — só o **gatilho** (condição de disparo)
precisa mudar.

---

## 6. Migrations (RF-028, RF-030, RF-031)

Uma única migration EF Core pode agrupar as três colunas novas
(`SymptomRaw`, `SubStatus`, `NextAppointmentAt`) em `work_orders` e
`work_order_histories` — são aditivas, sem dependência entre si, e serão
implementadas juntas. Nome sugerido:
`AddSubStatusNextAppointmentAndSymptomRawToWorkOrder`. Seguir o padrão de
`20260916000144_AddWorkOrderProviderFields.cs` (mesma migration adiciona
colunas nas duas tabelas).

Não é necessário reprocessamento retroativo para nenhum dos três campos
— populados na próxima coleta natural de cada OS (mesma decisão já
registrada em RF-028 e coerente com o princípio de coleta incremental,
RF-017).

---

## 7. RF-032 - Indicador por SubStatus (dashboard)

### API

Novo endpoint, mesmo padrão de tenant scoping da seção 5 de
`dashboard-work-order-queries.md`:

```
GET /api/tenants/{tenantId:guid}/work-orders/substatus-summary
```

Contrato:

```
Application/WorkOrders/Contracts/IWorkOrderSubStatusSummaryQuery.cs
```

```csharp
public interface IWorkOrderSubStatusSummaryQuery
{
    Task<WorkOrderSubStatusSummaryResult> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record WorkOrderSubStatusSummaryResult(IReadOnlyList<WorkOrderSubStatusCount> Groups);

public sealed record WorkOrderSubStatusCount(
    string SubStatus,             // "Sem substatus" quando SubStatus é null (RF-032.1)
    EWorkOrderSubStatusResponsible Responsible,
    int Total);
```

Implementação (`WorkOrderSubStatusSummaryQueryHandler`): agrupar
`WorkOrder` do tenant com `WorkOrderOpenStatuses.Contains(Status)`
(RF-032, escopo "apenas Em Aberto") por `SubStatus` (SQL `GROUP BY`,
tratando `NULL` como uma chave própria), depois resolver
`EWorkOrderSubStatusResponsible` via `WorkOrderSubStatusCatalog.Resolve`
em memória sobre os grupos já agregados (poucos grupos distintos, custo
desprezível — não precisa ir para SQL).

### Frontend (`apps/office`)

* Novo card no Dashboard, gráfico de barras com `react-apexcharts`
  (dependência já existente — resolve a pendência de RF-032 sobre
  biblioteca, sem nova dependência).
* Uma cor fixa por `EWorkOrderSubStatusResponsible` (paleta definida
  pelo `frontend-engineer`, respeitando `design-system.md`), legenda
  visível.
* Sem drill-down nesta fase (RF-032, "O que NÃO é").

---

## 8. RF-033 - Situação Crítica e Risco

### API

```
GET /api/tenants/{tenantId:guid}/work-orders/criticality-summary
```

Contrato:

```
Application/WorkOrders/Contracts/IWorkOrderCriticalitySummaryQuery.cs
```

```csharp
public interface IWorkOrderCriticalitySummaryQuery
{
    Task<WorkOrderCriticalitySummaryResult> ExecuteAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record WorkOrderCriticalitySummaryResult(int Critical, int AtRisk);
```

Implementação (`WorkOrderCriticalitySummaryQueryHandler`), mesmo padrão
de `WorkOrderAgingSummaryQueryHandler` (comparação direta em SQL, sem
necessidade de materializar em memória — `UpdatedAt` já é
`DateTimeOffset` UTC, e o critério de RF-033 não depende do fuso do
tenant, ao contrário de RF-030):

```csharp
private const int CriticalThresholdDays = 5;
private const int AtRiskThresholdDays = 4;

var openWorkOrders = dbContext.WorkOrders.AsNoTracking()
    .Where(wo => wo.TenantId == tenantId && WorkOrderOpenStatuses.Values.Contains(wo.Status));

var critical = await openWorkOrders.CountAsync(
    wo => wo.UpdatedAt <= now.AddDays(-CriticalThresholdDays), cancellationToken);

var atRisk = await openWorkOrders.CountAsync(
    wo => wo.UpdatedAt <= now.AddDays(-AtRiskThresholdDays) && wo.UpdatedAt > now.AddDays(-CriticalThresholdDays),
    cancellationToken);
```

As duas constantes (`5`/`4` dias) ficam como `const int` no handler —
RF-033 já registra que a janela de risco é "sugestão de produto... pode
ser ajustada sem impacto arquitetural (constante simples)"; não introduzir
configuração externa (appsettings/tabela) para isso agora.

### Extensão do Endpoint 2 (lista) para os 2 indicadores (RF-033.4)

Mesmo mecanismo da seção 4 (RF-030): novo enum
`EWorkOrderCriticalityFilter { Critical, AtRisk }` e novo parâmetro
`criticalityFilter` em `WorkOrderListByStatusQueryHandler`, participando
da mesma validação "exatamente um filtro" generalizada (status /
minAgeDays / nextAppointmentFilter / criticalityFilter).

### Frontend (`apps/office`)

Dois novos cards (mesmo padrão visual dos cards de aging existentes),
navegando para a lista filtrada correspondente.

---

## 9. Autorização e tenant scoping

Todos os endpoints novos (RF-030 `next-appointment-summary`, RF-032
`substatus-summary`, RF-033 `criticality-summary`, e os novos parâmetros
de filtro do Endpoint 2) seguem, sem exceção, o padrão já descrito na
seção 5 de `dashboard-work-order-queries.md`: `RequireAuthorization
("BrowserSession")`, validação de `TenantMembership` do usuário
autenticado para o `tenantId` do path, filtro explícito por `TenantId` em
toda query.

---

## 10. Resumo de componentes novos

| Componente | Tipo | Caminho |
|---|---|---|
| `WorkOrderOpenStatuses` | Constante de domínio (dup. Collector/API) | `Consumer/` (Collector) e `Domain/WorkOrders/` (API) |
| `EWorkOrderSubStatusResponsible` | Enum de domínio | `apps/api/Atua.Api/Domain/WorkOrders/` |
| `WorkOrderSubStatusCatalog` | Catálogo estático (Dictionary) | `apps/api/Atua.Api/Domain/WorkOrders/` (API) + versão só-chaves no Collector |
| `EWorkOrderNextAppointmentFilter` | Enum de domínio | `apps/api/Atua.Api/Domain/WorkOrders/` |
| `EWorkOrderCriticalityFilter` | Enum de domínio | `apps/api/Atua.Api/Domain/WorkOrders/` |
| `IWorkOrderNextAppointmentSummaryQuery` + Handler | Contrato + implementação | `Application/WorkOrders/(Contracts/)` |
| `IWorkOrderSubStatusSummaryQuery` + Handler | Contrato + implementação | `Application/WorkOrders/(Contracts/)` |
| `IWorkOrderCriticalitySummaryQuery` + Handler | Contrato + implementação | `Application/WorkOrders/(Contracts/)` |
| `WorkOrder.SymptomRaw`/`SubStatus`/`NextAppointmentAt` | Propriedades de domínio | `Domain/WorkOrders/WorkOrder.cs` + `WorkOrderHistory.cs` |
| Migration `AddSubStatusNextAppointmentAndSymptomRawToWorkOrder` | EF Core migration | `Infrastructure/Persistence/Migrations/` |
| 3 novos cards + 1 gráfico | UI | `apps/office/src/app/dashboard/` |

Nenhum novo projeto, serviço, fila ou tabela de configuração é
introduzido.

---

## 11. Impactos em testes existentes

* `WorkOrderAgingSummaryQueryHandler`/`WorkOrderListByStatusQueryHandler`
  (aging): testes que hoje assumem "aberta = não terminal" precisam ser
  atualizados para os status de `WorkOrderOpenStatuses` (ADR-033,
  critério de aceite 4 de RF-029).
* `IServiceProviderInteractionOrderAdapter`: testes de `ExtractSymptom`/
  `SymptomCandidateFields` devem ser substituídos por testes diretos de
  `symptomDescription` → `Symptom` e `symptom` → `SymptomRaw` (RF-028).
* `WorkOrderPgRepository.ProcessInteractionAsync`: novos testes cobrindo
  disparo de `work_order_histories` por mudança isolada de `SubStatus`
  e de `NextAppointmentAt` (RF-031, critérios de aceite 4 e 5).

Responsabilidade de implementação e ajuste fino dos testes é do
`backend-engineer`, com revisão do `qa-engineer`.
