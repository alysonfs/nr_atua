# Contexto do projeto ATUA (para compartilhar com outras IAs)

> Cole este documento no início de uma conversa com ChatGPT/DeepSeek/etc. para dar
> contexto do projeto sem precisar reexplicar tudo. Atualize conforme o projeto evoluir.

## 1. O que é o produto

**ATUA** (nome de marca em transição para **ATYNO**, ainda não confirmado
juridicamente) é um SaaS de **monitoramento e enriquecimento automático de
Ordens de Serviço (OS)** para empresas de assistência técnica (ex.: refrigeração)
que usam sistemas legados como **ICS-AMER / iService**. Um agente automatizado
coleta periodicamente dados do sistema legado (via automação de navegador),
enriquece e persiste esses dados, e expõe tudo via API/dashboards para as
equipes de atendimento, técnicos e gestão.

Empresa por trás: Moldato Soluções Tecnológicas. Sede: Natal/RN.

## 2. Arquitetura macro (monorepo)

```
apps/
  api/Atua.Api             -> ASP.NET Core (.NET 10), Master API, Minimal APIs
  collector/Atua.Collector -> Worker Service .NET, agente coletor (Playwright)
  landing/                 -> SPA React (site público)
  office/                  -> SPA React (portal do cliente/atendimento)
  manager/                 -> SPA React (superadmin)
  tecnica/                 -> PWA React mobile-first (equipe técnica em campo)
infra/                     -> AWS CDK (TypeScript) - IaC
packages/                  -> config compartilhada (eslint, tsconfig)
tests/                     -> Atua.Api.Tests, Atua.Collector.Tests
docs/                      -> documentação viva (requirements, architecture, decisions, features)
```

Fluxo de dados:
- `Atua.Collector` roda como EC2 efêmero, automatiza login/navegação no
  iService via Playwright, chama a API (`ICollectorApiClient`) para persistir
  OS/eventos, e também grava documentos "brutos" no MongoDB Atlas.
- `Atua.Api` é a fonte de verdade transacional: PostgreSQL (RDS) via EF Core.
- As 4 SPAs consomem a API via HTTP + JWT Bearer.
- Frontends são build estático hospedado em S3 + CloudFront, DNS em
  Route 53 (domínio atyno.com.br).

## 3. Stack tecnológica

| Camada | Tecnologia |
|---|---|
| API | ASP.NET Core 10 (Minimal APIs), EF Core + Npgsql, PostgreSQL |
| Collector | Worker Service .NET 10, Playwright (~1.62), MongoDB Atlas |
| Auth | JWT Bearer (chave simétrica) |
| Email | AWS SES v2 |
| Segredos/criptografia | AWS Secrets Manager + AWS KMS |
| Frontends | React 19 + TypeScript, Vite, Tailwind CSS + daisyUI |
| Frontend extras | react-i18next (i18n), React Router v7 e TanStack Table (no Office), Zod (validação, Office) |
| Package manager | pnpm + Turborepo (workspaces TS); .NET usa SDK/solution própria |
| Infra | AWS CDK em TypeScript (stacks em sa-east-1) |

## 4. Convenções de código (C# / backend)

- **Enums**: prefixo `E`, arquivo com mesmo nome (`EWorkOrderStatus.cs`).
- **Interfaces**: prefixo `I`, arquivo com mesmo nome (`IWorkOrderService.cs`).
- **Contratos entre camadas**: interfaces ficam em diretórios `Contracts/`
  (ex.: `src/Feature/Contracts/IFeatureService.cs`).
- **Casos de uso**: classes de `Application` que representam uma ação de
  negócio única terminam em `UseCase` (ex.: `AddTenantUseCase`), nunca
  `*Service`/`*OnboardingService` genérico.
- Camadas típicas: `Application/` (use cases/orquestração), `Domain/`a
  (entidades e regras), `Endpoints/` (Minimal APIs), `Infrastructure/`
  (EF Core, repositórios, integrações externas).

