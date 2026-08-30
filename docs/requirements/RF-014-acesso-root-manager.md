# RF-014 - Acesso de superadministração (ROOT) no Manager

Status: `Pendente`

## Objetivo

Permitir que o superadministrador autentique-se no Manager e acesse
exclusivamente as funcionalidades administrativas do ATUA — supervisão de
clientes, gestão de plano e visibilidade de bloqueio — utilizando o papel
global `ROOT`, que é independente de qualquer tenant.

O ROOT é o papel mais privilegiado do sistema. Um erro de implementação
aqui compromete todos os tenants simultaneamente. Todos os itens marcados
com ⚠️ SEGURANÇA SENSÍVEL devem ser tratados com atenção redobrada.

## Escopo

Autenticação do ROOT no Manager, aplicação da política de acesso exclusivo
por papel `ROOT`, separação entre o papel global e os papéis de tenant, e
tratamento de tentativas de acesso sem autorização. Não inclui as
funcionalidades do Manager em si (RF-015, RF-016, RF-017), recuperação de
senha, login federado nem múltiplos superadministradores além do necessário
para o MVP.

## Contexto e base existente

O enum `EGlobalUserRole { User, Root }` já existe em
`apps/api/Atua.Api/Domain/Identity/EGlobalUserRole.cs` e a propriedade
`User.GlobalRole` já está presente no modelo. Porém o papel `Root` está
**inerte**: não há endpoint, política de autorização nem regra de negócio
que o utilize no código atual.

A infraestrutura de autenticação (Argon2id, JWT de 15 min em memória,
refresh token opaco em cookie `HttpOnly`, `AuthSession`, rotação de família
de tokens) está implementada em `apps/api/` e validada (ADR-004, ADR-017).
O `.env.example` contém `BOOTSTRAP_TOKEN` e `BOOTSTRAP_ROOT_PASSWORD` como
placeholders, indicando intenção prévia de um mecanismo de bootstrap — mas
isso ainda não está implementado (ver D1).

Não existe nenhum endpoint de Manager. O app `apps/manager/` é scaffold
Vite publicado, sem conteúdo funcional.

## Requisitos funcionais

### RF-014.1 - Autenticação do ROOT

O superadministrador deve autenticar-se informando e-mail e senha. A
autenticação deve reutilizar a fundação de identidade e sessão já
implementada (ADR-004, ADR-017), sem criar novo mecanismo de sessão.

### RF-014.2 - Acesso exclusivo por papel ROOT

Somente usuários com `GlobalRole = Root` podem acessar qualquer rota do
Manager. Um usuário com `GlobalRole = User` — independentemente de qualquer
papel de tenant que possua — deve ser impedido de acessar o Manager.

### RF-014.3 - Independência entre papel ROOT e papéis de tenant

O papel `Root` é uma propriedade global do usuário e não tem relação com
`ETenantMembershipRole { Owner, Admin }`. Um usuário ROOT não precisa ser
membro de nenhum tenant para acessar o Manager. Um membro de tenant (Owner
ou Admin) não tem acesso ao Manager em virtude desse papel.

### RF-014.4 - Rejeição de acesso sem autorização ROOT

Qualquer tentativa de acessar uma rota do Manager por um usuário autenticado
sem papel `Root` deve resultar em resposta HTTP `403 Forbidden`. A resposta
não deve revelar a existência de funcionalidades internas do Manager.

### RF-014.5 - Logout

O logout do ROOT deve revogar o refresh token (e, quando aplicável, sua
família, conforme ADR-004) e invalidar o `sid` correspondente. Nenhuma
lógica adicional de revogação além da já implementada deve ser criada.

## Regras de negócio

