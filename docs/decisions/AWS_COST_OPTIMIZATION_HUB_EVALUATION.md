# Avaliação — AWS Cost Optimization Hub para o MVP ATUA

**Data:** 2026-08-30  
**Status:** Adiado; Cost Optimization Hub e Agent Toolkit não serão
habilitados/instalados nesta fase.

## Contexto

O MVP possui infraestrutura AWS de baixo custo e ciclo de vida efêmero para
compute. A documentação de infraestrutura estima custo residual de
aproximadamente US$ 1,20/mês quando destruído e US$ 1,70–3,21/mês quando
ligado dentro do Free Tier. Duas instâncias `t3.micro` ligadas continuamente
podem exceder o Free Tier combinado e adicionar cerca de US$ 8–9/mês.

O usuário informou a criação de budget mensal de US$ 5, com alerta em 80% do
custo previsto, e as tags `company=moldato` e `project=atua`. Essas
informações não foram consultadas na AWS nesta avaliação.

## Avaliação

O AWS Cost Optimization Hub consolida recomendações de otimização de custo e
uso de recursos. Ele pode ser útil para revisar recursos persistentes e
identificar desperdício após haver uso suficiente para gerar recomendações.
Sua habilitação não substitui Budget, Cost Explorer, tags de alocação nem o
processo de `up`/`down`.

Limites relevantes:

* recomendações dependem de telemetria e de histórico de uso; recursos recém
  provisionados ou usados por pouco tempo podem não gerar recomendações úteis;
* não é mecanismo de bloqueio, quota ou prevenção de gasto em tempo real;
* recomendações devem ser revisadas por pessoa responsável antes de qualquer
  alteração, pois uma otimização pode reduzir disponibilidade ou capacidade;
* eventuais economias propostas não incluem o custo operacional ou o risco de
  mudar recursos.

## Decisão operacional recomendada

**Não habilitar como pré-requisito de provisionamento.** O usuário confirmou o
adiamento do Cost Optimization Hub e do Agent Toolkit. Para o MVP descartável,
manter o Budget de US$ 5/mês (alerta de forecast em 80%) e o fluxo explícito de
desligamento como controles primários. Reavaliar o Hub somente após uso
mensurável ou quando recursos persistentes/contínuos justifiquem a revisão.

Caso seja habilitado posteriormente, tratar as recomendações como entrada de
análise do `aws-architect`; não executar remediação automática.

## Perfil AWS

O usuário confirmou o perfil explícito `moldato` para este projeto. Todo
comando AWS/CDK operacional deve usar `AWS_PROFILE=moldato`; não usar o perfil
default.

## Referências

* `docs/decisions/ADR-009-aws-cost-analysis-mvp.md`
* `docs/decisions/ADR-010-aws-mvp-free-tier-optimization.md`
* `docs/decisions/ADR-012-mongodb-atlas-free-tier.md`
* `docs/decisions/ADR-013-agente-copilot-monitoramento-gastos-aws.md`
* `docs/architecture/aws-setup-checklist.md`
* AWS Cost Optimization Hub: https://docs.aws.amazon.com/cost-management/latest/userguide/coh-what-is.html
