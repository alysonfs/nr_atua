# Guia de deploy — ATUA (API, Collector e Frontends)

**Status:** Documentação operacional, derivada do estado real provisionado
em 2026-08-30 (ver [`aws-ambiente-mvp.md`](./aws-ambiente-mvp.md)) e dos
comandos já validados em `infra/Makefile` e nas skills Batuta
(`.github/skills/atua-deploy/`).

**Objetivo deste documento:** deixar o passo a passo de deploy independente
de memória de conversa/sessão de IA. Se você (Alyson) perder acesso a um
token/sessão de IA, este arquivo sozinho deve bastar para republicar API,
Collector, e saber exatamente qual é a lacuna nos frontends.

Este documento **não inventa nenhum processo novo**. Todo comando aqui é
copiado ou derivado diretamente de `infra/Makefile`, `infra/README.md` e das
skills `atua-deploy`. Onde a fonte não deixava algo 100% explícito, isso está
sinalizado com `⚠️ CONFIRMAR`.

---

## 0. Pré-requisitos comuns a todo deploy

* **AWS CLI v2** instalado e um profile nomeado configurado — o profile em
  uso no ambiente é `moldato` (conta `462991286554`, região `sa-east-1`).
  Sempre exporte/`--profile` explicitamente:

  ```bash
  export AWS_PROFILE=moldato
  ```

  ⚠️ A máquina de desenvolvimento tem credenciais de **outros
  projetos/clientes** em `~/.aws/credentials`. Nunca rode os comandos abaixo
  sem `AWS_PROFILE=moldato` explícito — risco de afetar a conta errada.

* **SDK do .NET** instalado localmente (a API e o Collector são compilados
  localmente com `dotnet publish` antes de subir para o S3 — não há build
  remoto/CI).
* **pnpm** instalado (workspace do monorepo; usado pelos frontends e pelo
  `infra/Makefile` via `pnpm --filter`).
