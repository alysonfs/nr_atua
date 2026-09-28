# RF-030 - Data de Próximo Atendimento e Indicadores de Prazo

Status: `Proposto`

**Data:** 2026-09-26

## História de Usuário

Como usuário do Office,
eu quero saber quais OS têm atendimento vencido, vencendo hoje, ou
vencendo amanhã (em atenção),
para que eu possa agir antes que o atraso vire uma situação crítica.

## Contexto e motivação

A cliente precisa de três indicadores de prazo, todos derivados de uma
única data ainda não coletada pelo ATUA: a data do "Próximo Atendimento"
da OS (agendamento futuro, informado/gerenciado pelo provedor).

- **Atendimento Vencido:** `Próximo Atendimento < data atual`.
- **Atendimento Vence Hoje:** `Próximo Atendimento == data atual`.
- **Atendimento em Atenção (falta 1 dia):** `Próximo Atendimento == data
  atual + 1`.

Hoje o domínio `WorkOrder` não possui nenhum campo equivalente a "próximo
atendimento" — os campos de data existentes são `ProviderCreatedAt`
(criação) e `ProviderUpdatedAt` (última atualização), nenhum dos dois
representa um agendamento futuro.

## Objetivo

Coletar, persistir e expor via métricas a data de "Próximo Atendimento"
de cada OS, permitindo classificar OS em vencidas, vencendo hoje, ou em
atenção (falta 1 dia).

## Escopo

### O que É RF-030

- Novo campo de domínio `WorkOrder.NextAppointmentAt` (nome sujeito a
  validação).
- Extensão do Collector/adapter para capturar esse dado do payload do
  provedor (campo exato a confirmar — ver pendências).
- Três métricas/contagens (vencido, vence hoje, em atenção), calculadas
  no fuso horário do tenant (mesmo padrão de RF-017/ADR-015), comparando
  apenas a parte de **data** (não data+hora) de `NextAppointmentAt` com a
  data atual local do tenant.
- Exibição no Dashboard do Office (cards, mesmo padrão de RF-027).

### O que NÃO é RF-030

- Definição de como o Collector deve tratar OS que nunca tiveram
  agendamento (`NextAppointmentAt` nulo) em outras métricas — essas OS
  simplesmente não entram em nenhuma das 3 contagens deste RF.
- Alteração de agendamento pelo usuário do Office (somente leitura/
  visualização, sem escrita de volta ao provedor).

## Requisitos funcionais

### RF-030.1 - Novo campo de domínio

`WorkOrder` ganha um campo de data opcional para o próximo atendimento
agendado, análogo em tratamento a `ProviderCreatedAt`/`ProviderUpdatedAt`
(nulável, vindo do payload bruto do provedor).

### RF-030.2 - Captura pelo Collector

O adapter de interação com o provedor deve extrair essa data do campo
correspondente do payload do iService (a confirmar — ver pendências) e
propagá-la em todo upsert de OS.

### RF-030.3 - Cálculo dos 3 indicadores

Os três indicadores comparam apenas a data (sem hora), no fuso horário
do tenant — **decidido:** fuso de referência é `Tenant.TimeZoneId`
(mesma precedência já estabelecida em ADR-015: override de sessão →
`Tenant.TimeZoneId` → padrão `America/Sao_Paulo`), mesmo padrão já usado
por RF-017.

- Vencido: `NextAppointmentAt` (data local) `<` hoje (data local do
  tenant).
- Vence hoje: `NextAppointmentAt` (data local) `==` hoje.
- Em atenção: `NextAppointmentAt` (data local) `==` hoje + 1 dia.

Uma OS sem `NextAppointmentAt` não entra em nenhum dos 3 indicadores.

### RF-030.4 - Cruzamento com "Em Aberto" (RF-029)

**Decidido:** os 3 indicadores consideram apenas OS "Em Aberto"
(RF-029). Uma OS terminal não gera alerta de atraso de atendimento —
seja porque já foi concluída, seja porque a demanda foi descartada
(cancelamento). Coerente com RF-027 (aging), que também só considera OS
abertas.

### RF-030.5 - Exibição no Dashboard

Três novos cards no Dashboard do Office (mesmo padrão visual dos cards
existentes), cada um navegando para a lista de OS correspondente.

## Critérios de aceite

1. Uma OS com `NextAppointmentAt` anterior à data atual (fuso do tenant)
   é contada em "Atendimento Vencido" e não nas outras duas.
2. Uma OS com `NextAppointmentAt` igual à data atual é contada em
   "Vence Hoje" e não nas outras duas.
3. Uma OS com `NextAppointmentAt` igual a amanhã é contada em
   "Em Atenção" e não nas outras duas.
4. Uma OS sem `NextAppointmentAt` não aparece em nenhum dos 3 cards.
5. Uma OS terminal (RF-027/RF-029) não é contada em nenhum dos 3
   indicadores (decidido — RF-030.4).

## Pendências para validação (não bloqueante)

- **Decidido:** o campo do payload do iService (`queryWorkOrder`/
  `orders[]`) que representa "Próximo Atendimento" é `visitStartTime`,
  não `nextVisitDate` (hipótese anterior descartada — em amostra real
  de payload, `nextVisitDate`/`nextVisitEndDate`/`visitDate` vieram
  `null` enquanto `visitStartTime` veio populado com
  `"2026-08-12 00:00:00"`, coerente com `woVisitStartTime` e
  `srVisitStartTime` no mesmo payload). Confirmado pelo usuário
  comparando o payload real com a tela de detalhe da OS no iService em
  2026-09-26.
- **Confirmado pela cliente (2026-09-26):** toda vez que o status ou o
  substatus (RF-031) da OS muda — por ação do Posto Autorizado ou da
  Midea — um novo `visitStartTime` é informado pelo provedor.
  `WorkOrder.NextAppointmentAt` deve sempre refletir o valor mais
  recente (sobrescrita simples em `work_orders`, mesmo tratamento das
  demais colunas operacionais de data — sem `COALESCE` de
  preservação, que é exclusivo das colunas de contato). O valor anterior
  fica preservado em `work_order_histories` sempre que a mudança for
  acompanhada de troca de `Status` (comportamento já implementado) —
  mas **ver RF-031.4**: o gatilho atual de `work_order_histories`
  (baseado só em `Status`) precisa ser estendido para também disparar em
  mudança isolada de `SubStatus` ou de `NextAppointmentAt` (status
  igual), sob pena de perder o histórico do agendamento anterior.


