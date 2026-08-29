# Instruções do projeto

Este repositório utiliza um workflow multi-agente coordenado. Antes de
executar qualquer tarefa de análise, arquitetura, implementação, QA,
documentação ou release, consulte:

- `docs/protocols/hierarchy.md` — hierarquia e autoridade dos agentes.
- `docs/protocols/communication.md` — formato obrigatório de comunicação.
- `docs/protocols/workflow.md` — estados e ciclo de vida da tarefa.
- `docs/protocols/decisions.md` — quando e como registrar decisões (ADR).

Os agentes especializados estão definidos em `.github/agents/` (`orchestrator`,
`product-analyst`, `software-architect`, `aws-architect`, `backend-engineer`,
`frontend-engineer`, `qa-engineer`, `documentation`, `release-versioning`).

Para tarefas que envolvam mais de um domínio (produto, arquitetura,
infraestrutura, implementação, QA, documentação ou release), prefira acionar
o agente `orchestrator` em vez de executar a tarefa diretamente.

Documentação viva do projeto:

- Requisitos: `docs/requirements/`
- Funcionalidades: `docs/features/`
- Arquitetura: `docs/architecture/`
- Decisões: `docs/decisions/`
- Releases: `docs/releases/`

Quando a documentação e a memória da conversa divergirem, a documentação
versionada é a fonte de verdade, salvo decisão explícita do usuário em
contrário.

Convenções C# do projeto:

- enums devem começar com `E` e o arquivo deve ter o mesmo nome;
- interfaces devem começar com `I` e o arquivo deve ter o mesmo nome.
