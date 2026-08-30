# ADR-021 - Coleta Inicial: Acesso a Credenciais pelo Worker, Throttling, Timeout de Re-claim e Falha por Credencial Rejeitada

## Status

Proposed

## Contexto

RF-009 (Coleta Inicial) é o gate onde o Worker (`apps/collector`, rodando em
EC2 separada) passa a ter acesso real ao iService. Esta ADR resolve as
decisões arquiteturais que bloqueiam a implementação, incorporando decisões
do usuário/orchestrator sobre D1–D9. Também consolida o contrato concreto
dos endpoints `claim` e `complete` (esboçados em ADR-020 mas não
implementados) e registra contradições identificadas entre documentação e
código.

### Contexto técnico relevante

- `ImmediateCollectionCommand` já existe no PostgreSQL com
  `EImmediateCollectionCommandStatus`: `Pending`, `Claimed`, `Succeeded`,
  `Failed`, `Cancelled` — os cinco estados já estão presentes no código
  (`EImmediateCollectionCommandStatus.cs`). Não é necessário
  acrescentar estados.
- O índice único parcial impede mais de um comando `Pending` por
  `IntegrationId`. Um comando `Claimed` órfão bloqueia a integração
  indefinidamente, pois `Pending` não pode ser criado enquanto `Claimed`
  existir para a mesma integração.
- `CollectorActivationService.ReconcileEligibilityAsync` está **implementada**
  (`CollectorActivationService.cs`, linhas 163–186) e já é
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
  entidade `IServiceCredential`). Os campos cifrados são
  `UsernameCiphertext`, `PasswordCiphertext`, `BaseUrlCiphertext`, com
  `Nonce`, `Tag` e `DataKeyCiphertext` (chave de dados cifrada por KMS).
  O campo `AlgorithmVersion` está presente na entidade.

## Decisão

### D9 — Entrega de credenciais ao Worker

#### Contexto técnico atualizado (código verificado)

O código real (`AesGcmCredentialCipher.cs`, `ICredentialCipher.cs`,
`IServiceCredential.cs`) implementa **envelope encryption**:

- Cada integração possui sua própria **DEK** (Data Encryption Key, 32 bytes
  aleatórios, `CreateDataKey()`).
- A DEK é wrapped pela **chave mestra** via AES-256-GCM e armazenada como
  `DataKeyCiphertext` no registro da integração.
- Para decifrar credenciais, é preciso primeiro chamar `UnwrapDataKey()` para
  obter a DEK em claro, depois chamar `Decrypt(dekPlaintext, ...)` por campo.
- **A chave mestra atual** vem de `CredentialCipherOptions.MasterKeyBase64`,
  lida de `appsettings.Development.json` — arquivo versionado, com valor de
  dev hardcoded (`zubKUGEX...`). Isso é um TODO explícito no código; AWS KMS
  ainda não está implementado em C#.
- Não existe nenhum SDK de AWS KMS ou Secrets Manager no codebase além do
  AWSSDK.SimpleEmailV2 para SES.

#### Por que o desenho anterior estava errado

O desenho anterior propunha compartilhar a chave mestra entre API e Worker.
Isso **elimina o encapsulamento por tenant**: quem tiver a chave mestra pode
chamar `UnwrapDataKey` em qualquer `DataKeyCiphertext` e decifrar credenciais
de **todos os tenants** — exatamente o oposto do que o usuário pediu.

O envelope encryption da DEK por integração **já resolve o isolamento por
tenant** — mas apenas se a chave mestra ficar exclusivamente na API.

#### Variante escolhida: (B) — `claim` retorna credenciais já decifradas pela API

**A API é o único componente que conhece a chave mestra. O Worker nunca
recebe a chave mestra nem a DEK. Recebe apenas as credenciais em claro,
protegidas por TLS, com validade para um único comando.**

Avaliação das duas variantes:

| Critério | (A) API entrega DEK em claro | (B) API entrega credenciais em claro |
|---|---|---|
| Encapsulamento por tenant | ✅ DEK é da integração específica | ✅ Credenciais são da integração específica |
| Chave mestra no Worker | ❌ Não, mas DEK é material criptográfico sensível | ✅ Worker não recebe nenhum material de chave |
| Superfície de ataque no Worker | DEK comprometida → credenciais daquela integração decifráveis | Credenciais em claro por tempo de execução do comando |
| Acesso do Worker ao Postgres | Precisa ler `IServiceCredentials` | **Não precisa** — API lê e decifra |
| Complexidade no Worker | Worker precisa implementar AES-256-GCM | Worker é um consumidor simples de credenciais em claro |
| API já no fluxo | API já executa `claim` — decifragem é O(1) adicional | Idem |
| Raio de exposição em comprometimento | DEK vaza → uma integração exposta | Credenciais em claro por duração do comando |

