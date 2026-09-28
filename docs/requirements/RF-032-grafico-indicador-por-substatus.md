# RF-032 - Gráfico de Indicador por SubStatus

Status: `Proposto`

**Data:** 2026-09-26

## História de Usuário

Como usuário do Office,
eu quero ver um gráfico de barras com o total de OS agrupado por
substatus, colorido por responsável,
para que eu identifique rapidamente onde estão concentradas as pendências
e quem deveria atuar em cada uma.

## Contexto e motivação

Depende de RF-031 (captura do `SubStatus` e catálogo de responsáveis).
Sem o substatus persistido e mapeado a um responsável, este gráfico não
tem dado para exibir.

## Objetivo

Adicionar um card ao Dashboard do Office com um gráfico de barras: eixo
das categorias é o substatus, altura da barra é a contagem de OS naquele
substatus, e a cor da barra reflete o responsável (RF-031.2).

## Escopo

### O que É RF-032

- Endpoint de contagem de OS por `SubStatus` (com o responsável
  resolvido junto, para colorir no frontend).
- Card no Dashboard do Office com gráfico de barras, uma cor por
  responsável (legenda visível).
- Escopo: apenas OS **Em Aberto** (RF-029) — OS terminal não tem
  substatus relevante para ação.

### O que NÃO é RF-032

- Interatividade avançada (drill-down por barra) — pode ser considerado
  em iteração futura, seguindo o padrão de "Pendências por motivo" já
  existente no painel de referência da cliente (clique filtra lista).
  Nesta primeira versão, exibição é suficiente.

## Requisitos funcionais

### RF-032.1 - Endpoint de contagem por substatus

Novo endpoint (padrão REST já usado em `WorkOrderEndpoints`) retornando,
para o tenant autenticado, a contagem de OS **Em Aberto** agrupada por
`SubStatus`, incluindo o responsável resolvido (RF-031.2) para cada
grupo. Grupos com `SubStatus` nulo ou não mapeado devem ser incluídos
como uma categoria "Sem substatus"/"Não mapeado", não omitidos.

### RF-032.2 - Card com gráfico de barras

O Dashboard do Office exibe um card com gráfico de barras horizontais ou
verticais, usando `apexcharts`/`react-apexcharts` (já presentes nas
dependências de `apps/office/package.json`, sem necessidade de nova
biblioteca), uma barra por substatus, ordenadas por contagem
decrescente, coloridas por responsável, com legenda de cores.

## Critérios de aceite

1. O gráfico exibe uma barra por substatus distinto observado entre as OS
   Em Aberto do tenant.
2. Barras de substatus com o mesmo responsável têm a mesma cor.
3. OS Em Aberto sem substatus aparecem agrupadas em uma categoria visível
   (não são omitidas da contagem total).
4. OS terminais nunca entram na contagem deste gráfico.
5. Usuário de um tenant não vê contagem de substatus de outro tenant.

## Pendências para validação

- Confirmar biblioteca de gráficos já usada (ou a ser adotada) no
  `apps/office`, para reaproveitar padrão visual existente em vez de
  introduzir uma nova dependência.
- Depende da resolução das pendências de RF-031 (catálogo de substatus).
