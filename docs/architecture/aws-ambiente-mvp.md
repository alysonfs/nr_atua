# Ambiente AWS do MVP — estado provisionado

**Status:** Implementado
**Data do provisionamento:** 2026-08-30
**Conta:** 462991286554 (Moldato)
**Região:** `sa-east-1`
**Perfil CLI:** `moldato` (usuário IAM `admin-devops`, membro do grupo
`admin-devops`, com `PowerUserAccess`)

Este documento descreve o ambiente **efetivamente provisionado** na AWS em
2026-08-30. Tudo o que está aqui foi verificado na conta. O que ainda é
plano ou intenção está registrado nos ADRs referenciados no final e **não**
é repetido aqui como se existisse.

Complementos:

- Operação diária e comandos: [`infra/README.md`](../../infra/README.md)
- Recuperação/permissões do bootstrap: [`docs/guides/iam-bootstrap-cdk-moldato.md`](../guides/iam-bootstrap-cdk-moldato.md)

---

## 1. Bootstrap do CDK

**Status:** Implementado — stack `CDKToolkit` em `CREATE_COMPLETE`.

| Item | Valor |
|---|---|
| Qualificador | `hnb659fds` (padrão) |
| Roles | `cdk-hnb659fds-*` (deploy, file-publishing, image-publishing, lookup, cfn-exec) |
| Bucket de assets | `cdk-hnb659fds-assets-462991286554-sa-east-1` |
| Repositório ECR | `cdk-hnb659fds-container-assets-462991286554-sa-east-1` |
| Parâmetro de versão | `/cdk-bootstrap/hnb659fds/version` = `32` |

### 1.1 Policy adicional necessária ao bootstrap

`PowerUserAccess` exclui `iam:*` via `NotAction`, o que impede o bootstrap de
criar as roles `cdk-hnb659fds-*`. Para destravar, foi criada a customer
managed policy **`AtuaCdkBootstrapExecutionPolicy`** e anexada ao usuário
`admin-devops`, conforme o JSON documentado em
[`docs/guides/iam-bootstrap-cdk-moldato.md`](../guides/iam-bootstrap-cdk-moldato.md) (§2.2).

A policy **permanece anexada** ao usuário para permitir reruns do bootstrap.
Isso é uma divergência consciente em relação ao checklist do guia (item
"remover a policy temporária"), aceita enquanto o ambiente for de MVP.

### 1.2 ⚠️ DÉBITO TÉCNICO CRÍTICO — execution role com `AdministratorAccess`

A role `cdk-hnb659fds-cfn-exec-role-462991286554-sa-east-1` ficou com
**`AdministratorAccess`** anexada, que é o comportamento padrão do CDK quando
`cdk bootstrap` é executado **sem** `--cloudformation-execution-policies`.

| Campo | Conteúdo |
|---|---|
| Risco | Qualquer `cdk deploy` bem-sucedido pode alterar **qualquer** recurso da conta, incluindo IAM. Escalada de privilégio a partir de um template comprometido. |
| Decisão | Aceito durante o MVP. Decisão consciente do `orchestrator` (Otto). |
| Justificativa | Infra descartável (ciclo `down`/`up` diário), conta isolada, **sem dados de cliente**. |
| Condição de saída | **Obrigatório** substituir por uma execution policy restrita, derivada de `make synth`, **antes de qualquer uso com dados reais de clientes**. |
| Referência | `docs/guides/iam-bootstrap-cdk-moldato.md` §2 ("Limites importantes") já previa este risco e recomendava o contrário. |

Este item deve ser tratado como bloqueador de produção, não como melhoria
opcional.

---

## 2. Rede (`AtuaNetworkStack`)

**Status:** Implementado.

| Recurso | Identificador |
|---|---|
| VPC | `vpc-009cc337786410f7f` |
| Subnet pública | `subnet-08ce220c8ed92b57f` |
| Subnet isolada (AZ `sa-east-1a`) | `subnet-05a57a1367b314430` |
| Subnet isolada (AZ `sa-east-1b`) | `subnet-03c85e5521c9177ec` |
| Security Group RDS | `sg-078d7c67f3b750e9f` |
| Security Group API | `sg-09f1ffef1774526c1` |
| Security Group Collector | `sg-0fbd7bac215fedb3a` |
| Key Pair SSH | `atua-mvp-key` |

### Decisões de rede

- **Sem NAT Gateway** (`natGateways: 0`). Decisão de custo: um NAT Gateway em
  `sa-east-1` custaria mais que todo o restante do ambiente somado. As
  subnets privadas são **isoladas** (sem saída para a internet); recursos que
  precisam de internet ficam na subnet pública com IP público.
- **SSH restrito** ao IP `186.236.211.36/32` do operador. Não há `0.0.0.0/0`
  na porta 22.
- A **chave privada** do Key Pair é gerada pela AWS e armazenada em
  SSM Parameter Store como `SecureString`. O material privado não está no
  repositório, no `cdk.out`, nem neste documento. Recuperação via
  `make retrieve-keypair`.

---

