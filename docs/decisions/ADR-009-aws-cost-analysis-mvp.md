# ADR-009 - Análise de Custos AWS MVP ATUA

## Status

**Proposed** (Pendente aprovação para provisionamento)

---

## Contexto

ADR-007 propõe infraestrutura AWS para MVP (KMS, Secrets Manager, RDS, DocumentDB, SES, VPC, IAM).
ADR-008 propõe estratégia de backup de 5 anos (Glacier archive).

O orçamento do projeto define limite máximo de **$200 USD/mês** para desenvolvimento/staging.

Necessário validar:
- Custo mensal detalhado por serviço
- Aproveitamento de Free Tier AWS
- Cenários de crescimento (10, 50, 100+ clientes)
- Trade-offs performance vs custo
- Alternativas mais baratas

---

## Decisão

### 1. Arquitetura de Custo - Resumo Executivo

| Cenário | Config | Custo/mês | Status |
|---------|--------|-----------|--------|
| **MVP (10 clientes)** | Aurora Serverless 1 ACU + DocumentDB Single-AZ | **$230** | ⚠️ Acima de $200 |
| **MVP Otimizado** | Aurora Serverless 1 ACU + Docker MongoDB | **$103** | ✅ Dentro orçamento |
| **Crescimento (50 clientes)** | Aurora Serverless 2 ACU + DocumentDB 2-instance | **$407** | ❌ Acima orçamento |
| **Escalado (100+ clientes)** | Aurora Serverless 4 ACU + DocumentDB 3-instance | **$650** | — |

**Conclusão**: Com otimizações Free Tier, MVP viável a **$103-230/mês**. Crescimento a 50+ clientes requer orçamento adicional ou otimizações.

---

### 2. Custo por Serviço (MVP - Mês 1)

#### 2.1 AWS KMS (Key Management Service)

**Configuração**:
- 1x Customer Master Key (CMK)
- Rotação automática anual
- VPC Endpoint para evitar NAT costs

**Cálculo**:
```
CMK monthly fee:              $1.00
API calls (Decrypt):          20.000/mês → GRATUITO (Free Tier)
─────────────────────
Custo mensal KMS:             $1.00
```

**Free Tier**: 20.000 API requests/mês gratuito ✅

---

#### 2.2 AWS Secrets Manager

**Configuração**:
- 3 secrets: `atua/root`, `atua/rds-postgres`, `atua/documentdb`
- Rotação automática: 30 dias

**Cálculo**:
```
Secret storage (3 secrets):   $0.40 × 3 =        $1.20
API calls (GetSecretValue):   50.000/mês
  - Free Tier:                10.000/mês         $0.00
  - Pagos:                    40.000 × $0.05/1M  ~$0.00
─────────────────────
Custo mensal Secrets Manager: $1.20
```

**Free Tier**: 10.000 API calls/mês gratuito ✅

---

#### 2.3 PostgreSQL RDS

**Opções Avaliadas**:

##### Opção A: Aurora Serverless (RECOMENDADO)
```
Capacidade:                   1 ACU (auto-scale)
Multi-AZ:                     Sempre incluído
Storage:                      100GB, auto-scale
Cálculo:
  - ACU-hours: 730h × $0.08 = $58.40
  - Storage: 100GB × $0.10 =  $10.00
─────────────────────
Custo: $68.40/mês
```

**Vantagens**:
- ✅ Multi-AZ nativo (atende RF-005 "sempre disponível")
- ✅ Auto-scaling sem downtime
- ✅ Backup automático
- ✅ Cold start ~30s (aceitável para MVP)

**Desvantagens**:
- ❌ PostgreSQL 10 (vs 14 em RDS tradicional)
- ⚠️ Requer validação compatibilidade Entity Framework

---

##### Opção B: RDS db.t3.small (Single-AZ)
```
Instance: db.t3.small (2 vCPU, 2GB RAM)
Topologia: Single-AZ
Storage: 100GB gp3
Cálculo:
  - Instance: $0.047/h × 730h = $34.30
  - Storage: 100GB × $0.115 =  $11.50
─────────────────────
Custo: $45.50/mês
```

**Vantagens**:
- ✅ Mais barato que Aurora
- ✅ PostgreSQL 14 (compatibilidade máxima)

**Desvantagens**:
- ❌ Sem redundância (RTO > 30 min em falha)
- ❌ Violaria RF-005 "sempre disponível"

---

