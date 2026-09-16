# ADR-024 - Modelo de Plano, TenantPlan, papéis e revisão de Integration

## Status

Accepted

## Contexto

O modelo atual de "Trial" é uma entidade de banco (`TrialSubscription`)
ligada diretamente ao `User`, criada na confirmação de e-mail e associada ao
`Tenant` posteriormente (ADR-015). Essa modelagem não comporta a evolução de
negócio necessária: o produto precisa de um catálogo de planos (`Plan`) do
qual o Trial é apenas o plano gratuito, com limites de uso (quantidade de
integrações e de usuários) que se aplicam igualmente ao plano pago inicial
(`Essencial`, nome provisório).

Além disso, o papel de membership do tenant hoje é limitado a
`Owner`/`Admin`, sem refletir os perfis reais de uso do Office (admin,
default, technical), e a entidade `Integration` (tenant↔provider) está
minimamente modelada, sem campos de auditoria/operação (última coleta,
timestamps).

O ambiente de desenvolvimento roda direto contra o RDS compartilhado (sem
banco local via Docker); o usuário autorizou explicitamente apagar e
recriar o schema desse RDS de desenvolvimento para este trabalho.

## Decisão

### Plano (`Plan`) e vínculo com o tenant (`TenantPlan`)

- Nova entidade `Plan` (catálogo): `Id`, `Code`, `Name`, `IsFree`, `Value`,
  `DurationDays` (nulo = sem expiração automática), `MaxIntegrations`,
  `MaxUsers`, `IsActive`. Seed inicial: `trial` (gratuito, 7 dias, 2
  integrações, 5 usuários) e `essencial` (pago R$500, sem expiração automática,
  mesmos limites).
- Nova entidade `TenantPlan` (vínculo): `Id`, `TenantId`, `PlanId`,
  `StartsAt`, `ExpiresAt` (nulo = sem expiração), `Status`
  (`Active`/`Superseded`/`Expired`/`Cancelled`), timestamps de auditoria.
  Restrição de unicidade: no máximo um `TenantPlan` com `Status = Active`
  por tenant.
- `TrialSubscription`, `TrialEligibilityService` e `TrialEndpoints` são
  removidos. `TenantOnboardingService` passa a criar o `TenantPlan` ativo
  (plano `trial`) no momento da criação do tenant, com
  `StartsAt = User.EmailConfirmedAt` (preserva a regra de não reiniciar o
  prazo, herdada de ADR-015).
- `CollectorEligibilityEvaluator` deixa de exigir credencial iService com
  `ValidationStatus = Succeeded` para permitir a ativação do Agente
  Coletor: o usuário pode informar as credenciais e ativar o coletor
  imediatamente, sem validação prévia bem-sucedida. Falhas de credencial
  passam a ser tratadas apenas depois, por outro meio de aviso ao usuário
  (fora do escopo desta ADR). O bloqueio de ativação por plano/trial
  inelegível (RF-020.7) é mantido; o evaluator passa a checar apenas o
  `TenantPlan` ativo, e `EActivationBlockReason.CredentialsNotValidated` é
  removido (só resta o motivo de plano inelegível, renomeado para
  `PlanIneligible`).

### Papéis de membership (`ETenantMembershipRole`)

- `ETenantMembershipRole` passa a ter quatro valores: `Owner`, `Admin`,
  `Default`, `Technical`. `Owner` é o responsável legal pelo tenant
  (papel já existente, mantido); `Admin` é o novo papel de confiança do
  Owner para gestão operacional dos demais usuários; `Default` e
  `Technical` são novos papéis operacionais sem gestão.
- Somente `Owner` pode contratar/trocar/cancelar o plano do tenant
  (RF-020/RN-020.4) e alterar o papel de um `Admin`. `Admin` pode gerenciar
  usuários, credenciais e integrações, e alterar papéis de `Default`/
  `Technical`, mas não o plano nem o papel de `Owner`/`Admin`. Um tenant
  deve sempre ter exatamente um membership `Owner`.

### Revisão de `Integration`

- `Integration` (tenant↔provider) recebe campos adicionais: `CreatedAtUtc`,
  `UpdatedAtUtc`, `LastCollectionAtUtc`. O status de validação de credencial
  permanece exclusivamente em `IServiceCredential` (sem duplicar estado).
- A ativação de uma `Integration` passa a validar o limite `MaxIntegrations`
  do `TenantPlan` ativo do tenant antes de habilitar.

### Migrations

- Todas as migrations atuais em
  `Infrastructure/Persistence/Migrations/` e o
  `AtuaDbContextModelSnapshot.cs` são removidas e substituídas por uma
  única migration inicial consolidada, aplicada diretamente no RDS de
  desenvolvimento (autorização explícita do usuário; sem dados de produção
  a preservar neste ambiente).

