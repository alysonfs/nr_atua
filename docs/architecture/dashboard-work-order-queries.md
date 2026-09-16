# Contratos: Dashboard de OS por status (substituição dos mocks do Office)

Status: `Proposto` (aguardando implementação pelo backend-engineer)

Referências: RF-017 (`work_order` / `work_order_history`), ADR-026
(padrão `Contracts/`).

Este documento define os contratos de API para os dois endpoints que
substituem os hooks mockados:

- `apps/office/src/app/dashboard/hooks/useServiceOrderMonthSummary.ts`
- `apps/office/src/app/dashboard/hooks/useDesignatedServiceOrders.ts`

Não define implementação C# — apenas rotas, DTOs, contratos de interface
(nome/namespace) e regras de tenant scoping. Implementação é
responsabilidade do `backend-engineer`.

---

## 1. Endpoint 1 — Sumário mensal de OS por status

**Status: mantido no backend, não consumido pela UI — ver ADR-027.** O
Dashboard passou a usar o Endpoint 3 (contagem por status atual) abaixo.

### Rota

```
GET /api/tenants/{tenantId:guid}/work-orders/summary?month=YYYY-MM
```

- `tenantId`: path param (guid), segue o padrão já usado em
  `TenantEndpoints` (ex. `/api/tenants/{tenantId}/plan`).
- `month`: query param obrigatório, formato `YYYY-MM` (ex. `2026-09`),
  interpretado no fuso horário do tenant (`Tenant.TimeZoneId`, já existente
  no domínio — ver `ADR-015`). Se ausente, o handler deve rejeitar com
  `400 Bad Request` (não assumir "mês corrente" silenciosamente, para
  evitar ambiguidade de fuso entre cliente e servidor).

### Origem dos dados

`WorkOrderHistory` é a fonte de verdade para a série diária: cada entrada
representa uma mudança de status observada em um instante (`CreatedAt`).
A contagem diária de um status, em um dia D, é a quantidade de
`WorkOrder` cujo **status vigente em D** era aquele status — isto é
derivado por reconstrução de estado a partir de `WorkOrderHistory`
(entrada mais recente com `CreatedAt <= fim do dia D`, por
`WorkOrderId`), não por contagem simples de "quantas entradas de histórico
apareceram nesse dia com esse status" (isso subrepresentaria dias sem
mudança, mas com OS ainda naquele status).

Nota de risco de performance: essa reconstrução dia-a-dia pode ser cara em
volume alto. Para o MVP, com histórico incremental por tenant, calcular
via SQL (window function `ROW_NUMBER()` particionado por
`WorkOrderId, dia` ordenado por `CreatedAt DESC`) é aceitável. Se o volume
crescer, considerar um read-model diário materializado — não é necessário
agora (regra de evolução: não antecipar).

`total` do card = soma das contagens diárias do mês (equivalente a "OS
que estiveram nesse status em pelo menos 1 dia do mês" somado por dia, não
contagem de OS distintas — mantém paridade com o mock atual, que soma
`dailyCounts`).

### Response (200)

```json
{
  "month": "2026-09",
  "timeZoneId": "America/Sao_Paulo",
  "statuses": [
    {
      "status": "Designado",
      "total": 87,
      "dailyCounts": [4, 6, 5, 7, 3, ...]
    },
    {
      "status": "Em Processamento",
      "total": 102,
      "dailyCounts": [...]
    }
  ]
}
```