**A variante (B) é superior.** A DEK é material criptográfico permanente
(vinculada à integração até rotação); as credenciais em claro existem por
segundos e são descartadas. O Worker não precisa implementar criptografia,
não precisa de acesso à tabela de credenciais no PostgreSQL, e a superfície
de ataque no processo do Worker é menor. A API já está obrigatoriamente no
fluxo do `claim` — a decifragem é uma instrução adicional de custo zero.

#### Mecanismo (variante B)

1. **No `claim`:** a Master API, ao transitar o comando para `Claimed`,
   executa em memória:
   - Busca `IServiceCredential` da `IntegrationId` do comando.
   - Chama `ICredentialCipher.UnwrapDataKey(DataKeyCiphertext, KmsKeyId)` →
     obtém a DEK em claro.
   - Chama `ICredentialCipher.Decrypt(dek, UsernameCiphertext, Nonce, Tag)` →
     `username` em claro.
   - Chama `ICredentialCipher.Decrypt(dek, PasswordCiphertext, Nonce, Tag)` →
     `password` em claro.
   - Chama `ICredentialCipher.Decrypt(dek, BaseUrlCiphertext, Nonce, Tag)` →
     `baseUrl` em claro (se presente).
   - Zera a DEK da memória imediatamente após as três decifragens.
   - Inclui as credenciais em claro na resposta do `claim`, protegida por TLS.

2. **No Worker:** recebe `username`, `password` e `baseUrl` no corpo do
   `claim`. Usa imediatamente para autenticar no iService. Descarta da
   memória ao final do comando (ou na primeira exceção após o uso).

3. O Worker **não acessa** a tabela `IServiceCredentials`. Não precisa de
   nenhuma permissão sobre ela.

#### Contrato atualizado do `claim` (resposta `200 OK`)

```json
{
  "commandId": "uuid",
  "commandType": "ImmediateCollection",
  "integrationId": "uuid",
  "tenantId": "uuid",
  "providerId": "uuid",
  "requestedAtUtc": "2026-08-30T13:00:00Z",
  "claimedAtUtc": "2026-08-30T13:00:00Z",
  "claimExpiresAtUtc": "2026-08-30T13:30:00Z",
  "credential": {
    "username": "...",
    "password": "...",
    "baseUrl": "..."
  }
}
```

#### Controles contra vazamento

- O corpo completo da resposta do `claim` **nunca é logado** pela Master API
  — nem em DEBUG/TRACE. Middleware de request logging deve filtrar esta rota
  explicitamente ou omitir o body da resposta.
- O Worker não loga `credential.*` em nenhum sink.
- Exceções no Worker durante o uso das credenciais são logadas sem o valor —
  apenas tipo e mensagem genérica.
- O payload de `complete` nunca contém nenhum campo de `credential`.
- A DEK é zerada da memória da API imediatamente após as três decifragens,
  sem persistência intermediária.

#### Permissão do Worker no PostgreSQL

O Worker **não precisa** de acesso à tabela `IServiceCredentials`.

O Worker usa um role PostgreSQL exclusivo (`atua_worker_ro`) com permissões
mínimas apenas para ler o estado do comando:

```sql
GRANT SELECT (
    "Id",
    "IntegrationId",
    "TenantId",
    "ProviderId",
    "Status",
    "ClaimedAtUtc",
    "ClaimExpiresAtUtc",
    "RequestedAtUtc"
) ON "ImmediateCollectionCommands" TO atua_worker_ro;
```

Nenhuma permissão sobre `IServiceCredentials` ou qualquer outra tabela.
Nenhuma permissão de escrita em nenhuma tabela.

> Nota: com a variante (B), o Worker pode até dispensar conexão direta ao
> PostgreSQL — todas as informações necessárias para iniciar a coleta chegam
> via resposta do `claim`. A conexão de leitura ao PostgreSQL pode ser mantida
> para verificações de estado futuras, mas não é obrigatória para RF-009.

#### AWS KMS — implementação real (substituindo `MasterKeyBase64`)

**Situação atual:** `AesGcmCredentialCipher` usa `MasterKeyBase64` de
`appsettings.Development.json` (versionado, valor de dev hardcoded). Não há
AWS KMS implementado em C#. KMS é intenção do ADR-004, não realidade do
código.

**Desenho para produção:**

