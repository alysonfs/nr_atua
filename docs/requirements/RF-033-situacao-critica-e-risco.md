# RF-033 - Situação Crítica e Risco de Criticidade

Status: `Proposto`

**Data:** 2026-09-26

## História de Usuário

Como usuário do Office,
eu quero saber quantas OS estão em situação crítica (abertas há mais de 5
dias sem atualização) e quais estão em risco de se tornarem críticas em
breve,
para que eu possa agir preventivamente, antes que o problema se agrave.

## Contexto e motivação

Este requisito depende de RF-029 (agrupamento "Em Aberto") para a
definição de "aberta", e usa `WorkOrder.UpdatedAt` (decidido — ver
RF-033.1) como referência de "última atualização" — uma métrica
diferente da usada em RF-027 (que mede tempo desde a **abertura**, não
desde a última atualização).

Isso é o card que aparece no painel externo da cliente como "Críticas —
mais de 5 dias sem atualização". A cliente pede, além da contagem atual
de críticas, uma visão de **risco** — OS que ainda não são críticas, mas
estão perto de se tornarem (ex.: 3-4 dias sem atualização).

## Objetivo

Definir e expor a contagem de OS em Situação Crítica, e um indicador de
Risco de Criticidade para OS que ainda não são críticas mas estão perto do
limite.

## Escopo

### O que É RF-033

- Definição de "Situação Crítica": OS **Em Aberto** (RF-029) sem
  atualização há mais de 5 dias.
- Definição de "Risco de Criticidade": OS **Em Aberto** ainda não
  críticas, mas próximas do limite — **decidido: janela de 4 dias sem
  atualização** (sujeita a validação com a cliente, ver Pendências).
- Endpoint e card(s) no Dashboard do Office para as duas contagens.

### O que NÃO é RF-033

- Alteração da definição de "última atualização" usada por outras
  métricas já implementadas (RF-017, RF-027) — este RF apenas consome o
  campo existente.
- Alertas proativos (e-mail/push) quando uma OS entra em risco — trabalho
  futuro.

## Requisitos funcionais

### RF-033.1 - Definição de "última atualização" para este indicador

**Decidido:** a referência de "sem atualização" é `WorkOrder.UpdatedAt`
(instante em que o ATUA observou a última mudança relevante da OS), não
`ProviderUpdatedAt`. Justificativa: `UpdatedAt` é a fonte de verdade
interna do ATUA (RF-017) e não depende do provedor reenviar o campo de
data — evita "cegueira" caso o provedor atrase ou omita
`lastUpdateDate`.

### RF-033.2 - Situação Crítica

Conta OS **Em Aberto** (RF-029) cuja última atualização (RF-033.1) foi há
mais de 5 dias.

### RF-033.3 - Risco de Criticidade

Conta OS **Em Aberto** cuja última atualização foi há mais de 4 dias
corridos e menos de 5 (janela sugerida pelo `product-analyst`: 1 dia de
margem antes do limite crítico de 5 — sujeita a validação final com a
cliente, ver Pendências).

### RF-033.4 - Exibição no Dashboard do Office

Cards para as duas contagens (RF-033.2 e RF-033.3), com navegação para
lista filtrada de OS, seguindo o padrão visual já estabelecido.

## Critérios de aceite

1. Uma OS Em Aberto sem atualização há 6 dias é contada como "Situação
   Crítica".
2. Uma OS Em Aberto sem atualização há 4 dias é contada como "Risco de
   Criticidade", não como "Crítica".
3. Uma OS terminal nunca é contada em nenhuma das duas métricas,
   independentemente de há quanto tempo está sem atualização.
4. Os dois cards aparecem no Dashboard com contagens corretas para o
   tenant autenticado.

## Pendências para validação

- Confirmar com a cliente se a janela de 4 dias para "Risco de
  Criticidade" (RF-033.3) é adequada, ou se prefere outro valor —
  é uma sugestão de produto, não um dado técnico, e pode ser ajustada
  sem impacto arquitetural (constante/configuração simples).
- Depende da resolução de RF-029 (lista de status "Em Aberto").
