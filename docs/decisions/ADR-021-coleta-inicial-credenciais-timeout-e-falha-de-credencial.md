# ADR-021 - Coleta Inicial: Entrega de Credenciais ao Worker, Timeout de Re-claim e Falha por Credencial Rejeitada

## Status

Proposed

## Contexto

RF-009 (Coleta Inicial) é o gate onde o Worker (`apps/collector`, rodando em
EC2 separada) passa a ter acesso real ao iService. Três decisões arquiteturais
bloqueiam a implementação e foram delegadas ao `software-architect` em
RF-009:

- **D9**: como o Worker recebe as credenciais decifradas do iService em
  runtime, sem que apareçam em logs, eventos ou payloads.
- **D2**: o que acontece quando um Worker morre com o comando em `Claimed`
  (timeout de re-claim).
- **D4**: se e como uma conclusão `Failed` por credencial rejeitada deve
  desativar o Agente.

Esta ADR também consolida o contrato concreto dos endpoints `claim` e
`complete` (esboçados em ADR-020 mas não implementados) e registra
contradições identificadas entre documentação e código.

### Contexto técnico relevante

- `ImmediateCollectionCommand` já existe no PostgreSQL com
  `EImmediateCollectionCommandStatus`: `Pending`, `Claimed`, `Succeeded`,
  `Failed`, `Cancelled` — os cinco estados já estão presentes no código
  (commit 5ad77fe, `EImmediateCollectionCommandStatus.cs`). Não é necessário
  acrescentar estados.
- O índice único parcial impede mais de um comando `Pending` por
  `IntegrationId`. Um comando `Claimed` órfão bloqueia a integração
  indefinidamente, pois `Pending` não pode ser criado enquanto `Claimed`
  existir para a mesma integração.
- `CollectorActivationService.ReconcileEligibilityAsync` está **implementada**
  no commit 5ad77fe (`CollectorActivationService.cs`, linhas 163–186) e já é
  acionada por dois gatilhos existentes: troca de credencial
  (`IServiceCredentialService.SaveWithReconciliationAsync`) e validação
  `Failed` (`IServiceCredentialValidationService.ValidateAsync`). O texto de
  ADR-020 e RF-008 que a classificava como "prioridade posterior" ficou
  desatualizado quando o método foi implementado no fechamento de RF-008; o
  código é a evidência vigente. O que RF-009 acrescenta é apenas um **novo
  gatilho**: `complete(outcome=Failed, failureReason=CredentialRejected)`.
- O Worker é hoje um stub (`Worker.cs`) com `Task.Delay(1000)` e nenhuma
  lógica de polling ou consumo de comandos.
- MongoDB Atlas Free Tier está decidido (ADR-012) e há um secret
  `atua/mongodb-atlas` provisionado. A decisão de persistir dados de OS no
  MongoDB é consistente entre ADR-003, ADR-012 e RF-009 — não há contradição
  aqui.
- A credencial do iService está cifrada por AES-256-GCM com chave de dados
  por integração, chave de dados cifrada por AWS KMS (ADR-004, ADR-018,
  entidade `IServiceCredential`). O Master API já possui `ICredentialCipher`
  ou equivalente para decifrar.

## Decisão

### D9 — Entrega de credenciais ao Worker

**Decisão: endpoint dedicado na Master API com credencial de uso único e
curta duração (opção a).**

#### Mecanismo

1. O Worker, ao realizar o `claim`, recebe no corpo da resposta um
   **token de credencial de uso único** (`credentialToken`): um token opaco,
   UUIDv7, com validade de **10 minutos**, vinculado exclusivamente ao
   `commandId` e ao `integrationId` do comando reivindicado.

2. O token é persistido no PostgreSQL na tabela
   `collector_credential_tokens`, com campos:
   - `Id` (UUIDv7, PK)
   - `CommandId` (FK para `ImmediateCollectionCommand`, único)
   - `IntegrationId` (para auditoria; não é autorização)
   - `TokenHash` (hash SHA-256 do token opaco — o token em si nunca é
     persistido)
   - `ExpiresAtUtc`
   - `ConsumedAtUtc` (nulo até o uso)
   - `CreatedAtUtc`

