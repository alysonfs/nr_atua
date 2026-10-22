# ATUA MVP — Infraestrutura AWS (ADR-012, Plano v3)

Este diretório contém a infraestrutura como código (AWS CDK v2, TypeScript)
do MVP do ATUA, conforme `docs/decisions/ADR-012-mongodb-atlas-free-tier.md`
e as decisões de ciclo de vida registradas nas conversas com o
`orchestrator` (Plano v3: destroy+recreate como padrão de "desligar").

> **Estado atual: PROVISIONADO.** Em **2026-08-30** o ambiente foi
> efetivamente criado na conta **462991286554**, região **`sa-east-1`**,
> usando o perfil **`moldato`**. O bootstrap do CDK está concluído e as
> stacks Network/Data/Compute e o RDS estão implantados.
>
> Os IDs reais dos recursos, as decisões de custo e o **débito técnico
> crítico da execution role** estão em
> [`docs/architecture/aws-ambiente-mvp.md`](../docs/architecture/aws-ambiente-mvp.md).

Toda execução real (`cdk deploy`, `make up`, `make down`, scripts de RDS)
continua exigindo aprovação explícita e o perfil `moldato` configurado
localmente por quem for executar.

---

## 0. Operação diária

```bash
cd infra

AWS_PROFILE=moldato make up      # sobe o ambiente para trabalhar
AWS_PROFILE=moldato make status  # confere o que está no ar agora
AWS_PROFILE=moldato make down    # DESLIGA ao fim do dia
```

- `make up` — implanta Network + Data (idempotentes), restaura o RDS do
  snapshot mais recente e cria as 2 EC2. Os IDs e IPs públicos das
  instâncias **mudam a cada ciclo** (não há Elastic IP).
- `make down` — destrói EC2 + EBS e o RDS (gerando um **snapshot final**).
  Preserva Network, S3 e Secrets. Custo residual: **~US$ 1,20/mês**.
- `make destroy` — remove também Network e Data (S3/Secrets ficam retidos
  por `RemovalPolicy`).

⚠️ **`make down` ao fim do dia não é opcional.** Ver a tabela de custos em
§4: o padrão de uso projetado já excede o budget de US$ 5/mês configurado.

---

### Bucket de mídia da triagem

A `AtuaDataStack` provisiona `atua-<accountId>-media-triagem`. O nome pode
ser sobrescrito pelo contexto CDK `-c mediaTriagemBucketName=...`; isso
define o nome do bucket gerenciado, não importa um bucket existente.
A `AtuaComputeStack` recebe esse recurso e configura
`TriagemInboundMedia__BucketName` no bootstrap da API.

O bucket bloqueia acesso público, desabilita ACLs, usa SSE-S3 e exige HTTPS.
A role da API recebe somente `s3:PutObject` e `s3:DeleteObject` nos objetos
desse bucket, sem alterar as permissões existentes de outros recursos.
O bucket é retido mesmo na remoção da stack, não tem versionamento nem
exclusão automática. A retenção e o expurgo são controlados pela aplicação,
conforme o [ADR-047](../docs/decisions/ADR-047-ingestao-inicial-midias-triagem.md).