A chave mestra nunca sairá do KMS. Em vez de `AesGcmCredentialCipher` gerar
e fazer wrap/unwrap da DEK localmente com `MasterKeyBase64`, o fluxo passa a
ser:

- **`CreateDataKey()`:** chamar `kms:GenerateDataKey` no AWS KMS → retorna
  a DEK em claro (para uso imediato) e a DEK wrapped pelo KMS (para
  persistir em `DataKeyCiphertext`). A chave mestra nunca sai do KMS.
- **`UnwrapDataKey()`:** chamar `kms:Decrypt` no AWS KMS com o
  `DataKeyCiphertext` → retorna a DEK em claro. A chave mestra nunca sai
  do KMS.

**Pacote NuGet:** `AWSSDK.KeyManagementService` (mesmo publisher dos demais
AWSSDK já presentes no projeto).

**Permissão IAM mínima da API em produção:**
```json
{
  "Effect": "Allow",
  "Action": ["kms:GenerateDataKey", "kms:Decrypt"],
  "Resource": "arn:aws:kms:<region>:<account>:key/<key-id>"
}
```

**O Worker não precisa de nenhuma permissão KMS** com a variante (B) — a
decifragem ocorre integralmente na API.

**Migração/coexistência com dados já cifrados:**

O campo `AlgorithmVersion` (já presente em `IServiceCredential` e em
`CredentialCipherOptions`) foi projetado exatamente para este momento. O
procedimento:

1. Dados existentes têm `AlgorithmVersion = 1` e `KmsKeyId =
   "00000000-0000-0000-0000-000000000001"` (placeholder de dev). Em
   produção real ainda não há dados cifrados com KMS.
2. Ao ativar KMS real, uma nova `KmsKeyId` real é provisionada pelo
   `aws-architect`.
3. `AesGcmCredentialCipher.UnwrapDataKey()` passa a bifurcar pelo
   `AlgorithmVersion`: versão 1 usa `MasterKeyBase64` (path legado para
   dados de dev/teste); versão 2 usa `kms:Decrypt` (path de produção).
4. Novos registros são criados com `AlgorithmVersion = 2`. Registros legados
   são re-cifrados na primeira atualização de credencial pelo usuário (o
   `ReplaceSecret` já existe).
5. Quando não houver mais registros com `AlgorithmVersion = 1` em produção,
   o path legado pode ser removido.

**`KmsKeyId`** no schema já armazena o identificador da CMK por registro —
coexistência de múltiplas CMKs já está estruturalmente suportada.

#### Ambiente local (desenvolvimento)

**Problema atual:** `MasterKeyBase64` está em `appsettings.Development.json`
versionado — a chave de dev está exposta no repositório.

**Correção:**

1. Remover `MasterKeyBase64` de `appsettings.Development.json`.
2. Adicionar ao `.env` local (fora do git):
   ```
   Integrations__CredentialCipher__MasterKeyBase64=<chave-de-dev-gerada-localmente>
   ```
3. `.env` no `.gitignore` (verificar se já está; se não, adicionar).
4. A chave de dev é gerada uma vez por desenvolvedor (`openssl rand -base64 32`)
   e **nunca é a mesma que a chave de produção**. Cada desenvolvedor tem sua
   própria chave local — isolamento total entre ambientes.
5. O Worker local lê `Integrations__CredentialCipher__MasterKeyBase64` do
   mesmo `.env` que a API local — mas com a variante (B) o Worker não usa a
   chave mestra, então o Worker local não precisa desse valor.

> **Ação imediata necessária (pré-implementação):** remover `MasterKeyBase64`
> do `appsettings.Development.json` antes de qualquer merge. Este arquivo
> está versionado com a chave de dev exposta.

#### Rotação da chave mestra

Com envelope encryption real via KMS, a rotação da chave mestra é simples:

1. **Habilitar rotação automática da CMK no KMS:** `aws kms enable-key-rotation`
   — o KMS gerencia versões da CMK internamente. `kms:Decrypt` funciona com
   qualquer versão da CMK que gerou o ciphertext; `DataKeyCiphertext` não
   precisa ser re-cifrado.
2. **Rotação manual (troca de CMK):** se a CMK precisar ser substituída por
   uma nova, o procedimento é re-wrap das DEKs (chamar `kms:Decrypt` com a
   CMK antiga + `kms:Encrypt` com a CMK nova para cada `DataKeyCiphertext`).
   As credenciais em si (`UsernameCiphertext`, etc.) **não precisam ser
   re-cifradas** — apenas o wrapper da DEK muda. `KmsKeyId` por registro
   permite identificar quais registros usam qual CMK.
