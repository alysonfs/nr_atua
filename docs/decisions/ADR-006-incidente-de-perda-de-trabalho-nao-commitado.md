# ADR-006 - Incidente de perda de trabalho nao commitado

## Status

Accepted

## Contexto

Durante a organizacao retroativa do backend em commits incrementais (apos a
correcao do RF-001), duas execucoes do `backend-engineer` retornaram sem
resposta final ("Agent completed with no output"). Entre uma execucao e
outra, uma operacao destrutiva de Git (equivalente a um `git stash` seguido
de reversao do working tree, possivelmente combinada com remocao de arquivos
nao rastreados) apagou/reverteu todo o trabalho ainda nao commitado: o
endpoint `POST /auth/signup` ([AuthEndpoints.cs](../../apps/api/Atua.Api/Endpoints/AuthEndpoints.cs)),
cinco arquivos de teste ([SignUpServiceTests.cs](../../tests/Atua.Api.Tests/SignUpServiceTests.cs),
[AuthEndpointsTests.cs](../../tests/Atua.Api.Tests/AuthEndpointsTests.cs),
[EmailConfirmationTests.cs](../../tests/Atua.Api.Tests/EmailConfirmationTests.cs),
[PersistenceMappingTests.cs](../../tests/Atua.Api.Tests/PersistenceMappingTests.cs),
[Argon2idSecretHasherTests.cs](../../tests/Atua.Api.Tests/Argon2idSecretHasherTests.cs)),
a base de testes de dominio (`UnitTest1.cs`/`TenancyTests`), o `Program.cs`
com a composicao de DI, e edicoes recentes em
[docs/protocols/workflow.md](../protocols/workflow.md),
[docs/protocols/hierarchy.md](../protocols/hierarchy.md),
[docs/requirements/mvp-onboarding-coleta-e-supervisao.md](../requirements/mvp-onboarding-coleta-e-supervisao.md),
`.github/copilot-instructions.md`, `.github/agents/backend-engineer.agent.md`
e `.vscode/settings.json`.

A causa raiz nao pode ser determinada com certeza porque o `orchestrator` nao
possui acesso direto a terminal e depende de subagentes para executar
comandos Git; o comando exato que causou a perda nao foi identificado nos
logs disponiveis.

## Decisao

a) A maior parte do trabalho foi recuperada por meio de um `git stash` que
por acaso continha parte das alteracoes em arquivos ja rastreados,
complementado por extracao cirurgica arquivo a arquivo
(`git checkout stash@{0} -- <path>`) para evitar novos conflitos; o restante
(endpoint, testes, `Program.cs`) foi reconstruido/re-implementado.

b) A disciplina de commits incrementais definida em
[docs/protocols/workflow.md](../protocols/workflow.md) (secao 8.1) passa a ser
tratada como mitigacao primaria: nenhum trabalho relevante deve permanecer
nao commitado por mais de um incremento pequeno.

c) Agentes de implementacao ficam proibidos de executar comandos Git
destrutivos ou que afetem arquivos fora do escopo do comando pretendido
(`git clean`, `git reset --hard`, `git checkout` ou `git restore` sem
pathspec explicito de um arquivo/diretorio especifico, `git stash drop`/
`git stash pop`) sem autorizacao explicita do usuario.

d) Diante de uma falha inesperada ou execucao sem retorno, o agente deve
parar e reportar o estado exato (`git status`/`git diff`) antes de tentar
qualquer comando corretivo.

## Motivos

A causa do incidente foi a combinacao de trabalho acumulado sem commits
frequentes com uma operacao Git ampla demais executada sem supervisao.
Commits incrementais reduzem drasticamente o raio de impacto de qualquer
erro futuro, e restringir comandos destrutivos remove a possibilidade de um
agente "limpar" o repositorio inteiro para tentar sair de um estado de erro
local.

## Alternativas consideradas

### Confiar apenas na disciplina de commits incrementais ja definida

Rejeitada, pois nao impede a causa imediata (comando destrutivo executado
antes de qualquer commit).

### Proibir totalmente agentes de implementacao de executar qualquer comando Git

Centralizaria tudo no `release-versioning`. Rejeitada, pois contradiz a
decisao ja tomada de permitir commits locais incrementais pelo proprio
implementador, tornando o processo mais lento sem necessidade.

## Consequências

- Passa a existir uma lista explicita de comandos Git proibidos para
  `backend-engineer`/`frontend-engineer` sem autorizacao do usuario.
- Qualquer incidente futuro de perda de dados deve ser registrado da mesma
  forma, como novo ADR ou adenda a este.
- A recuperacao demonstrou que `git stash list` deve ser a primeira
  verificacao em qualquer suspeita de perda de trabalho antes de se presumir
  perda definitiva.

## Agentes envolvidos

- orchestrator: coordenacao da recuperacao e da resposta ao incidente.
- backend-engineer: execucao dos comandos que originaram e recuperaram o
  incidente.
- qa-engineer: autorizacao de commits incrementais.
- documentation: registro deste ADR.

## Data

2026-08-29

## Substitui

Nenhum. Complementa a disciplina de commits incrementais definida em
[docs/protocols/workflow.md](../protocols/workflow.md).