Armazenamento e requisições têm cobrança por uso
([preços S3](https://aws.amazon.com/s3/pricing/)), inclusive com compute
desligado. Logs de acesso, eventos de dados CloudTrail e métricas CloudWatch
são recomendados para auditoria; sua habilitação e custos ficam fora deste
incremento. Dados sintéticos devem ser removidos após os testes autorizados.

Em 2026-10-22, o deploy de Data e Compute foi aprovado e concluído
(`UPDATE_COMPLETE`), criando `atua-462991286554-media-triagem` em `sa-east-1`.
As EC2 não foram substituídas. Como o bootstrap não foi reexecutado, a
variável acima foi aplicada pontualmente em `/etc/atua-api.env`, com
autorização do operador, seguida de restart da API. A variável foi
confirmada no processo ativo e `/health` retornou HTTP 200. A policy foi
confirmada no template implantado; a leitura direta via `iam:GetRolePolicy`
foi negada ao perfil local. Não houve publicação de novos binários nem E2E
remoto de mídia neste deploy.

---

## 1. Arquitetura (3 stacks CDK + RDS operacional)

| Stack / recurso   | Ciclo de vida | Conteúdo | Custo aproximado |
|---|---|---|---|
| `AtuaNetworkStack` | Persistente (nunca precisa ser destruída) | VPC, subnet pública, subnets isoladas, Internet Gateway, **sem NAT Gateway**, 3 Security Groups (API, RDS, Collector), Key Pair `atua-mvp-key` | US$ 0/mês |
| `AtuaDataStack`    | Persistente (só remove com `destroy-all-data`) | S3 (`frontends`, `backups`, `releases`), Secrets Manager (3 secrets, sem valores em código) | ~US$ 1,20/mês (3 × US$ 0,40) |
| `AtuaComputeStack` | **Efêmero** (destruído/recriado a cada ciclo down/up) | EC2 t3.micro `atua-api-master` (API Master, com bootstrap real) **+** EC2 t3.micro `atua-collector-base` (apenas SO+rede, sem nenhuma lógica de coleta) | ~US$ 0,079/h fora do Free Tier (⚠️ ver §4) |
| RDS PostgreSQL     | **Semi-efêmero, gerenciado FORA do CDK** (ver §2) | `atua-postgres-mvp` (PostgreSQL 16.13, db.t3.micro, 20GB gp2, Single-AZ, encriptado, retenção de backup 1 dia) | incluído na linha acima; ~centavos/mês quando "down" (snapshot) |

**Confirmação final do usuário (nesta sessão)**: a instância `atua-collector-base`
nasce e morre junto com a API Master no mesmo ciclo `up`/`down`. Ela **NÃO
tem nenhum software de scraping, Playwright, cron ou agendamento** — é
apenas SO + rede + uma IAM role sem nenhuma policy anexada. A implementação
real do Collector só ocorre em uma tarefa futura própria, com aprovação
explícita separada (gate assistido do Agente Coletor).

---

## 2. Por que o RDS não é um recurso CDK

O plano aprovado usa "destroy + snapshot" no `down` e "restore-from-snapshot"
no `up` (Opção 2, mais barata que `stop`/7-dias). O identificador do
snapshot muda a cada ciclo (`atua-latest-snapshot-<timestamp>`), e o
identificador físico da instância restaurada não é o mesmo do
CloudFormation esperaria gerenciar de forma declarativa — colocar isso
dentro de uma stack CDK levaria a "drift" (o CloudFormation acharia que o
recurso ainda existe com a identidade antiga) e alto risco de falha ou
recriação indesejada no próximo `cdk deploy`.

Por isso o RDS é tratado como um **recurso operacional**, gerenciado por
scripts explícitos em `scripts/` (chamados pelo `Makefile`):

- `scripts/rds-bootstrap.sh` — cria o RDS do zero (rodar 1x, apenas na
  primeira vez, quando não há instância nem snapshot).
- `scripts/rds-down.sh` — snapshot final + destrói a instância.
- `scripts/rds-up.sh` — restaura do snapshot mais recente e atualiza
  automaticamente o secret `atua/rds-postgres` com o novo endpoint.

---

## 3. Ciclo de vida (`Makefile`)

```
make synth              # cdk synth local - NÃO cria nada na AWS
make install            # pnpm install (workspace)
make bootstrap-cdk      # cdk bootstrap da conta/região (1x por conta)
make rds-bootstrap      # cria o RDS do zero (1x, primeira vez)

make up                 # liga tudo: Network+Data (idempotente, cria o
                         # Key Pair 'atua-mvp-key' na 1a vez) + RDS
                         # (restore do snapshot, se existir) + Compute
                         # (API Master + Collector-base, SEM lógica)
make retrieve-keypair   # recupera a chave privada do Key Pair (gerada
                         # automaticamente pelo CDK e guardada em SSM
                         # Parameter Store) e salva em ~/.ssh/atua-mvp-key.pem,
                         # FORA do repositório (nunca commitar)
make down               # desliga: destrói Compute (EC2+EBS das 2
                         # instâncias) + RDS (com snapshot final).
                         # Preserva S3/Secrets/Mongo.
make destroy            # além do down, remove também Network e Data
                         # (S3/Secrets ficam RETIDOS por RemovalPolicy)
make destroy-all-data   # remove literalmente tudo: snapshots, buckets
                         # S3 e secrets. IRREVERSÍVEL.
make status             # mostra o que está no ar (para saber se algo
                         # está sendo cobrado agora)
```

### O que é preservado em cada nível

```
down              -> RDS (via snapshot), S3, Secrets Manager, MongoDB Atlas
destroy           -> S3, Secrets Manager, MongoDB Atlas (RDS não existe;
                      snapshot ainda existe)
destroy-all-data  -> Somente MongoDB Atlas (externo à AWS, não é afetado)
```

---

## 4. Custo esperado por estado

Valores apurados após o provisionamento de 2026-08-30.

| Estado | Custo |
|---|---|
| Ligado (`up`), **fora** do Free Tier | **~US$ 0,079/h** — ~US$ 0,64/dia em 8h; ~US$ 1,89/dia em 24h |
| Ligado (`up`), **dentro** do Free Tier | custo incremental ~US$ 0 (ver ⚠️ abaixo) |
| Desligado (`down`) | **~US$ 1,20/mês** (3 secrets × US$ 0,40 + S3 + snapshot RDS) |
| Destruído (`destroy`) | ~US$ 1,20/mês (S3 + Secrets retidos) |
| Destruído total (`destroy-all-data`) | ~US$ 0 |

⚠️ **Consumo dobrado do Free Tier de EC2**: as **duas** instâncias t3.micro
sobem juntas no mesmo ciclo `up`/`down`. O Free Tier concede 750h/mês
**combinadas** por conta (não por instância), então 2 instâncias consomem a
cota em dobro — rodando 24/7, ela se **esgota em ~15 dias** e o restante do
mês é cobrado a preço cheio.

⚠️ **Alerta de orçamento**: o budget configurado é de **US$ 5/mês**
("Five-Spend Budget", alerta em 80% do previsto). Com uso de **8h/dia útil
fora do Free Tier**, o custo projetado é de **~US$ 14/mês**, que **excede o
budget**. Mitigação imediata: disciplina de `make down` (§0). A revisão do
valor do budget, ou a decisão de não subir a instância Collector enquanto
ela não tiver função, cabe ao `orchestrator`.

⚠️ **Restrição do Free Tier no RDS**: a conta está no Free Tier plan, que
rejeitou `--backup-retention-period 7` com `FreeTierRestrictionError`. A
retenção foi ajustada para **1 dia** (commit `ba2583a`). O RPO de PITR cai de
7 para 1 dia; mitigado porque `make down` gera um snapshot final, que
persiste independentemente da janela de retenção automática.

Não incluídos nesta entrega (removidos/decididos pelo orchestrator):
RDS Proxy, KMS CMK dedicada (usa chaves gerenciadas padrão `aws/rds` e
`aws/secretsmanager`), domínio/Route53, Elastic IP (IP público dinâmico
aceito), NAT Gateway (`natGateways: 0` — decisão de custo; as subnets
privadas são isoladas, sem saída para a internet).

---

## 4.1 Recomendação de corte de custo — topologia colocada (`collocateCollector`)

**Status: preparado no CDK, NÃO aplicado em produção.** Requer aprovação
explícita antes de `cdk deploy`/`make up` (ver docs/architecture/aws-ambiente-mvp.md).

Análise do Cost Explorer (set/2026, ~US$ 2,50/dia com tudo ligado) e o
período 12–16/09 (ambiente rodou com apenas 1 instância) mostraram que o
Collector-base pode rodar **na mesma instância `atua-api-master`** em vez
de uma segunda t3.micro dedicada (`atua-collector-base`). Economia
estimada: **~US$ 17-18/mês** (EC2 ~US$ 12,10 + IP público ~US$ 3,60 + EBS
~US$ 2,00), além de eliminar por completo o excedente de Free Tier descrito
acima (⚠️ Consumo dobrado).

`AtuaComputeStack` agora aceita a prop `collocateCollector` (default
`false`, preserva a topologia atual de duas instâncias). Para ativar:

```bash
cd infra
# Dry-run — apenas gera o template, NÃO aplica nada na AWS:
pnpm --filter @atua/infra exec cdk synth AtuaComputeStack -c collocateCollector=true

# Deploy real (SOMENTE após validação e aprovação explícita — troca de
# topologia exige destruir e recriar a AtuaComputeStack):
pnpm --filter @atua/infra exec cdk deploy AtuaComputeStack -c collocateCollector=true --require-approval never
```

O que muda quando `collocateCollector=true`:

- **Uma única instância** `atua-api-master` (t3.micro) recebe os dois
  Security Groups (`sgApi` **e** `sgCollector` anexados via
  `addSecurityGroup`) e a `apiRole` (que já lê os 3 secrets — RDS, Mongo e
  App — e o bucket de releases, portanto já cobre o que o Collector
  precisa; trade-off aceito: nessa topologia o processo do Collector roda
  numa instância cuja role também tem acesso a KMS/`atua/app-secrets`, que
  ele não usa — ver `infra/lib/atua-compute-stack.ts`).
- O `user-data` combinado instala runtime .NET uma única vez
  (`aspnetcore-runtime-10.0` cobre o runtime base exigido pelo Worker),
  cria um **swapfile de 1GiB** (rede de segurança contra OOM-kill, já que
  API + Worker + Chromium headless dividem os mesmos 1GiB de RAM da
  t3.micro) e sobe os dois serviços systemd (`atua-api` e
  `atua-collector`) lado a lado.
- `CollectorWorker__ApiBaseUrl` passa a ser `http://127.0.0.1` (mesma
  instância), em vez do IP privado de uma segunda instância.
- Os outputs `CollectorInstanceId`/`CollectorPublicIp` passam a apontar
  para a **mesma** instância da API — os scripts de
  `.github/skills/atua-deploy` (`redeploy-live.sh collector`,
  `tail-logs.sh collector`) continuam funcionando sem alteração, pois
  resolvem o IP via esses outputs.
- Novo output `ComputeTopology` indica qual topologia está ativa no
  template sintetizado/deployado.

⚠️ **Risco de memória não validado empiricamente**: 1GiB de RAM física
dividido entre ASP.NET Core, o Worker .NET e o Chromium headless do
Playwright é uma margem apertada. O swapfile mitiga picos transitórios,
mas **não substitui validação real pós-rollout**. Antes de considerar a
topologia colocada estável, valide:

1. Memória livre (`free -h`) em repouso e durante um ciclo de coleta
   (`make collector-trigger-cycle`);
2. Que o ciclo de coleta completa sem o Worker ser OOM-killed
   (`sudo journalctl -u atua-collector` sem mensagens de kill/restart
   inesperado);
3. Que a API continua respondendo com latência normal durante uma coleta
   ativa (Chromium consumindo CPU/memória).

Se a validação apontar pressão de memória excessiva, os próximos ajustes
recomendados (não aplicados automaticamente) são limites `MemoryMax=`
por serviço systemd, calibrados com os dados reais de uso — não definidos
a priori neste PR por falta de medição empírica.

**Rollback**: a topologia é definida inteiramente por uma prop do CDK
(`collocateCollector`). Reverter é remover o `-c collocateCollector=true`
(ou setar `=false`) e rodar `cdk deploy AtuaComputeStack` novamente — a
`AtuaComputeStack` é destruída/recriada no ciclo normal `make down`/`make
up`, então a troca de topologia é apenas mais uma recriação dessa stack
efêmera (sem impacto em `AtuaNetworkStack`/`AtuaDataStack`, que são
persistentes e não são tocadas por essa prop).

---

## 5. Dependência de arquitetura de aplicação (confirmada)

A API Master é assumida como **stateless**: sessão via JWT persistida no
PostgreSQL, sem estado em memória ou disco local que precise sobreviver a
um ciclo `destroy`/`recreate` da instância EC2 (confirmado pelo
orchestrator nesta sessão). Qualquer mudança futura que introduza estado
local (uploads, cache em disco, sessão em memória) deve ser revalidada com
o `software-architect` antes de ser considerada segura para este ciclo de
vida, pois quebraria a premissa de "EC2 é efêmera".

A instância `atua-collector-base` segue a mesma premissa quando sua lógica
for implementada no futuro (fora de escopo agora): nenhum estado (fila,
progresso de coleta, cookies de sessão) pode depender do disco local.

---

## 6. Bootstrap da EC2 e deploy da API

O `user-data` da instância `atua-api-master` instala o runtime .NET 10,
escreve `/etc/atua-api.env` com o ARN da CMK, instala e habilita a unit
systemd `atua-api.service` (fonte de verdade em `infra/systemd/atua-api.service`),
sincroniza `s3://atua-<account-id>-releases/latest/` para `/opt/atua-api`
e **inicia o serviço somente se o artefato (`Atua.Api.dll`) estiver presente**.

### Fluxo completo para ter a API rodando

```bash
# 1. Publique o artefato (compile + upload S3) — a partir da raiz do repo:
cd infra
AWS_PROFILE=moldato make deploy-api

# 2. Suba/recrie a EC2 (o user-data sincroniza o artefato e inicia atua-api.service):
AWS_PROFILE=moldato make up
```

Se a EC2 já estiver no ar e você quiser atualizar sem recriar a instância:

```bash
# Após make deploy-api, na EC2 via SSH:
aws s3 sync s3://atua-<account-id>-releases/latest/ /opt/atua-api/
sudo systemctl restart atua-api
sudo journalctl -u atua-api -f   # acompanhar logs
```

A API escuta em `http://0.0.0.0:80` (porta 80 liberada no Security Group `sgApi`).

### Arquivos relevantes

- `infra/systemd/atua-api.service` — fonte de verdade do unit systemd.
  **Não editar diretamente na instância** (ela é efêmera; as alterações
  seriam perdidas no próximo `make up`). Edite aqui e rode `make up`.
- `infra/lib/atua-compute-stack.ts` — user-data que instala o unit e
  gerencia o ciclo de vida do serviço.

### Pendência resolvida

- ✅ Unit file `atua-api.service` criado em `infra/systemd/`.
- ✅ `make deploy-api` disponível para publicar o artefato.
- ✅ Runtime corrigido de `dotnet-runtime-8.0` para `dotnet-runtime-10.0`
  (alinhado ao `TargetFramework=net10.0` do projeto).

### Collector (atua-collector-base)

> **Nota de atualização (corte de custo, set/2026):** a descrição abaixo
> refere-se à topologia PADRÃO (duas instâncias). Para a topologia
> alternativa de instância única, ver §4.1 (`collocateCollector`).

A instância `atua-collector-base` roda o Agente Coletor real
(RF-009/016/017, ADR-023): Worker .NET com Microsoft.Playwright
(Chromium headless) e consumer de Change Streams do MongoDB. Seu
`user-data` instala o runtime .NET, as bibliotecas do Chromium headless,
lê os secrets de Mongo/RDS, escreve `/etc/atua-collector.env`, instala e
habilita a unit systemd `atua-collector.service` (fonte de verdade em
`infra/systemd/atua-collector.service`), sincroniza
`s3://atua-<account-id>-releases/collector-latest/` para
`/opt/atua-collector`, instala os browsers do Playwright e **inicia o
serviço somente se o artefato (`Atua.Collector.dll`) estiver presente**.
Sua IAM role (`atua-collector-ec2-role`) lê apenas os secrets de
Mongo/RDS e o bucket de releases — sem acesso a KMS nem ao secret
`atua/app-secrets` (D9 — Variante B).

#### Credenciais de serviço do Collector (service_credentials)

As 3 credenciais que o Collector usa para chamar a API interna
(`CollectorWorker__ClaimServiceToken`, `CollectorWorker__CompleteServiceToken`,
`CollectorWorker__EligibilityServiceToken`) são geradas pela skill
`.github/skills/atua-seed-collector-credentials` e gravadas (hash SHA-256)
na tabela `service_credentials` do RDS.

**Por que não ficam em `/etc/atua-collector.env`:** esse arquivo é
reescrito do zero pelo `user-data` a cada boot/recriação da instância
(topologia colocada ou não), apagando qualquer token colado manualmente
nele. Os tokens de serviço, portanto, ficam em um arquivo **separado** e
não gerenciado pelo CDK/user-data:

* Arquivo: `/etc/atua-collector.tokens.env` (root:root, `600`), fora do
  repositório e fora do alcance do `user-data`.
* Carregado via drop-in systemd:
  `/etc/systemd/system/atua-collector.service.d/tokens.conf` com
  `EnvironmentFile=/etc/atua-collector.tokens.env`.
* Sobrevive a `systemctl restart`/reboot da unit, mas **não sobrevive** à
  destruição/recriação da instância EC2 (ela é efêmera) — após um novo
  `make up`/recriação do Compute, os tokens precisam ser reinstalados
  manualmente seguindo os passos abaixo.

**Como regenerar/reinstalar (requer aprovação explícita):**

1. Rodar `.github/skills/atua-seed-collector-credentials/scripts/seed-collector-credentials.sh`
   (gera 3 credenciais novas; usar `--revoke-existing` apenas se as
   antigas devem ser invalidadas) com saída redirecionada para um arquivo
   local temporário com `chmod 600` — nunca imprimir os tokens no
   terminal/chat/logs.
2. Extrair as 3 linhas `CollectorWorker__*Token` para um arquivo
   `KEY=VALUE` (sem aspas, sem `export`).
3. Copiar esse arquivo via `scp` para a instância, mover com
   `sudo install -m 600 -o root -g root` para
   `/etc/atua-collector.tokens.env`, e apagar o arquivo temporário remoto.
4. Garantir o drop-in `tokens.conf` (criar se ainda não existir, ver
   acima), rodar `sudo systemctl daemon-reload && sudo systemctl restart atua-collector`.
5. Validar com `.github/skills/atua-seed-collector-credentials/scripts/list-collector-credentials.sh`
   que as credenciais esperadas estão `ativa` e acompanhar
   `journalctl -u atua-collector` por ausência de `401`.
6. Apagar o arquivo temporário local com segurança (`rm`) e confirmar que
   nenhum token ficou em histórico de shell, logs ou documentação.

**Melhoria futura sugerida (não aplicada):** mover esses 3 tokens para
AWS Secrets Manager ou SSM Parameter Store (`SecureString`), com o
`user-data`/um script de bootstrap buscando-os no boot via IAM role —
elimina a dependência de reinstalação manual pós-recriação da instância,
ao custo de ~US$ 0,40/mês por secret no Secrets Manager (ou gratuito via
SSM Parameter Store Standard). Decisão de infraestrutura a ser avaliada
e registrada em ADR separado antes de implementar.

---

Gerado automaticamente pelo CDK como parte de `AtuaNetworkStack`
(recurso nativo `AWS::EC2::KeyPair`, sem `publicKeyMaterial` informado).
A AWS gera o par e guarda a **chave privada** automaticamente em
**SSM Parameter Store** (`SecureString`, caminho `/ec2/keypair/<key-pair-id>`)
— o material privado nunca passa pelo código, pelo CDK.out ou pelo
repositório. Para uso com SSH, rode `make retrieve-keypair` após o
primeiro `make up`: isso busca o valor na SSM e salva localmente em
`~/.ssh/atua-mvp-key.pem` (fora do repositório, coberto pelo `.gitignore`).

---

## 8. Pré-requisitos de execução — estado em 2026-08-30

1. ✅ **Credenciais AWS**: perfil dedicado **`moldato`** (usuário IAM
   `admin-devops`, grupo `admin-devops` com `PowerUserAccess`), conta
   `462991286554`, região `sa-east-1`. Use sempre `AWS_PROFILE=moldato`.
2. ✅ **`make bootstrap-cdk`**: concluído. Stack `CDKToolkit` em
   `CREATE_COMPLETE`, parâmetro `/cdk-bootstrap/hnb659fds/version` = `32`.
   Foi necessário criar a customer managed policy
   `AtuaCdkBootstrapExecutionPolicy` e anexá-la ao usuário, porque
   `PowerUserAccess` exclui `iam:*` via `NotAction`. A policy **permanece
   anexada** para permitir reruns.
3. ✅ **SSH restrito** ao IP autorizado do operador (`186.236.211.36/32`)
   em `lib/atua-network-stack.ts`.
4. ✅ **Budget configurado**: "Five-Spend Budget", US$ 5/mês, alerta em 80%
   do previsto. ⚠️ Ver o alerta de estouro de orçamento em §4.
5. ✅ **Provisionamento executado** com autorização explícita do usuário.
6. ✅ **Unit file systemd**: `infra/systemd/atua-api.service` criado. O
   user-data instala e habilita `atua-api.service` automaticamente, e
   inicia o processo somente se o artefato estiver presente em
   `/opt/atua-api/Atua.Api.dll`. ⬜ **Ainda pendente**: publicar o
   primeiro artefato de deploy com `make deploy-api` e em seguida
   `make up` para a API subir de fato.

### ⚠️ Débito técnico crítico em aberto

A role `cdk-hnb659fds-cfn-exec-role-462991286554-sa-east-1` ficou com
**`AdministratorAccess`** — padrão do CDK quando `cdk bootstrap` roda sem
`--cloudformation-execution-policies`. Aceito conscientemente pelo
`orchestrator` durante o MVP, por ser infra descartável e **sem dados de
cliente**.

**Deve ser substituída por uma execution policy restrita, derivada de
`make synth`, antes de qualquer uso com dados reais de clientes.** Detalhes
em [`docs/architecture/aws-ambiente-mvp.md`](../docs/architecture/aws-ambiente-mvp.md) §1.2.

### Nota de segurança sobre credenciais

A máquina de desenvolvimento tem credenciais AWS de **outros
projetos/clientes** em `~/.aws/credentials`. Sempre passe
`AWS_PROFILE=moldato` explicitamente ao rodar os comandos deste `Makefile`,
para evitar tocar recursos de outra conta.

Nunca registre em documentação, commits ou issues: valores de secrets,
senhas do RDS ou o material privado do Key Pair. Apenas nomes e ARNs.

---

## Referências

- `docs/decisions/ADR-012-mongodb-atlas-free-tier.md`
- `docs/decisions/DECISAO_FINAL_ADR012_APROVADA.md`
- `docs/decisions/ADR-008-data-retention-backup-strategy.md`
- [`docs/architecture/aws-ambiente-mvp.md`](../docs/architecture/aws-ambiente-mvp.md)
  — estado real do ambiente provisionado, IDs dos recursos e débitos técnicos
- [`docs/guides/iam-bootstrap-cdk-moldato.md`](../docs/guides/iam-bootstrap-cdk-moldato.md)
  — permissões e recuperação do `cdk bootstrap`
- Histórico de decisões do Plano v3 (destroy+recreate) — registrado nas
  conversas do `orchestrator` com o `aws-architect` nesta sessão.