## 5. Infraestrutura AWS (visão rápida)

VPC (subnets públicas/privadas em 2 AZs) · RDS PostgreSQL · EC2 efêmero para API
e Collector · S3 (build estático das 4 SPAs) · CloudFront · Route 53 · ACM ·
AWS KMS · AWS Secrets Manager · SES v2 · MongoDB Atlas (fora da AWS) · CDK como
IaC. Detalhes completos e fluxo de deploy na seção 7.

## 6. Fluxo de trabalho / hierarquia de agentes (uso interno com Copilot)

Hierarquia: `orchestrator` coordena `product-analyst`, `software-architect`
(que coordena `backend-engineer` e `frontend-engineer`), `aws-architect` e
`qa-engineer` (que aciona `documentation` e depois `release-versioning`).
Ciclo de vida de tarefa: `NEW -> ANALYSIS -> REQUIREMENTS_DEFINED ->
ARCHITECTURE_DEFINED -> IMPLEMENTATION -> VALIDATION -> DOCUMENTATION ->
RELEASE_READY -> DONE` (com desvios para `BLOCKED`/`CANCELLED`). Isso é
específico do fluxo com GitHub Copilot neste repo — não é relevante para
ChatGPT/DeepSeek ajudando em código pontual, mas explica por que a
documentação em `docs/` é a fonte de verdade.

## 7. Arquitetura AWS e deploy

- **Região/conta**: `sa-east-1` (São Paulo), conta Moldato, perfil CLI
  `moldato`. Infra provisionada via 4 stacks CDK (TypeScript v2):
  `AtuaNetworkStack` (VPC), `AtuaDataStack` (RDS/segredos), `AtuaComputeStack`
  (EC2 API+Collector), `AtuaDomainStack` (Route 53/ACM/CloudFront).
- **Operação diária** (via `infra/Makefile`): `make up` sobe dados + EC2,
  `make down` derruba EC2/EBS (com snapshot do RDS) e preserva
  rede/S3/segredos — usado para controlar custo (ambiente não fica 24/7).
- **Rede**: 1 VPC, 1 subnet pública + 2 isoladas (`sa-east-1a`/`1b`), sem NAT
  Gateway (custo). SSH restrito ao IP do operador; 3 security groups (API,
  RDS, Collector).
- **Banco de dados**: RDS PostgreSQL `atua-postgres-mvp` (16.13,
  `db.t3.micro`, 20GB `gp2`, single-AZ, retenção de backup de 1 dia —
  Free Tier) é a fonte de verdade transacional. MongoDB Atlas (free tier,
  banco `atua`) guarda snapshots/documentos brutos coletados pelo Collector.
- **API (`Atua.Api`)**: hospedada em EC2 `t3.micro` efêmera
  (`atua-api-master`), rodando como serviço systemd (`atua-api.service`),
  artefato publicado (`dotnet publish -c Release`) via S3
  (`releases/latest/`). IP público muda a cada `make up` (sem Elastic IP);
  DNS de origem é resincronizado automaticamente (`make sync-api-domain`).
- **Collector (`Atua.Collector`)**: mesmo padrão da API (EC2 efêmera
  `atua-collector-base`, systemd `atua-collector.service`, publish
  `linux-x64 --self-contained false`, S3 `releases/collector-latest/`).
  Depende de credenciais em `service_credentials` no Postgres para chamar a
  API (`/claim`, `/complete`, `/eligibility`); sem isso retorna 401.
- **Frontends (landing/office/manager/tecnica)**: build Vite (`dist/`)
  publicado em bucket S3 privado com um prefixo por app, servido via
  CloudFront (Origin Access Control). Domínios:
  `atyno.com.br`/`www` (landing), `office.`, `manager.`, `tecnica.`,
  `api.` — todos subdomínios de `atyno.com.br`. Deploy manual via
  `make deploy-<app>`. Sem CI/CD automatizado ainda.