| Número    | Regra |
|-----------|-------|
| RN-014.1  | Somente usuários com `GlobalRole = Root` podem acessar rotas do Manager; qualquer outro papel resulta em `403 Forbidden`. |
| RN-014.2  | O papel `Root` é global e independente de memberships de tenant; um ROOT não precisa pertencer a nenhum tenant. |
| RN-014.3  | O papel `Root` não concede acesso ao Office nem aos dados de tenant de nenhum cliente; a fronteira é estrita e bidirecional. (⚠️ SEGURANÇA SENSÍVEL — ver D4 para o caso de ROOT com membership de tenant.) |
| RN-014.4  | Credenciais inválidas devem retornar mensagem genérica, sem indicar qual campo está incorreto (evitar enumeração de contas — RS-001). |
| RN-014.5  | E-mail não confirmado impede login, conforme RF-002 já validado. |
| RN-014.6  | Nenhuma informação sensível (senha, token, hash) pode aparecer em respostas de erro, logs ou exceções (RS-001). |
| RN-014.7  | ⚠️ SEGURANÇA SENSÍVEL — O primeiro ROOT deve ser criado por mecanismo de bootstrap de uso único; o segredo de bootstrap deve ser desabilitado ou removido após a criação (ADR-004). |
| RN-014.8  | A Master API deve auditar, sem registrar segredos: login, logout, rotação e revogação de sessão, e operações ROOT (ADR-004 — "A Master API deve auditar... operações ROOT"). |
| RN-014.9  | O bloqueio por Trial expirado (RF-005.4) não se aplica ao ROOT, pois o ROOT não possui Trial associado. |
| RN-014.10 | O Manager não deve exibir senhas, tokens, cookies, chaves, credenciais iService nem qualquer segredo de tenant (RS-001). |

## Casos de borda

- ROOT com e-mail não confirmado tenta logar → bloqueado com mensagem
  específica orientando confirmação (mesma regra de RF-002).
- Usuário comum (`GlobalRole = User`) autentica com sucesso e tenta acessar
  uma rota do Manager diretamente → `403 Forbidden`, sem revelar detalhes
  internos.
- Refresh token ROOT reutilizado após rotação → deve revogar toda a família
  de tokens e exigir novo login (ADR-004). ⚠️ SEGURANÇA SENSÍVEL.
- ROOT acessa Manager a partir do mesmo browser que tem sessão de Office
  aberta → comportamento dependente de D4 (ver decisões pendentes).

## Critérios de aceite

1. Dado um usuário com `GlobalRole = Root` e e-mail confirmado, quando
   informar e-mail e senha corretos, então o sistema deve autenticar e emitir
   a sessão (JWT + refresh) conforme ADR-004/ADR-017 e liberar acesso ao
   Manager.

2. Dado um usuário com `GlobalRole = User` autenticado com sucesso, quando
   tentar acessar qualquer rota do Manager, então o sistema deve retornar
   `403 Forbidden` sem revelar detalhes das funcionalidades internas.

3. Dado um e-mail ou senha incorretos, quando o usuário submeter o login,
   então o sistema deve rejeitar com mensagem genérica, sem indicar qual
   campo está incorreto.

4. Dado um e-mail não confirmado, quando o usuário tentar logar, então o
   sistema deve rejeitar informando que a confirmação está pendente,
   com mensagem distinta do erro de credenciais inválidas.

5. Dado um ROOT autenticado no Manager, quando solicitar logout, então o
   refresh token deve ser revogado e nenhuma nova renovação de sessão deve
   ser aceita com o token anterior.

6. Dado um refresh token ROOT reutilizado após rotação, quando a API
   detectar a reutilização, então toda a família de tokens deve ser revogada
   e um novo login deve ser exigido.

7. Dado um usuário ROOT, quando acessar o Manager, então nenhuma senha,
   token, chave, credencial iService ou segredo de tenant deve aparecer em
   qualquer resposta ou tela do Manager.

8. Dado um usuário com papel de tenant (Owner ou Admin) sem `GlobalRole = Root`,
   quando tentar acessar qualquer rota do Manager, então o acesso deve ser
   negado com `403 Forbidden`, independentemente de qualquer membership ativo.

## Decisões pendentes

> Este é o entregável mais importante deste documento. As decisões abaixo
> devem ser resolvidas antes de iniciar a implementação. Estão ordenadas por
> impacto de bloqueio.

---

### D1 — Como nasce o primeiro ROOT? ⚠️ SEGURANÇA SENSÍVEL

