# ADR-019 - Nome e versionamento do framework Batuta

## Status

Accepted

## Contexto

O workflow multi-agente (`.github/agents/`, `.github/skills/`,
`.github/copilot-instructions.md` e `docs/protocols/`) cresceu organicamente e
carece de identidade e versionamento proprios, separados do versionamento do
produto ATUA (`package.json` raiz).

## Decisao

Nomear esse framework de **Batuta** (referencia a batuta do maestro, coerente
com a metafora do orchestrator/Otto conduzindo a orquestra de agentes).

Escopo do Batuta: `.github/agents/`, `.github/skills/`,
`.github/copilot-instructions.md` e `docs/protocols/`. Os diretorios
`docs/decisions/`, `docs/architecture/`, `docs/requirements/` e
`docs/features/` continuam sendo memoria viva do produto, nao do framework em
si, mas podem referenciar decisoes do Batuta.

Adotar versionamento semantico (MAJOR.MINOR.PATCH) proprio do Batuta,
iniciando em 0.1.0, registrado em `.github/VERSION` e em
`.github/CHANGELOG.md`. Toda mudanca relevante em agentes, skills ou
protocolos deve gerar entrada no changelog do Batuta, seguindo o mesmo padrao
Conventional Changelog/Keep a Changelog usado pelo agente `release-versioning`
para o produto, mas em arquivo separado.

## Motivos

- Separar o ciclo de vida do framework de coordenacao do ciclo de vida do
  produto.
- Permitir rastreabilidade de quando agentes/protocolos mudam.
- Dar identidade e comunicabilidade ao conjunto.

## Alternativas consideradas

- Manter sem nome/versao: rejeitada, dificulta rastreio.
- Versionar junto do `package.json` raiz do produto: rejeitada, mistura
  ciclos de vida distintos.
- Outros nomes considerados (Maestro, Regencia): rejeitados em favor de
  Batuta por ser mais curto e objetivo.

## Consequencias

- Novos arquivos `.github/VERSION` e `.github/CHANGELOG.md`.
- Toda alteracao relevante em `.github/agents/`, `.github/skills/`,
  `.github/copilot-instructions.md` ou `docs/protocols/` deve ser acompanhada
  de entrada no changelog do Batuta.
- `orchestrator` (Otto) e `documentation` (Dora) responsaveis por manter esse
  changelog atualizado.

## Agentes envolvidos

- orchestrator
- documentation
- release-versioning

## Data

2026-08-30

## Substitui

Nao aplicavel.
