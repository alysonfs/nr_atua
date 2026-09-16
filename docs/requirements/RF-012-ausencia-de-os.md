# RF-012 - Ausência de OS

Status: `Implementado (por design)`

**Data:** 2026-08-31 (especificado) · **2026-09-02** (confirmado implementado
por design — sem código dedicado necessário — e revisado sobre o modelo
final de persistência)

> **Nota (2026-09-02):** Este documento foi corrigido para refletir o modelo
> final de persistência (RF-016/RF-017): `work_order_observations` foi
> substituído por `work_order_histories` (Postgres, append condicional à
> mudança de status) e `capturedAtUtc` por `work_order_histories.CreatedAt`.
> O "snapshot atual" da OS é o registro em `work_order` (Postgres, upsert),
> não mais um documento upsertado em `work_order_snapshots` — essa coleção
> (MongoDB) é append-only por design (RF-016) e nunca é lida para decidir
> ausência. O comportamento de negócio não mudou em relação à especificação
> original.

## Objetivo

Definir o comportamento do sistema quando uma OS conhecida não aparece em
uma coleta — impedindo que ausências transitórias sejam interpretadas
automaticamente como cancelamento, conclusão ou exclusão da OS.

## Escopo

RF-012 trata exclusivamente da interpretação da ausência de uma OS no
conjunto retornado por uma coleta. Ele não define o que ocorre quando uma OS
aparece com status diferente (RF-011), nem o comportamento de OS nunca antes
vistas.

### O que é RF-012

- Regra de interpretação negativa: ausência em uma coleta não é evidência
  de mudança de estado.
- Regra de preservação: OS previamente observadas não devem ter seu estado
  alterado nem ser marcadas como canceladas/concluídas/excluídas com base
  exclusivamente na ausência.
- Registro da ausência: a coleta em que a OS não apareceu pode ser registrada
  para fins de diagnóstico, mas não como mudança de estado da OS.

### O que NÃO é RF-012

- Definição de quando o ATUA pode concluir que uma OS foi encerrada (esse
  critério depende de evidência positiva — a OS aparecer com status Concluído
  ou Cancelado numa coleta futura).
- Mecanismo de alerta por OS há N coletas ausente (RF futuro).
- Exclusão de OS do sistema (RF futuro, dependente de política de retenção
  RF-009.7).

## Requisitos funcionais

### RF-012.1 - Ausência não implica mudança de estado

Quando uma OS previamente observada não aparece em uma coleta, o sistema não
deve alterar o estado atual dessa OS nem registrar qualquer transição de
status.

### RF-012.2 - Preservação do estado atual em `work_order`

O registro de estado atual (`work_order`) de uma OS ausente em uma coleta
deve permanecer inalterado: `status` e `updated_at` devem continuar
refletindo a última coleta em que a OS foi observada (isto é, o último
snapshot bruto que gerou um evento processado pelo Consumer).

### RF-012.3 - Ausência não gera entrada de histórico

A ausência de uma OS em uma coleta não deve gerar uma nova entrada na
tabela `work_order_histories`. Entradas de histórico registram aparições
reais da OS (mudanças de status observadas), não ausências.

### RF-012.4 - Razões conhecidas de ausência

As razões pelas quais uma OS pode não aparecer em uma coleta incluem, entre
outras: filtro de janela temporal do iService, instabilidade temporária do
portal, OS fora dos 5 status suportados naquele momento, ou paginação não
coberta. Nenhuma dessas razões justifica inferir mudança de estado.

## Regras de negócio

| Número   | Regra                                                                                                                     |
|----------|---------------------------------------------------------------------------------------------------------------------------|
| RN-012.1 | Ausência de OS em uma coleta não altera o estado atual (`work_order`) nem gera entrada em `work_order_histories`.         |
| RN-012.2 | O registro em `work_order` de uma OS ausente permanece com os valores da última coleta em que ela foi observada.          |
| RN-012.3 | Ausência não gera entrada de histórico — `work_order_histories` registra apenas mudanças de status realmente observadas. |
| RN-012.4 | Apenas evidência positiva (OS aparecendo com status Concluído ou Cancelado) pode registrar esses estados no ATUA.         |

## Critérios de aceite

1. Dado que a OS 123 foi observada na coleta C1 com status Designado,
   quando a coleta C2 não retornar a OS 123 (ausência),
   então o registro da OS 123 em `work_order` deve permanecer com
   `status = "Designado"` e `updated_at` da última vez em que ela foi
   processada (coleta C1),
   e nenhuma nova entrada deve ser inserida em `work_order_histories`
   para a OS 123 a partir da coleta C2.

2. Dado que a OS 123 esteve ausente nas coletas C2, C3 e C4,
   quando a coleta C5 retornar a OS 123 com status Em Processamento,
   então o registro da OS 123 em `work_order` deve ser atualizado para
   `status = "Em Processamento"` com `updated_at` da coleta C5,
   e uma nova entrada em `work_order_histories` deve ser inserida
   referenciando o snapshot de C5,
   e as coletas C2, C3 e C4 não devem gerar entradas de histórico para a
   OS 123.

3. Dado que a OS 123 esteve ausente em todas as coletas após C1,
   quando o histórico da OS 123 (`work_order_histories`) for consultado,
   então deve conter apenas a entrada referente a C1,
   sem qualquer registro de "ausência" ou mudança de estado inferida.

## Implementação e validação (2026-09-02)

