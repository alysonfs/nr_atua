# RF-008 - Ativação do Agente Coletor (Implementação)

**Status**: Implementado e aprovado pelo QA  
**Commit**: 5ad77fe  
**Data de implementação**: 2026-08-30

## Objetivo

Permitir que membros ativos `OWNER` e `ADMIN` ativem ou desativem o Agente Coletor de seu tenant no Office, condicionando a ativação à elegibilidade vigente e à validação bem-sucedida da credencial iService da integração atual.

## Contexto

O Agente Coletor inicia desativado para cada tenant. A ativação requer:
1. Membership ativo com role `OWNER` ou `ADMIN`
2. Trial elegível para o tenant (ADR-015)
3. Credencial iService da integração atual com `ValidationStatus == Succeeded` (ADR-018)

A implementação define o contrato local da Master API sem executar coleta, acessar o iService ou entregar credenciais ao Worker.

Requisito funcional relacionado: [`docs/requirements/RF-008-ativacao-agente-coletor.md`](../requirements/RF-008-ativacao-agente-coletor.md)

Decisão arquitetural: [`docs/decisions/ADR-020-ativacao-e-comando-imediato-do-agente-coletor.md`](../decisions/ADR-020-ativacao-e-comando-imediato-do-agente-coletor.md)

## Componentes Entregues

### Entidades PostgreSQL

#### `CollectorActivation`
- Chave única: FK para `Integration`
- Campos:
  - `Status`: `Inactive`, `Active`
  - `ActivatedAtUtc`: timestamp UTC
  - `DeactivatedAtUtc`: timestamp UTC
  - `DeactivationReason`: `Manual`, `TrialIneligible`, `CredentialNotValidated`
  - Token de concorrência (optimistic locking)
- Expressa a intenção operacional atual, não uma sessão do iService

#### `ImmediateCollectionCommand`
- Chave primária: `Id` (UUID)
- Campos:
  - `IntegrationId`: FK para `Integration`
  - `TenantId`: para consistência e auditoria
  - `ProviderKey`: identificador do provedor
  - `Status`: `Pending`, `Claimed`, `Succeeded`, `Failed`, `Cancelled`
  - `RequestedAtUtc`, `ClaimedAtUtc`, `CompletedAtUtc`: timestamps UTC
  - `CancellationReason`: razão do cancelamento
  - `AttemptCount`: contagem de tentativas
  - Token de concorrência
- Comando durável e rastreável
- Payload não contém credenciais, cookies, URLs sensíveis ou segredos

#### `CollectorControlIdempotency`
- Chave única composta: `(IntegrationId, Operation, IdempotencyKey)`
- Campos:
  - `Operation`: `Activate`, `Deactivate`
  - `IdempotencyKey`: chave opaca fornecida pelo cliente
  - Hash da requisição
  - Resposta final (para replay)
  - Expiração
- Garante segurança contra requisições duplicadas

#### Índices críticos
- Índice único parcial: máximo um `ImmediateCollectionCommand` em `Pending` por `IntegrationId`
  - Fornece garantia contra ativações concorrentes
- Índice único composto: `(IntegrationId, Operation, IdempotencyKey)`
  - Enforça unicidade de idempotência

### Enums

- `ECollectorActivationStatus`: `Inactive`, `Active`
- `ECollectorDeactivationReason`: `Manual`, `TrialIneligible`, `CredentialNotValidated`
- `ECollectorControlOperation`: `Activate`, `Deactivate`
- `EImmediateCollectionCommandStatus`: `Pending`, `Claimed`, `Succeeded`, `Failed`, `Cancelled`
- `EActivationBlockReason`: `None`, `TrialIneligible`, `CredentialsNotValidated`
- `ECollectorControlOperation`: `Activate`, `Deactivate`

### Serviços

#### `ICollectorEligibilityEvaluator`
Interface interna que avalia elegibilidade sob fronteira transacional única:
```
eligible = (Trial ativo para o tenant) AND (ServiceCredentialProfile.ValidationStatus == Succeeded)
```

#### `CollectorEligibilityEvaluator`
Implementação que consulta:
- Estado do Trial (ADR-015)
- Status de validação da credencial iService (ADR-018)

Retorna:
- `eligible: boolean`
- `evaluatedAtUtc: DateTime`

#### `CollectorActivationService`
Serviço de aplicação que:
- Bloqueia a linha de ativação/integração (pessimistic ou optimistic locking com retry)
- Reavalia pré-condições na transação
- Cria/atualiza `CollectorActivation` e `ImmediateCollectionCommand` atomicamente
- Grava registro de idempotência
- Trata colisão no índice parcial como resposta idempotente

## Endpoints

### GET /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation

**Autorização**: Session + membership ativo para `tenantId`  
**Roles permitidos**: `OWNER`, `ADMIN`

**Resposta (200)**:
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

### PUT /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation

**Autorização**: Session + membership ativo para `tenantId`  
**Roles permitidos**: `OWNER`, `ADMIN`  
**Headers obrigatórios**: `Idempotency-Key` (opaco)  
**Corpo**: vazio (significado: "ativar")