3. Com `AlgorithmVersion`, múltiplas versões coexistem durante a transição.

**Custo da rotação com variante (B):** zero impacto no Worker — ele não
participa de nenhuma operação de chave.

---

### ⚠️ BUG DE SEGURANÇA CRÍTICO — Nonce e Tag compartilhados (pré-existente)

**Confirmado no código real (`AesGcmCredentialCipher.cs` + `IServiceCredential.cs`).**

`IServiceCredential` armazena um único `Nonce` (byte[]) e um único `Tag`
(byte[]) para os três campos cifrados (`UsernameCiphertext`,
`PasswordCiphertext`, `BaseUrlCiphertext`).

`AesGcmCredentialCipher.Encrypt()` gera um nonce aleatório por chamada e
retorna um `CipherResult(CiphertextBase64, Nonce, Tag)`. Porém, ao persistir
três campos cifrados, o código que chama `Encrypt()` três vezes produz três
nonces e três tags distintos — mas a entidade `IServiceCredential` só tem
campos para armazenar **um** nonce e **uma** tag. Isso significa que, na
prática, o código de persistência salva apenas o nonce/tag de um dos campos
(provavelmente o último) e usa esse mesmo par para autenticar/decifrar os
outros dois.

**Consequências criptográficas:**

1. **Reutilização de nonce com a mesma DEK:** AES-GCM com nonce reutilizado
   permite recuperar o XOR dos plaintexts dos campos que compartilham o mesmo
   nonce. Um atacante com dois ciphertexts gerados com o mesmo (DEK, nonce)
   obtém `PT1 XOR PT2`.
2. **Tag única autenticando três cifragens:** o AES-GCM Tag autentica apenas
   o ciphertext com o qual foi gerado. Usar um Tag de um campo para verificar
   outro campo significa que a autenticação dos dois campos restantes **não é
   verificada** — a decifragem pode retornar dados corrompidos sem levantar
   exceção.

**Correção recomendada (não implementar agora — registrar para RF posterior):**

Opção preferida: **cifrar um único payload serializado** com os três campos
juntos. Um nonce, uma DEK, um tag, um ciphertext. A decifragem retorna o
payload completo. Alinhado com o princípio de autenticar tudo ou nada.

```
plaintext: JSON({ username, password, baseUrl })
→ AES-256-GCM(dek, nonce, plaintext) → (ciphertext, nonce, tag)
```

A entidade `IServiceCredential` passaria a ter um único campo de ciphertext
(ou manter os três para compatibilidade com `AlgorithmVersion`).

Alternativa: nonce e tag distintos por campo — três campos de nonce e três de
tag no schema. Mais verboso, menos limpo.

**Impacto atual:** todos os registros de `IServiceCredential` estão
potencialmente mal-autenticados. A decifragem de `username` com o nonce/tag
correto pode funcionar; a de `password` e `baseUrl` com nonce/tag errados
pode falhar silenciosamente ou retornar dados incorretos dependendo da
implementação. **Este defeito deve ser corrigido antes da entrada em produção
com dados reais de clientes.**

---

### D5 — Dados do cliente final (PII) no schema MongoDB

**Decisão do usuário:** coletar tudo que for necessário, incluindo dados do
cliente final. A adequação LGPD será tratada com fábrica e cliente.

#### Schema MongoDB — documentos podendo incluir PII

- `work_order_snapshots` e `work_order_observations` podem conter campos de
  PII do cliente final (nome, telefone, endereço, CPF/CNPJ — conforme o
  iService disponibilize).
- **Recomendação de arquitetura (não bloqueante, mas importante):** mapear e
  documentar quais campos de PII são efetivamente persistidos em cada coleta,
  em um artefato de mapeamento de dados (ex.: `docs/data-mapping/work-orders-pii.md`).
  Isso é barato agora e caro depois: facilita o inventário de dados pessoais
  exigido pela LGPD, o atendimento de requisições de titular (acesso,
  exclusão) e eventual expurgo por integração.
- Controle mínimo imediato: os documentos MongoDB devem ter `tenantId` como
  campo de nível raiz, indexado, para permitir expurgo por tenant sem
  varredura completa da coleção.

---

### D1 + D6 — Recorte temporal e throttling da coleta inicial

**Decisão do usuário:** limite de **3 meses** de histórico. Restrição
absoluta: não derrubar o iService do cliente. Os parâmetros amadurecem com
a descoberta do iService real.

#### Paginação e throttling