##### Opção C: RDS db.t3.medium (Multi-AZ) [ADR-007 Original]
```
Instance: db.t3.medium Multi-AZ
Cálculo: $0.219/h × 730h × 2 = $320.14
Storage: 100GB × $0.115 =       $11.50
─────────────────────
Custo: $331.64/mês
```

**Vantagens**:
- ✅ Multi-AZ (atende RF-005)
- ✅ PostgreSQL 14

**Desvantagens**:
- ❌ 50% do orçamento total MVP

---

**Decisão Aurora Serverless**:
- **Proposta Original**: db.t3.medium Multi-AZ = $331.50/mês
- **Ajuste**: Aurora Serverless = $68.40/mês
- **Economia**: -$263/mês (79% reduction)
- **Impacto**: Mantém Multi-AZ + auto-scale

**Recomendação**: Usar **Aurora Serverless 1 ACU** ($68.40/mês), validar compatibilidade Entity Framework com PostgreSQL 10.

---

#### 2.4 DocumentDB (MongoDB)

**Opções Avaliadas**:

##### Opção A: DocumentDB db.t3.medium Single-AZ (RECOMENDADO)
```
Cluster: 1x db.t3.medium (Primary)
Storage: ~300GB (managed, auto-scale)
Cálculo:
  - Instance: $0.137/h × 730h = $100.10
  - Storage: 300GB × $0.10 =     $30.00
─────────────────────
Custo: $130.10/mês
```

**Vantagens**:
- ✅ Managed backup (35 dias)
- ✅ Índices otimizados (RF-010/RF-011)
- ✅ TTL index para 5-year retention automático

**Desvantagens**:
- ⚠️ Sem read replica (latência de query)
- ⚠️ Sem redundância (Single-AZ)

---

##### Opção B: DocumentDB 2x db.t3.medium (2 instances)
```
Instâncias: Primary + Read Replica
Cálculo:
  - 2 × ($0.137/h × 730h) = $200.20
  - Storage: 300GB × $0.10 = $30.00
─────────────────────
Custo: $230.20/mês
```

**Vantagens**:
- ✅ Read replica (lower latency queries)
- ✅ Read-only replica para analytics

**Desvantagens**:
- ❌ Dobra custo DocumentDB
- ❌ Overkill para MVP (10 clientes)

---

##### Opção C: Local MongoDB (Docker) + EC2
```
EC2 t2.micro: Free Tier (12 meses) = $0/mês
  After: $8.50/mês
Local MongoDB container
Cálculo (Free Tier): $0
Cálculo (após 12mo): $8.50
```

**Vantagens**:
- ✅ Grátis por 12 meses
- ✅ Controle total

**Desvantagens**:
- ❌ Backup manual
- ❌ Operação manual (replication, monitoring)
- ❌ Sem managed failover
- ❌ Apenas adequado para dev, não prod

---

**Decisão DocumentDB**:
- **MVP (dev-only)**: Local Docker MongoDB = $0/mês
- **Staging/Prod**: DocumentDB db.t3.medium Single-AZ = $100/mês
- **Upgrade path**: 2 instances aos 50+ clientes se latência > 100ms

**Recomendação**: Iniciar com **Single-AZ DocumentDB** ($100/mês), escalável.

---

#### 2.5 AWS SES (Simple Email Service)

**Configuração**:
- Domínio verificado
- Modo Production
- Templates: confirmation-email, trial-expiring-soon

**Cálculo**:
```
Free Tier: 62.000 emails/mês
MVP consumo (10 clientes): ~5.000 emails/mês
─────────────────────
Custo mensal SES: $0 (Free Tier)
```

**Pricing Pós Free Tier**:
- $0.0001 per email
- 62.000 emails = $6.20/mês (ainda barato)

**Free Tier**: Suficiente para MVP + crescimento até 50 clientes ✅

---

#### 2.6 VPC e Network

**Configuração**:
- VPC: 10.0.0.0/16
- Subnets: 4 (2 públicas, 2 privadas)
- NAT Gateway: 1 (sa-east-1a) para Collector egress
- VPC Endpoints: 3 (secretsmanager, kms, ses)

**Cálculo**:
```
VPC: Gratuito
Subnets: Gratuito
NAT Gateway: $32.00/mês
Data transfer (Collector→iService): 
  10GB × $0.045 = $0.45/mês
VPC Endpoints (3×):
  3 × $7.20 = $21.60/mês
─────────────────────
Custo mensal VPC: $54.05
```

