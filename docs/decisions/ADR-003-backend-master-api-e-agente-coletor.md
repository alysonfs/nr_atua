# ADR-003 - Backend, Master API e Agente Coletor

## Status

Accepted

## Contexto

O ATUA precisa de uma API central para os frontends e de agentes capazes de
coletar dados de sistemas de clientes. A primeira integracao e o ICS-AMER /
iService, cujo Agente Coletor atual comprovou a coleta da API operacional, com
reuso de sessao CAS, leitura de cinco status, enriquecimento de OS Designadas
e execucao recorrente a cada 15 minutos.

O projeto tambem precisa preservar o estado operacional atual de cada OS,
registrar seu historico de eventos e preparar o uso futuro de dados por RAG e
agentes de IA. Observabilidade gerenciada centralizada nao cabe no MVP devido
ao custo operacional.

## Decisao

O backend sera composto inicialmente por duas aplicacoes .NET em `apps/`:

```text
apps/
  api/        # Master API em ASP.NET Core
  collector/  # Worker Service .NET para agentes coletores
```

### Master API

A Master API sera um monolito modular em ASP.NET Core. Ela sera a unica
fronteira publica de acesso a dados e capacidades do ATUA, expondo contrato
OpenAPI versionado para os frontends e para integracoes autorizadas.

O primeiro modulo sera de identidade e acesso, com `signup`, `signin` e
`signout`. Ele suportara credenciais locais de login e senha e sera preparado
para login federado por Google e Meta. Credenciais locais serao enviadas por
endpoints HTTPS de autenticacao; o esquema HTTP Basic Authentication nao sera
usado como mecanismo de sessao ou autenticacao da plataforma.

Login federado usara OAuth 2.0 com OpenID Connect e Authorization Code Flow
com PKCE. Google e Meta serao provedores configuraveis; escopos, politica de
vinculacao de contas e os detalhes do provedor Meta serao definidos antes da
implementacao.

PostgreSQL sera a fonte de verdade da plataforma, incluindo identidade,
autorizacao, configuracoes de tenant, referencias de integracao e os registros
transacionais necessarios para controlar o processamento do coletor. Entity
Framework Core sera usado para a persistencia relacional e migrations.

### Agente Coletor

`apps/collector` sera um Worker Service .NET e usara Microsoft Playwright para
automatizar o ICS-AMER / iService. Cada instancia sera vinculada a um tenant,
um provedor e uma configuracao de integracao. O modelo de provisionamento de
um coletor por cliente ainda sera refinado, mas nao permitira que dados ou
credenciais de tenants distintos sejam compartilhados.

O primeiro coletor deve preservar o comportamento funcional validado:

1. Executar uma coleta imediata e recorrente, com intervalo configuravel e
   valor inicial de 15 minutos.
2. Operar exclusivamente em modo somente leitura e bloquear, no codigo e nos
  contratos expostos, acoes de escrita no iService, como aceitar, reatribuir
  ou alterar o estado de OS. Esse bloqueio nao e configuravel no MVP.
3. Reutilizar uma sessao CAS valida por tenant e renovar a autenticacao apenas
   quando a sessao nao for mais aceita pelo provedor.
4. Consultar a API operacional `queryWorkOrder` para Designado, Em
   Processamento, Pendente, Concluido e Cancelado.
5. Usar Designado como lista operacional principal e enriquecer cada OS por
   `queryOneWorkOrder`.
6. Emitir eventos versionados e idempotentes para toda observacao, transicao,
   falha ou renovacao relevante de sessao.

Credenciais do iService, cookies CAS, chaves OAuth e senhas de bancos sao
segredos. Eles nao podem constar em codigo, artefatos, snapshots ou logs. Em
producao, serao fornecidos por cofre de segredos definido pela infraestrutura;
cookies persistidos devem ser criptografados e isolados por tenant.

### Persistencia e dados

O MongoDB armazenara os documentos operacionais do Agente Coletor: eventos
recebidos, payloads de coleta, snapshots e estado atual das OS, incluindo as
listas por status. Cada evento tera, no minimo, `eventId`, `eventType`,
`schemaVersion`, `tenantId`, `providerKey`, `collectorId`,
`providerOrderId`, `capturedAt`, `occurredAt` quando conhecido e uma chave de
idempotencia. `eventId`, `tenantId` e `collectorId` sao UUIDv7 internos do
ATUA; `providerOrderId` referencia o identificador externo do iService.