- **Janela temporal:** buscar OS com data de abertura/atualização nos últimos
  90 dias a partir da data de execução do comando.
- **Limite de OS por página:** configurável via variável de ambiente
  (`WORKER_PAGE_SIZE`, valor inicial sugerido: `50`). Nunca hardcoded.
- **Intervalo entre páginas (throttling):** configurável via variável de
  ambiente (`WORKER_PAGE_DELAY_MS`, valor inicial sugerido: `500`).
  O Worker aguarda esse intervalo entre requisições consecutivas ao iService.
- **Concorrência máxima:** o Worker processa **um comando por vez** por
  instância EC2. Nenhum paralelismo de coletas.
- **Backoff em erro:** se o iService retornar erro HTTP (5xx, timeout), o
  Worker aplica backoff exponencial com jitter antes de tentar novamente.
  Parâmetros configuráveis: `WORKER_MAX_RETRY_ATTEMPTS` (valor inicial: `3`),
  backoff inicial de `2s`.
- **Limite absoluto de OS por coleta:** configurável via variável de ambiente
  (`WORKER_MAX_ORDERS_PER_COLLECTION`). Sem valor default fixo — deve ser
  definido via configuração explícita. Não hardcoded.

> Todos os parâmetros de throttling são provisórios. O valor adequado depende
> do iService real. Configuração por variável de ambiente permite ajuste sem
> redeploy.

---

### D7 — Chave de identidade da OS (isolamento de decisão pendente)

**Status:** pendente de descoberta (campo `workOrderNo` vs `workOrderId` no
iService; comportamento em reabertura de OS).

#### Isolamento arquitetural

A escolha deve ser **isolada em um único componente**: o mapeador de OS do
Worker (ex.: `WorkOrderMapper` ou equivalente). A troca de `workOrderNo` para
`workOrderId` (ou vice-versa) exige alteração apenas nesse componente e na
configuração do índice MongoDB.

- O campo interno `providerOrderId` nos documentos MongoDB recebe o valor do
  campo configurado — abstraído por uma constante ou configuração, não
  repetido em múltiplos pontos do código.
- O índice de idempotência em `work_order_snapshots` é
  `(tenantId, providerOrderId)`.

**Implementação provisória:** usar `workOrderNo` com o ponto de troca
explicitamente marcado no código (comentário `// TODO(D7)`). Nenhuma outra
parte do Worker deve referenciar diretamente o nome do campo do iService.

---

### D2 — Timeout de re-claim

**Decisão (orchestrator):** timeout de 30 minutos → `Failed` com razão
`ClaimTimeout`. O Agente **não é desativado** automaticamente por timeout.

#### Mecanismo

1. `ImmediateCollectionCommand` recebe o campo `ClaimExpiresAtUtc`
   (nullable), preenchido na transação de `claim` como
   `ClaimedAtUtc + 30 minutos`.

2. **Verificação preguiçosa no `claim`:** antes de tentar reivindicar um
   novo comando, a Master API verifica se existe um comando `Claimed` para
   a integração com `ClaimExpiresAtUtc < now`. Se sim, transita
   atomicamente esse comando para `Failed` com razão `ClaimTimeout` e
   libera a integração.

   > O índice único parcial cobre apenas `Pending`. Um comando `Claimed`
   > expirado não impede novo `Pending` pela constraint de banco, mas a
   > lógica de negócio deve verificar antes de criar novo `Pending` para
   > evitar dois comandos simultâneos para a mesma integração.

3. **Job de reconciliação leve:** um `BackgroundService` na Master API
   executa a cada **5 minutos** e transita comandos `Claimed` com
   `ClaimExpiresAtUtc < now` para `Failed`. Garante a transição mesmo sem
   nova tentativa de `claim`.

4. A transição para `Failed` por `ClaimTimeout` **não aciona
   `ReconcileEligibilityAsync`** — o Agente permanece `Active`.

---

### D3 — Máximo de tentativas na coleta inicial

**Decisão (orchestrator):** máximo **1 tentativa**. Se o comando falhar, o
Worker reporta `complete(Failed, ...)` e não tenta novamente. A reativação
é manual.

O campo `AttemptCount` (já presente na entidade) é incrementado no `claim`.
A lógica de "não tentar mais de 1 vez" é aplicada pela Master API: se
`AttemptCount >= 1` e o comando já está em estado terminal, não cria novo
`Pending` automaticamente.

---

### D4 — Falha por credencial rejeitada

**Decisão (orchestrator):** apenas `failureReason = CredentialRejected`
invalida `ValidationStatus` e aciona `ReconcileEligibilityAsync`.