3. O Worker apresenta o token a um **endpoint de resgate de credencial**:

   ```
   POST /api/internal/collector/credentials/redeem
   Authorization: Bearer <credencial-de-serviço-do-Worker>
                  (escopo collector.command.claim)
   Body: { "credentialToken": "<uuid-opaco>" }
   ```

   A Master API:
   a. Valida a credencial de serviço do Worker (escopo
      `collector.command.claim`).
   b. Localiza o token pelo hash; verifica que não foi consumido e não
      expirou.
   c. Confirma que `CommandId` pertence ao mesmo Worker (via
      `integrationId` derivado da credencial de serviço).
   d. Decifra `username`, `password` e `baseUrl` da `IServiceCredential`
      via `ICredentialCipher` + AWS KMS — **em memória, sem persistir**.
   e. Marca o token como consumido (`ConsumedAtUtc = now`).
   f. Retorna as credenciais em claro na resposta HTTPS:

   ```json
   {
     "username": "...",
     "password": "...",
     "baseUrl": "..."
   }
   ```

4. O Worker usa as credenciais imediatamente para autenticar no iService,
   mantendo-as **somente em memória** durante a execução do comando. Após o
   uso (ou expiração), o Worker descarta as credenciais sem persistir.

5. Uma job de limpeza (pode ser executada no startup do Worker ou como
   background job da Master API) expira tokens não consumidos após
   `ExpiresAtUtc`.

#### Por que esta abordagem

| Critério | Endpoint dedicado (escolhida) | Secrets Manager | Var. de ambiente |
|---|---|---|---|
| Isolamento por tenant/integração | ✅ por design (token vinculado a commandId+integrationId) | ⚠️ requer secret por integração ($0.40/mês cada) | ❌ sem isolamento |
| Comprometimento do Worker | Não dá acesso a outros tenants (token é de uso único e expira) | ⚠️ IAM role da EC2 pode ser reutilizada até rotação | ❌ credenciais de todos os tenants expostas |
| Tempo de exposição | Mínimo: uma chamada HTTPS, token expira em 10 min | Depende de rotação (horas/dias) | Longa (até restart) |
| Rastreabilidade | ✅ auditável (token consumido registrado) | ⚠️ log de acesso do Secrets Manager | ❌ sem rastreabilidade |
| Custo | $0 (PostgreSQL já existente) | $0.40/secret/mês × N integrações | $0 mas inseguro |
| Complexidade operacional | Baixa (sem nova infraestrutura) | Média (provisionar secret por integração) | Baixa mas inadequada |

A opção (b) — Secrets Manager — foi considerada, mas o custo cresce
linearmente com o número de integrações (um secret por integração), e a IAM
role da EC2 do Worker, se comprometida, daria acesso a múltiplos secrets até
rotação manual. A opção escolhida é o menor mecanismo capaz de atender todos
os requisitos de segurança sem nova infraestrutura.

#### Controles obrigatórios

- O corpo da resposta de `/redeem` nunca é logado pela Master API (mesmo em
  nível DEBUG). O middleware de log deve ter filtro explícito para esta rota.
- `credentialToken` não aparece em eventos de domínio, payloads de erro ou
  respostas do `claim` além da entrega inicial.
- O Worker deve chamar `/redeem` **uma única vez** imediatamente após o
  `claim`. Se o token expirar antes do uso, o Worker deve chamar `complete`
  com `Failed` e razão `CredentialTokenExpired` (não exposta ao Office).
- A tabela `collector_credential_tokens` não é consultável por endpoints do
  Office.
- Tokens expirados e não consumidos devem ser limpos periodicamente (TTL
  sugerido: 24 horas após expiração, para auditoria).

---

### D2 — Timeout de re-claim

**Decisão: verificação preguiçosa no próximo `claim`, combinada com job de
reconciliação leve na Master API.**

#### Mecanismo

1. `ImmediateCollectionCommand` recebe o campo `ClaimExpiresAtUtc`
   (nullable), preenchido na transação de `claim` como
   `ClaimedAtUtc + 30 minutos`.

2. **Verificação preguiçosa no `claim`:** antes de tentar reivindicar um
   novo comando, a Master API verifica se existe um comando `Claimed` para
   a integração com `ClaimExpiresAtUtc < now`. Se sim, transita
   atomicamente esse comando para `Failed` com
   `CancellationReason = ClaimTimeout` e libera a possibilidade de criar
   novo `Pending` (via nova ativação).

   > Nota: o índice único parcial cobre apenas `Pending`. Um comando
   > `Claimed` expirado não impede novo `Pending` pela constraint de banco,
   > mas a lógica de negócio deve verificar antes de criar novo comando para
   > evitar dois comandos simultâneos para a mesma integração. A verificação
   > preguiçosa resolve isso na mesma transação de re-ativação.