* Confirme que a infraestrutura está no ar antes de qualquer redeploy "a
  quente":

  ```bash
  cd infra
  AWS_PROFILE=moldato make status
  ```

  Se `AtuaComputeStack` não estiver `CREATE_COMPLETE`/`UPDATE_COMPLETE`, a
  EC2 não existe — rode `make up` primeiro (ver §4, "subir a infraestrutura
  do zero"), não os comandos de redeploy "a quente" das seções 1 e 2.

* **Chave SSH** para acessar as instâncias EC2 (API e Collector), necessária
  apenas para redeploy a quente, logs e diagnóstico — não para o deploy
  inicial via `make up`:

  ```bash
  cd infra
  AWS_PROFILE=moldato make retrieve-keypair
  # salva ~/.ssh/atua-mvp-key.pem (fora do repo, chmod 400)
  ```

* **Seu IP público autorizado no Security Group** da instância, na porta 22.
  Os scripts de deploy **não abrem regras de SG automaticamente**. Se o SSH
  falhar com `Connection attempt timed out`, é necessário liberar o seu IP
  atual manualmente no Security Group correspondente (`sg-09f1ffef1774526c1`
  para API, `sg-0fbd7bac215fedb3a` para Collector, ambos em
  `sa-east-1` — ver [`aws-ambiente-mvp.md`](./aws-ambiente-mvp.md) §2) e
  revogar a regra depois de usar.

Todos os comandos abaixo, salvo indicação contrária, são executados a partir
do diretório `infra/` do repositório.

---

## 1. Deploy da API (`Atua.Api`)

A API roda como serviço systemd (`atua-api.service`) numa instância EC2
(`atua-api-master`), stateless (sessão via JWT no PostgreSQL — não guarda
estado em disco/memória que precise sobreviver a um restart).

### 1.1 Pré-requisitos específicos

* Infraestrutura no ar (`AtuaComputeStack` deployado — ver §0).
* Chave SSH recuperada (`~/.ssh/atua-mvp-key.pem`).
* Seu IP liberado no Security Group da API.

### 1.2 Passo a passo — redeploy "a quente" (EC2 já existente, caso mais comum)

Use este fluxo quando a EC2 da API já está no ar e você só quer publicar uma
alteração de código.

```bash
cd infra
AWS_PROFILE=moldato make redeploy-api
```

Isso executa `.github/skills/atua-deploy/scripts/redeploy-live.sh api`, que:

1. `dotnet publish apps/api/Atua.Api/Atua.Api.csproj -c Release -o /tmp/atua-api-publish`
2. `aws s3 sync /tmp/atua-api-publish/ s3://atua-<ACCOUNT_ID>-releases/latest/ --delete`
3. Via SSH (`ec2-user@<IP resolvido via CloudFormation Output ApiPublicIp>`):
   * `sudo systemctl stop atua-api`
   * remove `.dll/.pdb/.json` antigos em `/opt/atua-api` e refaz
     `aws s3 sync` (contorna heurística de tamanho/timestamp do S3 sync)
   * `sudo systemctl start atua-api`
4. Compara o MD5 do `Atua.Api.dll` local vs. remoto — **falha com
   `exit 1`** se não baterem (sinal de que o binário não foi realmente
   atualizado).
5. Mostra `systemctl status atua-api` no final.

O IP da instância é resolvido dinamicamente via CloudFormation Output
(`ApiPublicIp` do `AtuaComputeStack`) — nunca use um IP anotado manualmente,
ele muda a cada `make up`.

### 1.3 Passo a passo — deploy quando a EC2 precisa ser (re)criada

Use quando a infraestrutura está desligada (`make down` foi rodado) ou é a
primeira subida.

```bash
cd infra
AWS_PROFILE=moldato make deploy-api   # compila (Release) e publica em s3://atua-<ACCOUNT_ID>-releases/latest/
AWS_PROFILE=moldato make up           # cria/recria Network+Data+Compute; o user-data da EC2
                                       # sincroniza o artefato do S3 e inicia atua-api.service
```

O user-data só inicia o serviço se `/opt/atua-api/Atua.Api.dll` estiver
presente — por isso `make deploy-api` deve rodar **antes** de `make up` na
primeira subida (ou depois de um `destroy`).

### 1.4 Como verificar que o deploy funcionou

```bash
# Health check HTTP (porta 80, liberada no SG da API):
curl http://<IP público da instância>/health
# Esperado: {"status":"healthy"}

# Logs via journalctl (IP resolvido automaticamente):
cd infra
AWS_PROFILE=moldato make logs-api          # últimas ~200 linhas
AWS_PROFILE=moldato make logs-api-follow   # tempo real, Ctrl+C para sair
```

O IP público atual pode ser obtido com:

```bash
aws cloudformation describe-stacks --stack-name AtuaComputeStack --region sa-east-1 \
  --query "Stacks[0].Outputs[?OutputKey=='ApiPublicIp'].OutputValue" --output text
```

### 1.5 Rollback

**Não existe rollback automatizado para a API.** Não há versionamento de
múltiplos artefatos no S3 — o prefixo `latest/` é sobrescrito
(`--delete`) a cada `make deploy-api`/`make redeploy-api`. Não invente um
mecanismo que não existe.

Caminho manual possível, caso precise reverter para uma versão anterior:

1. Faça `git checkout <commit anterior>` do código da API localmente.
2. Rode `make redeploy-api` (ou `make deploy-api` + `make up`) normalmente a
   partir desse commit — isso republica o artefato antigo.
3. ⚠️ **CONFIRMAR**: não há backup automático do artefato anterior no S3
   antes do `--delete`. Se precisar de rollback rápido no futuro, isso é uma
   lacuna de processo a ser decidida (ex.: versionar o bucket de releases ou
   manter um prefixo `previous/`) — não algo já implementado hoje.

---

## 2. Deploy do Collector (`Atua.Collector`)

Mesma mecânica da API, com uma diferença crítica de build: o publish precisa
ser feito com RID explícito `linux-x64` e `--self-contained false`.

### 2.1 Pré-requisitos específicos

* Os mesmos do §1.1 (infra no ar, chave SSH, IP liberado no SG do
  Collector).
* **Nunca** rode `dotnet publish` sem `-r linux-x64 --self-contained false`
  para o Collector: sem o RID explícito, o MSBuild embute o driver Node do
  Playwright da plataforma de build (ex.: `darwin-x64` num Mac), que não
  funciona na EC2 Linux.

### 2.2 Passo a passo — redeploy "a quente"

```bash
cd infra
AWS_PROFILE=moldato make redeploy-collector
```

Executa `.github/skills/atua-deploy/scripts/redeploy-live.sh collector`,
mesmo ciclo do §1.2, mas com:

1. `dotnet publish apps/collector/Atua.Collector/Atua.Collector.csproj -c Release -r linux-x64 --self-contained false -o /tmp/atua-collector-publish`
2. `aws s3 sync` para `s3://atua-<ACCOUNT_ID>-releases/collector-latest/`
3. SSH, stop/resync/start do `atua-collector.service` em `/opt/atua-collector`
4. Verificação de MD5 (`Atua.Collector.dll`)
5. Status final do systemd

### 2.3 Passo a passo — deploy quando a EC2 precisa ser (re)criada

```bash
cd infra
AWS_PROFILE=moldato make deploy-collector   # compila (RID linux-x64) e publica em collector-latest/
AWS_PROFILE=moldato make up                 # user-data sincroniza o artefato, instala o Chromium
                                             # via driver Node embutido do Playwright, e inicia
                                             # atua-collector.service
```

### 2.4 Como verificar que o deploy funcionou

```bash
cd infra
AWS_PROFILE=moldato make logs-collector
AWS_PROFILE=moldato make logs-collector-follow
```

O Collector não expõe endpoint HTTP de health-check próprio (não foi
encontrado nenhum `/health` no código do `Atua.Collector`) — a verificação
é feita via log (`sudo systemctl status atua-collector` e `journalctl`),
confirmando que o serviço está `active (running)` e sem exceções repetidas
no ciclo `[WORKER]`.

Para testar um ciclo de coleta ponta a ponta após o deploy:

```bash
cd infra
AWS_PROFILE=moldato make collector-trigger-cycle
```

Isso renova o JWT do usuário de teste e reativa o `collector-activation`,
gerando um novo `ImmediateCollectionCommand` `Pending` para o Worker
reivindicar no próximo polling. Sobrescreva `ATUA_TEST_EMAIL` /
`ATUA_TEST_PASSWORD` / `ATUA_TENANT_ID` / `ATUA_INTEGRATION_ID` se o tenant
de teste mudar. Em seguida acompanhe com `make logs-collector-follow`.

⚠️ Se o Collector retornar `401 Unauthorized` em `/claim`, `/complete` ou
`/eligibility`, o problema não é o deploy em si — é falta de credenciais em
`service_credentials`. Ver skill
`.github/skills/atua-seed-collector-credentials/SKILL.md`.

### 2.5 Rollback

**Não existe rollback automatizado para o Collector**, pelo mesmo motivo do
§1.5: o prefixo `collector-latest/` é sobrescrito a cada deploy, sem
histórico de versões anteriores no S3. Caminho manual: `git checkout` do
commit anterior + `make redeploy-collector`. Mesma lacuna sinalizada acima
se um rollback mais rápido for necessário no futuro.

---

## 3. Frontends (`landing`, `manager`, `office`, `tecnica`)

### 3.1 Estado real hoje

Cada frontend é um app Vite/React independente, buildado com:

```bash
pnpm --filter <landing|office|manager|tecnica> build
# equivalente a: tsc -b && vite build (gera dist/)
```

Existe, sim, um alvo de publicação **já implementado no `infra/Makefile`**
para cada um: `make deploy-landing`, `make deploy-office`,
`make deploy-manager`, `make deploy-tecnica`. Cada alvo builda o app e
sincroniza `dist/` para o bucket S3 `atua-<ACCOUNT_ID>-frontends`, servido
como **website estático S3 (sem CloudFront)**, um prefixo por app.

### 3.2 Pré-requisitos específicos

* Infraestrutura de dados (`AtuaDataStack`, que contém o bucket
  `atua-<ACCOUNT_ID>-frontends`) no ar — este bucket é **persistente**, não
  depende do `AtuaComputeStack` (EC2) estar ligado.
* pnpm com dependências instaladas na raiz do monorepo (`pnpm install`).

### 3.3 Passo a passo do deploy

```bash
cd infra

AWS_PROFILE=moldato make deploy-landing
AWS_PROFILE=moldato make deploy-office
AWS_PROFILE=moldato make deploy-manager
AWS_PROFILE=moldato make deploy-tecnica
```

Cada alvo, por exemplo `deploy-landing`:

1. `pnpm --filter landing build`
2. `aws s3 sync ../apps/landing/dist/ s3://atua-<ACCOUNT_ID>-frontends/landing/ --delete --cache-control "public, max-age=31536000, immutable" --exclude "index.html"`
3. `aws s3 cp ../apps/landing/dist/index.html s3://atua-<ACCOUNT_ID>-frontends/landing/index.html --cache-control "no-cache, no-store, must-revalidate" --content-type "text/html"`

URLs resultantes (website hosting, HTTP, sem HTTPS/domínio próprio):

| App | URL |
|---|---|
| Landing | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/landing/` |
| Office | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/office/` |
| Manager | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/manager/` |
| Tecnica | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/tecnica/` |

⚠️ Substitua `462991286554` pelo valor de `aws sts get-caller-identity
--query Account --output text` se a conta mudar.

### 3.4 Como verificar que o deploy funcionou

```bash
curl -I http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/<app>/
# Esperado: HTTP 200 e Content-Type: text/html
```

Não há health-check de aplicação (SPA estática) — a verificação é abrir a
URL e confirmar que o HTML carrega e os assets (JS/CSS) resolvem
corretamente com o `base path` (`/landing/`, `/office/`, `/manager/`,
`/tecnica/`).

### 3.5 Rollback

**Não existe rollback automatizado.** O `aws s3 sync --delete` sobrescreve o
prefixo do app a cada deploy, sem versionamento do bucket. Caminho manual:
`git checkout` do commit anterior + rodar o `make deploy-<app>`
correspondente novamente.

### 3.6 Lacuna explícita — este NÃO é um pipeline de deploy "de produção"

O que existe hoje é **manual, sob demanda**: alguém com `AWS_PROFILE=moldato`
configurado localmente roda `make deploy-<app>` no terminal. Não há:

* CI/CD automatizado (GitHub Actions ou equivalente) disparando o deploy em
  push/merge;
* CloudFront/CDN, HTTPS ou domínio próprio na frente do S3 (é website
  hosting simples, HTTP puro);
* Cache invalidation além do `Cache-Control` definido no `s3 sync`/`s3 cp`;
* Ambientes separados (staging vs. produção) para os frontends.

Além disso, conforme registrado em
[`aws-ambiente-mvp.md`](./aws-ambiente-mvp.md) §5.1 (2026-08-30), os 4 apps
continham apenas o **scaffold do Vite**, sem conteúdo de negócio
implementado (RF-018 pendente para a Landing). **Este documento não decide**
se/quando um pipeline de CI/CD, CDN ou domínio próprio deve ser introduzido
— essa é uma decisão de arquitetura pendente, a ser levada ao
`orchestrator`/`software-architect` quando os frontends tiverem conteúdo
real a publicar.

---

## 4. Subir/desligar a infraestrutura do zero (contexto necessário para os itens acima)

Os deploys acima assumem que a infraestrutura básica existe. Caso ela tenha
sido desligada (`make down`) ou nunca tenha sido criada:

```bash
cd infra
AWS_PROFILE=moldato make up      # sobe Network+Data (idempotente) + RDS (restore de snapshot
                                  # ou 'make rds-bootstrap' na 1a vez) + Compute (2 EC2)
AWS_PROFILE=moldato make status  # confere o que está no ar
AWS_PROFILE=moldato make down    # desliga Compute + RDS (com snapshot final) ao fim do dia
```

⚠️ **`make down` ao fim do dia não é opcional** — o custo projetado de uso
contínuo excede o budget de US$ 5/mês configurado na conta (ver
[`aws-ambiente-mvp.md`](./aws-ambiente-mvp.md) §6). Detalhes completos do
ciclo de vida da infraestrutura (o que cada alvo destrói/preserva,
`destroy` vs. `destroy-all-data`) estão em
[`infra/README.md`](../../infra/README.md) — este guia cobre apenas o
deploy de aplicação, não o provisionamento de infraestrutura em si.

---

## 5. Diagnóstico de falhas comuns (todos os serviços)

| Sintoma | Causa provável |
|---|---|
| `Connection attempt timed out` no SSH | Seu IP público mudou; libere-o no Security Group (porta 22) e revogue depois. |
| MD5 local ≠ MD5 remoto no `redeploy-*` | `aws s3 sync` pode ter pulado o arquivo por heurística de tamanho/timestamp; o script já força `rm` antes do sync — se persistir, rode `redeploy-*` de novo. |
| `FATAL: não foi possível obter o IP público da instância` | `AtuaComputeStack` não está no ar — rode `make up` primeiro. |
| macOS sem `md5sum` | O script já tem fallback para `md5 -q`; não é necessário instalar nada. |
| Collector com `401 Unauthorized` em `/claim`/`/complete`/`/eligibility` | Falta de credenciais em `service_credentials` — ver skill `atua-seed-collector-credentials`, não é um problema do deploy. |

---

## Referências

* [`aws-ambiente-mvp.md`](./aws-ambiente-mvp.md) — estado real provisionado, IDs de recursos, custos e débitos técnicos.
* [`aws-setup-checklist.md`](./aws-setup-checklist.md) — setup inicial da conta AWS (histórico, já concluído).
* [`infra/README.md`](../../infra/README.md) — ciclo de vida completo da infraestrutura (`up`/`down`/`destroy`).
* [`infra/Makefile`](../../infra/Makefile) — fonte de verdade de todos os alvos `make` citados neste guia.
* `.github/skills/atua-deploy/SKILL.md` — skill Batuta com os scripts de redeploy a quente, logs e trigger de ciclo do Collector.
* `.github/skills/atua-seed-collector-credentials/SKILL.md` — resolução de `401` do Collector por falta de credenciais.