#### Mecanismo

1. O Worker envia `complete` com `outcome: Failed` e
   `failureReason: CredentialRejected`.

2. A Master API, ao processar `complete` com `failureReason = CredentialRejected`:
   a. Transita o comando para `Failed`.
   b. Chama `IServiceCredential.RecordValidation(EIServiceValidationStatus.Failed, now)`.
   c. Chama `CollectorActivationService.ReconcileEligibilityAsync(tenantId, integrationId)`
      na mesma transação — seguindo o padrão transacional de
      `IServiceCredentialValidationService.ValidateAsync` (linhas 77–92).
      Como `ValidationStatus` agora é `Failed`, a elegibilidade é negada,
      o Agente transita para `Inactive` com
      `DeactivationReason = CredentialNotValidated` e qualquer comando
      `Pending` residual é cancelado.

3. Outros `failureReason` (`IServiceUnavailable`, `ClaimTimeout`,
   `UnexpectedError`) **não invalidam** `ValidationStatus` nem acionam
   `ReconcileEligibilityAsync`.

---

### D8 — Mesma OS em múltiplos status na mesma coleta

**Decisão (orchestrator):** desempate por prioridade de status, na ordem:
Designado > Em Processamento > Pendente > Concluído > Cancelado.
Uma única observação inicial por OS por comando.

O upsert em `work_order_snapshots` aplica essa ordem: se a mesma OS aparecer
em múltiplos status durante a paginação, persiste o status de maior prioridade
na lista acima.

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
  integração → transita para `Failed` (`ClaimTimeout`) atomicamente antes de
  prosseguir.
- Busca o único comando `Pending` para a integração.
- Se não houver: retorna `204 No Content`.
- Se houver: em uma transação atômica:
  - Transita `Pending → Claimed`.
  - Preenche `ClaimedAtUtc = now`, `ClaimExpiresAtUtc = now + 30min`.
  - Incrementa `AttemptCount`.
  - Retorna `200 OK`:

```json
{
  "commandId": "uuid",
  "commandType": "ImmediateCollection",
  "integrationId": "uuid",
  "tenantId": "uuid",
  "providerId": "uuid",
  "requestedAtUtc": "2026-08-30T13:00:00Z",
  "claimedAtUtc": "2026-08-30T13:00:00Z",
  "claimExpiresAtUtc": "2026-08-30T13:30:00Z",
  "credential": {
    "username": "...",
    "password": "...",
    "baseUrl": "..."
  }
}
```

> O campo `credential` contém as credenciais já decifradas pela API no
> momento do `claim`. O Worker não precisa acessar `IServiceCredentials`
> nem realizar nenhuma operação de criptografia.

**Concorrência:** a transição `Pending → Claimed` usa `UPDATE ... WHERE
Status = 'Pending' AND "IntegrationId" = @integrationId` com
`ConcurrencyToken` (já presente na entidade). Se dois Workers tentarem
simultaneamente, apenas um terá linhas afetadas > 0.

---

#### `POST /api/internal/collector/commands/{commandId}/complete`

**Autorização:** credencial opaca de serviço do Worker, escopo
`collector.command.complete`, vinculada a `integrationId`.

**Validação:** a Master API confirma que `commandId` pertence ao
`integrationId` derivado da credencial do Worker.

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
- `failureReason` é um enum fechado. A razão técnica detalhada nunca é
  transportada — o Worker loga localmente sem transmitir à Master API.

**Comportamento por `outcome`:**

| outcome | failureReason | Ação na Master API |
|---|---|---|
| `Succeeded` | — | Transita para `Succeeded`. Preenche `CompletedAtUtc`. |
| `Cancelled` | — | Transita para `Cancelled`. Preenche `CompletedAtUtc`. |
| `Failed` | `CredentialRejected` | Transita para `Failed`. Invalida `ValidationStatus`. Chama `ReconcileEligibilityAsync`. |
| `Failed` | outros | Transita para `Failed`. Não altera `ValidationStatus`. |

**Idempotência:** se o comando já está em estado terminal, a Master API
retorna `200` sem re-executar efeitos colaterais.

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

O enum já contém todos os estados necessários:

```
Pending → Claimed → Succeeded
                  → Failed
                  → Cancelled
Pending → Cancelled
```

**Nenhum estado novo é necessário.**

---

### Acionamento do Worker (polling)

1. Loop com intervalo configurável (`WORKER_POLL_INTERVAL_MS`,
   valor inicial sugerido: `30000`).
