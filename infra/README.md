# ATUA MVP — Infraestrutura AWS (ADR-012, Plano v3)

Este diretório contém a infraestrutura como código (AWS CDK v2, TypeScript)
do MVP do ATUA, conforme `docs/decisions/ADR-012-mongodb-atlas-free-tier.md`
e as decisões de ciclo de vida registradas nas conversas com o
`orchestrator` (Plano v3: destroy+recreate como padrão de "desligar").

**Nenhum recurso foi criado na AWS a partir deste código ainda.** Toda
execução real (`cdk deploy`, `cdk bootstrap`, `make up`, `make down`,
scripts de RDS) requer aprovação explícita e credenciais AWS configuradas
localmente por quem for executar.

---

## 1. Arquitetura (3 stacks CDK + RDS operacional)

| Stack / recurso   | Ciclo de vida | Conteúdo | Custo aproximado |
|---|---|---|---|
| `AtuaNetworkStack` | Persistente (nunca precisa ser destruída) | VPC, subnets públicas/privadas isoladas, Internet Gateway, 3 Security Groups (API, RDS, Collector), Key Pair `atua-mvp-key` | US$ 0/mês |
| `AtuaDataStack`    | Persistente (só remove com `destroy-all-data`) | S3 (`frontends`, `backups`, `releases`), Secrets Manager (3 placeholders, sem valores reais em código) | ~US$ 1,20-1,80/mês |
| `AtuaComputeStack` | **Efêmero** (destruído/recriado a cada ciclo down/up) | EC2 t3.micro `atua-api-master` (API Master, com bootstrap real) **+** EC2 t3.micro `atua-collector-base` (apenas SO+rede, sem nenhuma lógica de coleta) | US$ 0/mês dentro do Free Tier (⚠️ ver risco no §4) |
| RDS PostgreSQL     | **Semi-efêmero, gerenciado FORA do CDK** (ver §2) | `atua-postgres-mvp` (db.t3.micro) | US$ 0/mês dentro do Free Tier; ~centavos/mês quando "down" (snapshot) |

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

| Estado | Custo aproximado/mês |
|---|---|
| Ligado (`up`, dentro do Free Tier) | ~US$ 1,70 – 3,21 (Secrets Manager + S3; sem domínio/Route53) |
| Desligado (`down`) | ~US$ 1,20 – 1,21 (Secrets Manager + storage do snapshot RDS, poucos centavos) |
| Destruído (`destroy`) | ~US$ 1,20 (S3 + Secrets retidos) |
| Destruído total (`destroy-all-data`) | ~US$ 0 |

⚠️ **Risco de custo confirmado e aceito nesta sessão**: como
`atua-collector-base` agora nasce junto com `atua-api-master` no mesmo
ciclo `up`/`down`, as **duas** instâncias t3.micro rodam simultaneamente.
O Free Tier de EC2 concede 750h/mês **combinadas** por conta (não por
instância) — rodar as duas 24/7 consome ~1.460h/mês, ~710h acima do Free
Tier. Isso pode gerar um custo extra estimado em **~US$ 8-9/mês**, não
incluído nos números acima nem no ADR-012 original. Se o padrão de uso for
"liga/desliga" (não 24/7 contínuo), esse excedente tende a ser bem menor
ou nulo — mas deve ser monitorado via Cost Explorer sob demanda.

Não incluídos nesta entrega (removidos/decididos pelo orchestrator):
RDS Proxy, KMS CMK dedicada (usa chaves gerenciadas padrão `aws/rds` e
`aws/secretsmanager`), domínio/Route53, Elastic IP (IP público dinâmico
aceito).

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

## 8. O que falta para autorizar a execução real

1. **Credenciais AWS configuradas** localmente, em um **perfil dedicado ao
   projeto ATUA** (não usar os perfis já presentes na máquina para outros
   clientes/projetos — ver nota de segurança abaixo), com permissão para
   criar VPC/EC2/RDS/S3/Secrets/IAM na conta e região `sa-east-1`.
2. `make bootstrap-cdk` — preparar a conta para assets do CDK (1x, custo
   desprezível, cria só um bucket S3 interno do CDK).
3. A regra de SSH (porta 22) do `sgApi` está restrita ao IP autorizado do
   operador (`186.236.211.36/32`) em `lib/atua-network-stack.ts`.
4. **Alerta de orçamento (Billing Alert)** configurado na conta AWS pelo
   usuário — este arquiteto não cria alarmes de CloudWatch customizados
   (restrição vigente), mas recomenda fortemente que o usuário configure
   o alerta de billing padrão do AWS Billing Console antes do primeiro
   `make up`, especialmente por causa do risco de Free Tier compartilhado
   descrito em §4.
5. **Confirmação final explícita do usuário**: "pode rodar `make up`" —
   nenhum destes scripts deve ser executado sem esse sinal verde direto,
   mesmo estando o código pronto e sintetizado com sucesso.
6. (Opcional, mais tarde) Publicar o primeiro artefato de deploy em
   `s3://atua-<account-id>-releases/latest/` — sem isso, a API sobe sem
   processo de aplicação rodando (apenas SO + runtime prontos).

### Nota de segurança sobre credenciais

Durante a validação desta entrega, foi identificado que a máquina onde
este código está sendo desenvolvido já tem credenciais AWS reais
configuradas (`~/.aws/credentials`), associadas a **outros
projetos/clientes**, não relacionadas ao ATUA. Antes de qualquer execução
real, configure um perfil AWS **separado e dedicado** a este projeto
(ex.: `aws configure --profile atua-mvp`) e use `AWS_PROFILE=atua-mvp` ao
rodar os comandos deste `Makefile`, para evitar qualquer risco de tocar
recursos de outra conta/cliente.

---

## Referências

- `docs/decisions/ADR-012-mongodb-atlas-free-tier.md`
- `docs/decisions/DECISAO_FINAL_ADR012_APROVADA.md`
- `docs/decisions/ADR-008-data-retention-backup-strategy.md`
- Histórico de decisões do Plano v3 (destroy+recreate) — registrado nas
  conversas do `orchestrator` com o `aws-architect` nesta sessão.
