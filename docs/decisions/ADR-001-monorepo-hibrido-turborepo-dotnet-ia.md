# ADR-001 - Monorepo hibrido com Turborepo, .NET e IA

## Status

Accepted

## Contexto

O projeto reunira aplicacao web, API backend, infraestrutura como codigo,
recuperacao aumentada por geracao (RAG) e agentes de IA em um unico
repositorio. O frontend e a infraestrutura poderao utilizar TypeScript, e a
API devera utilizar ASP.NET Core com Entity Framework Core.

E necessario manter a experiencia e os artefatos proprios do ecossistema .NET
sem abrir mao da orquestracao e do cache oferecidos pelo Turborepo aos pacotes
TypeScript.

## Decisao

O repositorio adotara a seguinte organizacao inicial:

```text
apps/
  web/       # Aplicacao frontend TypeScript
  api/       # API ASP.NET Core com Entity Framework Core
  ai/        # Servicos de RAG e agentes de IA
infra/       # Infraestrutura como codigo em TypeScript
packages/    # Pacotes TypeScript compartilhados, quando necessarios
docs/        # Documentacao do projeto
```

O Turborepo sera utilizado para orquestrar os workspaces TypeScript e os
comandos de desenvolvimento, build, teste e deploy que forem compativeis com
essa ferramenta.

O pnpm sera o gerenciador de pacotes e workspaces TypeScript do monorepo. O
arquivo `pnpm-lock.yaml` devera ser versionado para manter instalacoes e builds
reproduziveis.

O backend em `apps/api` permanecera um projeto .NET convencional, com seus
arquivos `.sln` e `.csproj`; restauracao de dependencias, compilacao, testes e
migrations serao executados pelo SDK .NET. O Turborepo podera coordenar esses
comandos a partir da raiz, mas nao substituira o gerenciamento de dependencias
do .NET.

`apps/ai` sera a fronteira inicial para RAG e agentes de IA. A linguagem,
frameworks, provedor de modelos, banco vetorial e contratos com a API serao
definidos por decisoes posteriores, conforme os requisitos do produto.

## Motivos

- Mantem frontend, backend, IA e infraestrutura proximos e rastreaveis.
- Preserva as ferramentas nativas do ASP.NET Core e do Entity Framework Core.
- Permite compartilhar configuracoes e pacotes TypeScript sem acoplar a API ao
  ecossistema Node.js.
- Cria uma fronteira explicita para as capacidades de RAG e agentes de IA.
- Evita utilizar `src/` como um agrupador de aplicacoes de naturezas distintas.

## Alternativas consideradas

### Manter todo o codigo em `src/`

Rejeitada porque nao diferencia aplicacoes implantaveis, infraestrutura e
pacotes compartilhados, dificultando ownership e automacao.

### Adotar apenas workspaces Node.js

Rejeitada porque a API .NET deve continuar usando o SDK, a solution e os
projetos C# como fonte de verdade de dependencias e build.

### Separar frontend, backend, IA e infraestrutura em repositorios distintos

Rejeitada neste momento porque aumenta a coordenacao entre repositorios antes
de haver uma necessidade operacional que a justifique.

## Consequencias

- A raiz devera conter configuracao do Turborepo, `pnpm-workspace.yaml` e o
  arquivo `pnpm-lock.yaml` versionado.
- A API exigira SDK .NET e mantera testes e migrations no padrao .NET.
- Os contratos entre frontend, API e IA deverao ser definidos antes de
  implementacoes integradas; quando aplicavel, a API devera publicar um
  contrato versionado, como OpenAPI.
- Novas escolhas para RAG, agentes, modelos e persistencia vetorial requerem
  avaliacao arquitetural e, se relevantes, uma nova ADR.

## Agentes envolvidos

- Usuário: decisao de produto e direcionamento tecnologico inicial.
- software-architect: registro e evolucao da arquitetura da aplicacao.
- aws-architect: decisao de infraestrutura e operacao em AWS, quando aplicavel.

## Data

2026-08-28

## Substitui

Nao aplicavel.