2. A cada iteração, chama `POST /api/internal/collector/commands/claim`.
3. Se `204`: aguarda o próximo intervalo.
4. Se `200` com `commandId`: busca credencial no banco (role `atua_worker_ro`),
   decifra em memória, acessa iService com throttling configurável, persiste
   no MongoDB, chama `complete`.

---

### Persistência de dados de OS coletados (MongoDB)

**MongoDB Atlas Free Tier** (ADR-012).

Coleções:

- `work_order_snapshots`: estado atual de cada OS. Upsert idempotente por
  `(tenantId, providerOrderId)`. Pode conter PII (D5).
- `work_order_observations`: uma observação por OS por coleta
  (`capturedAt` imutável). Índice único em
  `(tenantId, providerOrderId, commandId)`.

Campos obrigatórios em todo documento:
- `tenantId` (indexado — controle mínimo para expurgo LGPD por tenant).
- `providerOrderId` (chave externa do iService — campo provisório D7).
- `commandId` (rastreabilidade).
- `capturedAtUtc`.

PII coletada deve ser mapeada em `docs/data-mapping/work-orders-pii.md`
(recomendação — não bloqueia RF-009, mas deve ser feita em paralelo).

O payload do `complete` **nunca contém dados de OS** — a persistência é
responsabilidade exclusiva do Worker diretamente no MongoDB.

---

## Contradições e pontos de atenção identificados

### C1 — `providerKey` vs. `ProviderId` na entidade `ImmediateCollectionCommand`

**Documento (ADR-020):** refere-se a `providerKey` (string).

**Código real:** a entidade possui `ProviderId` do tipo `Guid`. A resposta
do `claim` usa `providerId` (Guid). O `backend-engineer` deve usar
`ProviderId` conforme a entidade existente.

### C2 — `ClaimExpiresAtUtc` e `ClaimTimeout` são adições novas

**Código real:** `ImmediateCollectionCommand` não possui `ClaimExpiresAtUtc`.
`ECollectorDeactivationReason` não possui `ClaimTimeout`. Ambos exigem
migration e extensão de enum como parte de RF-009.

### C3 — `ECollectorDeactivationReason` vs. `ECommandFailureReason`

**Código real:** `ImmediateCollectionCommand.CancellationReason` é do tipo
`ECollectorDeactivationReason?`. O `failureReason` do payload de `complete`
é um enum separado (novo: `ECommandFailureReason`). O mapeamento entre
`failureReason = CredentialRejected` e `CancellationReason =
CredentialNotValidated` ocorre na lógica de aplicação do `complete`.

### C4 — MongoDB: sem contradição

ADR-003, ADR-012 e o secret `atua/mongodb-atlas` são consistentes.

### C5 — Worker hoje é um stub inerte

`Worker.cs` executa apenas `Task.Delay(1000)`. RF-009 exige substituição
completa por loop de polling funcional.

### C6 — `MasterKeyBase64` exposta no repositório (pré-existente)

`appsettings.Development.json` contém `MasterKeyBase64` com valor de dev
hardcoded versionado no git. Deve ser removido do arquivo versionado e
movido para `.env` local antes de qualquer merge, conforme descrito em D9.

---

## Decisões de produto pendentes

| Decisão | Status | Impacto |
|---|---|---|
| **D7** — chave de identidade da OS (`workOrderNo` vs `workOrderId`) e comportamento em reabertura | **PENDENTE** (descoberta do iService real) | Bloqueia a definição definitiva do índice de idempotência. Implementar provisoriamente com `workOrderNo` e ponto de troca explícito no código. |

---

## Alternativas consideradas

### D9 — Variante (A): `claim` entrega DEK em claro, Worker decifra localmente

Avaliada e rejeitada. A DEK é material criptográfico permanente vinculado à
integração — comprometê-la equivale a comprometer a credencial
indefinidamente até rotação. A variante (B) adotada entrega credenciais em
claro por segundos e não exige que o Worker implemente criptografia nem
acesse `IServiceCredentials`.

### D9 — Worker recebe chave mestra e acessa Postgres diretamente

Rejeitada. Elimina o encapsulamento por tenant: quem tem a chave mestra pode
desembrulhar qualquer DEK e decifrar credenciais de todos os tenants.

### D9 — Token opaco de uso único + endpoint `/redeem` (desenho original)

Substituído pela decisão do usuário. A variante (B) adotada recupera
parcialmente o encapsulamento: a API decifra e entrega apenas a credencial
da integração do comando, sem expor material de chave ao Worker.

### D9 — AWS Secrets Manager com um secret por integração

