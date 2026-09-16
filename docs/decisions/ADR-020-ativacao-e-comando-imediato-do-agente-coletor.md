# ADR-020 - Ativação e comando imediato do Agente Coletor

## Status

Accepted

## Contexto

RF-008 requer que o Agente Coletor comece desativado, possa ser ativado no
Office após validação bem-sucedida das credenciais e solicite uma coleta
imediata. ADR-015 determina que o Trial é avaliado exclusivamente pela Master
API; ADR-017 define a credencial interna do futuro coletor; e ADR-018 propõe a
integração e o estado `Succeeded` das credenciais iService.

O produto também determinou que `OWNER` e `ADMIN` podem ativar e desativar, que
a desativação manual cancela o comando pendente, que ativação exige Trial
elegível e credencial atual validada, e que toda perda dessas condições desativa
o agente e cancela pendências. Esta decisão define apenas o contrato e os
limites locais da Master API; não cria Worker, AWS, Playwright ou acesso ao
iService.

## Decisão

### Limite e identidade

`Integration` continua sendo a fronteira de configuração do coletor: ela já
pertence a um `Tenant` e a um `IntegrationProvider` (ADR-005/ADR-018). Toda
consulta e toda chave de unicidade abaixo são limitadas a `integrationId`; a
Master API sempre confirma que seu `tenantId` é o da rota e que o usuário possui
membership ativo. Nenhum identificador fornecido pelo browser é uma autorização.

São acrescentadas, no módulo `Integrations/CollectorControl`, as entidades
transacionais PostgreSQL:

* `CollectorActivation`: uma por integração (FK única para `Integration`), com
  `Status` (`Inactive`, `Active`), `ActivatedAtUtc`, `DeactivatedAtUtc`,
  `DeactivationReason` (`Manual`, `TrialIneligible`, `CredentialNotValidated`)
  e token de concorrência. Ela expressa a intenção operacional atual, não uma
  sessão do iService.
* `ImmediateCollectionCommand`: comando durável, com `Id`, `IntegrationId`,
  `TenantId` de consistência/auditoria, `ProviderKey`, `Status` (`Pending`,
  `Claimed`, `Succeeded`, `Failed`, `Cancelled`), `RequestedAtUtc`,
  `ClaimedAtUtc`, `CompletedAtUtc`, `CancellationReason`, `AttemptCount` e
  token de concorrência. O payload do comando não contém credencial, cookie,
  URL privada nem segredo.
* `CollectorControlIdempotency`: registro de requisição de mutação, com
  `IntegrationId`, operação (`Activate` ou `Deactivate`), chave opaca enviada
  pelo cliente, hash da requisição, resposta final e expiração. A unicidade é
  `(IntegrationId, Operation, IdempotencyKey)`. Reusar uma chave com conteúdo
  diferente devolve conflito, sem executar nova mutação.

Há ainda um índice único parcial para no máximo um
`ImmediateCollectionCommand` em `Pending` por `IntegrationId`. O índice, e não
somente uma verificação prévia em memória, é a garantia contra ativações
concorrentes.

### Máquina de estados e transação

* `Inactive --activate elegível--> Active`: na mesma transação cria exatamente
  um comando `Pending` e grava a idempotência.
* `Active --deactivate manualmente--> Inactive`: na mesma transação cancela o
  comando ainda `Pending` e grava a idempotência.
* `Active --perda de Trial ou validação--> Inactive`: na mesma transação cancela
  o comando ainda `Pending`, com a razão aplicável.
* `Pending --claim atômico--> Claimed --conclusão--> Succeeded|Failed`.
* `Pending --cancelamento--> Cancelled`. `Cancelled` é terminal e nunca volta a
  `Pending`; somente uma nova ativação explícita, após o estado estar
  `Inactive` e elegível, pode criar novo comando.

O serviço de aplicação `CollectorActivationService` bloqueia a linha de
ativação/integração (ou usa concorrência otimista com repetição limitada),
reavalia as pré-condições e grava ativação, comando e idempotência em uma única
transação. Uma colisão no índice parcial é tratada como releitura do estado
atual/resposta idempotente, não como criação de outro comando.

Um comando já `Claimed` não pode ser desfeito à força: a desativação cancela
somente a pendência, como definido pelo produto. O contrato do Worker exige
nova consulta de elegibilidade imediatamente antes de qualquer acesso ao
provedor; conclusão posterior de trabalho inelegível é registrada como
`Cancelled`/não executada pela Master API, nunca como coleta válida.

### Elegibilidade centralizada

`ICollectorEligibilityEvaluator` é um componente interno da Master API. Ele
consulta, sob a mesma fronteira transacional, a regra de Trial de ADR-015 e a
credencial atual da integração de ADR-018:

```text
eligible = trial ativo para o tenant
           AND ServiceCredentialProfile.ValidationStatus == Succeeded
```

