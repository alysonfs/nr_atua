# ADR-017 - Contrato de autenticação browser e serviço interno

## Status

Accepted

## Contexto

As ADRs 003, 004, 005 e 015 estabelecem a Master API como fronteira de
acesso, o PostgreSQL como fonte de verdade, sessões de usuário com JWT de curta
duração e refresh token opaco rotacionado, memberships para autorização por
tenant e a consulta de elegibilidade do Trial pelo Agente Coletor exclusivamente
por contrato interno da Master API.

É necessário detalhar o contrato de autenticação do browser e o contrato de
serviço interno que será usado pelo futuro Agente Coletor, sem conceder ao
coletor um JWT de usuário, acesso direto ao PostgreSQL ou contexto de tenant
confiável vindo de um JWT.

## Decisão

### Sessão do browser

- O browser receberá um JWT de acesso com duração de 15 minutos e o manterá
  somente em memória.
- O JWT de acesso conterá exclusivamente as claims `sub`, `sid`, `iss`, `aud`,
  `iat`, `nbf`, `exp`, `jti` e `role`.
- `tenant_id` não será incluído no JWT nem usado para autorização. A
  autorização por tenant continuará sendo resolvida e validada no servidor
  conforme a ADR-005.
- O refresh token será opaco e será entregue apenas em cookie `HttpOnly`,
  `Secure` e `SameSite=Strict`.
- Apenas o hash do refresh token será persistido. Sua rotação deverá ocorrer de
  forma atômica.
- A Master API validará o `sid` no PostgreSQL em cada requisição autenticada.

### Serviço interno do futuro Agente Coletor

- O futuro Agente Coletor receberá uma credencial opaca por HTTPS, vinculada a
  `tenant`, `integration` e `provider`, com o escopo
  `collector.eligibility.read`.
- O coletor não usará JWT de usuário, não acessará o PostgreSQL diretamente e
  não determinará a elegibilidade a partir de dados locais de Trial.
- O endpoint interno
  `GET /api/internal/collector/eligibility` derivará o escopo da credencial
  apresentada e retornará somente `eligible` e `evaluatedAtUtc`.
- Endpoints internos de Trial sem autorização não deverão ser publicados.

Esta ADR não cria Redis, novo serviço ou infraestrutura AWS. O coletor não será
implementado nem alterado por esta decisão documental.

## Motivos

- Um JWT de acesso curto, mantido apenas em memória, reduz a persistência de
  material de acesso no browser.
- Um refresh opaco em cookie protegido permite renovação de sessão sem expor seu
  valor ao JavaScript; hash persistido e rotação atômica preservam a detecção de
  reutilização prevista na ADR-004.
- A validação de `sid` no PostgreSQL em cada requisição mantém a revogação de
  sessão sob a fonte de verdade já definida.
- Não transportar `tenant_id` no JWT evita tratar um contexto de tenant como
  autorização permanente, em conformidade com a ADR-005.
- Uma credencial opaca e de escopo mínimo preserva a fronteira da Master API e
  limita o futuro coletor à consulta de elegibilidade definida na ADR-015.
- Retornar apenas a decisão de elegibilidade e seu instante de avaliação reduz a
  exposição de dados de Trial ao coletor.

## Alternativas consideradas

### Persistir o JWT de acesso no browser

Rejeitada porque aumenta a permanência do token de acesso no cliente.

### Incluir `tenant_id` no JWT para autorização

Rejeitada porque um usuário pode ter memberships em vários tenants e o servidor
deve validar o contexto e o membership ativos, conforme a ADR-005.

### Permitir que o coletor use JWT de usuário

Rejeitada porque mistura a identidade de usuário com a identidade de serviço e
concede ao coletor privilégios além do escopo de elegibilidade.

### Permitir acesso direto do coletor ao PostgreSQL ou expor Trial sem autorização

Rejeitada porque contorna a fronteira autorizada da Master API e contraria a
ADR-015.

### Adicionar Redis, um novo serviço ou infraestrutura AWS para este contrato

Rejeitada no escopo desta decisão, pois o PostgreSQL já é a fonte de verdade e
não há necessidade definida para esses componentes.

## Consequências

- A implementação de autenticação do browser deverá emitir e validar somente as
  claims listadas nesta ADR, manter o token de acesso somente em memória e usar
  refresh token opaco conforme o cookie e a rotação definidos.
- Cada requisição autenticada deverá consultar o PostgreSQL para validar o
  `sid`.
- A implementação futura do coletor deverá autenticar-se por credencial opaca
  HTTPS e poderá consultar somente o contrato interno autorizado pelo escopo
  `collector.eligibility.read`.
- O contrato de elegibilidade interno não poderá retornar dados adicionais de
  Trial, tenant ou integração além de `eligible` e `evaluatedAtUtc`.
- A implementação aguarda o aceite desta ADR. Nenhum endpoint interno de Trial
  sem autorização deve ser publicado antes ou depois desse aceite.

## Agentes envolvidos

- software-architect (Sérgio): definição da arquitetura e dos contratos de
  autenticação.
- backend-engineer: futura implementação da Master API, validação de sessão e
  contrato interno.
- frontend-engineer: futura manutenção do token de acesso somente em memória no
  browser.
- qa-engineer: futura validação dos contratos, revogação por `sid`, isolamento e
  restrições de resposta.
- documentation: registro da decisão arquitetural.

## Data

2026-08-29

## Substitui

Não aplicável.