3. **Job de reconciliação leve:** um `BackgroundService` na Master API
   executa a cada **5 minutos** e transita comandos `Claimed` com
   `ClaimExpiresAtUtc < now` para `Failed`. Isso garante que a transição
   ocorra mesmo sem nova tentativa de `claim` pelo usuário, evitando que a
   integração fique travada indefinidamente.

4. A transição para `Failed` por timeout **não aciona `ReconcileEligibility`
   automaticamente** — o Agente permanece `Active` (aguarda nova ativação
   manual pelo usuário se necessário). A decisão de desativar automaticamente
   por timeout é de produto (D3/D4 tratam falhas por credencial, não por
   timeout).

#### Por que não visibility timeout (fila)

ADR-020 rejeitou explicitamente a introdução de fila no MVP. A verificação
preguiçosa + job de reconciliação leve resolvem o problema sem nova
infraestrutura, aproveitando o PostgreSQL já existente. O timeout de 30
minutos foi adotado conforme recomendação de produto (RF-009/D2).

---

### D4 — Falha por credencial rejeitada

**Decisão: `complete` com `Failed` e `failureReason = CredentialRejected`
deve invalidar `ValidationStatus` e acionar `ReconcileEligibilityAsync`
(já implementada) — introduzindo um novo gatilho ao mecanismo existente.**

#### Mecanismo

1. O Worker envia `complete` com `outcome: Failed` e
   `failureReason: CredentialRejected` (ver contrato abaixo).

2. A Master API, ao processar `complete` com `failureReason = CredentialRejected`:
   a. Transita o comando para `Failed`.
   b. Chama `IServiceCredential.RecordValidation(EIServiceValidationStatus.Failed, now)`
      — invalida `ValidationStatus` da credencial.
   c. Chama `CollectorActivationService.ReconcileEligibilityAsync(tenantId, integrationId)`
      na mesma transação — seguindo o mesmo padrão transacional já adotado
      por `IServiceCredentialValidationService.ValidateAsync`. Como
      `ValidationStatus` agora é `Failed`, a elegibilidade é negada, o
      Agente transita para `Inactive` com
      `DeactivationReason = CredentialNotValidated` e qualquer comando
      `Pending` residual é cancelado.

3. Outros `failureReason` (ex.: `IServiceUnavailable`, `ClaimTimeout`,
   `UnexpectedError`) **não invalidam** `ValidationStatus` nem acionam
   `ReconcileEligibilityAsync`.

#### Contexto de implementação

`ReconcileEligibilityAsync` está implementada no commit 5ad77fe
(`CollectorActivationService.cs`, linhas 163–186). Já é acionada por dois
gatilhos: troca de credencial e validação `Failed` via endpoint do Office.
O que RF-009 adiciona é um **terceiro gatilho**: a conclusão `Failed` por
`CredentialRejected` reportada pelo Worker via `complete`. O padrão
transacional a seguir é o mesmo já estabelecido em
`IServiceCredentialValidationService.ValidateAsync` (linhas 77–92).

---

### Contrato concreto: `claim` e `complete`

#### `POST /api/internal/collector/commands/claim`

**Autorização:** credencial opaca de serviço do Worker, escopo
`collector.command.claim`, vinculada a `integrationId`.

**Corpo:** vazio.

**Comportamento:**
- A Master API deriva `integrationId` exclusivamente da credencial de serviço
  (nunca do corpo ou query string).
- Verifica se existe comando `Claimed` com `ClaimExpiresAtUtc < now` para a
  integração → transita para `Failed` (ClaimTimeout) atomicamente antes de
  prosseguir.
- Busca o único comando `Pending` para a integração.
- Se não houver: retorna `204 No Content` (sem trabalho).
- Se houver: em uma transação atômica:
  - Transita `Pending → Claimed`.
  - Preenche `ClaimedAtUtc = now`, `ClaimExpiresAtUtc = now + 30min`.
  - Incrementa `AttemptCount`.
  - Cria registro em `collector_credential_tokens` (token de uso único,
    validade 10 min).
  - Retorna `200 OK`:

```json
{
  "commandId": "uuid",
  "commandType": "ImmediateCollection",
  "integrationId": "uuid",
  "providerId": "uuid",
  "requestedAtUtc": "2026-08-30T13:00:00Z",
  "claimedAtUtc": "2026-08-30T13:00:00Z",
  "claimExpiresAtUtc": "2026-08-30T13:30:00Z",
  "credentialToken": "uuid-opaco-uso-unico"
}
```

