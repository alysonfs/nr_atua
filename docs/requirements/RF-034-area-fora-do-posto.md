# RF-034 - Área "Fora do Posto"

Status: `Fora do planejamento atual` (retirado do backlog de
implementação em 2026-09-26, a pedido do orchestrator/cliente — ver
Decisões abaixo)

**Data:** 2026-09-26

## Decisões (2026-09-26)

Este RF foi **retirado do planejamento atual**. Não deve ser incluído em
nenhuma rodada de arquitetura/implementação junto com RF-028 a RF-033.
Fica registrado como referência caso a cliente retome o assunto no
futuro, mas não é um compromisso de entrega.

## História de Usuário

Como usuário do Office,
eu quero uma área dedicada a OS que estão em dependência do provedor (não
do Posto Autorizado),
para que eu possa acompanhar separadamente o que depende de terceiros
(Midea - Callcenter, Planejamento, Jurídico, Correios, Suporte Técnico)
do que depende da nossa própria operação.

## Contexto e motivação

A cliente sinalizou explicitamente que "ainda vamos falar sobre isso" —
este RF documenta o que já foi dito e propõe indicadores iniciais, mas
**não deve ser implementado sem uma rodada adicional de alinhamento**,
já que o escopo completo ainda não foi definido pela cliente.

Esta área depende diretamente de RF-031 (substatus e responsável): "Fora
do Posto" é, por definição, o conjunto de OS cujo responsável do
substatus atual não é "Posto Autorizado".

## Objetivo (proposto)

Criar uma nova rota e item de menu no Office, "Fora do Posto", reunindo
indicadores e listas de OS cujo responsável atual é uma área da Midea
(Callcenter, Planejamento, Jurídico, Correios, Suporte Técnico) — o
espelho complementar do card "Posto Autorizado" que já existe no painel
de referência da cliente.

## Escopo

### O que É RF-034 (nesta fase)

- Nova rota (`/fora-do-posto` ou equivalente) e item de menu no Office.
- Reaproveitamento da definição de responsável por substatus (RF-031).
- Proposta inicial de indicadores (RF-034.1), sujeita a validação.

### O que NÃO é RF-034 (ainda)

- Qualquer decisão final de indicadores, layout ou fluxo — depende de
  conversa adicional com a cliente, explicitamente meniconada por ela.
- Ações/automação por área responsável (ex.: encaminhar para
  "Midea - Jurídico") — apenas visão consolidada nesta fase.

## Requisitos funcionais (proposta sujeita a validação)

### RF-034.1 - Indicadores sugeridos para a área "Fora do Posto"

Sugestões a validar com a cliente antes de codificar:

1. **Total de OS Fora do Posto**, com quebra por responsável (mesmo
   padrão do card "Fora do posto" / "Posto autorizado" do painel de
   referência da cliente, que já mostra 175 fora do posto vs. 8 no
   posto).
2. **Tempo médio parado por responsável** — quanto tempo, em média, uma
   OS fica sob responsabilidade de cada área da Midea antes de retornar
   ao Posto Autorizado (usa `SubStatus`/histórico de mudança, RF-031 +
   RF-017/`WorkOrderHistory`).
3. **OS críticas dentro de "Fora do Posto"** — cruzamento com RF-033
   (situação crítica), para identificar quais pendências de terceiros já
   estão atrasadas.
4. **Lista navegável de OS**, filtrável por responsável (Callcenter,
   Planejamento, Jurídico, Correios, Suporte Técnico).

### RF-034.2 - Rota e menu

Nova rota no Office e item correspondente no menu de navegação principal,
visível para os papéis de usuário já autorizados a ver o Dashboard
(RF-021).

## Critérios de aceite

_A definir após validação do escopo com a cliente — não codificar
critérios de aceite definitivos enquanto RF-034.1 não estiver confirmado._

## Pendências para validação

- **Bloqueante:** aguardar retorno da cliente sobre quais indicadores
  específicos ela quer nesta área antes de iniciar arquitetura/
  implementação.
- Depende de RF-031 (substatus e responsável) estar implementado.
- Avaliar, junto ao `software-architect`, se esta é uma nova página
  isolada ou uma variação filtrada do Dashboard existente (decisão de
  arquitetura, não de produto).
