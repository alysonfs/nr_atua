# Batuta

**Batuta** é o framework multi-agente que coordena o time de desenvolvimento
do ATUA: o `orchestrator` ("Otto") e os demais agentes especializados
(produto, arquitetura, backend, frontend, QA, documentação, release, marca
etc.), trabalhando como uma orquestra conduzida pela batuta do maestro.

## Onde vive

- `.github/agents/` — definições dos agentes especializados.
- `.github/skills/` — skills reutilizáveis pelos agentes.
- `.github/copilot-instructions.md` — instruções globais do workflow.
- `docs/protocols/` — protocolos de hierarquia, comunicação, workflow e
  registro de decisões (`hierarchy.md`, `communication.md`, `workflow.md`,
  `decisions.md`).

## Versionamento

O Batuta possui versionamento semântico (semver) próprio, independente do
versionamento do produto ATUA (`package.json` raiz). Veja:

- `.github/VERSION` — versão atual do Batuta.
- `.github/CHANGELOG.md` — histórico de mudanças do framework.
- `docs/decisions/ADR-019-nome-e-versionamento-do-framework-batuta.md` —
  decisão que originou o nome e o versionamento do Batuta.

## Apelidos dos agentes

Os apelidos usados na comunicação entre agentes (Otto, Paula, etc.) estão
documentados em `docs/protocols/communication.md`, seção "Apelidos dos
agentes".

## Skills operacionais (deploy, validação de dados, custos)

Além das skills de workflow/documentação, o Batuta reúne skills
operacionais que automatizam tarefas repetitivas de infraestrutura viva —
criadas para não regenerar os mesmos comandos manualmente a cada
verificação:

- `.github/skills/atua-deploy/` — redeploy "a quente" da API/Collector já
  rodando na AWS, disparo de ciclos de teste do Collector e leitura de
  logs (`journalctl`) das instâncias EC2. Exposta via `infra/Makefile`
  (`make redeploy-api`, `make redeploy-collector`,
  `make collector-trigger-cycle`, `make logs-api`, `make logs-collector`,
  entre outros).
- `.github/skills/atua-mongo-inspect/` — consulta somente leitura ao
  MongoDB Atlas (`make mongo-peek COLLECTION=<nome>`,
  `make mongo-peek-all`) para validar se os dados coletados chegaram e com
  a qualidade esperada.
- `.github/skills/atua-pg-inspect/` — consulta somente leitura ao RDS
  PostgreSQL via túnel SSH (o RDS é privado, `make pg-peek
  TABLE=<nome>`, `make pg-peek-all`) para validar se os dados relacionais
  (ex.: `work_orders`, `work_order_histories` do RF-017/ADR-023) chegaram
  e com a qualidade esperada.
- `.github/skills/aws-cost-monitoring/` — consulta de gastos AWS via Cost
  Explorer/Budgets, somente leitura.

Cada skill documenta seus pré-requisitos e permissões mínimas no próprio
`SKILL.md`. Scripts que fazem parte de uma skill vivem em
`.github/skills/<nome-da-skill>/scripts/` — não em `infra/scripts/`, que é
reservado ao ciclo de vida declarativo da infraestrutura (`make up`/
`make down`, RDS).

### Configuração de `.env` para as skills operacionais

As skills acima rodam **na sua máquina**, não no runtime da aplicação, e
precisam de credenciais/configuração local (profile AWS, caminho da chave
SSH, ID do secret do Mongo, etc.). Essas variáveis estão documentadas na
seção "OPERAÇÃO LOCAL — SKILLS BATUTA" do `.env.example` na raiz do
repositório — copie para `.env.local` (nunca commitado) e ajuste os
valores ao seu ambiente antes de rodar qualquer target de deploy/validação.
Ao criar uma nova skill operacional que exija credenciais, adicione as
variáveis correspondentes nessa mesma seção do `.env.example`.

## Nota

O Batuta é o framework de coordenação da equipe multi-agente, distinto do
produto ATUA em si, que possui seu próprio ciclo de vida e versionamento
(`package.json`).