## Motivos

- Separar "catálogo de plano" de "vínculo do tenant com o plano" permite
  adicionar novos planos e trocar o plano de um tenant sem migração de
  schema, e mantém histórico auditável de mudança de plano.
- Extrair os limites (`MaxIntegrations`, `MaxUsers`) como dados do plano, em
  vez de valores fixos em código, evita nova migration a cada novo plano
  com limites diferentes.
- Manter `Owner` como papel legal distinto de `Admin` (gestão operacional)
  reflete a realidade de negócio: quem responde legalmente/financeiramente
  pelo tenant nem sempre é quem opera o dia a dia; introduzir `Default` e
  `Technical` cobre os demais perfis de uso reais do Office.
- Remover a exigência de credencial validada para ativar o Coletor reduz
  atrito no onboarding; o custo (coletor ativado com credencial inválida)
  é aceito pelo usuário como problema a ser tratado depois, por outro
  mecanismo de aviso.
- Squash das migrations é viável e mais simples do que uma migração
  incremental complexa, dado que o ambiente de desenvolvimento não possui
  dados a preservar e a mudança de modelo é estrutural (remoção de tabela
  de Trial, novas tabelas de plano). Não queremos ver tabelas ou Classes
  que realmente fazem parte do negócio.

## Alternativas consideradas

### Manter `TrialSubscription` e adicionar um plano pago em paralelo

Rejeitada: perpetuaria a modelagem incorreta identificada pelo usuário (uma
entidade de negócio, "Trial", tratada como conceito de banco em vez de um
valor dentro do catálogo de planos), dificultando evolução futura de planos.

### Modelar limites de plano como tabela chave-valor (`plan_limit`) desde já

Adiada: com apenas dois limites conhecidos hoje (`MaxIntegrations`,
`MaxUsers`) e dois planos com os mesmos valores, colunas simples no `Plan`
são suficientes. Reavaliar migração para chave-valor se um novo plano
exigir um limite muito diferente.

### Migration incremental preservando histórico de migrations antigas

Rejeitada para este ciclo: o usuário autorizou explicitamente recriar o
schema do RDS de desenvolvimento, e a mudança estrutural (remoção de
`trial_subscriptions`, novas tabelas) tornaria a migration incremental mais
arriscada do que uma migration inicial única revisável.

## Consequências

- `TrialSubscription`, `TrialEligibilityService`, `TrialEndpoints` e a rota
  `GET /api/users/me/trial` deixam de existir; o Office passa a consultar
  `GET /api/tenants/{tenantId}/plan` (RF-020.6).
- `ETenantMembershipRole` ganha os valores `Admin`, `Default` e `Technical`
  ao lado do `Owner` já existente; qualquer código, teste ou documentação
  que assumia `Owner` como único papel de controle precisa considerar a
  nova distinção Owner (legal/plano) vs. Admin (gestão operacional).
- O RDS de desenvolvimento terá seu schema apagado e recriado; qualquer
  dado de teste manual existente será perdido (aceito pelo usuário).
- Um `TenantPlan` nunca é apagado por expiração — apenas transiciona de
  status (RN-020.5); a API deve garantir que todo tenant tenha sempre um
  `TenantPlan`, mesmo que em estado `Expired`.
- A ativação do Agente Coletor deixa de depender de
  `ValidationStatus = Succeeded` da credencial iService; passa a depender
  somente do `TenantPlan` ativo do tenant. `IServiceCredentialValidationService`
  continua existindo para informar o status ao usuário, mas não bloqueia
  mais a ativação.
- ADR-015 permanece válida quanto à regra temporal do Trial (UTC, 7 dias,
  não bloqueia login) e ao contrato interno de elegibilidade do Coletor;
  fica parcialmente substituída apenas quanto ao modelo de persistência
  (`TrialSubscription` → `Plan`/`TenantPlan`).
- O Agente Coletor não é alterado nesta decisão.

## Agentes envolvidos

- product-analyst (Paula): requisitos RF-020 e RF-021.
- software-architect (Sérgio): modelo de dados e squash de migrations.
- backend-engineer: implementação em `apps/api/Atua.Api`.
- qa-engineer: validação de build, testes e critérios de aceite.
- documentation: manutenção de RF-020, RF-021 e desta ADR.

## Data

2026-09-04

## Substitui

Substitui parcialmente ADR-015: mantém a regra temporal do Trial (UTC, 7
dias, não bloqueio de login) e o contrato interno de elegibilidade do
Coletor, mas troca o modelo de persistência de `TrialSubscription` para
`Plan`/`TenantPlan`.
