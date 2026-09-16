# ADR-013 - Agente Copilot para monitoramento sob demanda de gastos AWS

## Status

Accepted

## Contexto

O projeto possui orçamento AWS restrito e decisões anteriores que exigem
controle rigoroso de custos. Consultas de custo precisam ser reproduzíveis,
seguras e alinhadas aos documentos existentes, sem executar provisionamento,
alterações de budget ou monitoramento contínuo em loop.

## Decisão

Criar um agente Copilot `aws-cost-monitor` e uma skill
`aws-cost-monitoring` para responder perguntas de gastos AWS sob demanda via
`aws-cli`.

A skill deve permitir apenas comandos de leitura, principalmente:

* `aws sts get-caller-identity`;
* `aws ce get-cost-and-usage`;
* `aws ce get-cost-forecast`;
* `aws budgets describe-budgets`.

O agente `aws-architect` deve referenciar a skill e o agente de monitoramento
para perguntas recorrentes sobre custo, budget, tendência e risco de estouro
do orçamento.

## Motivos

* Centralizar o procedimento de consulta de custos AWS.
* Evitar comandos mutáveis ou provisionamento acidental durante análises de
  custo.
* Dar rastreabilidade a perguntas recorrentes sobre gasto AWS.
* Manter o `aws-architect` responsável por decisões de infraestrutura e custo,
  com apoio operacional de um agente especializado.

## Alternativas consideradas

* Usar apenas o console AWS manualmente: rejeitado por reduzir
  reprodutibilidade e rastreabilidade.
* Criar automação agendada: rejeitado neste momento porque o escopo aprovado é
  consulta sob demanda por agente Copilot.
* Deixar o procedimento apenas na conversa: rejeitado por contrariar a regra de
  registrar decisões importantes no projeto.

## Consequências

* Perguntas de custo passam a ter um caminho operacional claro.
* O agente de custo precisa de permissões AWS somente leitura para Cost
  Explorer e Budgets.
* A ausência de perfil, período ou permissão deve ser tratada como bloqueio.
* A solução não substitui alertas nativos da AWS nem jobs agendados futuros.

## Agentes envolvidos

* Otto/orchestrator
* Ari/aws-architect
* Cora/aws-cost-monitor
* Queiroz/qa-engineer
* Dora/documentation

## Data

2026-08-29

## Substitui

Não aplicável.
