# ADR-026 - Padrão `Contracts/` para interfaces de camada de aplicação

## Status

Accepted

## Contexto

O Dashboard do `apps/office` hoje consome dois hooks com dados MOCKADOS
(`useServiceOrderMonthSummary` e `useDesignatedServiceOrders`) que precisam
ser substituídos por endpoints reais da API, lendo as entidades já
existentes `WorkOrder` e `WorkOrderHistory` (RF-017).

Até este ponto, o projeto não possui um diretório `Contracts/` em nenhuma
camada de `Atua.Api`. As interfaces de aplicação existentes (quando
existem, ex. `ICollectorEligibilityEvaluator`, `IServiceCredentialService`)
convivem soltas dentro da pasta do próprio módulo (`Application/Integrations`,
`Application/Billing`), sem uma convenção explícita de onde uma interface de
contrato entre camadas deve morar.

Este trabalho introduz as primeiras queries de leitura para o Dashboard
(sumário mensal de OS por status e listagem de OS por status). É uma boa
oportunidade para estabelecer o precedente de organização, já que:

- essas queries são candidatas naturais a terem múltiplas implementações no
  futuro (ex.: uma implementação otimizada com agregação no banco vs. uma
  implementação simples via LINQ, ou futura leitura via read-model
  materializado);
- separar a interface (o contrato) da implementação concreta facilita
  testes do endpoint (mock da interface) sem acoplar a um EF Core real.

## Decisão

Adotar, a partir de agora, a convenção:

```
Application/<Modulo>/Contracts/I<Nome>.cs
```

Regras:

1. Toda interface que representa um contrato consumido por um endpoint
   (Minimal API) ou por outro caso de uso — isto é, um ponto de
   extensão/abstração de aplicação — fica em um arquivo próprio dentro de
   `Contracts/`, com o mesmo nome da interface.
2. A implementação concreta (classe) permanece no diretório do módulo,
   fora de `Contracts/` (ex.: `Application/WorkOrders/WorkOrderMonthlySummaryQuery.cs`
   implementa `Application/WorkOrders/Contracts/IWorkOrderMonthlySummaryQuery.cs`).
3. Este padrão não é retroativo: interfaces já existentes fora de
   `Contracts/` (ex. `ICollectorEligibilityEvaluator`) não precisam ser
   movidas apenas para adequação ao padrão. A migração, se algum dia fizer
   sentido, deve ser uma tarefa própria, não um efeito colateral desta
   decisão.
4. Casos de uso simples, sem necessidade prevista de múltiplas
   implementações ou de mock em teste (ex. `AddTenantUseCase`, que já é
   usado diretamente como classe concreta), não são obrigados a ganhar uma
   interface em `Contracts/` só para seguir o padrão. Contratos são
   introduzidos quando há uma razão concreta (testabilidade via mock,
   múltiplas implementações, ou fronteira de camada relevante) — não por
   uniformidade estética.

## Consequências

- Módulo `Application/WorkOrders/` (novo) segue o padrão desde o início:
  `Application/WorkOrders/Contracts/IWorkOrderMonthlySummaryQuery.cs` e
  `Application/WorkOrders/Contracts/IWorkOrderListByStatusQuery.cs`, com
  implementações `WorkOrderMonthlySummaryQueryHandler` e
  `WorkOrderListByStatusQueryHandler` no nível do módulo.
- Próximos módulos que introduzirem contratos relevantes devem seguir a
  mesma convenção, mantendo consistência incremental sem exigir
  retrofitting do código já existente.