**Situação:** ADR-004 define que "o primeiro ROOT será criado no primeiro
deploy por segredo de bootstrap de uso único" e que "o segredo deve ser
removido ou desabilitado após a criação". O `.env.example` possui
`BOOTSTRAP_TOKEN` e `BOOTSTRAP_ROOT_PASSWORD` como placeholders, mas não há
implementação. Não está definido se o mecanismo é: (a) um endpoint protegido
por `BOOTSTRAP_TOKEN` que cria o ROOT e invalida o token após uso; (b) um
comando de seed/migration que lê `BOOTSTRAP_ROOT_PASSWORD` e cria o usuário
no banco; ou (c) criação manual direta no banco (fora da aplicação).

**Impacto se não decidido:** sem o primeiro ROOT, o Manager não pode ser
utilizado. A opção escolhida também define como novos ROOTs serão criados no
futuro (se necessário), e envolve segurança crítica de bootstrap.

**Opções e trade-offs:**

| Opção | Vantagem | Risco |
|-------|----------|-------|
| (a) Endpoint protegido por `BOOTSTRAP_TOKEN` | Rastreável, auditável, sem acesso ao banco | Token exposto em variável de ambiente; exige invalidação atômica |
| (b) Seed/migration que lê `BOOTSTRAP_ROOT_PASSWORD` | Simples, sem endpoint exposto | Senha em variável de ambiente durante o deploy; migration não pode ser revertida facilmente |
| (c) Criação manual no banco | Não requer código | Não auditável pela aplicação; senha em claro no comando SQL; alto risco operacional |

**Recomendação da analista:** opção (a), com `BOOTSTRAP_TOKEN` lido do AWS
Secrets Manager (não de `.env` de produção), invalidado atomicamente após
uso, e com log de auditoria da criação. A opção (c) é inaceitável para
produção.

**Aguarda:** decisão do usuário e do `software-architect`.

---

### D2 — O ROOT usa o mesmo endpoint de login do Office (`/auth/signin`) ou um endpoint separado?

**Situação:** O Office utiliza `POST /auth/signin`. O Manager é um app
distinto publicado em URL diferente. ADR-017 define `aud` (audience) no JWT,
mas não especifica se ROOT e usuário comum compartilham o mesmo endpoint e
o mesmo audience.

**Impacto se não decidido:** se compartilharem o mesmo endpoint e audience,
um ROOT autenticado teria um JWT válido que poderia ser apresentado ao Office
(e vice-versa), o que pode ser indesejável. Se os audiences forem separados,
o `software-architect` precisa definir o contrato antes da implementação.

**Opções e trade-offs:**

| Opção | Vantagem | Risco |
|-------|----------|-------|
| Mesmo endpoint, mesma audience | Sem duplicação de código | Um JWT ROOT aceito pelo Office (depende de validação de papel no middleware) |
| Mesmo endpoint, audience distinta por app | Fronteiras mais rígidas | Exige dois endpoints de validação ou discriminação de audience na API |
| Endpoint separado (`/auth/manager/signin`) | Isolamento total | Duplicação; mais superfície de ataque |

**Recomendação da analista:** mesmo endpoint (`/auth/signin`), com audience
distinta para o Manager (`aud: manager`). A autorização por papel ROOT deve
ser verificada em camada de middleware específica para as rotas do Manager,
de modo que um JWT de usuário comum com `aud: office` seja rejeitado nas
rotas do Manager mesmo que chegue a ser apresentado. Decisão técnica final
cabe ao `software-architect`.

**Aguarda:** decisão do `software-architect`.

---

### D3 — Um ROOT pode também ser membro de tenant e usar o Office?

**Situação:** ADR-004 define que "ROOT é uma identidade global, sem tenant,
que acessa apenas o Manager". O documento mestre afirma que "o ROOT é uma
identidade global, sem membership obrigatório". Porém não está explícito se
um ROOT está *proibido* de ter membership de tenant ou se apenas não é
*obrigado*. Isso tem implicações de produto (o fundador que é ROOT pode
também ser cliente?) e de segurança (um ROOT com membership de tenant tem
acesso a dados de tenant via Office?).

