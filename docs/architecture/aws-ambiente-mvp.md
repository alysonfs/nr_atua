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
  subnets privadas do RDS **ganharam rota `0.0.0.0/0` → IGW em 2026-09-03**
  para viabilizar o acesso público direto ao RDS (dev local, sem túnel SSH);
  instâncias sem IP público nessas subnets continuam inalcançáveis de fora.
- **SSH restrito** ao IP do operador (`186.236.211.36/32` — IP dinâmico,
  pode divergir do atual). Não há `0.0.0.0/0` na porta 22.
- **Postgres (5432) liberado no SG do RDS** para o IP do operador
  (`186.236.211.186/32` em 2026-09-03) além dos SGs da API e do Collector.
  IP dinâmico: quando mudar, atualizar o `/32` em
  `infra/lib/atua-network-stack.ts` e redeployar o `AtuaNetworkStack`.
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
| `PubliclyAccessible` | `true` (desde 2026-09-03 — dev local sem túnel SSH; ingress restrito ao IP do operador) |
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

**Atualizado em 2026-09-30 (topologia colocada ativa — ver nota abaixo):**

| Instância | ID (2026-09-30) | IP público (2026-09-30) | Tipo | Estado | Topologia |
|---|---|---|---|---|---|
| API + Collector (colocados) | `i-0f32f8f0cd771c01f` | `54.233.12.205` | `t3.micro` | `running` | `collocated` |

Não há Elastic IP: os IPs públicos são dinâmicos e trocam a cada recriação
(e também a cada `cloud-init clean && reboot` forçado, como ocorreu nesta
sessão).

A tabela de duas instâncias (histórica, válida até 30/09/2026) era:

| Instância | ID (2026-08-30) | IP público (2026-08-30) | Tipo | Estado |
|---|---|---|---|---|
| API Master | `i-0b991614473e5be48` | `18.231.108.178` | `t3.micro` | `running` |
| Collector (base) | `i-09334d266a60f8e38` | `18.231.109.150` | `t3.micro` | `running` |

A instância Collector, quando em topologia de duas instâncias, é apenas
**SO + rede** — nenhuma lógica de coleta, scraping ou agendamento foi
implantada (ver `infra/README.md` §6). Na topologia colocada (atual),
API e Collector rodam como dois serviços `systemd` (`atua-api`,
`atua-collector`) na mesma instância.

> **Nota (2026-09-29, preparação — NÃO aplicado na AWS):** o CDK foi
> preparado para suportar uma topologia alternativa de **instância
> única** (Collector rodando na mesma EC2 da API), via a prop
> `collocateCollector` de `AtuaComputeStack` (default `false`, preserva a
> topologia de duas instâncias descrita acima). É a Recomendação 1 de
> corte de custo da análise Cost Explorer de set/2026 (~US$ 17-18/mês de
> economia). Nenhum recurso real foi criado, alterado ou destruído por
> essa preparação — a tabela acima continua refletindo o estado
> efetivamente provisionado. Ativar essa topologia exige destruir e
> recriar `AtuaComputeStack` e aprovação humana explícita. Detalhes
> completos, plano de rollout/rollback e critérios de validação (memória
> livre, ciclo de coleta, logs) em `infra/README.md` §4.1.

