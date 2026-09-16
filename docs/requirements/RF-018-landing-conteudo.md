# RF-018 - Conteúdo da Landing Page

Status: `Implementado e publicado — falta apenas P7 (Política de Privacidade/Termos de Uso)`

> **Atualização (2026-09-02):** RF-018 está **implementado e publicado em
> produção** (`apps/landing`, commit `874b808`; deploy via `make
> deploy-landing` em
> http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/landing/).
> O bloqueador B1 (telas de login/cadastro inexistentes) foi resolvido pela
> implementação de RF-005 (Acesso à área da conta, `apps/office`, commit
> `4a12ee3`, 2026-08-31): existem rotas reais `/login` e `/cadastro`
> (react-router-dom), consumindo os endpoints `/auth/signin`/`/auth/signup`
> já existentes na API. P4 e P5 (destinos dos botões) foram resolvidas com
> `/office/cadastro` e `/office/login`. As pendências P1/P2/P3/P6 (rodapé)
> também foram resolvidas com o usuário, com validação do brand-guardian
> (Gabi) sobre consistência de voz — ver seção RODAPÉ atualizada. Todas as
> seções de conteúdo (Hero, seções 1-5, rodapé) foram transcritas fielmente
> e cobertas por 7 testes automatizados (Vitest + Testing Library), build
> limpo, sem avisos de ESLint. **Falta apenas P7** (Política de
> Privacidade/Termos de Uso) antes que o cadastro de usuários possa coletar
> e-mails em conformidade — não bloqueia o funcionamento atual da landing.
>
> **Atualização (2026-09-02, 17h):** por decisão do usuário, a landing não
> deve ficar presa ao iService. O iService passa a ser apresentado apenas como
> um dos provedores de dados possíveis, e a proposta central do ATUA passa a
> ser orientada a vários provedores: unificação operacional, conectividade
> técnica e dados organizados em uma camada única.
>
> **Complemento (2026-09-02, 17h):** a landing também não deve usar promessas
> absolutas como "nunca interfere". O posicionamento correto é evolutivo:
> nesta fase o ATUA prioriza coleta/leitura segura; automações futuras devem
> ser comunicadas como passos graduais, controlados e rastreáveis.
>
> **Diretriz de linguagem (2026-09-02, 17h):** enquanto a landing estiver em
> português, evitar termos de outros idiomas na experiência do usuário. Usar
> alternativas claras em português.

## Objetivo

Definir o conteúdo textual e a estrutura de seções da landing page pública
do ATUA, que posiciona o produto, apresenta o propósito da plataforma e
conduz o visitante ao cadastro ou ao login.

O conteúdo textual registrado neste documento foi definido pela governança
de marca e deve ser transcrito fielmente na implementação, sem alterações
editoriais.

## Escopo

Conteúdo textual, estrutura de seções, botões de ação e rodapé da landing page.
Não inclui design visual, implementação frontend, infraestrutura de
hospedagem nem autenticação.

## Contexto

O ATUA é uma plataforma operacional para vários provedores, voltada a empresas
de serviços técnicos. A landing page é o ponto de entrada público do produto.
Seu conteúdo deve comunicar claramente o propósito, os diferenciais e os
próximos passos para o visitante sem posicionar o iService como centro da
proposta de valor.

## Conteúdo aprovado pela governança de marca

### HERO

**Headline:**
> "Sua operação técnica, unificada em uma plataforma para vários provedores."

**Subheadline:**
> "O ATUA organiza dados, acompanhamento e visibilidade operacional para
> empresas de serviços técnicos — começando pelo conector iService e
> preparado para evoluir com novos provedores."

**Botões de ação:**
- `[Começar grátis]` → cadastro / período de avaliação na área da conta (RF-001) — **destino: `/office/cadastro`** (P4 resolvida)
- `[Entrar]` → login na área da conta (RF-005) — **destino: `/office/login`** (P5 resolvida)

> ⚠️ Itens de evolução: novos provedores, automações e supervisão ampliada
> devem ser comunicados como planejamento futuro, não como entrega atual
> consolidada.

---

### SEÇÃO 1 — "Uma plataforma para toda a operação"

> "O ATUA é a camada operacional para empresas de serviços técnicos que
> precisam transformar fontes dispersas em dados organizados, leitura clara
> e contexto compartilhado."

Blocos:

- **Dados organizados:** "Transforme leituras operacionais em uma base interna
  mais clara."
- **Menos atrito:** "Reduza a troca entre sistemas, planilhas e consultas
  dispersas."
- **Base para vários provedores:** "Comece pelo conector inicial sem limitar a
  evolução da plataforma."
- **Crescimento contínuo:** "Prepare a operação para novos provedores e
  automações graduais."

---

### SEÇÃO 2 — "Comece pelos provedores que sua operação já usa"

> "O ATUA nasce aberto a múltiplas fontes. iService é o conector inicial, mas
> não é o limite da plataforma."

