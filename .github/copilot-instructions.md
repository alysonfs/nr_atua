# Instruções do projeto

Este conjunto de agentes, skills e protocolos compõe o framework
**Batuta**, com versionamento próprio (ver `.github/README.md`,
`.github/VERSION` e `.github/CHANGELOG.md`).

Este repositório utiliza um workflow multi-agente coordenado. Antes de
executar qualquer tarefa de análise, arquitetura, implementação, QA,
documentação, release ou marca (marketing), consulte:

- `docs/protocols/hierarchy.md` — hierarquia e autoridade dos agentes.
- `docs/protocols/communication.md` — formato obrigatório de comunicação.
- `docs/protocols/workflow.md` — estados e ciclo de vida da tarefa.
- `docs/protocols/decisions.md` — quando e como registrar decisões (ADR).
- `docs/protocols/model-and-context.md` — seleção de modelo de IA e gestão de contexto.

Os agentes Batuta especializados estão definidos em `.github/agents/` (`orchestrator`,
`product-analyst`, `software-architect`, `aws-architect`, `backend-engineer`,
`frontend-engineer`, `qa-engineer`, `documentation`, `release-versioning`).

Para tarefas que envolvam mais de um domínio (produto, arquitetura,
infraestrutura, implementação, QA, documentação, release ou marca (marketing) ), prefira acionar
o agente `orchestrator` em vez de executar a tarefa diretamente.

Convençoes sobre subagentes:
- Use o apelido do subagente ao se referir a ele nas comunicações e tarefas.
- Efetive o uso do modelo escolhido por subagente;

Documentação viva do projeto:

- Requisitos: `docs/requirements/`
- Funcionalidades: `docs/features/`
- Arquitetura: `docs/architecture/`
- Decisões: `docs/decisions/`
- Releases: `docs/releases/`
- Bugs: `docs/bugs/`
- Marca (marketing): `docs/marketing/`

Quando a documentação e a memória da conversa divergirem, a documentação
versionada é a fonte de verdade, salvo decisão explícita do usuário em
contrário.

Convenções C# do projeto:

- enums devem começar com `E` e o arquivo deve ter o mesmo nome;
- interfaces devem começar com `I` e o arquivo deve ter o mesmo nome.
- interfaces de contrato entre camadas devem ficar em diretórios específicos de contratos, seguindo a convenção de nomenclatura. ex: `src/Feature/Contracts/IFeatureService.cs`
- classes de `Application` que representam um caso de uso devem terminar em
  `UseCase` (ex.: `AddTenantUseCase`), nunca `*OnboardingService` ou nomes
  genéricos de "serviço" para operações que representam uma ação de negócio
  única e nomeável.
