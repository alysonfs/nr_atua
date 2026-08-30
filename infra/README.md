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

## 6. Bootstrap da EC2 (mecanismo, sem conteúdo real ainda)

O `user-data` da instância `atua-api-master` instala o runtime .NET e
tenta sincronizar `s3://atua-<account-id>-releases/latest/` para
`/opt/atua-api`. Nesta entrega o bucket de releases está **vazio** —
nenhum artefato de deploy real foi publicado. Nenhum `systemd` service é
habilitado automaticamente (evita subir processo incompleto). A
publicação do artefato e o unit file do `systemd` são responsabilidade
futura do `backend-engineer` / `release-versioning`.

A instância `atua-collector-base` tem um `user-data` **absolutamente
mínimo**: apenas grava uma linha de log confirmando o boot
(`/var/log/atua-collector-boot.log`). Nenhum pacote, script, cron ou
dependência de scraping é instalado. Sua IAM role (`atua-collector-ec2-role`)
não tem nenhuma policy anexada — sem acesso a secrets, S3 ou RDS.

---

## 7. Key Pair SSH (`atua-mvp-key`)

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
6. ⬜ **Pendente**: publicar o primeiro artefato de deploy em
   `s3://atua-462991286554-releases/latest/`. Sem isso, a API sobe com
   SO + runtime prontos, mas sem processo de aplicação rodando.

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