Cards:

- iService — Conector inicial.
- Novos provedores — Planejado.
- Provedores futuros — Base expansível.

---

### SEÇÃO 3 — "Primeiro leitura confiável. Depois automação com controle."

> "Nesta fase, o ATUA coleta e organiza informações sem executar ações
> operacionais no provedor. Essa base dá visibilidade agora e prepara o
> caminho para automatizar passos futuros com regras claras, autorização e
> rastreabilidade."

Blocos:

- **Fase de leitura:** "A etapa atual prioriza coleta e organização dos dados
  do provedor."
- **Rastreabilidade:** "Comandos, tentativas e resultados ficam registrados
  para acompanhamento."
- **Automação gradual:** "Novas ações entram uma por vez, quando houver
  controle operacional suficiente."

> **Nota ao implementador:** argumento de confiança mais forte da fase inicial; destaque
> visual, sem alterar o texto.

---

### SEÇÃO 4 — "Comece em três passos"

> ⚠️ A narrativa desta seção depende das telas da área da conta (RF-005/006), que
> estão pendentes. Ver A3.

**Passo 1 — "Crie sua conta"**
> "Cadastre-se com e-mail e senha. Confirme o e-mail e seu período de
> avaliação começa automaticamente — sem cartão de crédito."

**Passo 2 — "Conecte suas fontes"**
> "Na área da sua conta, configure o conector inicial disponível para sua
> operação."

**Passo 3 — "Ative o Agente Coletor"**
> "Com a configuração pronta, ative o agente para solicitar a primeira coleta
> operacional."

> ⚠️ Coleta recorrente, automações no provedor e supervisão ampliada devem
> permanecer como evolução futura até validação explícita de produto.

---

### SEÇÃO 5 — chamada final "Pronto para enxergar sua operação com mais clareza?"

> "Comece pelo conector disponível hoje e prepare a base para ampliar suas
> integrações amanhã. Crie sua conta gratuitamente e explore o ATUA durante
> o período de avaliação."

**Botões de ação:**
- `[Criar conta grátis]` — **destino: `/office/cadastro`** (P4 resolvida)
- `[Já tenho conta — Entrar]` — **destino: `/office/login`** (P5 resolvida)

---

### RODAPÉ

> "ATUA — Plataforma operacional para empresas de serviços técnicos."

**Links:**
- Entrar
- Criar conta
- E-mail de suporte: `suporte@atua.com.br`
- Política de Privacidade (pendente — depende de P7)
- Termos de Uso (pendente — depende de P7)

**Copyright:**
> "© [ANO] Assistência Técnica Unificada Ltda. Todos os direitos reservados."

> Nota: o ano é dinâmico (gerado em build ou em runtime — decisão técnica do
> `frontend-engineer`). Sem CNPJ na linha de copyright — ver observação de
> marca abaixo.

**Aviso de fase inicial** (aprovado pelo usuário em 2026-09-02, com ajuste do
brand-guardian):
> "O ATUA está em fase inicial. Algumas funcionalidades podem estar em
> desenvolvimento ou sujeitas a alteração."

> Nota: o texto original aprovado pela marca dizia "fase de avaliação", mas
> esse termo colide com "período de avaliação" (trial do usuário), usado nas
> Seções 4 e 5. O brand-guardian (Gabi) recomendou "fase inicial" para evitar
> essa ambiguidade, sem alterar o sentido ou a honestidade da mensagem; o
> usuário aprovou a variante.

---

## Itens planejados não apresentados como entrega atual

Os itens abaixo existem como evolução de produto ou como dependências em
outros RFs, mas não devem ser apresentados na landing como entrega atual
consolidada.

| # | Tema | Tratamento na landing |
|---|--------|-------------|
| R1 | Supervisão ampliada e tempo real | Evitar prometer tempo real; usar "visibilidade operacional" em sentido amplo. |
| R2 | Coleta recorrente/monitoramento periódico | Evitar "intervalos regulares"; usar "primeira coleta operacional" enquanto o fluxo não estiver consolidado. |
| R3 | Novos provedores e automações no provedor | Comunicar como planejamento futuro/evolução gradual, não como capacidade atual. |

---

## Pendências que dependem exclusivamente do usuário/titular