**Respostas**:
- **200**: Ativação bem-sucedida (retorna DTO como em GET)
- **409**: Ativação recusada (pré-condições não atendidas)
  ```json
  {
    "error": "activation_not_eligible",
    "reason": "TrialIneligible|CredentialsNotValidated"
  }
  ```
- **404**: Tenant ou integração não encontrados
- **403**: Sem membership ou role insuficiente

**Comportamento**:
- Idempotente: mesma `Idempotency-Key` com conteúdo idêntico sempre retorna a mesma resposta
- Se `canActivate == false`, não cria comando e retorna 409
- Se sucesso, cria exatamente um `ImmediateCollectionCommand` em `Pending`

### DELETE /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation

**Autorização**: Session + membership ativo para `tenantId`  
**Roles permitidos**: `OWNER`, `ADMIN`  
**Headers obrigatórios**: `Idempotency-Key` (opaco)

**Respostas**:
- **200**: Desativação bem-sucedida (retorna DTO como em GET)
- **404**: Tenant ou integração não encontrados
- **403**: Sem membership ou role insuficiente

**Comportamento**:
- Idempotente: mesma `Idempotency-Key` sempre retorna a mesma resposta
- Muda status para `Inactive` com `DeactivationReason = Manual`
- Cancela qualquer `ImmediateCollectionCommand` em `Pending`
- Se já `Inactive`, retorna 200 com estado atual

## Máquina de Estados

```
Inactive
  |
  +---> [PUT com eligibilidade válida] ---> Active (cria comando Pending)
  |
  ^
  |
  +--<--- [DELETE] <--- Active
           (cancela comando Pending, reason=Manual)
           |
           v
           Inactive
           
  +--<--- [Perda de Trial ou validação] <--- Active
           (cancela comando Pending, reason aplicável)
           |
           v
           Inactive
```

Comando:
```
Pending
  |
  +---> [Claimed pelo Worker] ---> Claimed
  |                                  |
  |                                  +---> [Complete com sucesso] ---> Succeeded
  |                                  |
  |                                  +---> [Complete com falha] ---> Failed
  |
  +---> [Cancelamento] ---> Cancelled (terminal)
```

## Comportamento Detalhado

### Ativação

1. Usuário com `OWNER`/`ADMIN` faz PUT com `Idempotency-Key`
2. Master API valida:
   - Membership ativo para o tenant
   - Tenant e integração existem
   - Chave de idempotência nunca usada com conteúdo diferente (ou retorna resposta anterior)
3. Master API consulta `ICollectorEligibilityEvaluator`:
   - Trial elegível?
   - Credencial validada?
4. Se elegível:
   - Muda `CollectorActivation.Status` para `Active`
   - Cria `ImmediateCollectionCommand` em `Pending`
   - Grava `CollectorControlIdempotency` com resposta
   - Transação atômica
5. Se não elegível:
   - Não altera estado
   - Retorna 409 com motivo
   - Grava `CollectorControlIdempotency` com resposta 409

### Desativação Manual

1. Usuário com `OWNER`/`ADMIN` faz DELETE com `Idempotency-Key`
2. Master API valida autorização
3. Se `CollectorActivation.Status == Active`:
   - Muda para `Inactive`
   - Define `DeactivationReason = Manual`
   - Define `DeactivatedAtUtc`
   - Encontra comando `Pending` (se houver)
   - Cancela com `CancellationReason = ManualDeactivation`
   - Transação atômica
4. Se já `Inactive`:
   - Retorna 200 com estado atual (idempotência)

### Desativação por Perda de Condição

Quando Trial é alterado/expirado ou credencial muda de validação:
1. Chamada para `CollectorActivationService.ReconcileEligibility`
2. Se `CollectorActivation.Status == Active`:
   - Reavalia elegibilidade
   - Se não elegível:
     - Muda para `Inactive`
     - Define `DeactivationReason = TrialIneligible` ou `CredentialNotValidated`
     - Encontra comando `Pending`
     - Cancela com razão aplicável
     - Transação atômica

**Nota**: Esta reconciliação está documentada em ADR-020 mas permanece como prioridade de implementação posterior.

## Regras Críticas

1. **Isolamento por tenant**: Toda consulta é limitada a `integrationId`; autorização do `tenantId` é verificada na rota
2. **Comando único pendente**: Índice parcial garante no máximo um comando `Pending` por integração
3. **Atomicidade**: Ativação/desativação e comando são criados/atualizados na mesma transação
4. **Idempotência**: Mesma chave com conteúdo idêntico sempre retorna mesma resposta
5. **Sem segredos no DTO**: Resposta não contém credenciais, cookies, tokens internos ou URLs sensíveis
6. **Elegibilidade centralizada**: Consulta única a `ICollectorEligibilityEvaluator` na mesma transação

## Limitações Intencionais

⚠️ **O RF-008 implementa apenas o contrato de ativação. As seguintes características NÃO existem:**

