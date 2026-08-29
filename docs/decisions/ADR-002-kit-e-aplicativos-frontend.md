# ADR-002 - Kit e aplicativos frontend

## Status

Accepted

## Contexto

O ATUA tera interfaces distintas para apresentacao publica, clientes,
administracao e tecnicos. Elas possuem publicos, responsabilidades e
requisitos de uso diferentes, mas devem manter uma base tecnologica e visual
coerente.

A ADR-001 definiu o monorepo hibrido e reservou `apps/web` para o frontend.
Com a identificacao dos quatro produtos frontend, essa fronteira passa a ser
representada por aplicativos independentes em `apps/`.

## Decisao

Os aplicativos frontend serao implementados como projetos independentes:

```text
apps/
  landing/   # Apresentacao publica do ATUA
  office/    # Area restrita de clientes
  manager/   # Area restrita de superadministracao do ATUA
  tecnica/   # Area restrita de tecnicos, PWA mobile-first
```

Todos os aplicativos frontend utilizarao o seguinte kit:

- Vite como ferramenta de desenvolvimento e build.
- React com TypeScript.
- Tailwind CSS para estilos e tokens de tema.
- daisyUI como biblioteca de componentes integrada ao Tailwind CSS.
- `react-i18next` para internacionalizacao e arquivos de traducao por
  aplicativo.
- Tema claro e escuro implementado com os temas do daisyUI, preferencia do
  usuario persistida e respeito inicial a preferencia do sistema operacional.

O aplicativo `tecnica` sera uma PWA, com experiencia mobile-first para telas
de telefone e tablet. Recursos especificos de supervisao permanecem fora do
escopo desta decisao e serao definidos posteriormente.

Todos os aplicativos serao SPAs estaticas geradas pelo Vite. Os artefatos de
build serao hospedados em buckets Amazon S3 individuais por aplicativo e
ambiente. A distribuicao publica devera ocorrer por CloudFront, com buckets
privados, HTTPS e certificados gerenciados; os detalhes de DNS, certificados,
cache, fallback de rotas SPA e deploy serao definidos pelo `aws-architect`.

Os destinos iniciais pretendidos sao `atua.com.br` para `landing`,
`office.atua.com.br` para `office`, `m.atua.com.br` para `manager` e
`tecnica.atua.com.br` para `tecnica`.

## Motivos

- Separa superficies com autenticacao, permissoes e jornadas de usuario
  diferentes.
- Preserva consistencia tecnica entre todos os frontends.
- Vite e React oferecem um fluxo leve e rapido para aplicacoes TypeScript.
- Tailwind e daisyUI fornecem componentes consistentes, temas prontos e
  integracao direta com a configuracao de estilos do projeto.
- Internacionalizacao e temas sao requisitos basicos desde o inicio, evitando
  retrabalho estrutural posterior.
- A PWA em `tecnica` atende ao contexto operacional de uso prioritariamente
  movel.
- SPAs estaticas em S3 reduzem custo e eliminam a operacao de servidores de
  renderizacao para as interfaces iniciais.

## Alternativas consideradas

### Um unico frontend com rotas para todos os perfis

Rejeitada porque reuniria superficies publicas, administrativas e operacionais
com ciclos de entrega, politicas de acesso e requisitos de experiencia
diferentes.

### Criar frontends com stacks independentes

Rejeitada porque aumentaria custo de manutencao, inconsistencias visuais e
duplicacao de configuracoes.

### Usar HyperUI como catalogo de componentes

Rejeitada em favor do daisyUI, que oferece uma biblioteca gratuita de
componentes e temas integrada diretamente ao Tailwind CSS.

### Adiar internacionalizacao e suporte a temas

Rejeitada porque ambos afetam componentes, textos e tokens de estilo em toda a
aplicacao.

### Renderizacao no servidor

Rejeitada no momento porque os aplicativos nao possuem requisito que justifique
a infraestrutura e o custo operacional adicionais. Uma necessidade futura de
SEO dinamico, conteudo personalizado no servidor ou renderizacao no servidor
exigira nova ADR.

## Consequencias

- O workspace TypeScript devera reconhecer os quatro aplicativos em `apps/`.
- Componentes, configuracoes e utilitarios realmente compartilhados poderao
  ser extraidos para `packages/` quando houver necessidade concreta.
- Cada aplicativo devera possuir traducoes, tratamento de tema e testes
  adequados ao seu publico e fluxo.
- Dados, URLs e chaves embutidos no build sao publicos; nenhum segredo, token
  de servico ou credencial pode ser exposto por variaveis de ambiente do Vite.
- O frontend devera tratar rotas de cliente com fallback para `index.html` na
  distribuicao, conforme configuracao de CloudFront definida pela infraestrutura.
- O `frontend-engineer` implementara os aplicativos conforme requisitos e
  contratos aprovados; nao devera definir fluxos, planos ou permissoes ainda
  pendentes.
- Service worker, manifesto, estrategia offline e notificacoes do PWA serao
  detalhados em decisao posterior quando os requisitos de `tecnica` estiverem
  definidos.

## Agentes envolvidos

- Usuario: direcionamento de produto, aplicativos e kit frontend.
- software-architect: decisao e evolucao da arquitetura frontend.
- frontend-engineer: implementacao do kit e dos aplicativos.
- aws-architect: infraestrutura, dominios e deploy, quando aplicavel.

## Data

2026-08-28

## Substitui

Altera parcialmente ADR-001: a fronteira frontend `apps/web` e substituida
pelos aplicativos `apps/landing`, `apps/office`, `apps/manager` e
`apps/tecnica`.
