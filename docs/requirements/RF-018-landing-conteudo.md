# RF-018 - Conteúdo da Landing Page

Status: `Pendente`

> **⚠️ BLOQUEADOR CRÍTICO:** O app `apps/office` não possui telas de login
> nem de cadastro. Não há react-router, não há rota `/login` ou `/signup` —
> é uma SPA de dashboard pós-autenticação com componentes de Onboarding
> (CreateTenantForm), Integrations, Settings e Trial. As únicas ocorrências
> de "cadastro" no código referem-se a credenciais do iService, não a
> usuários. Os CTAs da landing (P4 e P5) não têm destino existente. A API
> possui os endpoints (`/auth/signup`, `/auth/signin`), mas não há UI.
> Este RF está bloqueado pelo RF-005 (Acesso ao Office), que está pendente.

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
- `[Começar grátis]` → cadastro / Trial no Office (RF-001) — **destino: P4 — PENDENTE**
- `[Entrar]` → login no Office (RF-005) — **destino: P5 — PENDENTE**

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
- `[Criar conta grátis]` — **destino: P4 — PENDENTE**
- `[Já tenho conta — Entrar]` — **destino: P5 — PENDENTE**

---

### RODAPÉ

> "ATUA — Plataforma operacional para empresas de serviços técnicos."

**Links:**
- Entrar
- Criar conta
- E-mail de suporte — **PENDENTE (P1)**

**Copyright:**
> "© [ANO] [RAZÃO SOCIAL DO TITULAR — PENDENTE]. Todos os direitos reservados."

> Nota: o ano pode ser dinâmico (gerado em build ou em runtime). Ver P3.

**Aviso MVP** (sujeito a aprovação do usuário — P6):
> "O ATUA está em fase de avaliação. Algumas funcionalidades podem estar em
> desenvolvimento ou sujeitas a alteração."

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
| P1 | E-mail de suporte a ser exibido no rodapé |
| P2 | Razão social do titular do copyright |
| P3 | Ano do copyright (pode ser dinâmico) |
| P4 | URL da rota de cadastro no Office (tela não existe — bloqueador) |
| P5 | URL da rota de login no Office (tela não existe — bloqueador) |
| P6 | Aprovação do aviso de MVP no rodapé |
| P7 | Política de Privacidade e Termos de Uso (obrigatório para coleta de e-mail no cadastro) |

---

## Bloqueadores

### B1 — Telas de login e cadastro inexistentes no Office (crítico)

O app `apps/office` não possui telas de login nem de cadastro. Verificado
no código-fonte: não há react-router, não há rota `/login` ou `/signup`.
O app é uma SPA de dashboard pós-autenticação com os componentes:

- `CreateTenantForm` (Onboarding)
- Integrations
- Settings
- Trial

As ocorrências de "cadastro" no código referem-se a credenciais do iService
(RF-006), não ao cadastro de usuários da plataforma.

A API possui os endpoints `/auth/signup` e `/auth/signin` implementados, mas
não existe UI correspondente.

**Consequência:** os CTAs `[Começar grátis]` (P4) e `[Entrar]` (P5) não têm
destino existente. A landing não pode ser publicada como funcional sem que
RF-005 (Acesso ao Office) seja implementado.

**Dependência:** RF-005 (Acesso ao Office) — pendente.

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
