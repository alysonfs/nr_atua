# ADR-016 - Ramo multiagente de marca do ATUA

## Status

Accepted

## Contexto

O manifesto do ATUA define posicionamento, missao, visao e principios, mas o
workflow nao possuia agentes ou fontes de verdade para transformar esses
fundamentos em expressao estrategica, visual, verbal e de conteudo.

## Decisao

Criar os agentes `brand-strategist`, `brand-identity`, `brand-copywriter`,
`brand-content` e `brand-guardian`, todos subordinados operacionalmente ao
`orchestrator`. O strategist e a referencia de estrategia, sem poder de
delegacao. O ramo atende somente a marca ATUA e e acionado sob demanda.

O guardian revisa consultivamente apenas entregaveis de marca quando o
orchestrator solicitar. Essa revisao nao substitui QA nem bloqueia releases.
Identity trata materiais externos e diretrizes; UI permanece sob arquitetura e
frontend.

## Motivos

- Separar estrategia, execucao e revisao de marca sem diluir autoridades atuais.
- Preservar o orchestrator como ponto unico de delegacao e controle.
- Manter a complexidade organizacional proporcional a demandas de marca.

## Alternativas consideradas

- Criar apenas strategist e guardian: rejeitada pelo escopo aprovado de cinco
  papeis desde o inicio.
- Delegar os demais agentes ao strategist: rejeitada por conflitar com a
  coordenacao central dos protocolos.
- Tornar guardian um gate de toda feature ou release: rejeitada para preservar
  a autoridade do QA e o acionamento sob demanda.

## Consequencias

- Protocolos e perfil do orchestrator passam a reconhecer o ramo de marca.
- `docs/brand/` torna-se a fonte de verdade para os guias de marca.
- Mudancas de UI continuam dependendo de `software-architect` e
  `frontend-engineer`.

## Agentes envolvidos

- orchestrator
- brand-strategist
- brand-identity
- brand-copywriter
- brand-content
- brand-guardian
- documentation

## Data

2026-08-29

## Substitui

Nao aplicavel.
