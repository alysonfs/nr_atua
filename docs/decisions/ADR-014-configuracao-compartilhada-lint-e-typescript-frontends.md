# ADR-014 - Configuração compartilhada de lint e TypeScript para frontends

## Status

Accepted

## Contexto

Os frontends `landing`, `manager`, `office` e `tecnica` utilizam
Vite, React e TypeScript, mas possuem ferramentas de lint divergentes: `landing`
utiliza ESLint, enquanto `manager`, `office` e `tecnica` utilizam Oxlint. A
divergência dificulta manter regras e comandos consistentes no workspace.

É necessário definir uma configuração compartilhada de lint e TypeScript antes
da migração de linting, sem transferir a verificação de tipos para o lint.

## Decisão

- Criar uma configuração ESLint flat compartilhada no workspace
  `@atua/eslint-config/react-vite`.
- Aplicar as regras recomendadas de JavaScript e TypeScript, React Hooks e React
  Refresh.
- Não habilitar lint com análise de tipos (type-aware).
- Criar `@atua/tsconfig` com bases TypeScript compartilhadas e `strict: true`
  declarado explicitamente.
- Uniformizar os scripts de lint dos frontends como `eslint .`.
- Migrar os frontends para a configuração ESLint compartilhada e, após a
  migração, remover Oxlint e os arquivos `.oxlintrc`.
- Manter o build como responsável pela verificação de tipos.

## Motivos

- Eliminar a divergência atual de ferramentas e comandos entre os frontends.
- Centralizar regras de lint e bases TypeScript reutilizáveis no workspace.
- Preservar uma separação clara entre lint e verificação de tipos no build.
- Adotar uma solução de baixo custo e baixa complexidade operacional.

## Alternativas consideradas

### Manter as ferramentas e configurações atuais por aplicação

Rejeitada porque preserva regras e comandos divergentes entre frontends com a
mesma base tecnológica.

### Manter Oxlint nos aplicativos que já o utilizam

Rejeitada porque não atende ao objetivo de unificar o lint em uma configuração
ESLint compartilhada.

### Habilitar lint type-aware

Rejeitada nesta decisão. A verificação de tipos continua sendo responsabilidade
do build.

## Consequências

- Os quatro frontends passam a utilizar o mesmo pacote de configuração ESLint e
  o mesmo comando de lint.
- A configuração TypeScript compartilhada explicita o modo estrito para os
  consumidores que a adotarem.
- A migração exige a remoção posterior de Oxlint e de arquivos `.oxlintrc`.
- Erros de tipo não serão reportados pelo lint; continuarão sendo identificados
  pelo processo de build.

## Agentes envolvidos

- software-architect (Sérgio): decisão técnica.
- frontend-engineer: migração das configurações e scripts dos frontends.
- qa-engineer: validação da migração.
- documentation (Dora): registro da decisão.
- orchestrator (Otto): coordenação do workflow.

## Data

2026-08-29

## Substitui

Não aplicável.