**Impacto se não decidido:** se permitido, a implementação precisa garantir
que o contexto ROOT (Manager) e o contexto de tenant (Office) sejam
rigorosamente isolados mesmo para o mesmo usuário. Se proibido, o banco deve
impor a restrição, e o bootstrap não deve criar um ROOT com membership.

**Opções e trade-offs:**

| Opção | Vantagem | Risco |
|-------|----------|-------|
| ROOT proibido de ter membership | Separação de papéis clara; menor superfície | Fundador precisa de duas contas |
| ROOT permitido com membership | Conveniência operacional | Exige isolamento rigoroso; complexidade maior; risco de acesso cruzado |

**Recomendação da analista:** para o MVP, proibir que um ROOT possua
membership de tenant. Isso simplifica o modelo de autorização e reduz o
risco de acesso cruzado. Se o fundador precisar de acesso ao Office, deve
usar uma conta separada com papel `User` e membership `Owner`.

**Aguarda:** decisão do usuário.

---

### D4 — O ROOT enxerga dados pessoais (PII) de clientes finais presentes nas OS?

**Situação:** RF-015 permitirá ao ROOT visualizar, para cada tenant, plano,
validade, resultado da última validação do iService e estado do Agente
Coletor. Não está definido se o ROOT terá acesso às OS coletadas ou aos
campos de OS que podem conter PII (nome de técnico, nome de cliente final,
endereço, telefone — levantado no D5 do RF-009).

**Impacto se não decidido:** se o ROOT acessar OS com PII, isso conecta
diretamente à questão legal em aberto do D5 do RF-009 (LGPD) e pode criar
obrigação de conformidade adicional para o operador do ATUA como controlador.
RS-001 já proíbe o Manager de exibir segredos, mas não define a fronteira
com PII de terceiros.

**Recomendação da analista:** para o MVP, restringir o acesso do ROOT ao
estado operacional (plano, validade, estado do Agente, resultado de
validação), sem acesso às OS coletadas nem a qualquer PII de técnico ou
cliente final. Esse limite deve ser documentado e alinhado com a decisão
do D5 do RF-009.

**Aguarda:** decisão do usuário. Conectado a D5 do RF-009.

---

### D5 — Exigir MFA para ROOT no MVP?

**Situação:** O ROOT é o único papel capaz de alterar planos de todos os
tenants (RF-016). Uma conta ROOT comprometida tem impacto sistêmico. O
sistema atual não implementa MFA. Exigir MFA no MVP aumenta a proteção, mas
adiciona escopo de implementação.

**Impacto se não decidido:** sem MFA, o ROOT é protegido apenas por
e-mail/senha. Com MFA, o risco de comprometimento cai significativamente,
mas a implementação pode bloquear o MVP.

**Opções e trade-offs:**

| Opção | Proteção | Impacto no MVP |
|-------|----------|----------------|
| MFA obrigatório no MVP | Alta | Adiciona escopo; pode atrasar entrega |
| MFA opcional no MVP, obrigatório depois | Média | Não bloqueia MVP; risco aceito explicitamente |
| Sem MFA no MVP | Baixa | Risco aceito; deve ser documentado como débito de segurança |

**Recomendação da analista:** adiar MFA para pós-MVP, desde que a decisão
seja registrada explicitamente como débito de segurança aceito pelo usuário,
e que a senha do ROOT seja suficientemente forte (exigir política de senha
mínima). Decisão final do usuário.

**Aguarda:** decisão do usuário.

---

### D6 — Há trilha de auditoria das ações do ROOT? É requisito do MVP?

**Situação:** ADR-004 define que a Master API deve auditar "operações ROOT"
sem registrar segredos. Não está definido o que exatamente constitui uma
"operação ROOT" auditável no MVP, qual o formato do log, onde é persistido
e se o próprio ROOT pode acessar a trilha.

**Impacto se não decidido:** sem auditoria, ações administrativas (extensão
de plano, renovação, conversão — RF-016) não são rastreáveis, o que pode
ser problemático operacionalmente e potencialmente relevante para LGPD
(accountability do controlador).