**Concorrência:** a transição `Pending → Claimed` usa `UPDATE ... WHERE
Status = 'Pending' AND integrationId = ?` com token de concorrência (ou
pessimistic lock na linha). Se dois Workers tentarem simultaneamente, apenas
um terá linhas afetadas > 0; o outro recebe `204` ou tenta novamente. O
índice único parcial em `Pending` não é necessário para esta garantia (já
existe para impedir dois `Pending` simultâneos), mas a transição atômica via
UPDATE condicional resolve a exclusividade do `claim`.

**Idempotência:** o `claim` não é idempotente por design — um segundo `claim`
do mesmo Worker para o mesmo `integrationId` retornaria `204` (o comando já
está `Claimed`). Se necessário, o Worker pode consultar
`GET /api/internal/collector/eligibility` para verificar o estado antes de
tentar.

---

#### `POST /api/internal/collector/commands/{commandId}/complete`

**Autorização:** credencial opaca de serviço do Worker, escopo
`collector.command.complete`, vinculada a `integrationId`.

**Validação:** a Master API confirma que `commandId` pertence ao
`integrationId` derivado da credencial do Worker. Worker não pode completar
comando de outro tenant.

**Corpo:**

```json
{
  "outcome": "Succeeded | Failed | Cancelled",
  "failureReason": "CredentialRejected | IServiceUnavailable | ClaimTimeout | UnexpectedError | null",
  "completedAtUtc": "2026-08-30T14:00:00Z"
}
```

**Restrições:**
- `failureReason` é obrigatório quando `outcome = Failed`; ignorado nos
  demais casos.
- O corpo **nunca contém**: credenciais, cookies CAS, dados de OS, resposta
  bruta do iService, PII. Qualquer campo não listado acima é rejeitado (`400`).
- `failureReason` é um enum fechado. A razão técnica detalhada (ex.: mensagem
  de erro do iService) **não é transportada** — o Worker pode logar
  localmente sem transmitir à Master API.

**Comportamento por `outcome`:**

| outcome | failureReason | Ação na Master API |
|---|---|---|
| `Succeeded` | — | Transita para `Succeeded`. Preenche `CompletedAtUtc`. |
| `Cancelled` | — | Transita para `Cancelled`. Preenche `CompletedAtUtc`. |
| `Failed` | `CredentialRejected` | Transita para `Failed`. Invalida `ValidationStatus`. Chama `ReconcileEligibilityAsync`. |
| `Failed` | outros | Transita para `Failed`. Não altera `ValidationStatus`. |

**Idempotência:** se o comando já está em estado terminal (`Succeeded`,
`Failed`, `Cancelled`), a Master API retorna `200` com o estado atual sem
re-executar efeitos colaterais.

**Resposta `200 OK`:**

```json
{
  "commandId": "uuid",
  "status": "Succeeded | Failed | Cancelled",
  "completedAtUtc": "2026-08-30T14:00:00Z"
}
```

---

### Estados de `EImmediateCollectionCommandStatus`

O enum já contém todos os estados necessários para RF-009:

```
Pending → Claimed → Succeeded
                  → Failed
                  → Cancelled
Pending → Cancelled
```

**Nenhum estado novo é necessário.** O RF-009 menciona `InProgress` como
possível estado intermediário entre `Claimed` e conclusão, mas a coleta
inteira ocorre dentro do mesmo `Claimed` — o Worker não envia atualizações
intermediárias de progresso. `InProgress` seria redundante e não adiciona
valor sem um mecanismo de polling de progresso (fora de escopo).

---

### Acionamento do Worker (polling)

O Worker (`apps/collector`) é atualmente um stub com `Task.Delay(1000)`. A
arquitetura de RF-009 adota **polling ativo** como mecanismo de acionamento:

1. O Worker executa um loop com intervalo configurável (valor inicial
   sugerido: **30 segundos** para a coleta inicial, podendo ser reduzido
   quando RF-011 introduzir coleta recorrente).
2. A cada iteração, o Worker chama `POST /api/internal/collector/commands/claim`.
3. Se receber `204`, aguarda o próximo intervalo.
4. Se receber `200` com `commandId`, executa o fluxo completo de coleta e
   chama `complete` ao final.

Esta abordagem é consistente com ADR-020 ("polling interno" como transporte)
e não introduz fila, Redis ou infraestrutura nova.

---

### Persistência de dados de OS coletados

