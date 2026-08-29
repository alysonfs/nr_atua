# ADR-015 - Ciclo de vida do Trial e preferência de fuso horário

## Status

Accepted

## Contexto

O Trial é iniciado pela confirmação de e-mail, antes da criação do Tenant na
primeira configuração de integração. A plataforma precisa calcular sua validade
de modo determinístico, preservar o acesso e os dados após a expiração e impedir
que um coletor execute ou seja ativado quando o Trial não estiver elegível.

Também é necessário definir onde a preferência de fuso horário é mantida e como
resolver o fuso efetivo para apresentação de datas. O Agente Coletor não pode
depender de acesso direto à persistência de Trial ou Tenant para tomar sua
decisão de execução.

## Decisão

### Ciclo de vida do Trial

- O Trial inicia no instante da confirmação de e-mail.
- O vencimento é `00:00:00 UTC` da data UTC da confirmação acrescida de sete
  dias.
- O Trial está ativo somente quando `nowUtc < expiresAtUtc`.
- A expiração não remove login, acesso ao Office, dados nem configurações.
- Um Trial expirado bloqueia a ativação do Agente Coletor e a execução de
  coletas.

O Trial iniciado para o usuário antes da criação do Tenant continua com a mesma
validade quando associado ao Tenant, sem reinício do prazo.

### Preferência de fuso horário

- `Tenant` persiste o fuso horário no formato IANA.
- `User` não persiste fuso horário no MVP.
- `AuthSession` pode manter um override temporário de fuso horário.
- O fuso efetivo deve ser resolvido na seguinte ordem: override da sessão,
  fuso do Tenant, `America/Sao_Paulo` para `pt-BR`, e UTC.

Datas de validade e a elegibilidade do Trial permanecem calculadas em UTC; o
fuso efetivo é aplicável à preferência e à apresentação de datas.

### Elegibilidade interna do coletor

A Master API expõe um endpoint interno de elegibilidade para que o Agente
Coletor consulte se pode ativar ou executar coleta para o contexto autorizado.
Esse endpoint aplica a regra de Trial ativo definida nesta ADR.

O Agente Coletor é proibido de acessar diretamente a persistência de `Trial` ou
`Tenant`, inclusive para consultar validade, plano ou fuso horário. A decisão de
elegibilidade deve ocorrer exclusivamente por esse contrato interno da Master
API.

## Motivos

- O cálculo integral em UTC elimina ambiguidades de horário local, mudança de
  data e horário de verão no vencimento.
- A expiração preserva o contexto do cliente para consulta e retomada posterior,
  sem permitir processamento de coleta fora da validade.
- A precedência permite ajuste temporário por sessão sem criar uma preferência
  persistida em `User` no MVP.
- Centralizar a elegibilidade na Master API mantém a fronteira de dados e as
  regras de plano fora do Agente Coletor.

## Alternativas consideradas

### Vencimento após sete períodos de 24 horas a partir da confirmação

Rejeitada porque não representa o vencimento no início da data UTC definida para
o sétimo dia.

### Persistir o fuso horário em `User`

Rejeitada no MVP. A preferência persistida pertence ao Tenant; a necessidade de
preferências persistidas por usuário poderá ser avaliada em decisão posterior.

### Permitir que o Agente Coletor consulte diretamente Trial e Tenant

Rejeitada porque acopla o Worker à persistência e contorna a fronteira de
autorização e regra de negócio da Master API.

### Desativar login, Office ou remover dados ao expirar o Trial

Rejeitada porque a expiração deve bloquear somente ativação e coleta, preservando
acesso e informações existentes.

## Consequências

- A implementação deve persistir o instante de expiração do Trial e avaliá-lo
  comparando-o com `nowUtc`.
- Fluxos de ativação e execução de coleta devem depender da resposta do endpoint
  interno de elegibilidade.
- A Master API passa a ser a única responsável por consultar a persistência de
  Trial e Tenant para fins de elegibilidade do coletor.
- O Tenant deve armazenar um identificador IANA de fuso horário; a sessão pode
  fornecer override não persistente.
- Interfaces devem manter dados e configurações acessíveis após a expiração,
  mas não oferecer ativação nem permitir nova coleta enquanto o Trial estiver
  expirado.
- A definição de preferências persistidas por usuário permanece fora do escopo
  do MVP.

## Agentes envolvidos

- product-analyst: regras aprovadas de Trial e comportamento após expiração.
- software-architect (Sérgio): ciclo de vida técnico, precedência de fuso e
  contrato interno de elegibilidade.
- backend-engineer: implementação da Master API, persistência e integração do
  Agente Coletor pelo contrato interno.
- qa-engineer: validação dos limites de vencimento, bloqueios de coleta e
  precedência de fuso.
- documentation: registro rastreável da decisão.

## Data

2026-08-29

## Substitui

Não aplicável.