## 3. Banco de dados — RDS PostgreSQL

**Status:** Implementado. Gerenciado **fora do CDK**, por scripts em
`infra/scripts/` (ver `infra/README.md` §2 para a justificativa).

| Atributo | Valor |
|---|---|
| Identificador | `atua-postgres-mvp` |
| Engine | PostgreSQL 16.13 |
| Classe | `db.t3.micro` |
| Storage | 20 GB, `gp2` |
| Multi-AZ | Não (Single-AZ) |
| `StorageEncrypted` | `true` (chave gerenciada `aws/rds`) |
| `PubliclyAccessible` | `false` |
| Retenção de backup | **1 dia** (ver §3.1) |

### 3.1 Restrição do Free Tier na retenção de backup

A conta está no **Free Tier plan**. A tentativa de criar a instância com
`--backup-retention-period 7` foi rejeitada com `FreeTierRestrictionError`.
O valor foi ajustado para **1 dia** (commit `ba2583a`).

| Campo | Conteúdo |
|---|---|
| Trade-off | O RPO garantido por PITR (point-in-time recovery) cai de 7 dias para 1 dia. |
| Mitigação | `make down` gera um **snapshot final** da instância. Snapshots manuais persistem independentemente da janela de retenção automática, então o histórico de recuperação não fica limitado a 24h enquanto houver disciplina de `down`. |
| Reversão | Se a conta sair do Free Tier plan, a retenção pode voltar a 7 dias sem impacto arquitetural. |

---

## 4. Computação (`AtuaComputeStack`)

**Status:** Implementado. Recursos **efêmeros** — destruídos por `make down`
e recriados por `make up`; os IDs e IPs abaixo mudam a cada ciclo.

| Instância | ID (2026-08-30) | IP público (2026-08-30) | Tipo | Estado |
|---|---|---|---|---|
| API Master | `i-0b991614473e5be48` | `18.231.108.178` | `t3.micro` | `running` |
| Collector (base) | `i-09334d266a60f8e38` | `18.231.109.150` | `t3.micro` | `running` |

Não há Elastic IP: os IPs públicos são dinâmicos e trocam a cada recriação.

A instância Collector é apenas **SO + rede** — nenhuma lógica de coleta,
scraping ou agendamento foi implantada (ver `infra/README.md` §6).

---

## 5. Dados persistentes (`AtuaDataStack`)

**Status:** Implementado. Sobrevive a `make down` e a `make destroy`.

**Buckets S3:**

- `atua-462991286554-frontends` — hospeda aplicativos web estáticos (Landing, Office, Manager, Tecnica)
- `atua-462991286554-backups` — backups de RDS
- `atua-462991286554-releases` — artifacts de release

### 5.1 Frontends publicados (2026-08-30)

**Bucket:** `atua-462991286554-frontends`  
**Região:** `sa-east-1`  
**Tipo:** website estático (sem CloudFront)

**URLs de acesso (website hosting):**

| App | URL | Base path | Deploy |
|---|---|---|---|
| Landing | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/landing/` | `/landing/` | `make deploy-landing` |
| Office | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/office/` | `/office/` | `make deploy-office` |
| Manager | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/manager/` | `/manager/` | `make deploy-manager` |
| Tecnica | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/tecnica/` | `/tecnica/` | `make deploy-tecnica` |

**Configuração:**

- Cada app usa base path condicional no `vite.config.ts`:
  - Em `serve` (desenvolvimento): base path é `/`.
  - Em `build` (produção): base path é `/<prefixo>/` (ex.: `/office/`).
- Referências absolutas a `icons.svg` e outros assets foram migradas para `import.meta.env.BASE_URL` (commits `c4ed31d`, `0d5551f`, `8c75772`).
- Bucket policy `PublicReadFrontends` no CDK cobre os 4 prefixos; a raiz do bucket (`/`) permanece `403 Forbidden`.

**Estado do conteúdo (2026-08-30):**

- Os 4 apps contêm scaffold do Vite — nenhum conteúdo de negócio implementado.
- RF-018 (Landing com conteúdo) pendente.
- Roteamento não está implementado; ao introduzir react-router, será necessário configurar `basename` com o prefixo correspondente.

**Procedimento de deploy:**

```bash
cd infra/

# Deploy individual
make deploy-landing   # publica Landing em /landing/
make deploy-office    # publica Office em /office/
make deploy-manager   # publica Manager em /manager/
make deploy-tecnica   # publica Tecnica em /tecnica/

# Listar todos os alvos
make help | grep deploy
```

Os alvos estão definidos em `infra/Makefile`. Cada um executa:
1. `cd apps/<app> && pnpm run build` (vite build)
2. `aws s3 sync dist/ s3://atua-462991286554-frontends/<prefixo>/` (upload)

**Secrets (AWS Secrets Manager):**

- `atua/rds-postgres`
- `atua/mongodb-atlas`
- `atua/app-secrets`

> Apenas os **nomes** dos secrets são documentados. Valores, senhas,
> endpoints com credenciais e chaves privadas **nunca** devem ser registrados
> em documentação, commits ou issues.

