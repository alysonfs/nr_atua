# RF-011 - Atualização de Estado e Transições

> **⚠️ Status: Obsoleto (substituído por redesenho de 2026-08-31)**
>
> Este requisito foi **integralmente substituído** pelo redesenho do modelo
> de dados de Ordem de Serviço decidido em 2026-08-31.
>
> **Motivo:** RF-011 definia o comportamento de coletas recorrentes sobre o
> modelo antigo: upsert de `work_order_snapshots` (estado único por OS, por
> `(tenantId, providerOrderId)`) e append de `work_order_observations` (uma
> observação por OS por coleta). Ambas as entidades e suas mecânicas foram
> redesenhadas:
>
> - `work_order_snapshots` passa a ser **append-only** (não upsert), guardando
>   o payload bruto de cada coleta sem mapeamento de campos — ver RF-016.
> - A entidade `work_order_observations` foi eliminada.
> - O estado atual e o histórico de status agnóstico de provedor passam a
>   viver em `work_order` (upsert) e `work_order_history` (append) — ver RF-017.
>
> **Substituído por:**
> - **RF-016** — Persistência de Snapshots Brutos de OS.
> - **RF-017** — Modelo Agnóstico de OS: `work_order` e `work_order_history`.
>
> O comportamento de coletas recorrentes sobre o novo modelo (o que acontece
> com `work_order` e `work_order_history` quando uma OS já conhecida aparece
> numa coleta subsequente) será redefinido em um RF futuro de coleta recorrente,
> após RF-016 e RF-017 estarem implementados.
>
> **Decisões pendentes de RF-011 que permanecem abertas:**
> - DP-011.1 (detecção explícita de transição) — transferida para RF-017.
> - DP-011.2 (intervalo e comando da coleta recorrente) — aguarda RF futuro.
> - DP-011.3 (desempate de status entre coletas) — aguarda RF futuro.
>
> **Data de obsolescência:** 2026-08-31.

---

*Conteúdo original preservado abaixo para rastreabilidade histórica.*

---

## Objetivo (histórico)

Garantir que cada coleta recorrente mantivesse o estado atual de cada OS
atualizado e registrasse uma nova observação no histórico quando o status
observado mudasse, sem criar duplicatas nem perder o histórico anterior.

## Por que foi substituído

O modelo anterior assumia um snapshot de estado único por OS (upsert idempotente)
e observações imutáveis acumuladas por coleta. A decisão de 2026-08-31 separou
o dado bruto (append-only, sem mapeamento) do estado agnóstico (status mapeado,
upsert em `work_order`, append em `work_order_history`). Esse redesenho invalida
a estrutura de coleções e as regras de negócio sobre as quais RF-011 operava.