DTO (nomes propostos, C# records):

```csharp
public sealed record WorkOrderMonthlySummaryResponse(
    string Month,           // "YYYY-MM"
    string TimeZoneId,
    IReadOnlyList<WorkOrderStatusMonthSummary> Statuses);

public sealed record WorkOrderStatusMonthSummary(
    string Status,          // string crua (RF-017 — sem enum)
    int Total,
    IReadOnlyList<int> DailyCounts); // index 0 = dia 1 do mês
```

Decisão: `statuses` não é uma lista fixa (`TRACKED_STATUSES` do mock era
um artefato do mock, não uma regra de negócio — RF-017 explicitamente não
define catálogo de status). O endpoint deve retornar **todos os status
distintos observados no tenant naquele mês**, ordenados por `total`
decrescente. O front decide como exibir (inclusive achar um subconjunto,
se quiser). Isso evita hardcoded de status no backend, coerente com
DP-017.2 (status é string livre por provedor).

### Contrato de interface

```
Application/WorkOrders/Contracts/IWorkOrderMonthlySummaryQuery.cs
```

```csharp
namespace Atua.Api.Application.WorkOrders.Contracts;

public interface IWorkOrderMonthlySummaryQuery
{
    Task<WorkOrderMonthlySummaryResult> ExecuteAsync(
        Guid tenantId, DateOnly monthStart, string timeZoneId,
        CancellationToken cancellationToken);
}
```

Implementação: `Application/WorkOrders/WorkOrderMonthlySummaryQueryHandler.cs`
(nome de classe termina em `QueryHandler`, não `UseCase` — é leitura pura,
sem efeito colateral de negócio).

---

## 2. Endpoint 2 — Lista de OS por status

### Rota

```
GET /api/tenants/{tenantId:guid}/work-orders?status={status}&page={page}&pageSize={pageSize}
```

- `status`: query param obrigatório, string crua (ex. `Designado`). Sem
  paginação no MVP seria aceitável dado volume atual, mas propor
  `page`/`pageSize` (default `page=1`, `pageSize=20`) desde já evita
  reabrir o contrato quando o volume crescer — é paginação simples de
  lista, não é complexidade antecipada indevida.
- Sem parâmetro de mês: esta lista é "OS atualmente nesse status", não
  histórica por período (paridade com o mock, que lista `WorkOrder`
  correntes, não um recorte temporal).

### Origem dos dados

Tabela `WorkOrder` (estado atual), filtrando por `TenantId` e `Status`.
Ordenação proposta: `UpdatedAt` descendente (mais recente primeiro).

### Response (200)

```json
{
  "status": "Designado",
  "page": 1,
  "pageSize": 20,
  "totalCount": 5,
  "items": [
    {
      "id": "b6e2...",
      "providerId": "OS-1001",
      "status": "Designado",
      "createdAt": "2026-09-01T09:15:00.000Z",
      "updatedAt": "2026-09-02T13:40:00.000Z"
    }
  ]
}
```

DTO:

```csharp
public sealed record WorkOrderListResponse(
    string Status, int Page, int PageSize, int TotalCount,
    IReadOnlyList<WorkOrderListItem> Items);

public sealed record WorkOrderListItem(
    Guid Id, string ProviderId, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
```

### Contrato de interface

```
Application/WorkOrders/Contracts/IWorkOrderListByStatusQuery.cs
```

```csharp
namespace Atua.Api.Application.WorkOrders.Contracts;

public interface IWorkOrderListByStatusQuery
{
    Task<WorkOrderListResult> ExecuteAsync(
        Guid tenantId, string status, int page, int pageSize,
        CancellationToken cancellationToken);
}
```

Implementação: `Application/WorkOrders/WorkOrderListByStatusQueryHandler.cs`.

---

## 3. Endpoint 3 — Contagem de OS por status atual (dashboard "resumo por status")

### Rota

```
GET /api/tenants/{tenantId:guid}/work-orders/status-summary
```

- Substitui, na UI do Dashboard, o Endpoint 1 (ver ADR-027): sem parâmetro
  de mês, sem série diária — apenas contagem total por status **atual**.

### Origem dos dados

Tabela `WorkOrder` (estado atual), agrupada por `TenantId` e `Status`,
sem depender de `WorkOrderHistory`.

### Response (200)

```json
{
  "statuses": [
    { "status": "pending", "total": 119 },
    { "status": "Payment Approved", "total": 157 }
  ]
}
```

DTO:

```csharp
public sealed record WorkOrderStatusSummaryResponse(IReadOnlyList<WorkOrderStatusCount> Statuses);

public sealed record WorkOrderStatusCount(string Status, int Total);
```

### Contrato de interface

```
Application/WorkOrders/Contracts/IWorkOrderStatusSummaryQuery.cs
```

```csharp
namespace Atua.Api.Application.WorkOrders.Contracts;

public interface IWorkOrderStatusSummaryQuery
{
    Task<WorkOrderStatusSummaryResult> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken);
}
```

Implementação: `Application/WorkOrders/WorkOrderStatusSummaryQueryHandler.cs`.

Autorização e tenant scoping seguem o mesmo padrão da seção 5 abaixo.

---

## 4. Decisão sobre `providerName`

**`providerName` NÃO existe na resposta. Não é uma lacuna a preencher — é
uma incompatibilidade conceitual do mock que deve ser corrigida no front.**

Motivo: no domínio real (`WorkOrder.cs`, RF-017), `ProviderId` **não
identifica uma empresa/provedor terceirizado**. É o identificador *externo
da própria OS* no sistema de origem (ex. `workOrderId` do iService — ver
comentário em `WorkOrder.cs`: "Identificador externo da OS no provedor").
Ou seja, o domínio hoje não modela "provedor" como uma entidade de negócio
(empresa prestadora) — modela apenas a integração de origem (iService) e o
ID da OS dentro dela.

O mock (`providerName: 'Refrigeração Natal Ltda'`, etc.) presumiu, de forma
incorreta, que `providerId` seria um identificador de empresa prestadora
de serviço. Essa entidade não existe no domínio hoje e **não deve ser
criada apenas para satisfazer o mock** (regra de evolução: não antecipar
modelagem sem necessidade real identificada em RF).

Ação recomendada para o `frontend-engineer`: renomear/reinterpretar a
coluna da tabela do Dashboard de "Provedor" para algo como "Nº da OS no
provedor" ou similar, exibindo `providerId` (ex. `OS-1001`) como o
identificador externo da OS, não como nome de empresa. Se o produto
realmente precisar exibir "qual empresa prestou o serviço", isso é um novo
requisito de negócio (nova entidade `Provider`/empresa) que deve ser
levantado como RF novo, não resolvido silenciosamente aqui.

---

## 5. Autorização e tenant scoping

Seguir o padrão já estabelecido em `TenantEndpoints`:

- `RequireAuthorization("BrowserSession")` nos dois endpoints.
- Extrair `userId` da claim `sub` do `ClaimsPrincipal`; se ausente,
  `401 Unauthorized`.
- O `tenantId` vem do path, **não** da claim — mas o handler (query) deve
  validar que o `userId` autenticado possui `TenantMembership` para aquele
  `tenantId` antes de consultar `WorkOrder`/`WorkOrderHistory`. Se não for
  membro, retornar `403 Forbidden` (mesmo padrão de `GetTenantPlan`, que
  retorna `Results.Forbid()` quando o `use case` resulta em acesso
  negado).
- Toda query a `WorkOrder`/`WorkOrderHistory` deve filtrar por `TenantId`
  explicitamente (RN-017.6 — `TenantId` é redundante nas duas tabelas
  justamente para permitir esse filtro direto, sem join custoso).
- Não introduzir uma nova policy de autorização — `BrowserSession` já é o
  padrão correto para endpoints consumidos pelo `apps/office` autenticado
  via sessão de navegador.

---

## 6. Resumo de componentes novos

| Componente | Tipo | Caminho |
|---|---|---|
| `IWorkOrderMonthlySummaryQuery` | Interface (contrato) | `Application/WorkOrders/Contracts/` |
| `WorkOrderMonthlySummaryQueryHandler` | Implementação | `Application/WorkOrders/` |
| `IWorkOrderListByStatusQuery` | Interface (contrato) | `Application/WorkOrders/Contracts/` |
| `WorkOrderListByStatusQueryHandler` | Implementação | `Application/WorkOrders/` |
| `IWorkOrderStatusSummaryQuery` | Interface (contrato) | `Application/WorkOrders/Contracts/` |
| `WorkOrderStatusSummaryQueryHandler` | Implementação | `Application/WorkOrders/` |
| `WorkOrderEndpoints` | Minimal API endpoints | `Endpoints/WorkOrderEndpoints.cs` |

Nenhuma nova entidade de domínio, nenhuma migration, nenhuma tabela nova.
Ambos os endpoints são consultas de leitura sobre `WorkOrder` e
`WorkOrderHistory` já existentes.