RF-012 **não exigiu código dedicado** — é satisfeito por design pela
arquitetura já implementada para RF-016/RF-017 (ADR-023), confirmada por
leitura de código:

- `SnapshotConsumerWorker.RunChangeStreamAsync` (Change Stream sobre
  `work_order_snapshots`) processa exclusivamente eventos de
  `OperationType == Insert`. Não existe nenhuma rotina de varredura
  periódica que compare o conjunto de OS de uma coleta com o estado
  atual em `work_order` para detectar/marcar ausências — o Consumer só
  reage a inserções reais, uma por vez.
- `WorkOrderRepository.InsertSnapshotsAsync` (RF-016) é estritamente
  append-only: insere um documento por OS presente no `CollectionResult`
  da coleta atual. OS ausentes na coleta simplesmente não geram nenhum
  documento — não há operação de "limpeza" ou remoção de snapshots
  anteriores de OS não vistas na coleta corrente.
- `WorkOrderPgRepository.UpsertWorkOrderAsync`/`ProcessSnapshotAsync` só
  são invocados a partir de um evento de Change Stream — ou seja, apenas
  para (tenant, provider_id) que **de fato apareceram** em algum snapshot.
  Não existe consulta que itere sobre todos os `work_order` existentes
  para comparar com a coleta atual e marcar os ausentes.

Como consequência arquitetural direta (sem necessidade de regra explícita
de "ignorar ausência"): RN-012.1, RN-012.2 e RN-012.3 são satisfeitas
porque **não existe nenhum caminho de código que leia "ausência"** — o
sistema é inteiramente orientado a eventos de aparição real. RN-012.4
também é satisfeita, pois o único jeito de `work_order.status` chegar a
`Concluído`/`Cancelado` é um snapshot real com esse status (evidência
positiva).

**Não validado com dados reais de produção** (diferente de RF-016/RF-017):
não há, até o momento, uma OS que tenha reaparecido após ausência
observada, então os critérios de aceite 1-3 acima foram confirmados por
leitura de código, não por observação de um caso real na base do RDS.
Se quiser uma validação empírica, seria necessário aguardar uma OS sumir
de uma coleta e reaparecer (ou simular removendo temporariamente uma OS
do escopo de coleta).

## Dependências

- RF-009 (coleta inicial): estabelece o conjunto inicial de OS conhecidas.
- RF-016 (persistência de snapshots brutos): `work_order_snapshots` é
  append-only; a ausência de uma OS em uma coleta simplesmente não gera
  documento algum, não havendo "remoção" a se preocupar.
- RF-017 (modelo agnóstico work_order/work_order_history): o Consumer
  (`SnapshotConsumerWorker`/`WorkOrderPgRepository`) só atualiza
  `work_order` e insere em `work_order_histories` a partir de eventos de
  inserção reais em `work_order_snapshots` — RF-012 é o complemento
  para quando a OS não aparece.
- RF-011 (atualização de estado): define o que acontece quando a OS aparece
  — RF-012 é o complemento para quando ela não aparece.

## Impactos

- RF futuro de alerta por OS cronicamente ausente dependerá da contagem de
  coletas sem aparição, que este requisito preserva implicitamente (ausência
  não apaga o registro em `work_order`, que permanece com o `updated_at` da
  última aparição real).

## Decisões pendentes

### DP-012.1 — Registro explícito de ausência para diagnóstico

**Situação:** RF-012.3 proíbe gerar observações de estado por ausência. Não
está definido se o sistema deve registrar, em algum outro lugar (ex.: log
estruturado, campo no snapshot como `lastSeenCommandId`), que a OS não
apareceu em determinadas coletas.

**Impacto se não decidido:** sem registro de ausência, não é possível
construir alertas ou diagnósticos de "OS há N coletas ausente" sem consultar
a ausência de observações — o que é possível, mas menos eficiente.

**Aguarda:** decisão de produto sobre necessidade de diagnóstico de ausência.

### DP-012.2 — ~~Critério de conclusão por evidência positiva vs. ausência prolongada~~ ✅ Resolvido (2026-09-02)

**Situação:** RF-012 define que ausência não implica conclusão. Não estava
definido se, após N coletas consecutivas sem aparição, o sistema poderia
assumir que a OS foi concluída ou cancelada fora dos status suportados.

**Decisão do usuário:** nunca inferir conclusão/cancelamento por ausência
prolongada, independentemente de N. `work_order.status` só muda mediante
**evidência positiva** — a OS reaparecer numa coleta com um status
diferente (RN-012.4 permanece válida sem exceção por tempo). Não há
"timeout" que force uma OS ausente há muitas coletas a ser marcada como
Concluída/Cancelada automaticamente.

**Consequência:** OS que nunca mais aparecerem (ex.: excluídas no
iService, ou fora da janela de coleta) permanecem indefinidamente em
`work_order` com o último status observado. Isso é aceito como
comportamento correto — qualquer alerta de "OS cronicamente ausente" (RF
futuro, fora de escopo) deve ser tratado como diagnóstico/alerta, nunca
como mudança automática de estado.

**Nenhuma mudança de código foi necessária** — o comportamento atual
(RF-012 implementado por design) já satisfaz esta decisão sem alteração,
pois nunca existiu lógica de inferência por ausência a remover.

## Fora do escopo

- Alerta por OS ausente por N coletas consecutivas (RF futuro).
- Exclusão ou arquivamento automático de OS (RF futuro, política RF-009.7).
- Interpretação de OS que aparecem com status diferente (RF-011).
- Escrita no iService (RF-013).
