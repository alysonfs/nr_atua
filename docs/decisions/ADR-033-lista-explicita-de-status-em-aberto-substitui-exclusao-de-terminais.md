# ADR-033 - Lista Explícita `WorkOrderOpenStatuses` Substitui Exclusão por Status Terminal

## Status

Accepted

## Contexto

RF-027 (aging) e o Endpoint 2.1 (`aging-summary`) definiam "OS aberta"
por **exclusão**: qualquer status que não estivesse em
`WorkOrderTerminalStatuses` (`"Payment Approved"`, `"cancelled"`,
`"closed"`) contava como aberta.

RF-029 revela que esse critério diverge do controle manual da cliente:
ela define "Em Aberto" como uma **whitelist** de 5 categorias de status
(pendente, proposta de troca, pedido de explicar, designado/assign, em
processamento), verificada com dados reais de produção
(`work_order_histories.Status`, ver RF-029). Existem status raw
observados (`Exchange Approved`, `Exchange Rejected`, `Request Cancel`,
`Payment Rejected`) que não são nem terminais nem parte das 5 categorias
— com o critério por exclusão, esses status eram silenciosamente
contados como "abertos"; a cliente não os reconhece como tal.

## Decisão

1. Substituir o critério de "aberta" — em todos os lugares que hoje usam
   `!WorkOrderTerminalStatuses.Contains(status)` para esse fim
   (`WorkOrderAgingSummaryQueryHandler`,
   `WorkOrderListByStatusQueryHandler.ApplyAgingFilter`) — pela
   verificação positiva `WorkOrderOpenStatuses.Contains(status)`.
2. `WorkOrderTerminalStatuses` não é removida: continua sendo a fonte de
   verdade para "OS concluída/encerrada" onde esse conceito
   especificamente é necessário (ex.: `WorkOrderMonthlyTrendQueryHandler`,
   `WorkOrderPgRepository` no Collector, para decidir
   `NeedsDetailFetch`).
3. `WorkOrderOpenStatuses` segue exatamente o mesmo padrão de
   duplicação deliberada entre `Atua.Collector` e `Atua.Api` já
   estabelecido em ADR-032 (mesmo critério objetivo de quando eliminar a
   duplicação).
4. Um status raw que não pertença a nenhuma das duas listas
   (`WorkOrderOpenStatuses` nem `WorkOrderTerminalStatuses`) fica
   automaticamente fora de ambas as métricas — não é necessário nenhum
   código adicional de exclusão nas queries da API, pois a checagem já é
   positiva (`Contains`), não por exclusão. A responsabilidade de
   **alertar** sobre esse caso (RF-029.3) fica no Collector, no momento
   da ingestão (`WorkOrderPgRepository`), e não nas queries da API — evita
   logar o mesmo alerta a cada carregamento do Dashboard.

## Consequências

* **Positivas**: a contagem de "OS aberta" passa a refletir exatamente o
  controle manual da cliente; status desconhecidos não inflam
  silenciosamente a métrica de "aberta"; o alerta de status não
  classificado é emitido uma vez por evento de ingestão, não por
  requisição de leitura.
* **Negativas**: a lista `WorkOrderOpenStatuses` exige manutenção manual
  sempre que o provedor introduzir uma nova categoria de status "em
  aberto" (mesmo trade-off já aceito para `WorkOrderTerminalStatuses` em
  ADR-032). Os 4 status observados fora das duas listas (`Exchange
  Approved`, `Exchange Rejected`, `Request Cancel`, `Payment Rejected`)
  deixam de contar em "aberta" a partir desta mudança — impacto esperado
  e intencional (RF-029, "Pendências para validação"), a ser
  reclassificado quando a cliente confirmar seu grupo correto.
* Testes existentes de `WorkOrderAgingSummaryQueryHandler` e
  `WorkOrderListByStatusQueryHandler` (aging) precisam ser atualizados
  para os novos status de referência (RF-029, critério de aceite 4).
