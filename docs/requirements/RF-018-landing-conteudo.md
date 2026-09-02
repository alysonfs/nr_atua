# RF-018 - Conteúdo da Landing Page

Status: `Implementado e publicado — falta apenas P7 (Política de Privacidade/Termos de Uso)`

> **Atualização (2026-09-02):** RF-018 está **implementado e publicado em
> produção** (`apps/landing`, commit `874b808`; deploy via `make
> deploy-landing` em
> http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/landing/).
> O bloqueador B1 (telas de login/cadastro inexistentes) foi resolvido pela
> implementação de RF-005 (Acesso ao Office, `apps/office`, commit
> `4a12ee3`, 2026-08-31): existem rotas reais `/login` e `/cadastro`
> (react-router-dom), consumindo os endpoints `/auth/signin`/`/auth/signup`
> já existentes na API. P4 e P5 (destinos dos CTAs) foram resolvidas com
> `/office/cadastro` e `/office/login`. As pendências P1/P2/P3/P6 (rodapé)
> também foram resolvidas com o usuário, com validação do brand-guardian
> (Gabi) sobre consistência de voz — ver seção RODAPÉ atualizada. Todas as
> seções de conteúdo (Hero, seções 1-5, rodapé) foram transcritas fielmente
> e cobertas por 7 testes automatizados (Vitest + Testing Library), build
> limpo, sem avisos de ESLint. **Falta apenas P7** (Política de
> Privacidade/Termos de Uso) antes que o cadastro de usuários possa coletar
> e-mails em conformidade — não bloqueia o funcionamento atual da landing.

## Objetivo

Definir o conteúdo textual e a estrutura de seções da landing page pública
do ATUA, que posiciona o produto, apresenta o propósito da plataforma e
conduz o visitante ao cadastro ou ao login.

O conteúdo textual registrado neste documento foi definido pela governança
de marca e deve ser transcrito fielmente na implementação, sem alterações
editoriais.

## Escopo

Conteúdo textual, estrutura de seções, CTAs e rodapé da landing page.
Não inclui design visual, implementação frontend, infraestrutura de
hospedagem nem autenticação.

## Contexto

O ATUA é uma plataforma operacional para empresas de serviços técnicos que
integra a operação ao iService. A landing page é o ponto de entrada público
do produto. Seu conteúdo deve comunicar claramente o propósito, os
diferenciais e os próximos passos para o visitante.

## Conteúdo aprovado pela governança de marca

### HERO

**Headline:**
> "A complexidade fica dentro do ATUA. A simplicidade fica para você."

**Subheadline:**
> "ATUA conecta a operação da sua assistência técnica ao iService em um único
> ambiente — com dados organizados, histórico observado e supervisão em
> tempo real."

**CTAs:**
- `[Começar grátis]` → cadastro / Trial no Office (RF-001) — **destino: `/office/cadastro`** (P4 resolvida)
- `[Entrar]` → login no Office (RF-005) — **destino: `/office/login`** (P5 resolvida)

> ⚠️ Itens aspiracionais: "dados organizados, histórico observado e supervisão
> em tempo real" referem-se a RF-010/011 e RF-015, que estão pendentes. O
> Worker não está implementado. Ver A1.

---

### SEÇÃO 1 — "Uma plataforma para toda a operação"

> "O ATUA é a plataforma operacional para empresas de serviços técnicos.
> Conecte sua conta ao iService, ative o Agente Coletor e tenha suas ordens
> de serviço organizadas, acompanhadas e disponíveis em um único lugar. Chega
> de alternar entre sistemas, planilhas e portais desconectados para saber o
> que está acontecendo na sua operação."

---

### SEÇÃO 2 — "Feito para assistências técnicas e empresas de serviços técnicos"

> "Se a sua operação depende do iService para registrar e acompanhar ordens
> de serviço, o ATUA foi construído para a sua realidade. Sem migração de
> sistema, sem treinamento extenso. Conecte suas credenciais, ative o agente
> e comece a supervisionar."

---

### SEÇÃO 3 — "O ATUA observa. Nunca interfere."

> "O Agente Coletor do ATUA opera exclusivamente em modo somente leitura.
> Isso significa que o ATUA nunca escreve, altera, aceita ou reatribui ordens
> de serviço no seu iService. Seus dados no iService permanecem intactos,
> exatamente como a sua equipe os registrou. O ATUA coleta, organiza e
> apresenta — a operação é sempre sua."

> **Nota ao implementador:** argumento de confiança mais forte do MVP; destaque
> visual, sem alterar o texto.

---

### SEÇÃO 4 — "Comece em três passos"

> ⚠️ A narrativa desta seção depende das telas do Office (RF-005/006), que
> estão pendentes. Ver A3.

**Passo 1 — "Crie sua conta"**
> "Cadastre-se com e-mail e senha. Confirme o e-mail e seu período de
> avaliação começa automaticamente — sem cartão de crédito."

**Passo 2 — "Conecte ao iService"**
> "No Office, informe suas credenciais do iService. O ATUA valida a conexão
> antes de prosseguir."

**Passo 3 — "Ative o Agente Coletor"**
> "Com a conexão validada, ative o agente. Ele inicia a coleta imediatamente
> e passa a monitorar suas ordens de serviço em intervalos regulares."

> ⚠️ "monitorar em intervalos regulares" — RF-008 define 15 min, mas o Worker
> não existe. Ver A2.

---

### SEÇÃO 5 — CTA final "Pronto para conectar sua operação?"

> "Crie sua conta gratuitamente e explore o ATUA durante o período de
> avaliação. Sem compromisso."