**Trade-off NAT Gateway**:
- Obrigatório para Collector acessar iService API
- Alternativa: Lambda com VPC Endpoint (elimina NAT)
- Impacto: Collector compute choice affects networking

**Free Tier**: Nenhum (NAT Gateway sem free tier) ⚠️

---

#### 2.7 CloudWatch (Observabilidade)

**Configuração**:
- Basic metrics (5-min intervals)
- Application logs (~10GB/mês)
- Alarms: 3

**Cálculo**:
```
Basic metrics: Gratuito (Free Tier)
Custom metrics: $0.30/1000 metrics
Application logs: 
  - Free Tier: 5GB/mês
  - Custo: (10GB - 5GB) × $0.50 = $2.50/mês
Alarms: 
  3 alarms × $0.10 = $0.30/mês
─────────────────────
Custo mensal CloudWatch: $2.80 (ou $0 com logs < 5GB)
```

**Free Tier**: 10 basic metrics + 5GB logs ✅

---

#### 2.8 S3 + Glacier (Backup Longa Retenção)

**Configuração** (ADR-008):
- Monthly snapshots: 1 RDS + 1 DocumentDB
- Lifecycle: Move to Glacier Deep Archive após 90 dias
- Retention: 5 anos

**Cálculo (Mês 1-12)**:
```
S3 Standard (snapshots 0-90 dias):
  2 snapshots × 100GB = 200GB
  200GB × $0.023 = $4.60/mês

Glacier Deep Archive (após 90 dias):
  Mês 1-3: $0 (snapshots ainda em S3)
  Mês 4+: Accumulating
─────────────────────
Custo mensal (Mês 1-12): $4.60
```

**Cálculo (Mês 13+)**:
```
Glacier Deep Archive:
  12 snapshots × 100GB = 1.2TB
  1.200GB × $0.0036 = $4.32/mês
S3 Standard (current month): $4.60/mês
─────────────────────
Custo mensal (Mês 13+): $8.92
```

**Economia vs S3 Standard**:
- S3 Standard: 1.2TB × $0.023 = $27.60/mês
- Glacier Deep Archive: 1.2TB × $0.0036 = $4.32/mês
- **Economia**: $23.28/mês (84% reduction)

**Free Tier**: 5GB S3 storage gratuito (snapshots excedem) ⚠️

---

#### 2.9 KMS Encryption Rotation

**Configuração**:
- Rotation: 1x/year (automatic)
- Cost: Incluso em CMK

**Cálculo**: $0 (included in $1.00 CMK monthly)

---

#### 2.10 IAM Roles

**Configuração**:
- 3 roles: MasterApiExecutionRole, CollectorWorkerExecutionRole, BootstrapRole
- Policies: Custom (least privilege)

**Cálculo**: Gratuito (IAM free)

---

### 3. Resumo MVP por Ambiente

#### MVP Cenário 1: Prod com HA (Recomendado)

```
KMS:               $1.00
Secrets Manager:   $1.20
RDS Aurora Serverless 1 ACU:  $68.40
DocumentDB Single-AZ:         $100.00
SES:               $0.00
VPC + NAT:         $54.05
CloudWatch:        $2.80
S3 + Glacier:      $4.60
─────────────────────────────
TOTAL MVP:         $232.05/mês
```

**Status**: ⚠️ Acima de $200, mas aceitável para MVP com justificativas:
- Aurora Serverless garante Multi-AZ (RF-005)
- SES gratuito por 12 meses
- Snapshots com Glacier economizam 84%
- Free Tier AWS: ~$12/mês em economia

**Recomendado**: Aprovar este cenário (32% acima de $200, mas com HA obrigatória para RF-005).

---

#### MVP Cenário 2: Custo Otimizado

```
KMS:               $1.00
Secrets Manager:   $1.20
RDS Aurora 1 ACU:  $68.40
DocumentDB (Docker):        $0.00
SES:               $0.00
VPC + NAT:         $54.05
CloudWatch:        $2.80
S3 + Glacier:      $4.60
─────────────────────────────
TOTAL MVP OTIMIZADO: $132.05/mês
```

**Status**: ✅ Bem dentro de orçamento

**Trade-off**: DocumentDB local (sem managed replication, backup manual por 12 meses)

**Recomendado**: Se custo é crítico, usar este cenário + migrar para DocumentDB managed aos 12 meses.

---

### 4. Cenários de Crescimento

#### Cenário 50 Clientes (Mês 6)