O processamento de cada evento sera controlado por registros transacionais no
PostgreSQL. Assim, ele permanece a fonte de verdade para a plataforma, e o
MongoDB concentra os documentos flexiveis e operacionais originados pelo
Agente Coletor.

Redis, quando utilizado, sera transporte transitório ou cache e nao fonte de
verdade. Os frontends e `apps/ai` acessarao dados somente por contratos
autorizados da Master API, nunca diretamente por PostgreSQL, MongoDB, Redis ou
pelas credenciais de provedores.

### IA e operacao futura

RAG, LangChain, LangGraph, LLMs, embeddings e banco vetorial permanecem fora
do escopo inicial. `apps/ai` consumira contratos autorizados e dados
sanitizados. Quando houver caso de uso de busca semantica validado, a extensao
`pgvector` no PostgreSQL sera avaliada antes de um banco vetorial dedicado. A
estrategia de vetorizacao, segmentacao, retencao e controle de acesso exigira
ADR posterior.

Observabilidade gerenciada centralizada fica adiada por custo. No MVP, as
aplicacoes devem manter logs estruturados minimos, sem segredos ou dados
pessoais desnecessarios, e tratamento de erros suficiente para operacao.

## Motivos

- ASP.NET Core e EF Core atendem a API transacional e ao dominio de identidade.
- Um monolito modular reduz complexidade inicial sem impedir separacoes futuras.
- Playwright .NET permite reproduzir a automacao de navegador validada no
  coletor atual dentro do kit .NET.
- PostgreSQL e MongoDB limitam o MVP a dois bancos de dados, reduzindo custo e
  operacao sem perder os registros transacionais e os documentos operacionais.
- A Master API centraliza autorizacao e evita exposicao direta de dados,
  infraestrutura e segredos aos clientes.
- A separacao entre eventos, projecoes e IA evita usar dados brutos de OS e
  credenciais como entrada irrestrita para modelos de linguagem.

## Alternativas consideradas

### Manter o Agente Coletor Node.js como coletor definitivo

Rejeitada como arquitetura alvo porque o kit backend definido adota .NET. O
Agente Coletor atual continua sendo referencia funcional e pode apoiar a
migracao.

### Adotar Cassandra para o historico de eventos

Rejeitada no MVP porque adiciona custo e complexidade operacional antes de
haver requisitos de volume, consultas ou retencao que justifiquem um banco
especializado em series de eventos.

### Adotar banco vetorial dedicado no MVP

Rejeitada porque ainda nao ha caso de uso de busca semantica, requisitos de
embeddings, segmentacao, LGPD ou autorizacao para IA definidos.

### Adotar observabilidade gerenciada no MVP

Rejeitada por custo. A necessidade sera reavaliada com requisitos de escala e
operacao.

## Consequencias

- O monorepo devera conter solution, projetos e testes .NET em `apps/api` e
  `apps/collector`.
- A infraestrutura devera prover PostgreSQL, MongoDB, Redis quando necessario
  e um cofre de segredos, conforme decisao posterior do
  `aws-architect`.
- Autorizacao por tenant, papeis por frontend, regras de signup, duracao e
  revogacao de tokens, retencao de dados e a semantica final dos eventos devem
  ser definidas antes de implementar fluxos de producao.
- MongoDB devera ter indices e politica de retencao definidos conforme as
  consultas e a necessidade historica do produto.
- O coletor deve ter testes para isolamento de tenant, modo somente leitura,
  reuso e expiracao de sessao, idempotencia e reconciliacao de projecoes.
- O Manager pode apenas exibir o bloqueio de escrita; nao devera expor controle
  ou endpoint capaz de alterar esse estado.

## Agentes envolvidos

- Usuario: direcionamento do kit backend, persistencia e objetivos do coletor.
- software-architect: arquitetura da Master API e limites entre componentes.
- backend-engineer: implementacao da API e do Worker Service.
- aws-architect: secrets, bancos, rede, criptografia, backups e custo.
- qa-engineer: validacao de autenticacao, isolamento e comportamento coletor.

## Data

2026-08-28

## Substitui

Complementa ADR-001. Nao substitui ADR existente.