**CTAs:**
- `[Criar conta grátis]` — **destino: `/office/cadastro`** (P4 resolvida)
- `[Já tenho conta — Entrar]` — **destino: `/office/login`** (P5 resolvida)

---

### RODAPÉ

> "ATUA — Plataforma operacional para empresas de serviços técnicos."

**Links:**
- Entrar
- Criar conta
- E-mail de suporte: `suporte@atua.com.br`

**Copyright:**
> "© [ANO] Assistência Técnica Unificada Ltda. Todos os direitos reservados."

> Nota: o ano é dinâmico (gerado em build ou em runtime — decisão técnica do
> `frontend-engineer`). Sem CNPJ na linha de copyright — ver observação de
> marca abaixo.

**Aviso MVP** (aprovado pelo usuário em 2026-09-02, com ajuste do
brand-guardian):
> "O ATUA está em fase inicial. Algumas funcionalidades podem estar em
> desenvolvimento ou sujeitas a alteração."

> Nota: o texto original aprovado pela marca dizia "fase de avaliação", mas
> esse termo colide com "período de avaliação" (trial do usuário), usado nas
> Seções 4 e 5. O brand-guardian (Gabi) recomendou "fase inicial" para evitar
> essa ambiguidade, sem alterar o sentido ou a honestidade da mensagem; o
> usuário aprovou a variante.

---

## Itens aspiracionais (aprovação pendente do usuário)

Estes trechos do conteúdo aprovado pela marca referem-se a funcionalidades
que ainda não estão implementadas. Devem ser revisados antes da publicação
da landing ou acompanhados de aviso adequado ao visitante.

| # | Trecho | Dependência |
|---|--------|-------------|
| A1 | "dados organizados, histórico observado e supervisão em tempo real" | RF-010, RF-011, RF-015 pendentes; Worker não implementado |
| A2 | "monitorar em intervalos regulares" | RF-008 define 15 min, mas o Worker (`apps/collector`) não existe |
| A3 | Narrativa "Comece em três passos" inteira | Telas do Office (RF-005/006) pendentes |

---

## Pendências que dependem exclusivamente do usuário/titular

| # | Pendência |
|---|-----------|
| P1 | ~~E-mail de suporte a ser exibido no rodapé~~ ✅ Resolvida: `suporte@atua.com.br` |
| P2 | ~~Razão social do titular do copyright~~ ✅ Resolvida: "Assistência Técnica Unificada Ltda." — validado pelo brand-guardian; CNPJ ainda não definido, será incluído na Política de Privacidade/Termos de Uso (P7) quando disponível, **não** publicado como placeholder mascarado na landing |
| P3 | ~~Ano do copyright~~ ✅ Resolvida: dinâmico (implementação técnica cabe ao `frontend-engineer`) |
| P4 | ~~URL da rota de cadastro no Office~~ ✅ Resolvida: `/office/cadastro` (implementado em `apps/office`, commit `4a12ee3`) |
| P5 | ~~URL da rota de login no Office~~ ✅ Resolvida: `/office/login` (implementado em `apps/office`, commit `4a12ee3`) |
| P6 | ~~Aprovação do aviso de MVP no rodapé~~ ✅ Resolvida: texto ajustado para "fase inicial" (ver nota no RODAPÉ acima) e aprovado pelo usuário |
| P7 | Política de Privacidade e Termos de Uso (obrigatório para coleta de e-mail no cadastro) — **inclui o CNPJ do titular quando definido** |

---

## Bloqueadores

### B1 — ~~Telas de login e cadastro inexistentes no Office~~ ✅ Resolvida (2026-09-02)

**Situação original:** O app `apps/office` não possuía telas de login nem de
cadastro. Não havia react-router, nem rota `/login` ou `/signup`. O app era
uma SPA de dashboard pós-autenticação com os componentes:

- `CreateTenantForm` (Onboarding)
- Integrations
- Settings
- Trial

As ocorrências de "cadastro" no código referiam-se a credenciais do iService
(RF-006), não ao cadastro de usuários da plataforma.

**Status: resolvido (2026-09-02).** RF-005 foi implementado — os CTAs
`[Começar grátis]` (P4) e `[Entrar]` (P5) já têm destino real:
`/office/cadastro` e `/office/login`, respectivamente (ambos publicados sob
o mesmo bucket S3, prefixos `/office/` e `/landing/` — ver
`apps/office/vite.config.ts`/`apps/landing/vite.config.ts`). A landing
ainda precisa ser implementada com o conteúdo aprovado neste documento e
com esses CTAs apontando para as rotas reais.

---

## Dependências

- **RF-001:** Trial — o CTA "Começar grátis" pressupõe início automático do
  Trial após confirmação de e-mail.
- **RF-005:** Acesso ao Office — as telas de login e cadastro referenciadas
  pelos CTAs não existem; este RF bloqueia a publicação da landing com CTAs
  funcionais.
- **RF-006:** Configuração de credenciais do iService — referenciado na
  Seção 4 (Passo 2).
- **RF-008:** Ativação do Agente Coletor — referenciado na Seção 4 (Passo 3)
  e no item aspiracional A2.
- **RF-010, RF-011, RF-015:** funcionalidades referenciadas no item aspiracional
  A1 ("histórico observado", "supervisão em tempo real"); ainda não implementadas.

## Fora do escopo

- Design visual e implementação frontend da landing.
- Infraestrutura de hospedagem da landing.
- SEO, analytics e rastreamento.
- Conteúdo de páginas internas (Office, Manager).
- Política de Privacidade e Termos de Uso (P7 — exigidos mas não definidos aqui).