**MongoDB Atlas Free Tier** (ADR-012), conexão via string de conexão
armazenada no AWS Secrets Manager (`atua/mongodb-atlas`).

Coleções propostas para RF-009:

- `work_order_snapshots`: estado atual de cada OS (um documento por
  `providerOrderId` por `tenantId`). Upsert idempotente por
  `(tenantId, providerOrderId)` — atende RF-009.9.
- `work_order_observations`: observações históricas (uma por OS por coleta,
  `capturedAt` imutável). Índice único em
  `(tenantId, providerOrderId, commandId)` para idempotência.

Os documentos **não contêm**: credenciais, cookies CAS, `username`,
`password`, `baseUrl`, sessão CAS, ou qualquer campo sensível (RF-009.6).

O `providerOrderId` é mantido como referência externa, separado do UUIDv7
interno do ATUA (RF-009.5, ADR-004).

---

## Contradições e pontos de atenção identificados

### C1 — `providerKey` vs. `ProviderId` na entidade `ImmediateCollectionCommand`

**Documento (ADR-020):** refere-se a `providerKey` como campo do comando,
sugerindo um identificador de string do provedor.

**Código real:** a entidade `ImmediateCollectionCommand` (commit 5ad77fe)
possui `ProviderId` do tipo `Guid` — não uma string `providerKey`. A resposta
do `claim` deve usar `providerId` (Guid) no payload, não `providerKey`
(string). O `backend-engineer` deve usar `ProviderId` conforme a entidade
existente.

### C2 — `ClaimExpiresAtUtc` e `ClaimTimeout` são adições novas

**Código real:** `ImmediateCollectionCommand` não possui `ClaimExpiresAtUtc`.
`ECollectorDeactivationReason` não possui `ClaimTimeout`. Ambos são campos/
valores **novos** a serem adicionados via migration e extensão de enum como
parte de RF-009. A ADR os trata como adições, mas é importante que o
`backend-engineer` saiba que exigem migration e alteração de enum.

### C3 — `ECollectorDeactivationReason` é usado como `CancellationReason` no comando

**Código real:** `ImmediateCollectionCommand.CancellationReason` é do tipo
`ECollectorDeactivationReason?` — o mesmo enum usado para desativação do
Agente. O `failureReason` do payload de `complete` é um **enum separado**
(novo, a ser criado: ex. `ECommandFailureReason`) que não deve ser confundido
com `ECollectorDeactivationReason`. O mapeamento entre `failureReason =
CredentialRejected` e `CancellationReason = CredentialNotValidated` ocorre na
lógica de aplicação do `complete`.

### C4 — MongoDB: decisão vigente confirmada, sem contradição

O RF-009 menciona MongoDB. ADR-003, ADR-012 e o secret `atua/mongodb-atlas`
são consistentes. Não há contradição. A persistência de OS no MongoDB é a
decisão arquitetural vigente.

### C5 — Worker hoje é um stub inerte

O `Worker.cs` executa apenas `Task.Delay(1000)`. Não há lógica de polling,
autenticação de serviço ou consumo de comandos. O RF-009 exige substituição
completa desse stub por um loop de polling funcional.

---

## Dependências desta ADR que requerem decisões de produto pendentes

| Decisão de produto | Impacto arquitetural se não respondida |
|---|---|
| **D1** (recorte temporal: todas as OS ou janela?) | O Worker não sabe se deve paginar ilimitadamente ou aplicar filtro. Sem resposta, implementar como "sem filtro" (recomendação do RF-009) com paginação por conta do Worker. |
| **D3** (máximo de tentativas) | `AttemptCount` já existe. Sem resposta, o Worker não sabe quando parar de tentar após `Failed`. Recomendação: 1 tentativa para coleta inicial. |
| **D5** (LGPD — campos de OS) | Sem resposta, o Worker não sabe quais campos de OS podem ser persistidos. **Bloqueia a definição do schema MongoDB.** |
| **D6** (limite de OS por coleta) | Sem resposta, o Worker não sabe quando truncar. O schema e o comportamento de loop dependem disso. |
| **D7** (qual `providerOrderId` — `workOrderNo` ou `workOrderId`?) | **Bloqueia a definição do índice de idempotência** em `work_order_snapshots`. Sem resposta, não é possível garantir RF-009.9. |
| **D8** (mesma OS em múltiplos status na mesma coleta) | Sem resposta, o Worker não sabe qual estado persistir. Bloqueia a lógica de upsert. |