- **DNS/certificados**: domínio `.com.br` registrado no GoDaddy, zona
  autoritativa em Route 53; certificado ACM em `us-east-1` (exigência do
  CloudFront) cobrindo os subdomínios.
- **Segredos**: AWS Secrets Manager para credenciais RDS
  (`atua/rds-postgres`), MongoDB Atlas (`atua/mongodb-atlas`) e segredos de
  app (`atua/app-secrets`); chave SSH da EC2 no SSM Parameter Store. Uma
  CMK do KMS (`alias/atua-credential-cipher-dev-mvp`) faz criptografia de
  envelope das credenciais de integração (DEK por integração, não por
  tenant); role da API tem `kms:GenerateDataKey/Decrypt/DescribeKey`, role
  do Collector não tem acesso direto ao KMS.
- **Redeploy "a quente"** (skill `atua-deploy`): publish local → `aws s3
  sync` para o prefixo `latest`/`collector-latest` → SSH na EC2 (stop do
  systemd, resync do S3, start do systemd) → verificação de MD5 local vs.
  remoto → checagem de `systemctl status`/`journalctl`. Sem rollback
  automatizado (S3 sobrescreve com `--delete`); rollback é manual
  (`git checkout <commit>` + redeploy).
- **Riscos/débitos técnicos conhecidos**: execution role do CDK com
  `AdministratorAccess` (crítico, bloqueador antes de dados reais de
  clientes); sem Elastic IP (IP da EC2 muda a cada `make up`, mitigado por
  script de sync automático); sem versionamento de artefatos no S3; sem
  CI/CD (deploy manual com credenciais locais); retenção de backup do RDS
  limitada a 1 dia (Free Tier).

## 8. Descrição visual — Landing (site institucional público)

- **Rotas**: SPA de página única (sem router), seções em scroll contínuo:
  Hero → Platform (4 cards de valor) → Audience (3 provedores) → Trust (3
  cards de confiança) → Steps (3 passos: criar conta / conectar fontes /
  ativar coletor) → Final CTA → Footer. Links externos rígidos para
  `/login` e `/cadastro` no Office (subdomínio office.atyno.com.br).
- **Layout**: sem sidebar; header minimalista só com seletor de idioma
  (pt-BR/en-US/es-AR). Hero em grid 2 colunas no desktop (texto+CTA vs.
  diagrama visual escuro). Seções com container max-width 6xl (1152px).
  Footer escuro (slate-950) com logo em negativo.
- **Paleta**: Deep Navy `#0F172A` (headings/footer), Tech Blue
  `#3B82F6`→`#2563EB` (gradiente, CTAs/links), Slate Gray `#64748B`
  (texto secundário), superfícies `#F8FAFC`/`#FFFFFF`/`#E2E8F0`/`#F1F5F9`.
  Tema daisyUI customizado chamado `atua` (light only, sem dark mode).
- **Tipografia**: Sora para headlines (bold/semibold, tracking apertado
  -0.02/-0.03em), Inter para corpo de texto (16-18px).
- **Componentes recorrentes**: botões (`btn-primary` gradiente azul,
  `btn-outline` navy, `btn-ghost`), cards `rounded-2xl` com sombra suave e
  acento superior em barra azul, badges `rounded-full` azul claro.
- **Logo**: `assets/logo-bg-light.svg` (fundo claro) e
  `assets/logo-bg-dark.svg` (fundo escuro), wordmark horizontal.
- **Responsividade**: mobile-first, breakpoints sm/lg/xl, hero vira 1 coluna
  no mobile, tipografia e paddings reduzem em telas menores.
- **Design system documentado em**: `docs/architecture/design-system.md` e
  `docs/DESIGN.md` (brand pillar: "Unified Technical Connectivity"; shapes
  geométricos precisos; elevação com sombras suaves).

## 10. Descrição visual — Office (portal logado do cliente/atendimento)