Os endpoints Office chamam esse componente diretamente; a Master API não chama
o próprio endpoint HTTP interno. O endpoint interno existente
`GET /api/internal/collector/eligibility` também o chama e continua retornando
apenas `eligible` e `evaluatedAtUtc`.

Toda alteração que possa tornar a expressão falsa (expiração/alteração de
Trial, substituição de credencial, validação `Failed`) deve chamar
`CollectorActivationService.ReconcileEligibility` na transação que persiste a
alteração. O ciclo de expiração do Trial também deve invocá-lo para ativações
existentes. Até que essa reconciliação seja implementada, o Worker permanece
proibido de executar: a consulta interna de elegibilidade é uma segunda barreira
e impede execução, mas não substitui o cancelamento durável exigido por RF-008.

### Contrato Office

As rotas dependem de sessão de browser e de membership ativo para `{tenantId}`.
Leitura é permitida a `OWNER` e `ADMIN`; mutações de ativação e desativação
também são permitidas a ambos, conforme decisão de produto.

```text
GET    /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation
PUT    /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation
DELETE /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation
```

`PUT` e `DELETE` exigem `Idempotency-Key` opaco. `PUT` não aceita corpo e
significa “ativar”; `DELETE` significa “desativar”. Ambos devolvem o estado
atual sanitizado e são seguros para repetição com a mesma chave. Ativar sem
Trial elegível ou sem `ValidationStatus == Succeeded` devolve `409
activation_not_eligible`, sem criar comando. Recursos fora do tenant ou sem
membership não são revelados indevidamente.

DTO de leitura/mutação:

```json
{
  "status": "Inactive|Active",
  "canActivate": true,
  "activationBlockReason": "None|TrialIneligible|CredentialsNotValidated",
  "activatedAtUtc": "2026-08-30T13:00:00Z",
  "deactivatedAtUtc": null,
  "lastImmediateCommand": {
    "commandId": "uuid",
    "status": "Pending|Claimed|Succeeded|Failed|Cancelled",
    "requestedAtUtc": "2026-08-30T13:00:00Z"
  }
}
```

Todos os instantes são UTC. O DTO nunca contém credenciais cifradas ou em
claro, cookie/sessão CAS, token interno, URL sensível, detalhes brutos de falha
do provedor ou dados de Trial além da decisão necessária.

### Contrato para Worker futuro

Quando um Worker for aprovado, ele receberá uma credencial opaca vinculada a
tenant, integração e provedor. Esta proposta amplia ADR-017 com os escopos
mínimos `collector.command.claim` e `collector.command.complete`, além de
`collector.eligibility.read`; não usa JWT de usuário nem acesso direto a banco.

```text
POST /api/internal/collector/commands/claim
POST /api/internal/collector/commands/{commandId}/complete
GET  /api/internal/collector/eligibility
```

`claim` deriva o contexto exclusivamente da credencial, reivindica de modo
atômico no máximo o comando `Pending` daquele contexto e retorna somente
`commandId`, `commandType: ImmediateCollection`, `integrationId`,
`providerKey` e `requestedAtUtc`; se não houver comando, retorna sem trabalho.
`complete` aceita `Succeeded`, `Failed` ou `Cancelled` e timestamps, mas não
payload de OS, segredo ou resposta bruta do iService. Antes de executar, o
Worker consulta elegibilidade; se negativa, completa como `Cancelled` sem
acessar o provedor. Como a entrega de credenciais ao Worker ainda não foi
definida, estes contratos deliberadamente não transportam segredos.

## Alternativas consideradas

* Fazer o Worker consultar PostgreSQL/Trial ou decidir elegibilidade localmente:
  rejeitada por ADR-015 e ADR-017, isolamento e acoplamento.
* Criar comando apenas em memória ou permitir mais de um pendente: rejeitada
  porque reinícios e chamadas concorrentes podem duplicar a coleta inicial.
* Publicar elegibilidade detalhada para o browser ou Worker: rejeitada por
  expor dados de plano/integração sem necessidade.
* Introduzir fila, Redis ou infraestrutura nova agora: rejeitada; o comando
  transacional e o polling interno formam o menor contrato durável. A escolha de
  transporte futuro é decisão separada com o aws-architect.

## Consequências

Uma migration PostgreSQL, endpoints, autorização, serviço de elegibilidade e
testes serão necessários após aprovação. Esta ADR altera/estende o escopo de
credencial interna de ADR-017 e depende da aprovação/implementação de
`ServiceCredentialProfile` proposta na ADR-018. Não implementa integração real
com iService nem o consumidor Worker.

## Agentes envolvidos

* software-architect (Sérgio): contrato e limites.
* product-analyst: regras de ativação já fornecidas.
* backend-engineer: implementação local posterior.
* qa-engineer: validação de concorrência, autorização e regressão.
* aws-architect: somente se/quanto a entrega de credencial ou transporte do
  Worker exigir infraestrutura.

## Data

2026-08-30

## Substitui

Não aplicável. Complementa ADR-003, ADR-015 e ADR-018; propõe ampliação
pontual da credencial interna definida na ADR-017.