> **D5 e D7 são os bloqueadores mais críticos para a implementação do schema
> MongoDB e da lógica de persistência.** As demais podem ter comportamento
> padrão definido pelo `backend-engineer` enquanto aguardam resposta.

---

## Alternativas consideradas

### D9 — Secrets Manager com IAM role da EC2

Rejeitada. O custo cresce com o número de integrações ($0.40/secret/mês × N).
A IAM role da EC2 do Worker, se comprometida, permite acesso a múltiplos
secrets até rotação. Não oferece rastreabilidade por comando.

### D9 — Variável de ambiente injetada no deployment

Rejeitada. Exigiria redeploy por integração, expõe credenciais de múltiplos
tenants no ambiente do processo, sem isolamento ou auditabilidade.

### D2 — Visibility timeout via fila (SQS)

Rejeitada. ADR-020 rejeitou explicitamente a introdução de fila no MVP.
A verificação preguiçosa + job de reconciliação resolvem o problema com
infraestrutura já existente.

### D2 — Apenas verificação preguiçosa (sem job)

Considerada. Suficiente se a integração for sempre re-ativada pelo usuário
após falha. Rejeitada porque se o Worker morrer e o usuário não tentar
re-ativar, o comando `Claimed` permanece travado indefinidamente. O job de
5 minutos é leve e resolve sem nova infraestrutura.

### D4 — Não acionar `ReconcileEligibilityAsync` automaticamente

Considerada (deixar como inconsistência operacional visível). Rejeitada
porque gera estado inválido: Agente `Active` com coleta `Failed` por
credencial inválida, sem possibilidade de nova coleta bem-sucedida.

---

## Consequências

**Positivas:**
- Nenhuma nova infraestrutura AWS é necessária para D9 (PostgreSQL já
  existente, Secrets Manager já provisionado para a string de conexão).
- O mecanismo de credencial de uso único é auditável e isolado por tenant/comando.
- O timeout de re-claim resolve o bloqueio de integração sem fila.
- A cadeia D4 → `ReconcileEligibilityAsync` → desativação garante
  consistência operacional.

**Negativas / trade-offs:**
- Nova tabela `collector_credential_tokens` no PostgreSQL (migration
  necessária).
- Campo `ClaimExpiresAtUtc` na entidade `ImmediateCollectionCommand`
  (migration necessária).
- Novo valor `ClaimTimeout` em `ECollectorDeactivationReason` e novo enum
  `ECommandFailureReason` para o payload de `complete` (ambos adições novas).
- O Worker precisa de lógica de polling e de chamada a `/redeem` antes de
  qualquer acesso ao iService — aumenta a complexidade do stub atual.
- O endpoint `/redeem` é sensível; requer atenção especial de log e de
  middleware de request logging.

**Dependências de implementação:**
- `backend-engineer` implementa: `collector_credential_tokens` (migration +
  entidade), campo `ClaimExpiresAtUtc`, enum `ECommandFailureReason`, novo
  valor `ClaimTimeout` em `ECollectorDeactivationReason`, endpoints `claim`
  e `complete`, endpoint `/redeem`, job de timeout de re-claim, e o novo
  gatilho de `ReconcileEligibilityAsync` no handler de `complete`
  (`CredentialRejected`) — seguindo o padrão transacional já existente em
  `IServiceCredentialValidationService.ValidateAsync`.
- `backend-engineer` (apps/collector) substitui o stub por loop de polling
  com autenticação de serviço, chamada a `/redeem` e fluxo Playwright.
- `aws-architect` não precisa de alterações de infraestrutura para D9 (o
  secret `atua/mongodb-atlas` já existe; KMS e Secrets Manager já estão
  provisionados).

## Agentes envolvidos

- `software-architect`: definição desta ADR.
- `product-analyst`: decisões D1, D3, D5, D6, D7, D8 (bloqueadores de
  produto ainda pendentes).
- `backend-engineer`: implementação de Master API e Worker conforme esta ADR.
- `aws-architect`: confirmação de que IAM role do Worker e rede EC2→Master
  API estão corretas para o endpoint `/redeem` (sem nova infraestrutura
  prevista).
- `qa-engineer`: validação de concorrência no `claim`, isolamento de tenant
  no `/redeem`, auditoria de logs (ausência de credenciais).

## Data

2026-08-30

## Substitui

Não substitui ADR anterior. Complementa ADR-003, ADR-004, ADR-012, ADR-017,
ADR-018 e ADR-020. Resolve D2, D4 e D9 de RF-009.