> **Atualização (2026-09-30, tentativa de ativação real — BLOCKED,
> nenhuma mudança aplicada):** o orchestrator autorizou explicitamente a
> ativação da Recomendação 1 (`cdk deploy AtuaComputeStack
> -c collocateCollector=true`). Antes de agir, o estado real da AWS foi
> verificado (somente leitura) e **contradisse a hipótese de ambiente
> parado desde 25/09**: `AtuaComputeStack` seguia `CREATE_COMPLETE` com
> as duas instâncias descritas na tabela acima em `running` (sem
> reinício desde o lançamento original), RDS `atua-postgres-mvp` em
> `available` e `https://api.atyno.com.br/health` respondendo HTTP 200.
> O `cdk diff` confirmou escopo isolado à `AtuaComputeStack` (Network e
> Data sem diferenças). O `cdk deploy` falhou com `UPDATE_FAILED —
> "User data is limited to 16384 bytes"`: o `UserData` fundido (boot da
> API + boot do Collector, incluindo instalação do Chromium do
> Playwright) excede o limite rígido de 16 KiB do EC2. CloudFormation
> executou rollback automático (`UPDATE_ROLLBACK_COMPLETE`) sem afetar
> as instâncias originais nem qualquer recurso de Network/Data/RDS/KMS.
> **A tabela acima continua refletindo o estado real e efetivamente
> provisionado** — a topologia colocada segue NÃO aplicada. Detalhes da
> causa raiz e alternativas de correção do `UserData` em
> `docs/decisions/ADR-034-topologia-colocada-api-collector-corte-de-custo.md`
> **Atualização (2026-09-30, ativação real concluída e validada):** o
> defeito de `UserData` > 16 KiB descrito acima foi corrigido
> externalizando o bootstrap do Collector para um objeto S3
> (`releasesBucket`), lido pela própria instância no boot. A topologia
> colocada foi ativada com sucesso: `cdk deploy AtuaComputeStack
> -c collocateCollector=true` destruiu as duas instâncias antigas e
> criou a instância única `i-0f32f8f0cd771c01f` (`54.233.12.205`).
> `AtuaNetworkStack` e `AtuaDataStack` confirmados sem diferenças (RDS
> e KMS intocados). Validado: `UserData` final ≈10,1–10,7 KB (bem
> abaixo do limite), `https://api.atyno.com.br/health` → 200, ambos os
> serviços `systemd` (`atua-api`, `atua-collector`) `active (running)`
> sem restarts nem OOM-kill, memória ~185–197Mi de 916Mi (abaixo do
> baseline da topologia de duas instâncias: API 257Mi + Collector 292Mi).
> **Atualização (2026-09-30, validação funcional concluída):** a
> "ressalva" registrada anteriormente aqui (connection string Postgres
> aparentemente corrompida em runtime) foi investigada até a causa raiz
> e **corrigida**. Não era um defeito de código nem do `UserData`: as
> units `systemd` já estavam `enabled` de uma sessão de depuração manual
> anterior e foram iniciadas pelo boot normal **antes** do estágio
> `cloud-final` do `cloud-init` reescrever `/etc/atua-api.env` /
> `/etc/atua-collector.env` com o valor correto do secret — os processos
> ficaram com um ambiente residual (`Password=` com placeholder de uma
> sessão manual anterior) na memória, enquanto o arquivo em disco já
> estava correto. Corrigido com `systemctl restart` dos dois serviços
> (ação operacional, sem alteração de código/CDK, sem troca de senha do
> RDS). Validado pós-restart: SHA-256 do campo `Password=` em
> `/proc/<PID>/environ` de ambos os processos bate com o SHA-256 do
> secret; `journalctl` sem `ArgumentException`; `GET /health` → 200;
> `make collector-trigger-cycle` executado do início ao fim (signin,
> desativação/reativação da integração, novo comando `Pending`);
> `free -h` monitorado por 60s durante o ciclo sem uso de swap e memória
> estável (~230Mi de 916Mi usados). Único log residual é um `401` em
> `/commands/claim`, débito técnico pré-existente e documentado
> (`ServiceCredential` vazio, ADR-021), fora do escopo de infraestrutura.
> Recomendação de robustez registrada (trocar `systemctl start` por
> `systemctl restart` no `UserData` para eliminar esta classe de risco
> em reboots futuros) e item de higiene de SGs (regras de SSH temporárias
> de depuração ainda presentes) — ambos pendentes de decisão do
> `orchestrator`. Detalhes completos, evidências e comandos de
> diagnóstico em
> `docs/decisions/ADR-034-topologia-colocada-api-collector-corte-de-custo.md`
> (seção "Defeito de aplicação — causa raiz real encontrada e corrigida
> (30/09/2026)").
>
> **Atualização (2026-09-30, aprovação executada — código e SGs; deploy
> pendente):** as duas recomendações acima foram aprovadas pelo usuário.
> (1) `systemctl start` → `systemctl restart` aplicado no `UserData` da
> API e do Collector (`infra/lib/atua-compute-stack.ts`), validado com
> `tsc --noEmit` e `cdk synth AtuaComputeStack` nas duas topologias
> (UserData final seguiu bem abaixo do limite de 16 KiB). **`cdk deploy`
> não foi executado nesta rodada**: `cdk diff` revelou que
> `ec2.MachineImage.latestAmazonLinux2023()` resolveu uma AMI mais nova
> que a da instância em produção, o que forçaria `ImageId requires
> replacement` — ou seja, o próximo deploy recriaria a instância
> (`i-0f32f8f0cd771c01f`, novo IP) em vez de apenas reiniciar o serviço,
> contrariando a premissa de "reboot in-place" desta tarefa. Decisão
> necessária do `orchestrator`/operador antes do deploy: pinar a AMI
> atual (evitar recriações não intencionais a cada synth) ou aceitar a
> recriação (a stack já é tratada como efêmera) e seguir o roteiro
> completo de pós-deploy (`make sync-api-domain`, checagem de hash do
> secret em `/proc/<pid>/environ`, `/health`, `journalctl`,
> `make collector-trigger-cycle`, `free -h`). (2) Auditoria de ingress
> SSH (somente leitura) nos SGs `sg-09f1ffef1774526c1` e
> `sg-0fbd7bac215fedb3a` confirmou 2 regras residuais fora do CDK
> (`186.236.211.186/32`, descrição "temp-servicecredential*") — ambas
> **removidas**. A regra `186.236.210.61/32` (também fora do CDK, e
> descrita como debug residual no SG do Collector) **não foi removida**
> por ser o IP público atual da sessão operacional usada nesta execução
> (confirmado por checagem externa de IP) — reportada para decisão
> (declarar no CDK ou remover mediante confirmação do operador).
>
> **Atualização (2026-10-01, AMI pinada e deploy aplicado):** a AMI foi
> fixada no CDK via contexto `pinnedAmiId` (default =
> `ami-069bb7e5cd028c614`, a AMI confirmada da instância viva
> `i-0f32f8f0cd771c01f`), substituindo
> `ec2.MachineImage.latestAmazonLinux2023()` nas 3 declarações de
> `ec2.Instance` em `infra/lib/atua-compute-stack.ts`. `tsc --noEmit` e
> `cdk synth` das duas topologias OK; `ImageId` confirmado fixo
> (`ami-069bb7e5cd028c614`) no template sintetizado. `cdk diff
> AtuaComputeStack -c collocateCollector=true` mostrou somente a
> mudança de `UserData` (`systemctl start` → `restart`, já aprovada
> acima) e remoção do parâmetro SSM de AMI dinâmica não mais
> referenciado — **sem** `ImageId` e **sem** replacement de instância.
> `cdk deploy` aplicado: CloudFormation atualizou o `UserData` via
> stop/start in-place (`Replacement: Conditional`, não efetivado — o ID
> da instância não mudou). Esse stop/start trocou o IP público (sem
> Elastic IP, por design) de `54.233.12.205` para `18.231.123.4`;
> `make sync-api-domain` rodado para atualizar
> `api-origin.atyno.com.br`. Como o stop/start por si só não reexecuta
> o `UserData` (cloud-init só roda user-data uma vez por `instance-id`),
> foi necessário `cloud-init clean --logs && reboot` (autorizado) para o
> novo `UserData` rodar de fato. Validação pós-reboot: `cloud-init
> status` = `done`; `atua-api`/`atua-collector` ativos; `/health`
> (porta 80) = HTTP 200; hash SHA-256 de `Authentication__SigningKey`
> em `/proc/<pid>/environ` do `atua-api` idêntico ao hash do
> `jwtSigningKey` no secret `atua/app-secrets` (sem expor o valor);
> `journalctl` sem erros de conexão persistentes (um timeout de DNS do
> MongoDB Atlas visto apenas nos primeiros segundos pós-boot,
> auto-recuperado); `make collector-trigger-cycle` disparou ciclo com
> sucesso (único log residual é o `401` pré-existente e já documentado
> de `ServiceCredential` vazio); `free -h` ~472Mi disponíveis, swap 0
> usado. Detalhes completos em
> `docs/decisions/ADR-034-topologia-colocada-api-collector-corte-de-custo.md`
> (seção "AMI fixada (pin) no CDK"). Item de higiene de SG
> (`186.236.210.61/32` fora do CDK) segue pendente de decisão, não
> alterado nesta execução.
> `cdk diff AtuaNetworkStack` → sem diferenças; `detect-stack-drift`
> confirma que o único drift remanescente nos SGs é exatamente essa
> regra preservada. Detalhes em
> `docs/decisions/ADR-034-topologia-colocada-api-collector-corte-de-custo.md`.

---

## 5. Dados persistentes (`AtuaDataStack`)

**Status:** Implementado. Sobrevive a `make down` e a `make destroy`.

**Buckets S3:**

- `atua-462991286554-frontends` — hospeda aplicativos web estáticos (Landing, Office, Manager, Tecnica)
- `atua-462991286554-backups` — backups de RDS
- `atua-462991286554-releases` — artifacts de release

### 5.1 Frontends publicados (atualizado em 2026-09-10)

**Bucket:** `atua-462991286554-frontends`  
**Região:** `sa-east-1`  
**Tipo:** origem S3 privada para CloudFront/OAC.

**URLs públicas HTTPS atuais:**

| App | URL |
|---|---|
| Landing | `https://atyno.com.br/` |
| Landing (`www`) | `https://www.atyno.com.br/` |
| Office | `https://office.atyno.com.br/` |
| Manager | `https://manager.atyno.com.br/` |
| Tecnica | `https://tecnica.atyno.com.br/` |

**URLs de acesso legado por website hosting:**

| App | URL | Base path | Deploy |
|---|---|---|---|
| Landing | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/landing/` | `/landing/` | `make deploy-landing` |
| Office | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/office/` | `/office/` | `make deploy-office` |
| Manager | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/manager/` | `/manager/` | `make deploy-manager` |
| Tecnica | `http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com/tecnica/` | `/tecnica/` | `make deploy-tecnica` |

**Configuração:**

- No estado legado, cada app usava um prefixo no `vite.config.ts`. Para o
  domínio aprovado, os builds usam `/` na raiz de cada subdomínio.
- Referências absolutas a `icons.svg` e outros assets foram migradas para `import.meta.env.BASE_URL` (commits `c4ed31d`, `0d5551f`, `8c75772`).
- O bucket bloqueia acesso público e permite leitura via CloudFront/OAC.

**Destino aprovado para o domínio `atyno.com.br`:**

| App | URL pública desejada |
|---|---|
| Landing | `https://atyno.com.br/` |
| Office | `https://office.atyno.com.br/` |
| Manager | `https://manager.atyno.com.br/` |
| Tecnica | `https://tecnica.atyno.com.br/` |
| API | `https://api.atyno.com.br/` |

Essa publicação usa Route 53, certificado ACM em `us-east-1` e CloudFront com
origem S3 protegida por OAC. O registro `.com.br` permanece no GoDaddy;
somente os nameservers foram delegados para a hosted zone Route 53. O
procedimento completo e os bloqueios remanescentes estão em
[`dominio-atyno-aws.md`](./dominio-atyno-aws.md).

A API pública responde em `https://api.atyno.com.br`. A distribuição
CloudFront usa a origem técnica `api-origin.atyno.com.br`, atualizada pelo alvo
`make sync-api-domain` com o IP público atual da EC2 efêmera após cada
`make up`.

**Estado do conteúdo (2026-08-30):**

- Os 4 apps contêm scaffold do Vite — nenhum conteúdo de negócio implementado.
- RF-018 (Landing com conteúdo) pendente.
- Roteamento não está implementado; ao introduzir react-router, será necessário configurar `basename` com o prefixo correspondente.

**Procedimento de deploy:**

```bash
cd infra/

# Deploy individual — destinos S3 usados pelas distribuições CloudFront
make deploy-landing
make deploy-office
make deploy-manager
make deploy-tecnica

# Atualizar a origem da API quando a EC2 for recriada
make sync-api-domain

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
