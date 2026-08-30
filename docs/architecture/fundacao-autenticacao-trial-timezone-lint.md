# Fundação de autenticação, Trial, fuso horário e lint compartilhado

## Objetivo

Registrar, de forma enxuta, o que foi implementado nesta sessão de
desenvolvimento na Master API (`apps/api/Atua.Api`) e no workspace de
frontends, com base nas decisões já aceitas em ADR-014, ADR-015 e ADR-017.

Este documento não substitui as ADRs; ele resume o estado implementado para
consulta rápida. Em caso de divergência, as ADRs prevalecem.

## Autenticação (ADR-004, ADR-017)

- Sessão do browser autenticada por JWT de acesso com expiração de 15
  minutos, mantido em memória pelo cliente (`sub`, `sid`, `iss`, `aud`,
  `iat`, `nbf`, `exp`, `jti`, `role`; sem `tenant_id`).
- Refresh token opaco entregue somente via cookie `HttpOnly`, `Secure`,
  `SameSite=Strict` (`atua_refresh`, escopo de path `/auth`).
- Apenas o hash do refresh token é persistido; a rotação ocorre por
  compare-and-set atômico (implementada em `IRefreshTokenStore` /
  `RefreshTokenStore`).
- Sessão (`AuthSession`) persistida em PostgreSQL; cada requisição
  autenticada valida `sid` ativo no banco (`OnTokenValidated` do
  `JwtBearer`), permitindo revogação imediata por sessão.
- Endpoints expostos em `Endpoints/AuthEndpoints.cs`:
  - `POST /auth/signup`
  - `POST /auth/confirm-email`
  - `POST /auth/signin`
  - `POST /auth/refresh`
  - `POST /auth/signout` (requer sessão de browser)
- Credencial de serviço opaca para autenticação interna, sem JWT de usuário
  e sem acesso direto ao PostgreSQL pelo consumidor (`ServiceCredentialAuthenticationHandler`),
  reservada para a futura comunicação Agente Coletor ↔ Master API.
- Política de autorização `CollectorEligibility` exige a claim de escopo
  `collector.eligibility.read`.

## Trial (ADR-015)

- Expiração calculada em UTC: `00:00:00 UTC` da data UTC da confirmação de
  e-mail acrescida de sete dias.
- Trial ativo somente quando `nowUtc < expiresAtUtc`; a expiração não remove
  login, acesso ao Office nem dados — bloqueia apenas ativação do Agente
  Coletor e execução de coleta.
- Endpoint `GET /api/users/me/trial` (requer sessão de browser) retorna
  `TrialId`, `ExpiresAtUtc`, `DaysRemaining` e `Status` (`active`/`expired`).
- Endpoint interno `GET /api/internal/collector/eligibility` (requer
  credencial de serviço com escopo `collector.eligibility.read`) retorna
  somente `Eligible` e `EvaluatedAtUtc`, sem detalhes adicionais de Trial,
  tenant ou integração.

## Fuso horário (ADR-015)

- Precedência de resolução do fuso efetivo: override de `AuthSession` →
  fuso do `Tenant` → sugestão por idioma (`America/Sao_Paulo` para `pt-BR`)
  → `UTC`.
- `Tenant` persiste fuso horário em formato IANA; `User` não persiste fuso
  horário no MVP.
- Endpoints (requerem sessão de browser):
  - `PUT /api/sessions/current/timezone` — define override temporário na
    sessão atual.
  - `PUT /api/tenants/{tenantId}/timezone` — define o fuso do Tenant.
- Datas de validade e elegibilidade do Trial permanecem calculadas em UTC
  independentemente da preferência de apresentação.

## Lint compartilhado (ADR-014)

- Configuração ESLint flat compartilhada `@atua/eslint-config/react-vite`
  (regras recomendadas de JS/TS, React Hooks e React Refresh, sem
  verificação de tipos no lint).
- `@atua/tsconfig` com bases compartilhadas e `strict: true` explícito.
- Script de lint uniformizado como `eslint .` nos frontends
  (`landing`, `manager`, `office`, `tecnica`).
- Oxlint e arquivos `.oxlintrc` removidos após a migração.

## Validação (QA)

- 41 testes de frontend e 54 testes de backend cobrem o ciclo de vida do
  Trial, a precedência de fuso horário e os fluxos de autenticação
  descritos acima. Ver RF-003 e RF-004 em
  `docs/requirements/mvp-onboarding-coleta-e-supervisao.md`.

## Documentação de API

A Master API gera sua documentação OpenAPI automaticamente via
`Microsoft.AspNetCore.OpenApi` (`builder.Services.AddOpenApi()` e
`app.MapOpenApi()` em `Program.cs`, habilitado em ambiente de
desenvolvimento). Os endpoints de autenticação (`signin`, `refresh`,
`signout`) e o endpoint interno
`GET /api/internal/collector/eligibility` são mapeados via Minimal APIs em
`Endpoints/AuthEndpoints.cs` e `Endpoints/TrialEndpoints.cs` e, portanto,
aparecem no documento OpenAPI gerado automaticamente a partir das
assinaturas e atributos (`.Produces(...)`, `.WithName(...)`) já
declarados no código. Não existe um arquivo OpenAPI estático mantido
manualmente neste projeto; este documento não deve ser criado enquanto a
geração automática for a fonte de verdade.

## Referências

- ADR-004 - Identidade, sessões e segredos de integração.
- ADR-005 - Tenancy, memberships e integrações.
- ADR-014 - Configuração compartilhada de lint e TypeScript para frontends.
- ADR-015 - Ciclo de vida do Trial e preferência de fuso horário.
- ADR-017 - Contrato de autenticação browser e serviço interno.
