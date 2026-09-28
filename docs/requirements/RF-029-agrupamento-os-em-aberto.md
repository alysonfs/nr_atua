# RF-029 - Agrupamento "OS Em Aberto"

Status: `Proposto`

**Data:** 2026-09-26

## História de Usuário

Como usuário do Office,
eu quero que o sistema reconheça um agrupamento de negócio chamado
"OS Em Aberto" — que não é um único status, mas sim a união de vários
status brutos do provedor —,
para que métricas como "Situação Crítica" (RF-033) e a contagem geral de
OS em aberto usem exatamente o mesmo critério que a cliente já usa no seu
controle manual, evitando divergência de números entre painéis.

## Contexto e motivação

A cliente definiu explicitamente que **"Em Aberto"** é o agrupamento das
OS com status:

- pendente;
- proposta de troca;
- pedido de explicar (explicação);
- designado/assign;
- em processamento.

Este é um recorte diferente do que já existe implementado em RF-027
(`WorkOrderTerminalStatuses` — API/Collector), que usa a lógica inversa
("aberta" = status **fora** da lista terminal: `"Payment Approved"`,
`"cancelled"`, `"closed"`). As duas abordagens podem divergir se existir
algum status raw que não seja nem terminal nem um dos 5 grupos acima
(ex.: um status de "triagem" observado no dashboard mockado do Office,
`apps/office/.../dashboard`, que hoje não está mapeado em nenhuma das duas
listas).

**Decisão necessária:** este RF propõe que a lista explícita de "Em
Aberto" (whitelist) se torne a fonte de verdade para tudo que hoje se
chama "aberta" no sistema (RF-027 incluso), substituindo o critério por
exclusão de terminais. Isso é mais preciso e auditável (nenhuma
ambiguidade sobre o que conta), ao custo de exigir manutenção da lista
sempre que o provedor introduzir um novo status.

## Objetivo

Definir e centralizar a lista de status brutos do provedor que compõem o
agrupamento "OS Em Aberto", e propagar essa definição para todas as
métricas que hoje usam (ou deveriam usar) o conceito de "OS aberta".

## Escopo

### O que É RF-029

- Lista central `WorkOrderOpenStatuses` (nome sugerido) com os status
  brutos exatos correspondentes aos 5 grupos citados pela cliente.
- Atualização de RF-027 (aging) para usar esta lista no lugar da
  exclusão por status terminal.
- Definição de comportamento para status que não se enquadrem nem em
  "Em Aberto" nem em terminal (RF-029.3).

### O que NÃO é RF-029

- Implementação de métricas específicas que consomem este agrupamento
  (RF-033 "Situação Crítica", cards de contagem) — tratadas em RFs
  próprios.
- Definição de substatus (`pendingReason`) — tratado em RF-031.

## Requisitos funcionais

### RF-029.1 - Lista central de status "Em Aberto"

Deve existir uma lista central e compartilhada (mesmo padrão de
`WorkOrderTerminalStatuses`, duplicada entre Collector e API por não
haver projeto compartilhado — ver ADR-032) contendo os status brutos
exatos do provedor que representam: pendente, proposta de troca, pedido
de explicar, designado/assign, em processamento.

### RF-029.2 - RF-027 passa a usar esta lista

A contagem de aging (RF-027: `WorkOrderAgingSummaryQueryHandler`,
`WorkOrderListByStatusQueryHandler.ApplyAgingFilter`) deve considerar
"aberta" apenas as OS cujo status pertença à lista de RF-029.1, e não
mais "qualquer status fora da lista terminal".

### RF-029.3 - Status não classificado

Um status raw que não pertença nem à lista de "Em Aberto" (RF-029.1) nem
à lista de terminais (`WorkOrderTerminalStatuses`) não deve ser contado
silenciosamente em nenhuma das duas métricas. O sistema deve registrar
log de alerta (mesmo padrão já usado em `IServiceCollectorService` para
`woStatus` não mapeado em `StatusConfigs`) para permitir identificar e
classificar esse status posteriormente.

## Critérios de aceite

1. As 5 categorias citadas pela cliente (pendente, proposta de troca,
   pedido de explicar, designado/assign, em processamento) somadas
   reproduzem o mesmo total de "OS em aberto" reportado no painel manual
   da cliente, para a mesma massa de dados.
2. Uma OS com status terminal (RF-027) nunca é contada como "Em Aberto".
3. Um status raw desconhecido (nem terminal, nem "Em Aberto") gera log de
   alerta e não é contado em nenhuma das duas métricas.
4. RF-027 (aging) passa a usar a lista de RF-029.1 e os testes existentes
   são atualizados/ajustados sem regressão de comportamento para os
   status já cobertos.

## Decisões (dados reais, RDS `work_order_histories`, 2026-09-26)

Consulta somente leitura ao RDS de produção (`work_order_histories.Status`,
distinct + count, skill `atua-pg-inspect`) confirmou os valores brutos
exatos:

| Status raw (`woStatus`)  | Ocorrências | Grupo "Em Aberto"        |
| ------------------------ | ----------- | ------------------------ |
| `pending`                | 279         | pendente                 |
| `assigned`               | 81          | designado/assign         |
| `accepted`               | 18          | em processamento         |
| `Exchange Proposal`      | 27          | proposta de troca        |
| `Request to Explain`     | 9           | pedido de explicar        |

**Decidido — Lista `WorkOrderOpenStatuses` (RF-029.1):** `pending`,
`assigned`, `accepted`, `Exchange Proposal`, `Request to Explain`.

Também foram observados na mesma consulta status **fora** das 5
categorias da cliente e fora da lista terminal atual
(`WorkOrderTerminalStatuses` = `"Payment Approved"`, `"cancelled"`,
`"closed"`): `Exchange Approved` (13), `Exchange Rejected` (9),
`Request Cancel` (6), `Payment Rejected` (3). Estes caem no caso RF-029.3
(não classificado → log de alerta, não contam em nenhuma métrica) até
que a cliente confirme se são terminais, "em aberto" ou um terceiro
grupo — não são bloqueantes para implementar RF-029.1/RF-029.2, pois o
comportamento de log já cobre esse cenário.

## Pendências para validação (não bloqueante)

- Confirmar com a cliente a classificação de `Exchange Approved`,
  `Exchange Rejected`, `Request Cancel` e `Payment Rejected` (terminal,
  em aberto, ou um terceiro grupo) — hoje tratados como "não
  classificado" (RF-029.3) até essa confirmação.
