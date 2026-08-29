# Git Worktree — guia rápido

`worktree` é um recurso nativo do Git (não é algo específico deste
projeto ou de ferramentas de IA). Ele permite ter **múltiplas branches do
mesmo repositório, cada uma em uma pasta separada no disco**, todas
compartilhando o mesmo histórico (`.git`) sem precisar clonar o
repositório várias vezes.

## Por que isso importa aqui

Cada sessão/tarefa de agente pode operar em um worktree diferente,
apontando para uma branch diferente. Se o worktree que você está usando
foi criado a partir de uma branch desatualizada (ex.: `main`, quando o
código real está em outra branch, como aconteceu com
`agents/atualizar-status-tarefas-pendentes`, que só tinha `docs/` até ser
rebaseada em `feat-agents-config`), você pode ter a impressão de que
"faltam arquivos" quando na verdade eles só não existem *naquela
branch/worktree específico*.

## Comandos essenciais

### Listar todos os worktrees do repositório

```bash
git worktree list
```

Saída típica:

```text
/caminho/para/atua                                 83519a1 [feat-agents-config]
/caminho/para/atua.worktrees/tarefa-a               008aa6e [agents/tarefa-a]
/caminho/para/atua.worktrees/tarefa-b                008aa6e [agents/tarefa-b]
```

Cada linha mostra: caminho no disco, commit atual e branch associada.

### Ver em qual branch e worktree você está agora

```bash
git branch --show-current
git rev-parse --show-toplevel   # caminho do worktree atual
```

### Comparar branches para achar a mais atualizada

```bash
git fetch --all
git branch -a --sort=-committerdate
git log --oneline --graph <branch-1> <branch-2> <branch-3> -20
```

### Ver a partir de onde uma branch divergiu de outra

```bash
git merge-base <branch-a> <branch-b>
```

### Criar um novo worktree (não usado neste projeto até agora, mas útil saber)

```bash
git worktree add ../caminho-nova-pasta nome-da-branch
```

### Remover um worktree que não é mais necessário

```bash
git worktree remove ../caminho-da-pasta
```

## Diagnóstico rápido quando "sumir código"

1. `git worktree list` → confirme em qual worktree/branch você está.
2. `git log --oneline -5` → veja se o histórico bate com o esperado.
3. `git branch -a --sort=-committerdate` → veja qual branch tem os commits
   mais recentes.
4. Se a branch atual estiver desatualizada em relação à branch certa,
   normalmente o conserto é um `git rebase <branch-correta>` (como feito
   para trazer `apps/api`, `apps/collector`, etc. para
   `agents/atualizar-status-tarefas-pendentes`).

## Resumo

- `worktree` = comando nativo do Git, não é conceito exclusivo deste
  workflow de agentes.
- Serve para trabalhar em múltiplas branches ao mesmo tempo, em pastas
  separadas, sem múltiplos clones.
- Antes de assumir que "falta código", sempre confira com
  `git worktree list` + `git log` se você está na branch certa.
