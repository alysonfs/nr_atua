# RF-010 - Histórico Observado

> **⚠️ Status: Obsoleto (substituído por redesenho de 2026-08-31)**
>
> Este requisito foi **integralmente substituído** pelo redesenho do modelo
> de dados de Ordem de Serviço decidido em 2026-08-31.
>
> **Motivo:** RF-010 definia regras de integridade para a entidade
> `work_order_observations` (campo `capturedAtUtc`, imutabilidade,
> proibição de fabricação de datas). Essa entidade foi eliminada do modelo.
> O novo design persiste o dado bruto primeiro (`work_order_snapshots` —
> append-only, sem mapeamento de campos) e mantém apenas o status agnóstico
> em `work_order_history`. Não existe mais uma entidade de "observação" com
> `capturedAtUtc` como campo de integridade.
>
> **Substituído por:**
> - **RF-016** — Persistência de Snapshots Brutos de OS (modelo de dado bruto,
>   append-only, sem mapeamento).
> - **RF-017** — Modelo Agnóstico de OS: `work_order` e `work_order_history`
>   (status agnóstico de provedor, histórico de transições).
>
> O **princípio** subjacente de RF-010 — o ATUA não retroage datas nem fabrica
> registros históricos — permanece válido e está incorporado às regras de
> RF-016 e RF-017. O que foi eliminado é a entidade (`work_order_observations`)
> e o campo (`capturedAtUtc`) sobre os quais RF-010 operava.
>
> **Data de obsolescência:** 2026-08-31.

---

*Conteúdo original preservado abaixo para rastreabilidade histórica.*

---

## Objetivo (histórico)

Garantir que o histórico de uma OS no ATUA contenha exclusivamente estados
observados pelo sistema a partir da primeira coleta, sem retroação a eventos
anteriores a ela e sem fabricação de observações.

## Escopo (histórico)

Este requisito definia as regras de integridade temporal do histórico de OS.
Ele complementava RF-009 (que produzia a observação inicial) e era consumido por
RF-011 (que acrescentava observações em coletas posteriores).

O mecanismo de persistência subjacente — coleções `work_order_snapshots` e
`work_order_observations` no MongoDB — estava implementado em
`WorkOrderRepository`, `WorkOrderSnapshotDocument` e
`WorkOrderObservationDocument`. Esses artefatos de código **serão substituídos**
pela implementação de RF-016 e RF-017.

## Por que foi substituído

O modelo anterior assumia que o Worker mapeava campos do iService para uma
entidade de "observação" com `capturedAtUtc`, regras de imutabilidade e
idempotência por `(tenantId, providerOrderId, commandId)`. Esse mapeamento
foi considerado prematuro: não há conhecimento suficiente sobre o iService
(e futuros provedores) para definir esse mapeamento com confiança. A decisão
foi guardar o dado bruto primeiro e mapear depois, quando houver mais provedores
conhecidos e a camada agnóstica puder ser desenhada adequadamente.