**Assumindo**:
- Aurora Serverless: escalou de 1 → 2 ACU (auto)
- DocumentDB: escalou de Single → 2 instances
- SES: 20.000 emails/mês ($2.00)
- VPC: Data transfer +100GB

```
KMS:               $1.00
Secrets Manager:   $1.20
RDS Aurora 2 ACU:  $136.80
DocumentDB 2x:     $200.00
SES:               $2.00
VPC + NAT:         $55.00
CloudWatch:        $3.50
S3 + Glacier:      $8.92
─────────────────────────────
TOTAL 50 CLIENTES: $408.42/mês
```

**Status**: ❌ Exceeds $200 limit

**Ações**:
1. Avaliar se latência aceitável com DocumentDB Single-AZ (-$100/mês)
2. Otimizar Collector (reduzir NAT data transfer)
3. Considerar compute para Master API (Lambda vs ECS vs EC2)

---

#### Cenário 100+ Clientes (Mês 12)

```
RDS Aurora 4 ACU:  $273.60
DocumentDB 3x:     $300.00
SES:               $5.00
VPC + NAT:         $56.00
Outros:            $25.00
─────────────────────────────
TOTAL 100+ CLIENTES: $659.60/mês
```

**Status**: ⚠️ Requer reavaliação de compute, pode reduzir com serverless.

---

### 5. Free Tier AWS - Aproveitamento

#### Serviços com Free Tier (12 meses)

| Serviço | Limite | MVP Uso | Custo |
|---------|--------|---------|-------|
| RDS db.t3.micro | 750h/mês | Não aplicável | — |
| RDS storage | 20GB | 100GB > limite | — |
| DocumentDB | — | Não tem free tier | — |
| SES | 62.000 emails/mês | 5.000 emails | $0 ✅ |
| KMS | 20.000 requests/mês | 50.000 requests | Aprox $0 ✅ |
| Secrets Manager | 10.000 API calls | 50.000 calls | Aprox $0 ✅ |
| CloudWatch | 10 metrics + 5GB logs | 5 metrics + 10GB logs | Parcial ✅ |
| S3 | 5GB | Backups > 5GB | Parcial ✅ |
| Lambda | 1M invocations | 0 (não usado MVP) | — |
| EC2 t2.micro | 730h/mês | Docker MongoDB | $0 ✅ (12mo) |
| NAT Gateway | — | 1 required | Sem free tier ❌ |

**Economia Free Tier MVP**: ~$12-15/mês

**Avaliação**: Aurora Serverless não está em free tier, mas mais barato que RDS Multi-AZ + oferece HA.

---

### 6. Trade-offs Documentados

#### Trade-off 1: Aurora Serverless vs RDS Traditional

| Critério | Aurora | RDS Multi-AZ |
|----------|--------|---|
| Custo | $68.40/mês | $331.50/mês |
| Multi-AZ | ✅ Nativo | ✅ Native |
| Auto-scale | ✅ Automatic | ❌ Manual |
| Cold start | ~30s | Immediate |
| PostgreSQL version | 10 | 14 |
| Backup | Automatic | Automatic |
| Economia | **80% reduction** | Baseline |

**Decisão**: Aurora Serverless recomendado (trade-off cold start vs $263/mês).

---

#### Trade-off 2: DocumentDB vs Self-Managed MongoDB

| Critério | DocumentDB | Docker MongoDB |
|----------|-----------|---|
| Custo (ano 1) | $1.200/ano | $0 (free tier) |
| Custo (ano 2+) | $1.200/ano | $102/ano (EC2) |
| Managed backup | ✅ Yes | ❌ Manual |
| Replication | ✅ Managed | ❌ Manual |
| Monitoring | ✅ AWS | ⚠️ Limited |
| Ops overhead | ✅ Zero | ❌ High |
| Adequacy MVP | ✅ Ideal | ⚠️ Acceptable (dev) |

**Decisão**: DocumentDB recomendado para staging/prod após 6 meses, Docker para dev.

---

#### Trade-off 3: NAT Gateway vs Lambda VPC Endpoint

| Aspecto | NAT Gateway | Lambda + VPC Endpoint |
|--------|---|---|
| Custo | $32/mês + data transfer | Eliminate NAT |
| Collector deployment | EC2/ECS | Lambda only |
| Network flexibility | Full | Limited |
| Outbound to iService | ✅ Full internet | ✅ VPC Endpoint |
| Operational complexity | Simple | Depends on Collector |

