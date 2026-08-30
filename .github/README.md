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

## Nota

O Batuta é o framework de coordenação da equipe multi-agente, distinto do
produto ATUA em si, que possui seu próprio ciclo de vida e versionamento
(`package.json`).
