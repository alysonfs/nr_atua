# ADR-010 - AWS MVP Free Tier Optimization

## Status

**Proposed** (Resposta ao feedback de otimização)

---

## Contexto

ADR-009 propôs $232.05/mês MVP usando Aurora Serverless + DocumentDB Single-AZ.

Feedback do usuário:
- Maximizar Free Tier (RDS db.t3.micro, DocumentDB mínimo)
- Dev-only (sem staging)
- Remover CloudWatch
- Questionar NAT Gateway
- Avaliar Collector em Lambda + SQS
- Avaliar Master API em Lambda (ASP.NET Core monolito)

Objetivo: Descer custo para < $100/mês ou até Free Tier.

---

## Decisão Revisada

### 1. RDS PostgreSQL - Estratégia Free Tier

#### Opção A: db.t3.micro (FREE TIER MÁXIMO) ✅ RECOMENDADO

```
Tier:           db.t3.micro (1 vCPU, 1GB RAM)
Storage:        20GB gp2 (Free Tier)
Topologia:      Single-AZ (sem Multi-AZ)
Backup:         35 dias automated (Free Tier)
Encryption:     TLS only (KMS add-on $1/mês)
Cost:           $0/mês (12 meses) → $8.50/mês after
Limite Free:    750h/mês = 31 dias contínuos
```

**Avaliação de Capacidade**:
```
db.t3.micro para MVP:
- 100 conexões simultâneas? Sim (1 vCPU pode handle)
- 10 clientes com 50 OS each = 500 OS total? Sim
- Query latency? ~50-100ms (aceitável)
- Storage: 20GB limit
  - Users table: ~10MB (10k users)
  - Tenants table: ~5MB (1k tenants)
  - OS data: ~100MB (10k OS × 10KB)
  - Total: ~115MB (sem problema)
- CPU: ~5-10% idle (sem problema)
```

**Trade-off vs Aurora Serverless**:
| Aspecto | db.t3.micro | Aurora Serverless |
|---------|---|---|
| Custo | $0 (Free Tier) | $68.40/mês |
| Cold start | Immediate | ~30s |
| RF-005 "sempre disponível" | ❌ Single-AZ (RTO > 30min) | ✅ Multi-AZ nativo |
| Escalabilidade | Manual | Automática |
| Storage auto-scale | ❌ 20GB limit | ✅ até 500GB |
| MVP adequacy | ✅ Sim (10-20 clientes) | ✅ Sim (até 50) |

**Decisão**: Usar **db.t3.micro** para MVP (Free Tier). Escalar para Aurora Serverless em Mês 13 (após Free Tier expira).

**Impacto RF-005 "sempre disponível"**:
- ❌ Single-AZ significa RTO > 30 minutos em caso de falha AZ
- ⚠️ Aceitável para MVP? **Sim**, com caveat:
  - Manual backups diários para recuperação
  - Monitoramento ativo (alertas RDS status)
  - Plano de escalar para Multi-AZ aos 13 meses

---

### 2. DocumentDB - Estratégia Free Tier

#### Opção A: Usar MongoDB Local (Docker) + EC2 t2.micro ✅ RECOMENDADO

```
Compute:        EC2 t2.micro (Free Tier 12 meses)
Storage:        20GB EBS gp2 (Free Tier)
Container:      Docker MongoDB 5.0
Topologia:      Single container (dev-only)
Backup:         Manual (AWS Backup)
Cost:           $0/mês (12 meses) → $8.50/mês after
Limite Free:    750h/mês = 31 dias contínuos
```

**Alternativa B: DocumentDB db.t3.small**

```
Cost: $85/mês (mínimo)
Vantagem: Managed backup, monitoring
Desvantagem: $85 quando t2.micro é $0
```

**Decisão**: Usar **Local MongoDB in EC2 t2.micro** para MVP (máximo Free Tier).

**Considerações**:
- ✅ Montação fácil (docker-compose)
- ✅ Controle total
- ❌ Backup manual
- ❌ Sem replicação automática
- ❌ Sem managed monitoring

