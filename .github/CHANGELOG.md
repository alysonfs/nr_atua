# Changelog do Batuta

Todas as mudanças relevantes no framework multi-agente **Batuta** (definições
de agentes em `.github/agents/`, skills em `.github/skills/`,
`.github/copilot-instructions.md` e protocolos em `docs/protocols/`) são
registradas neste arquivo.

Este changelog é independente do changelog do produto ATUA.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/) e
o versionamento segue [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [0.1.1] - 2026-09-11

### Changed

- Recalibrado o agente `qa-engineer` (ADR-025): adicionada regra de
  agilidade e escopo com prioridade máxima (proíbe descoberta de ambiente
  por conta própria, scripts descartáveis fora do framework de testes e
  auditoria exaustiva desproporcional ao escopo da tarefa) e trocado o
  modelo padrão de Claude Sonnet 5 para Claude Haiku 4.5.

## [0.1.0] - 2026-08-30

### Added

- Nome e versionamento próprios do framework multi-agente: **Batuta** (ADR-019).
- Consolidação da estrutura existente sob o nome Batuta: 15 agentes especializados
  em `.github/agents/`, skills em `.github/skills/`, protocolos de hierarquia,
  comunicação, workflow e decisões em `docs/protocols/`.