| # | Pendência |
|---|-----------|
| P1 | ~~E-mail de suporte a ser exibido no rodapé~~ ✅ Resolvida: `suporte@atua.com.br` |
| P2 | ~~Razão social do titular do copyright~~ ✅ Resolvida: "Assistência Técnica Unificada Ltda." — validado pelo brand-guardian; CNPJ ainda não definido, será incluído na Política de Privacidade/Termos de Uso (P7) quando disponível, **não** publicado como placeholder mascarado na landing |
| P3 | ~~Ano do copyright~~ ✅ Resolvida: dinâmico (implementação técnica cabe ao `frontend-engineer`) |
| P4 | ~~URL da rota de cadastro na área da conta~~ ✅ Resolvida: `/office/cadastro` (implementado em `apps/office`, commit `4a12ee3`) |
| P5 | ~~URL da rota de login na área da conta~~ ✅ Resolvida: `/office/login` (implementado em `apps/office`, commit `4a12ee3`) |
| P6 | ~~Aprovação do aviso de fase inicial no rodapé~~ ✅ Resolvida: texto ajustado para "fase inicial" (ver nota no RODAPÉ acima) e aprovado pelo usuário |
| P7 | Política de Privacidade e Termos de Uso (obrigatório para coleta de e-mail no cadastro) — **inclui o CNPJ do titular quando definido** |
| P8 | Confirmar que o e-mail `suporte@atua.com.br` existe, recebe mensagens e será monitorado antes da publicação com link de contato definitivo |

---

## Mapa de links e dependências de ativação

| Link visível | Destino técnico | Onde aparece | Status | Dependência para ativação plena |
|---|---|---|---|---|
| Começar grátis | `/office/cadastro` | Hero | Implementado | RF-005 para rota de cadastro; RF-001 para período de avaliação; P7 antes de coletar e-mail em conformidade |
| Entrar | `/office/login` | Hero e rodapé | Implementado | RF-005 para rota de login |
| Ver como funciona | `#como-funciona` | Hero | Implementado | Seção "Comece em três passos" precisa manter `id="como-funciona"` |
| Criar conta grátis | `/office/cadastro` | Chamada final | Implementado | RF-005 para rota de cadastro; RF-001 para período de avaliação; P7 antes de coletar e-mail em conformidade |
| Já tenho conta — Entrar | `/office/login` | Chamada final | Implementado | RF-005 para rota de login |
| Criar conta | `/office/cadastro` | Rodapé | Implementado | RF-005 para rota de cadastro; P7 antes de coletar e-mail em conformidade |
| `suporte@atua.com.br` | `mailto:suporte@atua.com.br` | Rodapé | Implementado como link | P8 para confirmar caixa ativa e monitorada |
| Política de Privacidade | A definir | Rodapé | Pendente | P7 para criar/aprovar conteúdo legal e definir URL pública |
| Termos de Uso | A definir | Rodapé | Pendente | P7 para criar/aprovar conteúdo legal e definir URL pública |

Regra: links pendentes não devem ser publicados como placeholders. Quando P7
for concluída, a landing deve incluir Política de Privacidade e Termos de Uso
no rodapé e, se necessário, no fluxo de cadastro.

---

## Bloqueadores

### B1 — ~~Telas de login e cadastro inexistentes na área da conta~~ ✅ Resolvida (2026-09-02)

**Situação original:** O app da área da conta (`apps/office`) não possuía telas de login nem de
cadastro. Não havia react-router, nem rota `/login` ou `/signup`. O app era
uma SPA de dashboard pós-autenticação com os componentes:

- `CreateTenantForm` (Onboarding)
- Integrations
- Settings
- Período de avaliação

As ocorrências de "cadastro" no código referiam-se a credenciais do iService
(RF-006), não ao cadastro de usuários da plataforma.

**Status: resolvido (2026-09-02).** RF-005 foi implementado — os botões
`[Começar grátis]` (P4) e `[Entrar]` (P5) já têm destino real:
`/office/cadastro` e `/office/login`, respectivamente (ambos publicados sob
o mesmo bucket S3, prefixos `/office/` e `/landing/` — ver
`apps/office/vite.config.ts`/`apps/landing/vite.config.ts`). A landing
ainda precisa ser implementada com o conteúdo aprovado neste documento e
com esses botões apontando para as rotas reais.

---

## Dependências

- **RF-001:** Período de avaliação — o botão "Começar grátis" pressupõe início automático do
  período de avaliação após confirmação de e-mail.
- **RF-005:** Acesso à área da conta — as telas de login e cadastro referenciadas
  pelos botões existem e são usadas pela landing (`/office/login` e
  `/office/cadastro`).
- **RF-006:** Configuração de credenciais de provedor — na fase inicial, iService é o
  conector inicial referenciado pela Seção 4 (Passo 2), mas a landing deve
  preservar a narrativa de vários provedores.
- **RF-008:** Ativação do Agente Coletor — referenciado na Seção 4 (Passo 3)
  sem prometer coleta recorrente como entrega consolidada da landing.
- **RF-010, RF-011, RF-015:** funcionalidades de histórico/supervisão ampliada
  devem permanecer como evolução de produto até validação explícita.

## Fora do escopo

- Design visual e implementação frontend da landing.
- Infraestrutura de hospedagem da landing.
- SEO, analytics e rastreamento.
- Conteúdo de páginas internas da área da conta e da administração.
- Política de Privacidade e Termos de Uso (P7 — exigidos mas não definidos aqui).