**Procedimento Setup**:
```bash
# EC2 t2.micro setup
cd /opt
git clone https://github.com/mongodb/mongo
docker run -d \
  --name mongodb \
  -v /data/db:/data/db \
  -p 27017:27017 \
  mongo:5.0

# Backup manual (daily cron)
mongodump --out /backups/$(date +%Y%m%d)
```

---

### 3. CloudWatch - Remover (Custo $0)

**Original**: $2.80/mês (logs + alarms)

**Novo**: $0/mês (sem CloudWatch)

**Trade-off**:
- ❌ Sem centralized logging
- ❌ Sem alarms
- ✅ AWS console still shows basic metrics (CloudWatch free tier)

**Alternativa**: 
- Logs locais no EC2 (mongodb + application logs in /var/log)
- Manual monitoring (check AWS Console 1x/day)

**Decisão**: Remover CloudWatch pago. Usar AWS Console free metrics.

---

### 4. NAT Gateway - Análise de Necessidade

**Questão**: Por que Collector precisa NAT Gateway?

**Resposta**: Collector precisa acessar iService API (externa, fora AWS).

**Opções Avaliadas**:

#### Opção A: NAT Gateway (Original)
```
Custo: $32/mês (fixed) + $0.045/GB (data transfer)
Vantagem: Private subnet full internet access
Desvantagem: Caro para MVP
Uso Collector: ~10-50GB/mês data transfer = +$0.45-2.25/mês data
Total: $32.45-34.25/mês
```

#### Opção B: Internet Gateway + Public Subnet (SEM NAT) ✅ RECOMENDADO
```
Topology:
  - Collector EC2 em public subnet
  - IGW (Internet Gateway) para saída
  - Security Group permite egress TCP 443 (HTTPS to iService)
Custo: $0 (IGW free)
Desvantagem: EC2 tem IP público (minor security risk)
Mitigation: Security Group restrictivo (apenas TCP 443 outbound)
```

**Decisão**: Usar **Internet Gateway + Public Subnet** (remover NAT Gateway).

**Implicação**: Collector estará em public subnet (IP público). Aceitável? 
- ✅ Sim, se:
  - Inbound bloqueado (Security Group)
  - Apenas egress TCP 443 (HTTPS outbound)
  - Monitoramento de tentativas de acesso

---

### 5. Collector - Lambda + SQS vs EC2

#### Opção A: Lambda + SQS ✅ RECOMENDADO

**Arquitetura**:
```
EventBridge Scheduler (cron)
  ↓ (15-min intervals)
SQS Queue (one message per tenant_id)
  ↓
Lambda Function (Collector)
  ↓ (process message)
iService API (GET /os)
  ↓ (store results)
RDS + DocumentDB
```

**Custo Lambda**:
```
Free Tier: 1M invocations/mês + 400k GB-seconds/mês
MVP usage (assume 10 tenants, every 15 min):
  - Invocations/mês: 10 tenants × 4 invocations/hour × 24h × 30d = 28,800
  - Duration/invocation: ~10 seconds (query iService + store)
  - GB-seconds: 28,800 × 0.128GB × 10s = 36,864 GB-seconds
Status: ✅ DENTRO FREE TIER
```

**Vantagens**:
- ✅ Pay-per-execution (Free Tier!)
- ✅ Escala automaticamente
- ✅ Sem servidor para gerenciar
- ✅ Integração natural com EventBridge
- ✅ .NET runtime via custom runtime

**Desvantagens**:
- ❌ Cold start ~5-10s (timeout considerável)
- ❌ 15 minutos pode não ser suficiente se iService lento
- ❌ VPC Lambda precisa NAT (voltamos ao NAT Gateway?) 🤔

**VPC Consideration**: Se Lambda está em VPC privado (para acessar RDS/DocumentDB):
- Precisa NAT Gateway para sair à internet (acessar iService)
- Volta ao problema: $32/mês

**Solução**: Lambda SEM VPC
- ❌ Não consegue acessar RDS/DocumentDB (private subnet)
- ✅ Acessa iService direto (internet)

