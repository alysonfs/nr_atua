# ADR-027: Remoção do agrupamento mensal do "summary month" no Dashboard

## Status

Aceita

## Contexto

O Dashboard do Office (Passo 3 do plano de implementação) introduziu uma
seção "summary month" que agrupa a contagem de ordens de serviço por status,
com série diária (sparkline) para o mês corrente, consumindo o endpoint
`GET /api/tenants/{tenantId}/work-orders/summary?month=YYYY-MM`
(ver `docs/architecture/dashboard-work-order-queries.md`).

Esse endpoint reconstrói o status vigente de cada OS dia a dia a partir do
histórico (`WorkOrderHistory`), assumindo que já existe volume e
regularidade suficientes de coleta para produzir uma série mensal
significativa.

## Decisão

**Ainda não temos maturidade sobre os dados coletados para sustentar uma
contagem agregada por mês.** A coleta de ordens de serviço (via Collector)
está em estágio inicial, com poucos tenants e período curto de operação —
uma série mensal completa (com todos os dias do mês preenchidos) não reflete
a realidade operacional atual e pode induzir a conclusões erradas sobre o
comportamento das OS.

Por isso, o agrupamento por mês na seção "summary month" do Dashboard
**não deve ser mantido** nesta fase. A próxima iteração deve reavaliar o
recorte temporal/agregação adequado ao volume real de dados disponível
(ex.: período mais curto, sem série diária, ou uma visão que não dependa de
histórico contínuo).

## Consequências

- O endpoint `GET /api/tenants/{tenantId}/work-orders/summary?month=` e o
  handler `WorkOrderMonthlySummaryQueryHandler` continuam existindo no
  backend, mas a UI do Dashboard não deve mais consumi-los na forma atual
  até a nova definição de produto.
- Cabe ao `product-analyst` definir o recorte/visualização substituta antes
  de qualquer nova implementação de front para essa seção.
- Não é uma reversão de código imediata — é um registro de que a
  funcionalidade "summary month" (mensal) não deve avançar/ser usada como
  está, para orientar o próximo passo do plano.

## Atualização (recorte substituto implementado)

O recorte substituto foi definido e implementado: contagem total de OS por
**status atual** (tabela `WorkOrder`, sem recorte de mês nem série diária),
via novo endpoint `GET /api/tenants/{tenantId}/work-orders/status-summary`
(`IWorkOrderStatusSummaryQuery` / `WorkOrderStatusSummaryQueryHandler`).

- A seção do Dashboard (`StatusSummary`, antigo `SummaryMonth`) passou a
  consumir esse endpoint via `useWorkOrderStatusSummary`, exibindo apenas o
  total atual por status (sem sparkline).
- `GET /api/tenants/{tenantId}/work-orders/summary?month=` e
  `WorkOrderMonthlySummaryQueryHandler` foram mantidos no backend (não
  removidos), mas não são mais consumidos por nenhuma tela do Office.
