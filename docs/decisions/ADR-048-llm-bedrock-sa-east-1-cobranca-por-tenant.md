# ADR-048 - LLM da Triagem no Amazon Bedrock (sa-east-1) com Cobrança por Tenant

## Status

**Accepted** — decisão do Product Owner em 2026-10-22. Não bloqueia a
implementação: hoje existe um único tenant.

## Contexto

A especificação [triagem-llm-conducao.md](../features/triagem-llm-conducao.md)
deixou o fornecedor da LLM em aberto. As opções eram OpenAI, Anthropic e
Amazon Bedrock. A escolha afeta onde os dados são processados (LGPD), o
contrato com o fornecedor e o custo.

## Decisão

- A LLM da triagem usa o **Amazon Bedrock na região `sa-east-1`**, na conta
  AWS do ATUA.
- A Master API chama o Bedrock através de um adaptador por trás da
  interface que não depende de fornecedor (`ITriagemLlmClient`). Trocar de
  modelo ou de fornecedor continua sendo questão de configuração e de
  adaptador.
- O custo de inferência será **cobrado por tenant**. Cada chamada registra o
  tenant, a triagem, o modelo e os tokens de entrada e saída. Esse registro é
  a base da cobrança e do orçamento por tenant.
- Neste momento só há um tenant, então o rateio, a fatura e o repasse **não
  bloqueiam** a implementação. O registro de uso por tenant é obrigatório
  desde a primeira chamada.

## Consequências

- Os dados ficam na conta AWS e na região brasileira. Não é preciso um
  contrato separado com um fornecedor externo de LLM. Continuam valendo os
  termos de serviço da AWS e o parecer LGPD.
- A role da API precisará da permissão `bedrock:InvokeModel` (ou
  `bedrock:Converse`) restrita aos modelos escolhidos, concedida via CDK.
- O custo aparece na fatura da AWS. A atribuição por tenant vem do registro
  da aplicação, não das tags da AWS.

## Verificações antes de implementar (não bloqueiam esta decisão)

- Confirmar que o plano da conta (hoje FREE) permite usar o Bedrock e
  habilitar acesso aos modelos (`aws freetier get-account-plan-state`).
- Escolher um modelo disponível **nativamente** em `sa-east-1`. Não usar
  inferência entre regiões (cross-region inference), que pode processar os
  dados fora do Brasil, sem decisão explícita.
- Confirmar que o modelo aceita imagens, necessárias para verificar se a
  nota fiscal e a etiqueta estão legíveis, conforme
  [triagem-llm-identidade-tom.md](../features/triagem-llm-identidade-tom.md).
- Estimar o custo por triagem e definir o orçamento mensal por tenant.

## Adendo 2026-10-23: modelo escolhido e inferência global

Verificação feita em `sa-east-1` com leituras somente, mais uma chamada mínima
à Converse API.

Resultados:

- O plano da conta é FREE, ativo, e a chamada ao Bedrock funcionou.
- O Claude Haiku 4.5 (`anthropic.claude-haiku-4-5-20251001-v1:0`) está
  disponível e autorizado, mas **apenas** pelo perfil de inferência
  `global.anthropic.claude-haiku-4-5-20251001-v1:0`. Não há modo sob demanda
  na região.
- O Claude 3 Haiku foi bloqueado por ser modelo legado. O Amazon Nova não
  existe em `sa-east-1`.
- Modelos sob demanda que rodam só na região e aceitam imagem: Qwen3-VL 235B,
  Mistral Large 3, Ministral 3, Gemma 3 e Kimi K2.5.

**Decisão do PO:** usar `global.anthropic.claude-haiku-4-5-20251001-v1:0`,
aceitando que a inferência seja processada fora do Brasil. A chamada continua
saindo de `sa-east-1`, mas o perfil global pode encaminhar a inferência para
qualquer região comercial da AWS.

Isso substitui, apenas para a escolha do modelo, a verificação acima que
proibia a inferência entre regiões. Consequências:

- O parecer LGPD precisa registrar a transferência internacional e a base
  legal, e a minimização de dados do
  [triagem-llm-conducao.md](../features/triagem-llm-conducao.md) passa a ser
  obrigatória.
- O modelo é configurável (`TriagemLlm:ModelId`). Trocar por um modelo que
  roda só em `sa-east-1` não exige mudança de código.
- A permissão IAM deve cobrir o perfil de inferência e os modelos de destino
  dele.

## Pendências futuras

- Modelo de cobrança para o tenant: repasse direto, franquia ou faixa.
- Relatório de consumo por tenant no Office.