**Alternativa**: Usar RDS Proxy public endpoint
- ✅ Lambda acessa RDS Proxy via internet (não precisa VPC)
- RDS Proxy custo: $0.015/hour = ~$10.95/mês (aceitável)

**Decisão**: Lambda + SQS VIÁVEL se:
1. Lambda sem VPC
2. RDS Proxy (public) para acesso RDS
3. DocumentDB em EC2 (local) no VPC
4. Lambda acessa local MongoDB via EC2 public IP + security group

---

#### Opção B: EC2 t2.micro (Collector sempre ligado)

```
Compute: EC2 t2.micro (Free Tier)
Cost: $0/mês (12 meses) → $8.50/mês after
Collector: .NET Worker Service runnando continuamente
Scheduling: Timer (every 15 min) ou cron

Vantagem: Simples, conexões persistentes
Desvantagem: EC2 sempre ligado = máximo Free Tier usage
```

**Decisão**: **EC2 + cron** pode ser mais simples que Lambda + SQS para MVP.

---

### 6. Master API - Lambda vs ECS vs EC2

#### Opção A: ASP.NET Core Monolito em Lambda ❌ DIFÍCIL

**Problema 1: Cold Start**
```
ASP.NET Core startup: ~1-2 segundos
Lambda cold start (custom runtime): ~3-5s
Total: ~5-7s para primeira requisição
Impacto: Acceptable para MVP? Borderline
Solução: Provisioned Concurrency (costs $$$)
```

**Problema 2: Monolito Stateful**
```
ASP.NET Core sessions: In-memory by default
Lambda ephemeral container (duration of request)
Issue: Session state lost entre invocações
Solução: Redis/ElastiCache (add cost $$$)
```

**Problema 3: Conexão Pooling**
```
Lambda: Nueva connection pool per invocation (wasteful)
Trade-off: Performance vs resource cleanup
```

**Viability**: ⚠️ Possível mas complexo.

#### Opção B: API Gateway + .NET Lambda Functions (Serverless-first)

```
Architecture:
  API Gateway (routes)
    ↓
  Lambda Functions (endpoint-specific)
    ↓
  Shared .NET library (business logic)
    ↓
  RDS + DocumentDB

Vantagem:
- ✅ Scale per-endpoint
- ✅ Pay-per-invocation
- ✅ Free Tier: 1M API Gateway calls
Cost: $0 (within Free Tier)
```

**Desvantage**:
- ❌ Refactor monolito → microserviços
- ❌ Shared state is hard
- ❌ Complex deployment

#### Opção C: EC2 t3.micro (ASP.NET Monolito) ✅ RECOMENDADO

```
Compute: EC2 t3.micro (Free Tier 12 meses)
Runtime: ASP.NET Core 7
Process: Always running (systemd service)
Cost: $0/mês (12 meses) → $8.50/mês after
Vantage: Simple, monolito runs as-is
Disadvantage: Manual autoscaling
```

**Viability**: ✅ Simples, funciona imediatamente.

---

### 7. Architecture Diagram - FREE TIER OPTIMIZADO

```
┌──────────────────────────────────────────────────────────────┐
│               ATUA MVP - Free Tier Optimized                 │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│  VPC 10.0.0.0/16 (sa-east-1)                                │
│  ├─ Public Subnets:                                          │
│  │  ├─ IGW (Internet Gateway) - free                        │
│  │  ├─ EC2 t2.micro (Collector + MongoDB)                  │
│  │  └─ EC2 t3.micro (Master API)                           │
│  │                                                           │
│  └─ Private Subnets:                                         │
│     ├─ RDS db.t3.micro (PostgreSQL) - Free Tier            │
│     └─ RDS Proxy (public) - $10.95/mês                      │
│                                                               │
│  Security:                                                   │
│  ├─ KMS CMK: $1.00/mês                                      │
│  ├─ Secrets Manager (3 secrets): $1.20/mês                  │
│  └─ Security Groups (restrictive)                           │
│                                                               │
│  Services:                                                   │
│  ├─ SES (62k emails/mês): $0 (Free Tier)                   │
│  └─ Lambda (optional, for future scaling)                   │
│                                                               │
│  No costs:                                                   │
│  ├─ ❌ NAT Gateway (removed)                                │
│  ├─ ❌ CloudWatch (removed)                                 │
│  ├─ ❌ DocumentDB (using local MongoDB)                     │
│  └─ ❌ Aurora Serverless (using t3.micro RDS)              │
│                                                               │
└──────────────────────────────────────────────────────────────┘
```