### 5.2 CMK — Credential Cipher Key (D9)

**Status:** Implementado (2026-08-30).

| Atributo | Valor |
|---|---|
| Key ID | `93ad01dc-9907-41ba-a926-5f6f17f3c3da` |
| Alias | `alias/atua-credential-cipher-dev-mvp` |
| ARN | `arn:aws:kms:sa-east-1:462991286554:key/93ad01dc-9907-41ba-a926-5f6f17f3c3da` |
| Tipo | Simétrica (`ENCRYPT_DECRYPT`, `AWS_KMS`) |
| Estado | `Enabled` |
| Rotação automática | `true` — anual (365 dias), próxima: 2027-08-30 |
| `removalPolicy` | `RETAIN` — **obrigatório**, nunca destruir |
| Stack | `AtuaDataStack` (dado persistente, sobrevive a `down`/`destroy`) |

**Modelo de uso (envelope encryption):**

- A API gera uma DEK por integração via `kms:GenerateDataKey`.
- A DEK em texto claro cifra a credencial; só a DEK cifrada é persistida.
- Para decifrar, a API chama `kms:Decrypt` com a DEK cifrada.
- O isolamento por tenant vem da **DEK por integração** (no código da aplicação), não de uma CMK por tenant.

**Permissões IAM:**

| Role | Permissões KMS |
|---|---|
| `atua-api-ec2-role` | `kms:GenerateDataKey`, `kms:Decrypt`, `kms:DescribeKey` (apenas nesta CMK) |
| `atua-collector-ec2-role` | **Nenhuma** — Variante B aprovada |

**Identificador em runtime:**

O ARN da CMK é armazenado em SSM Parameter Standard (gratuito):
`/atua/dev-mvp/kms/credential-cipher-key-arn`

O user-data da instância da API lê este parâmetro no boot e injeta como
variável de ambiente `ATUA_KMS_KEY_ARN` em `/etc/environment`.

**SSM Parameter Standard** foi escolhido em vez de um novo Secret porque o ARN
da CMK não é dado sensível (não é a chave em si). Custo: US$0 vs US$0,40/mês
de um Secret adicional.

---

## 6. Custos observados

| Estado | Custo |
|---|---|
| Infra parada (`down`: Network + S3 + Secrets + CMK) | **~US$ 2,20/mês** — 3 secrets × US$ 0,40 + 1 CMK × US$ 1,00 |
| Infra ligada (`up`), fora do Free Tier | **~US$ 0,079/h** |
| Ligada 8h/dia útil | ~US$ 0,64/dia |
| Ligada 24h/dia | ~US$ 1,89/dia |

Dentro do Free Tier, o custo **incremental** de ligar a infra é ~0. Porém:

- as **2 instâncias t3.micro** consomem a cota de 750h/mês de EC2 em **dobro**;
- rodando 24/7, a cota se esgota em **~15 dias**, e o restante do mês é
  cobrado a preço cheio.

### ⚠️ Alerta de orçamento

O budget configurado pelo usuário é de **US$ 5/mês** ("Five-Spend Budget",
alerta em 80% do previsto).

O custo fixo da infra parada é agora **~US$ 2,20/mês** (3 secrets + 1 CMK),
consumindo 44% do budget só com recursos permanentes.

Com um padrão de uso de **8h/dia útil fora do Free Tier**, o custo projetado
é de **~US$ 14/mês**, o que **excede o budget em quase 3×**.

Recomendações (não são decisões — dependem do `orchestrator`):

1. Disciplina rígida de `make down` ao fim de cada dia de trabalho.
2. Revisar o valor do budget para um patamar compatível com o uso real, ou
   reduzir o uso (ex.: não subir a instância Collector enquanto ela não tiver
   função).

---

## 7. Divergências conhecidas em relação ao planejado

| Item | Planejado | Estado real |
|---|---|---|
| Execution role do CDK | Policy restrita derivada de `make synth` | `AdministratorAccess` (§1.2) |
| Policy de bootstrap do usuário | Temporária, remover após bootstrap | Permanece anexada, para reruns (§1.1) |
| Retenção de backup do RDS | 7 dias | 1 dia, por restrição do Free Tier (§3.1) |
| Budget mensal | US$ 5 | Projeção de uso 8h/dia excede o budget (§6) |
| Conteúdo dos frontends | Aplicativos funcionais (Landing, Office, Manager, Tecnica) | Scaffold do Vite; conteúdo pendente (RF-018) (§5.1) |

---

## Referências

- [`infra/README.md`](../../infra/README.md)
- [`docs/guides/iam-bootstrap-cdk-moldato.md`](../guides/iam-bootstrap-cdk-moldato.md)
- [`docs/architecture/aws-setup-checklist.md`](./aws-setup-checklist.md)
- [`docs/decisions/ADR-012-mongodb-atlas-free-tier.md`](../decisions/ADR-012-mongodb-atlas-free-tier.md)
- [`docs/decisions/ADR-008-data-retention-backup-strategy.md`](../decisions/ADR-008-data-retention-backup-strategy.md)