Rejeitada. Custo cresce com o número de integrações ($0.40/secret/mês × N).

### D2 — Visibility timeout via fila (SQS)

Rejeitada. ADR-020 rejeitou explicitamente fila no MVP.

### D2 — Apenas verificação preguiçosa (sem job)

Rejeitada. Se o Worker morrer e o usuário não re-ativar, o comando `Claimed`
permanece travado indefinidamente.

### D4 — Não acionar `ReconcileEligibilityAsync` automaticamente

Rejeitada. Gera estado inválido: Agente `Active` com credencial inválida.

---

## Consequências

**Positivas:**
- Worker não precisa de acesso à tabela `IServiceCredentials` nem de nenhuma
  operação de criptografia — simplifica o Worker e elimina uma classe de
  vulnerabilidade.
- Encapsulamento por tenant preservado: Worker recebe apenas a credencial da
  integração do comando reivindicado.
- Chave mestra permanece exclusivamente na API; com KMS real, nunca sai do
  hardware security module.
- Rotação da chave mestra via `kms:enable-key-rotation` é transparente.
- Parâmetros de throttling configuráveis permitem ajuste sem redeploy.
- Isolamento de D7 em um único mapeador permite troca de campo sem
  refatoração ampla.
- Cadeia D4 → `ReconcileEligibilityAsync` → desativação garante
  consistência operacional.

**Negativas / trade-offs:**
- **Bug crítico pré-existente** de nonce/tag compartilhados em
  `IServiceCredential` deve ser corrigido antes de produção com dados reais
  (ver seção "Bug de Segurança Crítico" em D9).
- `MasterKeyBase64` exposta em `appsettings.Development.json` deve ser
  removida antes de qualquer merge (C6).
- KMS real ainda não implementado em C# — `backend-engineer` precisa
  implementar `AWSSDK.KeyManagementService` substituindo `GetMasterKey()`.
- Campo `ClaimExpiresAtUtc` (migration), novo valor `ClaimTimeout` em
  `ECollectorDeactivationReason` (migration de enum) e novo enum
  `ECommandFailureReason` — todos são adições novas.
- Schema MongoDB com PII exige mapeamento de dados para adequação LGPD
  posterior.
- Resposta do `claim` inclui `credential` com dados sensíveis em claro —
  middleware de log deve filtrar o body desta rota explicitamente.

**Dependências de implementação:**
- `backend-engineer` (Master API): campo `ClaimExpiresAtUtc`, enum
  `ECommandFailureReason`, valor `ClaimTimeout` em
  `ECollectorDeactivationReason`, endpoints `claim` e `complete`, decifragem
  de credencial no handler do `claim` via `ICredentialCipher` existente,
  job de timeout de re-claim (BackgroundService, 5min), novo gatilho de
  `ReconcileEligibilityAsync` em `complete(CredentialRejected)`, filtro de
  log no body da resposta do `claim`.
- `backend-engineer` (Worker): substituição do stub por loop de polling,
  consumo do campo `credential` da resposta do `claim`, paginação com
  throttling configurável, persistência no MongoDB, isolamento do mapeador
  D7 com ponto de troca explícito. Worker não implementa criptografia.
- `backend-engineer` (transversal): remover `MasterKeyBase64` de
  `appsettings.Development.json`; implementar `AWSSDK.KeyManagementService`
  em `AesGcmCredentialCipher`; planejar correção do bug de nonce/tag.
- `aws-architect`: provisionar CMK real no KMS; IAM role da API com
  `kms:GenerateDataKey` + `kms:Decrypt`; Worker sem permissões KMS nem
  acesso a `IServiceCredentials` no Postgres.
- Artefato `docs/data-mapping/work-orders-pii.md`: criar em paralelo.

## Agentes envolvidos

- `software-architect`: definição desta ADR.
- `backend-engineer`: implementação de Master API e Worker conforme esta ADR.
- `aws-architect`: provisionar CMK KMS real, IAM roles com escopos mínimos,
  confirmar ausência de permissão KMS e de acesso a `IServiceCredentials` no
  Worker.
- `qa-engineer`: validação de concorrência no `claim`, ausência de
  `credential.*` em logs da API e do Worker, isolamento de tenant,
  throttling respeitado.

## Data

2026-08-30

## Substitui / Complementa

Não substitui ADR anterior. Complementa ADR-003, ADR-004, ADR-012, ADR-017,
ADR-018 e ADR-020. Resolve D1, D2, D3, D4, D5, D6, D8 e D9 de RF-009.
D7 permanece parcialmente aberto (implementação provisória com ponto de
troca explícito).
