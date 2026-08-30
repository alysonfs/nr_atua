---
name: aws-cost-monitoring
description: Consulta gastos AWS sob demanda usando AWS CLI, Cost Explorer e Budgets com comandos somente leitura. Use para responder quanto está sendo gasto, quais serviços mais custam e se o budget está em risco.
---

# Monitoramento de custos AWS via CLI

Use esta skill para responder perguntas sobre gastos AWS usando `aws-cli` de
forma segura, pontual e somente leitura.

## Pré-requisitos

* AWS CLI v2 instalado.
* Perfil AWS explícito configurado, por exemplo `moldato`.
* Permissões mínimas:
  * `sts:GetCallerIdentity`
  * `ce:GetCostAndUsage`
  * `ce:GetCostForecast`, quando houver previsão
  * `budgets:ViewBudget`, quando houver consulta de budgets
* Datas explícitas no formato `YYYY-MM-DD`.

Não registre access keys, secret keys, tokens, senhas ou secrets na resposta.

## Variáveis esperadas

Defina mentalmente ou no shell apenas para a sessão atual:

```bash
AWS_PROFILE=<profile>
START_DATE=<YYYY-MM-DD>
END_DATE=<YYYY-MM-DD>
```

`END_DATE` é exclusivo no Cost Explorer. Para consultar um único dia, use o dia
seguinte como `END_DATE`.

## Comandos permitidos

### 1. Verificar identidade da conta

```bash
aws sts get-caller-identity \
  --profile "$AWS_PROFILE" \
  --output json
```

Use o `Account` retornado para consultar budgets. Masque o Account ID na
resposta se não houver necessidade de expor o valor completo.

### 2. Consultar custo por serviço

```bash
aws ce get-cost-and-usage \
  --profile "$AWS_PROFILE" \
  --time-period Start="$START_DATE",End="$END_DATE" \
  --granularity DAILY \
  --metrics BlendedCost UnblendedCost \
  --group-by Type=DIMENSION,Key=SERVICE \
  --output json
```

Use esta consulta para responder:

* custo total no período;
* custo diário;
* serviços com maior custo;
* serviços inesperados;
* tendência contra limites documentados.

### 3. Consultar budgets

```bash
aws budgets describe-budgets \
  --profile "$AWS_PROFILE" \
  --account-id "$AWS_ACCOUNT_ID" \
  --output json
```

Se a permissão estiver ausente, reporte `BLOCKED` para a verificação de budget
e informe que a permissão necessária é `budgets:ViewBudget`.

### 4. Consultar previsão de custo

Use somente quando o usuário pedir tendência ou risco de fechamento do mês:

```bash
aws ce get-cost-forecast \
  --profile "$AWS_PROFILE" \
  --time-period Start="$FORECAST_START",End="$FORECAST_END" \
  --metric BLENDED_COST \
  --granularity MONTHLY \
  --output json
```

## Comandos proibidos

Não execute:

* comandos de criação, alteração ou remoção;
* comandos com `create`, `put`, `update`, `delete`, `start` ou `stop`;
* loops como `while true` ou consultas recorrentes;
* provisionamento de recursos;
* alteração de budgets, alarmes, IAM, Cost Explorer ou infraestrutura.

## Interpretação obrigatória

Ao responder, compare os valores com os documentos do projeto:

* `docs/decisions/COST_APPROVAL_CHECKLIST.md`
* `docs/decisions/PROVISIONING_COST_CONTROL.md`
* `docs/decisions/ADR-009-aws-cost-analysis-mvp.md`
* `docs/decisions/ADR-010-aws-mvp-free-tier-optimization.md`

Se houver conflito entre limites documentados, reporte o conflito em vez de
assumir um limite silenciosamente.

## Formato de resposta

Responda com:

```text
Status:
<OK | ALERTA | BLOCKED>

Período:
<START_DATE até END_DATE exclusivo>

Perfil/conta:
<profile e conta mascarada quando apropriado>

Custo total:
<valor e moeda>

Top serviços:
1. <serviço> - <valor>
2. <serviço> - <valor>
3. <serviço> - <valor>

Budget:
<status ou motivo do bloqueio>

Comparação com limites:
<resultado>

Riscos:
<riscos>

Próxima ação:
<ação recomendada>
```

## Tratamento de bloqueios

Use `BLOCKED` quando:

* o perfil AWS não foi informado;
* o período não foi informado;
* o AWS CLI não está instalado;
* a conta não possui permissão de Cost Explorer ou Budgets;
* o Cost Explorer ainda não possui dados para o período solicitado.

Explique a informação ou permissão necessária sem sugerir bypass de segurança.
