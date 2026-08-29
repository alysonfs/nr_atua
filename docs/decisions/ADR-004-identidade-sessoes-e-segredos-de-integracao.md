# ADR-004 - Identidade, sessoes e segredos de integracao

## Status

Accepted

## Contexto

O MVP possui um Owner por tenant, um superadministrador global `ROOT`,
credenciais locais, confirmacao de e-mail e integracao com o iService. A Master
API e o Agente Coletor precisam acessar credenciais e sessoes CAS sem expo-las
a usuarios, frontends, logs ou outros tenants.

O projeto usa PostgreSQL como fonte de verdade, Amazon SES para e-mails e uma
API consumida por SPAs em subdominios distintos.

## Decisao

### Identidade e tenant

- Cada tenant possui exatamente um usuario com papel `OWNER` no MVP.
- `OWNER` pertence a exatamente um tenant e acessa somente seus recursos no
  Office.
- `ROOT` e uma identidade global, sem tenant, que acessa apenas o Manager e as
  capacidades administrativas aprovadas.
- O primeiro `ROOT` sera criado no primeiro deploy por segredo de bootstrap de
  uso unico. O segredo deve ser removido ou desabilitado apos a criacao.
- O banco deve impor `tenant_id` obrigatorio para `OWNER`, ausente para `ROOT`
  e no maximo um Owner ativo por tenant.
- Todos os recursos internos do ATUA devem usar UUIDv7 como chave primaria e
  identificador exposto em contratos. IDs sequenciais nao devem ser usados.

### Senhas, e-mail e sessao

- Senhas de usuarios serao armazenadas apenas como hash adaptativo com sal,
  usando Argon2id. SHA-256 nao pode ser usado para senhas.
- Amazon SES enviara confirmacao de cadastro e avisos de expiracao do Trial.
- A sessao tera JWT de acesso com curta duracao e refresh token opaco, aleatorio
  e rotacionado, entregue apenas em cookie `HttpOnly`, `Secure` e com `SameSite`
  restritivo compativel com os dominios aprovados.
- Apenas o hash do refresh token sera guardado no PostgreSQL. O uso de token
  ja rotacionado revoga toda a sua familia e exige novo login.
- Signout, alteracao de senha, desativacao de conta e eventos administrativos
  relevantes devem revogar os refresh tokens da conta afetada.
- O JWT contem apenas claims minimas de identidade, papel, tenant quando
  aplicavel, sessao, emissao, expiracao, emissor e audiencia.

### Credenciais iService e sessao CAS

- Credenciais iService e estado de sessao CAS serao persistidos apenas no
  PostgreSQL, cifrados antes da gravacao.
- Senha do Owner nao sera usada como chave de cifra e sua troca nao recifrara
  credenciais iService. A aplicacao nao armazena senhas em claro.
- Cada integracao tera uma chave de dados aleatoria, usada por cifra autenticada
  AES-256-GCM para os campos sensiveis e sessao CAS.
- A chave de dados sera armazenada cifrada por uma chave de criptografia externa
  a aplicacao. O PostgreSQL guarda somente ciphertext, nonce, tag, versao do
  algoritmo e identificador da chave.
- A chave externa sera gerenciada pelo AWS KMS; o segredo de bootstrap e demais
  configuracoes sensiveis serao fornecidos pelo AWS Secrets Manager.
- A sessao CAS deve ser invalidada quando a credencial iService mudar, quando a
  validacao falhar definitivamente, quando a integracao for desativada ou quando
  o tenant for removido.
- O Agente Coletor recebe segredos somente durante sua execucao e apenas para o
  tenant e a integracao que esta processando.

### Auditoria e protecao

- Nenhuma senha, token, cookie, chave, URL assinada ou ciphertext completo pode
  aparecer em logs, respostas de API, excecoes serializadas, snapshots ou
  frontends.
- A Master API deve auditar, sem registrar segredos: cadastro, confirmacao,
  login, logout, rotacao e revogacao de sessao, operacoes `ROOT`, alteracoes de
  plano, configuracao e validacao da integracao e uso operacional de segredo.
- O acesso a decifragem e permitido apenas a identidades de execucao da Master
  API e do Agente Coletor; frontends e operadores humanos nao recebem essa
  permissao por padrao.

### Descoberta do iService

O Agente Coletor usara Playwright .NET para descobrir e validar paginacao,
filtros, estabilidade dos identificadores `workOrderNo` e `workOrderId` e os
limites reais das consultas do iService. Essa descoberta continua limitada a
operacoes de leitura definidas na ADR-003.

Os identificadores `workOrderNo` e `workOrderId` pertencem ao iService e devem
ser mantidos como referencias externas. Eles nao substituem o UUIDv7 atribuido
aos recursos internos do ATUA.

## Motivos

- Argon2id reduz o risco de ataques offline contra senhas comprometidas.
- JWT curto e refresh token rotacionado equilibram experiencia de SPA e
  revogacao de sessao.
- Separar a chave de criptografia da senha do Owner permite troca e recuperacao
  de senha sem afetar a integracao do cliente.
- KMS, Secrets Manager e SES sao servicos gerenciados que evitam operar cofre,
  servidor de e-mail e chaves manualmente no MVP.
- Um Owner por tenant reduz o modelo de autorizacao inicial sem perder o
  isolamento entre clientes.
- UUIDv7 evita IDs previsiveis e preserva uma ordenacao temporal conveniente
  para indices e registros distribuidos.

## Alternativas consideradas

### SHA-256 para senhas

Rejeitada porque e uma funcao rapida e inadequada para armazenamento de senha.

### Senha do Owner como chave de criptografia

Rejeitada porque exigiria material equivalente a senha em claro, acoplaria a
troca de senha a segredos de integracao e ampliaria o impacto de comprometimento
da conta.

### Chave simetrica fixa em variavel de ambiente

Rejeitada porque nao oferece a mesma separacao, controle de acesso e rotacao do
AWS KMS.

### IDs sequenciais

Rejeitada porque sao previsiveis e dificultam a geracao distribuida de
identificadores sem coordenacao central.

## Consequencias

- A Master API devera manter usuarios, sessoes, tokens, integracoes, sessoes CAS
  e eventos de auditoria no PostgreSQL.
- Entidades, eventos e recursos internos expostos pela API deverao receber
  UUIDv7; referencias externas, como os IDs do iService, permanecem em campos
  separados.
- A infraestrutura devera definir IAM de menor privilegio, custo e rotacao de
  KMS, Secrets Manager e SES por ambiente.
- Recuperacao de senha, multiplos membros por tenant e login federado continuam
  fora do MVP e exigirao requisitos adicionais.
- A descoberta Playwright do iService deve ser tratada como spike de leitura e
  nao pode introduzir acoes de escrita no provedor.

## Agentes envolvidos

- Usuario: regras de sessao, Owner, ROOT e armazenamento de credenciais.
- software-architect: arquitetura de identidade, sessao e criptografia.
- aws-architect: KMS, Secrets Manager, SES, IAM, rotacao e custos.
- backend-engineer: implementacao da Master API e Agente Coletor.

## Data

2026-08-29

## Substitui

Complementa ADR-003. Nao substitui ADR existente.