**Recomendação da analista:** para o MVP, registrar no mínimo: login do
ROOT, logout, e cada operação de RF-016 (extensão/renovação/conversão de
plano), com `userId`, `action`, `targetTenantId`, `timestamp` e `ip`, sem
segredos. Persistência nos logs existentes da Master API é suficiente para o
MVP; solução dedicada de auditoria pode ser pós-MVP. Decisão técnica de
formato cabe ao `software-architect`.

**Aguarda:** decisão do usuário (escopo mínimo de auditoria) e do
`software-architect` (mecanismo de persistência).

---

### D7 — Rate limiting / bloqueio por força bruta no endpoint de login para ROOT?

**Situação:** RF-005 registrou como fora de escopo o bloqueio de conta por
tentativas malsucedidas ("decisão de arquitetura/infra"). Para um usuário
comum, isso é aceitável. Para o ROOT — o papel mais privilegiado do sistema
— tentativas ilimitadas de login representam risco maior.

**Impacto se não decidido:** sem rate limiting, o endpoint de login do ROOT
está exposto a ataques de força bruta irrestrita.

**Recomendação da analista:** mesmo que rate limiting geral seja pós-MVP, é
prudente definir se haverá alguma proteção específica para o ROOT (ex.: rate
limiting por IP no API Gateway ou bloqueio temporário após N tentativas).
Decisão técnica cabe ao `software-architect`/`aws-architect`.

**Aguarda:** decisão do `software-architect`/`aws-architect`.

---

## Dependências

- **ADR-004:** define o papel ROOT, o mecanismo de bootstrap, a auditoria de
  operações ROOT e a sessão de autenticação.
- **ADR-005:** define tenancy e memberships; ROOT é independente de membership.
- **ADR-017:** define o contrato JWT (claims, audience, refresh token); o
  tratamento do ROOT deve respeitar esse contrato.
- **RF-002 (implementado):** confirmação de e-mail; aplica-se ao ROOT da
  mesma forma que a usuários comuns.
- **RF-005:** acesso ao Office; RF-014 é o análogo para o Manager. Decisões
  tomadas no RF-005 devem ser verificadas quanto à compatibilidade.
- **`EGlobalUserRole` (existente, inerte):** o enum e a propriedade já
  existem; o que falta é a aplicação de políticas de autorização com base neles.
- **D5 do RF-009:** a questão de PII nas OS conecta-se diretamente ao D4
  deste documento.

## Impactos

- **Desbloqueia RF-015** (supervisão de clientes): o Manager só pode exibir
  dados de tenants se o acesso ROOT estiver implementado e protegido.
- **Desbloqueia RF-016** (gestão de plano): operações de extensão, renovação
  e conversão dependem da identidade ROOT estar ativa e auditável.
- **Desbloqueia RF-017** (visibilidade de bloqueio de escrita): exige ROOT
  autenticado no Manager.
- **Ativa o enum `EGlobalUserRole.Root`:** a implementação deste RF tornará
  o valor `Root` funcional pela primeira vez no sistema; toda política de
  autorização que depende desse papel deve ser implementada aqui e não
  gradualmente em outros RFs.
- **Não altera RF-005:** o fluxo do Office não deve ser afetado. A
  separação entre Office e Manager deve ser garantida.
- **Cria precedente de autorização global:** a forma como o middleware de
  autorização verificará `GlobalRole = Root` pode ser reutilizada por
  funcionalidades administrativas futuras; o `software-architect` deve
  considerar isso no design.

## Fora do escopo

- Funcionalidades do Manager (supervisão, gestão de plano, visibilidade de
  bloqueio) — RF-015, RF-016, RF-017.
- Recuperação de senha para ROOT (fora do escopo do MVP).
- Login federado.
- MFA (levantado como D5; decisão pendente).
- Criação de múltiplos ROOTs via interface do Manager (MVP prevê no mínimo
  um ROOT via bootstrap).
- Gerenciamento de ROOTs por outros ROOTs (pós-MVP).