- **Worker não recebe credenciais**: Nenhum mecanismo de entrega de credenciais opacas ao Worker
- **Worker não consome comandos**: Endpoints `claim` e `complete` não estão implementados
- **Sem coleta real**: Nenhuma interação com iService, Playwright, scraping ou acesso a API do provedor
- **Comando inerte**: `ImmediateCollectionCommand` em `Pending` permanece inerte indefinidamente
- **Sem reconciliação automática**: `CollectorActivationService.ReconcileEligibility` não é invocada durante alterações de Trial ou validação (prioridade posterior)

Qualquer expansão dessas funcionalidades exige gate explícito do usuário e aprovação arquitetural.

## Testes

- 125 testes passando
- Cobertura: autorização, idempotência, concorrência, transições de estado, validação de pré-condições
- Aprovado pelo QA sem achados críticos ou altos

## Indicadores de Implementação

### Arquivos criados/modificados (commit 5ad77fe)

- Migration: `20260830153940_AddCollectorControl.cs`
- Entidades: `CollectorActivation.cs`, `ImmediateCollectionCommand.cs`, `CollectorControlIdempotency.cs`
- Enums: `ECollectorActivationStatus.cs`, `ECollectorDeactivationReason.cs`, `ECollectorControlOperation.cs`, `EImmediateCollectionCommandStatus.cs`, `EActivationBlockReason.cs`
- Serviços: `ICollectorEligibilityEvaluator.cs`, `CollectorEligibilityEvaluator.cs`, `CollectorActivationService.cs`
- Endpoints: `CollectorActivationEndpoints.cs`

### Certificações

✓ Limite de escopo respeitado  
✓ Nenhuma coleta real  
✓ Nenhum acesso ao iService  
✓ Nenhum scraping/Playwright  
✓ Worker NÃO consome comandos  
✓ Endpoints claim/complete do Worker NÃO implementados  
✓ QA aprovado  

## Próximos Passos (Bloqueados)

Implementações futuras que dependem de gate explícito do usuário:

1. **Entrega de credenciais ao Worker** → Requer arquitetura e aprovação de segurança
2. **Worker reclamando e completando comandos** → Endpoints não implementados
3. **Coleta real e reconciliação** → Exige aprovação de produto e validação de segurança
4. **Reconciliação automática de elegibilidade** → Prioridade posterior

Ver [`docs/requirements/RF-008-ativacao-agente-coletor.md#gate-de-aprovação-explícito-para-fases-futuras`](../requirements/RF-008-ativacao-agente-coletor.md#gate-de-aprovação-explícito-para-fases-futuras).

## Revisão (2026-09-17)

Feita durante a implementação da UI de integração/ativação no Office
([apps/office/src/app/office/components/Integrations](../../apps/office/src/app/office/components/Integrations)).
Achados:

1. **Elegibilidade não depende mais da validação de credencial.** A
   descrição original deste documento ("Credencial iService... com
   `ValidationStatus == Succeeded`" na seção Contexto) está desatualizada
   pela ADR-024: `CollectorEligibilityEvaluator` hoje só verifica
   `TenantPlan` ativo/não expirado; `ValidationStatus` é lido apenas para
   fins informativos no DTO (`CredentialValidationStatus`), sem bloquear a
   ativação.
2. **`Integration.LastCollectionAtUtc` está órfão.** O campo e o método
   `RecordCollection(now)` existem em
   [`Integration.cs`](../../apps/api/Atua.Api/Domain/Integrations/Integration.cs)
   desde a ADR-024, mas nenhum código do repositório chama
   `RecordCollection` — não há, hoje, nenhuma "data da última coleta com
   sucesso" sendo gravada ou exposta por nenhum endpoint. Isso é esperado
   dado que o Worker ainda não consome comandos (ver "Limitações
   Intencionais" acima), mas fica registrado para quando a coleta real for
   habilitada: será necessário (a) chamar `RecordCollection` ao concluir um
   `ImmediateCollectionCommand` com sucesso e (b) expor esse timestamp no
   `CollectorActivationView`/Office.

## Referências

- [`docs/requirements/RF-008-ativacao-agente-coletor.md`](../requirements/RF-008-ativacao-agente-coletor.md) — Requisito funcional
- [`docs/decisions/ADR-020-ativacao-e-comando-imediato-do-agente-coletor.md`](../decisions/ADR-020-ativacao-e-comando-imediato-do-agente-coletor.md) — Decisão arquitetural
- [`docs/decisions/ADR-015-ciclo-de-vida-trial-e-preferencia-fuso-horario.md`](../decisions/ADR-015-ciclo-de-vida-trial-e-preferencia-fuso-horario.md) — Elegibilidade de Trial
- [`docs/decisions/ADR-017-contrato-de-autenticacao-browser-e-servico-interno.md`](../decisions/ADR-017-contrato-de-autenticacao-browser-e-servico-interno.md) — Credencial de serviço interno
- [`docs/decisions/ADR-018-contratos-acesso-office-credenciais-iservice-e-validacao.md`](../decisions/ADR-018-contratos-acesso-office-credenciais-iservice-e-validacao.md) — Contrato de credenciais e validação
- [`docs/features/mvp-onboarding-coleta-e-supervisao.md`](./mvp-onboarding-coleta-e-supervisao.md) — Fluxo MVP incluindo ativação do Coletor