- **Rotas**: público (`/login`, `/cadastro`, `/confirmar-email`) e protegido
  via `AppLayout` (`/home` dashboard, `/service-orders` listagem de OS,
  `/work-orders/:workOrderId` detalhe de OS, `/settings` configurações).
  Raiz `/` redireciona para `/home`.
- **Layout master (`AppLayout`)**: desktop = Sidebar fixo à esquerda
  (dark, `bg-slate-950`, w-56, logo + navegação) + coluna direita com
  Header sticky (branco, 64px, trial badge, notificações, seletor de
  idioma, menu de usuário) + conteúdo principal em grid de 12 colunas com
  breadcrumbs. Mobile: sidebar escondido, `MobileMenu` em overlay
  full-screen acionado por ícone hambúrguer no header.
- **Paleta**: `--color-atua-navy #0F172A`, `--color-atua-blue #3B82F6`
  (+ dark `#2563EB`), tokens de status harmonizados: success `#16A34A`,
  danger `#DC2626`, info `#0EA5E9`, warning `#D97706`, neutral `#475569`.
  Superfícies iguais à Landing. Fonte corporativa Aptos (fallback Helvetica
  Neue) além de Sora/Inter do design system.
- **Tipografia**: títulos de página `text-2xl/3xl font-semibold`, seções em
  uppercase pequeno (`text-sm font-semibold uppercase text-slate-500`).
- **Componentes recorrentes**: Card compound (`Card.Header/Body/Footer`),
  DataTable com TanStack Table (header sticky, zebra striping, paginação
  10/15/20/25/50), badges de status por "tone" (success/danger/info/
  warning/neutral, `rounded-full` com anel de cor), KPI Cards (label +
  valor + delta colorido + sparkline via ApexCharts), gráficos de tendência
  e distribuição de status (ApexCharts line/donut), inputs com foco azul,
  toasts (react-toastify, canto superior direito), tooltip nativo daisyUI.
- **Dashboard (`/home`)**: KPIs (OS criadas, concluídas, taxa de conclusão,
  total ativas), gráfico de tendência (criadas vs. concluídas, 6-12 meses),
  distribuição de status (top 5 + "outros"), cards de aging (OS abertas
  há >5/>10/>30 dias) com drill-down para a listagem.
- **Service Orders (`/service-orders`)**: `StatusSummary` com cards de
  filtro por status, tabela paginada com colunas: número da OS (link para
  detalhe), status (badge), data de criação, modelo do equipamento, nome
  do consumidor, provedor. Estados de loading/erro/vazio tratados.
- **Settings**: navegação lateral (Overview, Integrations, Collector,
  Preferences), card de trial (dias restantes, alerta se <7 dias), dados
  da empresa (nome/CNPJ/papel do usuário), formulário de credenciais
  iService, seletor de timezone.
- **Auth pages (login/cadastro)**: coluna esquerda escura (desktop) com
  logo + mensagem institucional; coluna direita com card branco e
  formulário; no mobile a coluna institucional some e o card ocupa a
  largura toda.
- **Logo**: mesmos assets da Landing; sidebar usa versão para fundo escuro
  em tamanho pequeno (24px), auth pages usam versão clara/escura conforme
  a coluna.
- **Responsividade**: sidebar oculto abaixo de `sm` (640px) com menu mobile
  em overlay; grids do dashboard e settings colapsam para 1 coluna em telas
  pequenas; alvos de toque ≥44px no mobile.
- **Sem dark mode** implementado atualmente (tema único, light).

## 11. Documentação viva (fonte de verdade)

- Requisitos: `docs/requirements/`
- Funcionalidades: `docs/features/`
- Arquitetura: `docs/architecture/`
- Decisões (ADRs): `docs/decisions/`
- Bugs: `docs/bugs/`
- Marca/marketing: `docs/marketing/` (nome em transição ATUA -> ATYNO, ainda
  sem aprovação jurídica final do INPI)