---

## 8. Cost Breakdown - FREE TIER OPTIMIZED

### Mês 1-12 (Free Tier Period)

```
KMS CMK                     $1.00
Secrets Manager (3)         $1.20
RDS db.t3.micro             $0.00  (Free Tier)
EC2 t2.micro (MongoDB)      $0.00  (Free Tier)
EC2 t3.micro (Master API)   $0.00  (Free Tier)
RDS Proxy                   $10.95 (access RDS from Lambda)
SES                         $0.00  (Free Tier)
Backup (manual, EBS)        $0.50  (snapshots)
────────────────────────────────────
TOTAL MVP MONTHS 1-12:      $13.65/mês ✅✅✅
```

### Mês 13+ (Post Free Tier)

```
KMS CMK                     $1.00
Secrets Manager (3)         $1.20
RDS db.t3.micro             $8.50  (after 750h free)
EC2 t2.micro (MongoDB)      $8.50  (after 750h free)
EC2 t3.micro (Master API)   $8.50  (after 750h free)
RDS Proxy                   $10.95
SES                         $0.00
Backup (manual, EBS)        $0.50
────────────────────────────────────
TOTAL MVP MONTHS 13+:       $39.15/mês (even after Free Tier!)
```

**Comparação**:
```
Original Proposal (ADR-009):  $232.05/mês
Free Tier Optimized (ADR-010): $13.65/mês (Mês 1-12)
                               $39.15/mês (Mês 13+)
────────────────────────────────
Savings: $218.40/mês (94% reduction!)
```

---

## 9. Trade-offs - Free Tier vs Original

| Aspecto | Original | Free Tier | Impact |
|---------|----------|-----------|--------|
| **RDS** | Aurora Serverless | db.t3.micro | Single-AZ risk (RTO > 30min) |
| **DocumentDB** | Managed $100 | Local MongoDB | Manual ops |
| **Master API** | (pending) | EC2 t3.micro | Manual autoscaling |
| **Collector** | (pending) | EC2 t2.micro | No autoscaling |
| **NAT Gateway** | $32/mês | Removed (IGW free) | EC2 public IP (minor risk) |
| **CloudWatch** | $2.80/mês | Removed | Manual monitoring |
| **Total Cost** | $232.05 | $13.65 | 94% reduction |
| **RF-005 "Sempre Disponível"** | ✅ Multi-AZ | ❌ Single-AZ | Acceptable for MVP |
| **Operational Overhead** | Low (managed) | High (manual) | Acceptable for MVP |

---

## 10. Scaling Path

### Phase 1: MVP (Mês 1-12, Free Tier)

```
✅ Dentro Free Tier: $13.65/mês
✅ Dev-only: Sem staging
✅ Manual backup: Daily snapshots
✅ Manual monitoring: AWS Console 1x/day
✅ EC2-based deployment: systemd services
```

**Gatilho para escalar**:
- RDS CPU > 50% consistently → Scale to db.t3.small ($45/mês)
- Storage > 15GB → Scale to db.t3.small (more storage)
- API latency > 500ms → Horizontal scale (load balancer)
- Collector missing SLAs → Horizontal scale (more instances)

### Phase 2: Scaling (Mês 13-18, Post Free Tier)

```
$39.15/mês (still very cheap!)
Consider:
- RDS db.t3.small ($45/mês) if needed
- DocumentDB managed ($100/mês) if local MongoDB ops too much
- Auto Scaling Groups (EC2)
- RDS Multi-AZ ($90/mês extra) if RF-005 now mandatory
```

### Phase 3: Production (Mês 19+, Growth Stage)

