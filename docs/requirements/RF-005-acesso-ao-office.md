# RF-005 - Acesso ao Office

Status: `Pendente`

## Objetivo

Permitir que um cliente confirmado autentique-se no Office e acesse somente
os dados do(s) tenant(s) aos quais possui membership ativo, reaproveitando
integralmente a fundação de identidade e sessão já implementada
(ADR-004, ADR-005, ADR-015, ADR-017).

## Escopo

Login, seleção/troca de tenant ativo quando o usuário possuir mais de um
membership, logout e tratamento de erros de autenticação no Office. Não
inclui recuperação de senha, login federado nem múltiplos membros por
tenant além do já decidido em ADR-005 (fora do escopo do MVP).

## Requisitos funcionais

### RF-005.1 - Login por e-mail e senha

O cliente deve autenticar-se informando e-mail e senha. Em caso de sucesso,
o sistema deve emitir um JWT de acesso (15 min, mantido em memória no
browser) e um refresh token opaco rotacionado (cookie `HttpOnly`, `Secure`,
`SameSite=Strict`), conforme ADR-004 e ADR-017. Nenhum novo mecanismo de
sessão deve ser criado para o Office.

### RF-005.2 - Resolução de tenant ativo

Autenticação não determina automaticamente qual tenant é acessado. O
`tenant_id` não deve ser transportado no JWT (ADR-017). O servidor deve
resolver e validar o tenant ativo a partir do membership do usuário a cada
requisição.

- Se o usuário possuir exatamente um membership ativo, esse tenant deve ser
  selecionado automaticamente como padrão da sessão.
- Se o usuário possuir mais de um membership ativo, o sistema deve permitir
  a escolha explícita do tenant antes de liberar acesso às áreas
  dependentes de tenant.
- Se o usuário não possuir nenhum membership ativo (ainda não configurou a
  primeira integração), o Office deve conduzi-lo ao fluxo de RF-006 em vez
  de negar acesso.

### RF-005.3 - Logout

O logout deve revogar o refresh token (e, quando aplicável, sua família,
conforme ADR-004) e invalidar o `sid` correspondente no servidor. Nenhuma
lógica adicional de revogação além da já implementada deve ser criada.

### RF-005.4 - Bloqueio por Trial expirado

Trial expirado **não bloqueia login nem acesso ao Office** (ADR-015). O
login deve ser aceito normalmente; apenas a ativação do Agente Coletor e a
execução de coleta são bloqueadas pela elegibilidade de Trial, tratadas em
outros requisitos (RF-007/RF-008).

## Regras de negócio

- RN-005.1: Um usuário pode pertencer a mais de um tenant (ADR-005); a
  seleção de tenant ativo é resolvida no servidor, nunca inferida do JWT.
- RN-005.2: Credenciais inválidas (e-mail inexistente ou senha incorreta)
  devem retornar mensagem genérica de erro, sem indicar qual dado está
  incorreto (evitar enumeração de contas).
- RN-005.3: E-mail não confirmado impede login (decorrência de RF-002 já
  validado); a mensagem de erro deve orientar a confirmação pendente.
- RN-005.4: Nenhuma informação sensível (senha, token, hash) deve aparecer
  em respostas de erro, logs ou exceções (RS-001).

## Casos de borda

- Usuário com e-mail não confirmado tenta logar → deve ser bloqueado com
  mensagem específica orientando confirmação, distinta do erro de
  credenciais inválidas.
- Usuário sem nenhum tenant tenta acessar uma rota dependente de tenant →
  deve ser redirecionado ao fluxo de criação de tenant (RF-006), não deve
  receber erro genérico de autorização.
- Refresh token reutilizado após rotação (indício de comprometimento) →
  deve revogar toda a família de tokens e exigir novo login, conforme
  ADR-004.
- Tentativa de acesso a recurso de um tenant ao qual o usuário não possui
  membership ativo → deve ser negado independentemente do tenant estar
  selecionado na sessão local (validação sempre no servidor).

## Decisões mínimas assumidas para o MVP (não bloqueiam implementação)

1. Não há tela dedicada de "troca de tenant" obrigatória no MVP se, na
   prática, cada usuário tende a ter um único tenant (Owner). O mecanismo
   de seleção deve existir no contrato, mas a UI pode ser simplificada
   (ex.: seletor discreto) — detalhamento de UX cabe ao `frontend-engineer`
   /`software-architect`.
2. Não há bloqueio de conta por tentativas de login malsucedidas definido
   neste requisito (rate limiting é decisão de arquitetura/infra, a ser
   avaliada por `software-architect`/`aws-architect` se necessário).

## Critérios de aceite

1. Dado um e-mail confirmado e senha correta, quando o cliente submeter o
   login, então o sistema deve autenticar e emitir a sessão (JWT + refresh)
   conforme ADR-004/ADR-017.
2. Dado um e-mail ou senha incorretos, quando o cliente submeter o login,
   então o sistema deve rejeitar com mensagem genérica, sem indicar qual
   campo está incorreto.
3. Dado um e-mail não confirmado, quando o cliente tentar logar, então o
   sistema deve rejeitar informando que a confirmação está pendente.
4. Dado um usuário com um único membership ativo, quando autenticado, então
   o tenant correspondente deve ser resolvido automaticamente como ativo.
5. Dado um usuário com múltiplos memberships ativos, quando autenticado,
   então o sistema deve exigir a seleção explícita do tenant antes de
   liberar dados dependentes de tenant.
6. Dado um usuário autenticado, quando solicitar logout, então o refresh
   token deve ser revogado e nenhuma nova renovação de sessão deve ser
   aceita com o token anterior.
7. Dado um Trial expirado, quando o cliente logar, então o acesso ao Office
   deve ser concedido normalmente, sem bloqueio de login.
8. Dada uma tentativa de acessar dados de um tenant sem membership ativo,
   quando a requisição for processada, então o servidor deve negá-la
   independentemente do estado da sessão local.

## Dependências

- ADR-004 (identidade, sessão, segredos).
- ADR-005 (tenancy e memberships).
- ADR-015 (elegibilidade de Trial não bloqueia login).
- ADR-017 (contrato JWT + refresh, sem `tenant_id` no token).
- Implementação existente em `apps/api/Atua.Api/Application/Identity/`.

## Impactos

- Reaproveita infraestrutura de auth já implementada por Beto; não deve
  criar novo mecanismo de sessão.
- Introduz necessidade de endpoint(s) de resolução/seleção de tenant ativo
  caso ainda não existam — decisão técnica cabe a `software-architect`.

## Fora do escopo

- Recuperação de senha (já listado como fora do escopo do MVP no documento
  mestre).
- Login federado.
- Bloqueio de conta por força bruta (avaliar separadamente se necessário).