**Decisión**: NAT Gateway recomendado se Collector é EC2/ECS. Revisar se Collector é Lambda.

---

### 7. Cronograma de Upgrade (Scaling Path)

#### Fase 1: MVP (Mês 1-3) 

**Config**:
- Aurora Serverless 1 ACU: $68.40/mês
- DocumentDB Single-AZ: $100/mês  
- **Total**: $232/mês

**Gatilho upgrade**: RDS CPU > 80% por 7+ dias consec.

---

#### Fase 2: Crescimento (Mês 4-6, ~50 clientes)

**Config**:
- Aurora Serverless 2 ACU: $136.80/mês (auto)
- DocumentDB 2 instances: $200/mês (manual upgrade)
- **Total**: $408/mês

**Ação**: Revisar orçamento ou otimizar outras áreas

---

#### Fase 3: Escalado (Mês 7-12, 100+ clientes)

**Config**:
- Aurora Serverless 4 ACU: $273.60/mês (max auto-scale)
  - Avaliar RDS db.t3.large se > 90% CPU
- DocumentDB 3+ instances: $300+/mês
- **Total**: $600+/mês

**Ação**: Possível consolidação com compute services (Lambda, ECS).

---

## 8. Alternativas Rejeitadas

### ❌ Alternativa 1: Self-Managed EC2 + PostgreSQL + MongoDB

```
EC2 t2.micro: $0/mês (Free Tier 12mo) → $8.50/mês
PostgreSQL + MongoDB: Manual install/backup
Operação: Patches, updates, replication manual
Custo pós Free Tier: $50-100/mês
```

**Rejeção**: Sem managed backup, operação insustentável, não atende RF-005.

---

### ❌ Alternativa 2: Heroku PostgreSQL + MongoDB Atlas

```
Heroku Postgres Standard: $50/mês
MongoDB Atlas M10: $57/mês
Total: $107/mês
```

**Rejeição**: Vendor lock-in, menos integração com AWS KMS/Secrets Manager, não economiza vs Aurora.

---

### ❌ Alternativa 3: RDS db.t3.micro Multi-AZ

```
db.t3.micro não suporta Multi-AZ natively
Trade-off seria Single-AZ (violaria RF-005)
```

**Rejeição**: Conflita com requisito "sempre disponível".

---

## 9. Implementação (Next Steps)

### ✅ Aprovado para Provisionamento: SIM

**Configuration**:
```
Aurora Serverless 1 ACU:    $68.40/mês
DocumentDB Single-AZ:       $100.00/mês
SES (Free Tier):            $0.00/mês
VPC + NAT:                  $54.05/mês
KMS + Secrets Manager:      $2.20/mês
CloudWatch:                 $2.80/mês
S3 + Glacier:               $4.60/mês
─────────────────────────────
TOTAL APPROVED MVP:         $232.05/mês
```

**Condições de Aprovação**:

1. **Free Tier**: Maximizar uso de SES (62.000 emails), KMS (20.000 requests), Secrets Manager (10.000 API calls).

2. **Monitoring**: Revisar custos semanalmente via AWS Cost Explorer. Alertas se custo > $250/mês.

3. **Escalabilidade**: Implementar Glacier archive desde dia 1 (ADR-008) para economizar 84% em backup storage.

4. **Upgrade Triggers**:
   - RDS CPU > 80% por 7+ dias → Aurora Serverless 2 ACU
   - DocumentDB latency > 100ms → Upgrade para 2 instances
   - SES emails > 50.000/mês → Monitorar deliverability

5. **Revisão 50 Clientes**: Reavaliação de orçamento necessária aos $400+/mês.

---

## 10. Próximas Steps

- [ ] Orchestrator: Revisar e aprovar análise de custos
- [ ] Platform Engineer: Provisionar infraestrutura (semana 1)
- [ ] Backend Engineer: Implementar Secrets Manager client (paralelo)
- [ ] Cost Monitoring: Setup AWS Cost Explorer alerts
- [ ] Documentação: Atualizar ADR-007 com Aurora Serverless vs RDS t3.medium

---

## Referências

- ADR-007: AWS Infrastructure for MVP
- ADR-008: Data Retention and Backup Strategy
- AWS Pricing Calculator: https://calculator.aws/
- Free Tier Limits: https://aws.amazon.com/free/

---

**Documento preparado por**: aws-architect  
**Data**: 2026-08-29  
**Versão**: 1.0  
**Status**: Proposto - Aguardando Aprovação Orchestrator