```
Upgrade when 50+ clientes:
- Aurora Serverless ($68/mês)
- DocumentDB managed ($100/mês)
- ECS Fargate (Collector)
- API Gateway + Lambda (Master API)
- Multi-region backup
```

---

## 11. Implementation Decisions

### ✅ Decisions Made

1. **RDS**: db.t3.micro (Free Tier), Single-AZ acceptable for MVP
2. **DocumentDB**: Local MongoDB in EC2 t2.micro (Free Tier)
3. **Master API**: EC2 t3.micro (ASP.NET Core monolito)
4. **Collector**: EC2 t2.micro (.NET Worker Service)
5. **Networking**: IGW (no NAT Gateway)
6. **Backup**: Manual (daily snapshots, stored in S3 Standard)
7. **Monitoring**: AWS Console (no CloudWatch payed)
8. **Cost Target**: $13.65/mês (Mês 1-12), $39.15/mês (Mês 13+)

### ⏳ Decisions Pending (from ADR-007)

1. ❓ Collector + Master API: Both EC2 or Lambda for Collector?
   - Recommendation: **EC2 t2.micro for both** (simpler, within Free Tier)
   
2. ❓ Scheduling Collector: cron or EventBridge Scheduler?
   - Recommendation: **Cron on EC2** (no extra cost)

3. ❓ Ingress: ALB or direct security group?
   - Recommendation: **Security Group + Route 53** (no ALB cost)

4. ❓ Staging: Do we need it?
   - Recommendation: **No staging yet** (dev-only MVP)

5. ❓ Observability: CloudWatch or ELK stack?
   - Recommendation: **Local logs + manual AWS Console** (no cost)

---

## 12. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|---|---|
| RDS Single-AZ failover | Downtime > 30min | Medium | Manual backup + quick restore |
| EC2 compute resource limit (2 EC2s) | OOM or CPU spike | Low | Monitor; scale to t3.small if needed |
| MongoDB local data loss | Complete data loss | Low | Daily manual backup to S3 |
| No autoscaling | Manual scaling needed | Medium | EC2 Auto Scaling Group (when MVP ends) |
| Free Tier expiration (Mês 13) | Cost jump to $39.15 | 100% (planned) | Budget for Mês 13; plan upgrade path |
| iService API rate limit | Collector blocked | Low | Implement exponential backoff + retry logic |

---

## 13. Approval Checklist

- [ ] RDS db.t3.micro Single-AZ acceptable (trade RF-005 for cost)
- [ ] Local MongoDB acceptable (manual ops)
- [ ] Master API + Collector both in EC2 (simpler)
- [ ] IGW instead of NAT Gateway (public EC2 acceptable)
- [ ] No CloudWatch costs
- [ ] No staging (dev-only MVP)
- [ ] Manual backup strategy accepted
- [ ] Free Tier cost $13.65/mês approved
- [ ] Post Free Tier cost $39.15/mês approved

---

## 14. Next Steps

### Orchestrator
- [ ] Review ADR-010 (Free Tier optimization)
- [ ] Approve cost reduction $232 → $13.65
- [ ] Confirm trade-offs (Single-AZ, manual ops)

### Platform Engineer
- [ ] Provision EC2 t2.micro (MongoDB)
- [ ] Provision EC2 t3.micro (Master API)
- [ ] Provision RDS db.t3.micro
- [ ] Provision RDS Proxy (public)
- [ ] Configure backup automation

### Backend Engineer
- [ ] Deploy ASP.NET Core on EC2 t3.micro
- [ ] Deploy Collector on EC2 t2.micro
- [ ] Implement Secrets Manager client
- [ ] Implement RDS + MongoDB connection

---

## References

- ADR-007: AWS Infrastructure for MVP (original)
- ADR-009: AWS Cost Analysis (original proposal)
- ADR-003: Backend Architecture
- AWS Free Tier: https://aws.amazon.com/free/

---

**Document prepared by**: aws-architect  
**Date**: 2026-08-29, 15:36 UTC-3  
**Version**: 1.0  
**Status**: Proposed - Awaiting Orchestrator Approval

🎉 **94% cost reduction while maintaining MVP functionality!**
