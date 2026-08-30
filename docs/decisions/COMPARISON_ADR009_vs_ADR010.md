# Comparação: ADR-009 vs ADR-010 - Free Tier Optimization

**Data**: 2026-08-29, 15:40 UTC-3  
**Status**: Pronto para decisão do Orchestrator  

---

## 🎯 Comparação Executiva

### Cenário 1: Original Proposal (ADR-009)

```
┌─────────────────────────────────────────────────┐
│  CUSTO ORIGINAL - MVP                           │
├─────────────────────────────────────────────────┤
│  Aurora Serverless 1 ACU     $68.40             │
│  DocumentDB Single-AZ        $100.00            │
│  VPC + NAT Gateway           $54.05             │
│  KMS + Secrets Manager       $2.20              │
│  SES                         $0.00              │
│  CloudWatch                  $2.80              │
│  S3 + Glacier                $4.60              │
├─────────────────────────────────────────────────┤
│  TOTAL/MÊS:                 $232.05             │
├─────────────────────────────────────────────────┤
│  Target:                    $200.00             │
│  Desvio:                    +$32.05 (+16%)      │
└─────────────────────────────────────────────────┘

Vantagens:
✅ Multi-AZ HA (RF-005 atendido)
✅ Managed services (low ops overhead)
✅ Auto-scaling
✅ Profissional

Desvantagens:
❌ Acima do orçamento
❌ "Overkill" para MVP
```

---

### Cenário 2: Free Tier Optimized (ADR-010) 🎉

```
┌─────────────────────────────────────────────────┐
│  CUSTO OTIMIZADO - MVP (Free Tier Máximo)      │
├─────────────────────────────────────────────────┤
│  RDS db.t3.micro (Free)      $0.00              │
│  EC2 t2.micro MongoDB (Free) $0.00              │
│  EC2 t3.micro Master API (Free) $0.00           │
│  KMS + Secrets Manager       $2.20              │
│  SES                         $0.00              │
│  RDS Proxy (access)          $10.95             │
│  Backup (manual)             $0.50              │
├─────────────────────────────────────────────────┤
│  TOTAL/MÊS (Mês 1-12):       $13.65  ✅✅       │
│  TOTAL/MÊS (Mês 13+):        $39.15  ✅         │
├─────────────────────────────────────────────────┤
│  vs Target ($200):           -$186.35 (-93%)    │
│  vs Original ($232):         -$218.40 (-94%)    │
└─────────────────────────────────────────────────┘

Vantagens:
✅ Máximo Free Tier (94% economia!)
✅ Dentro orçamento (13.65/mês << 200/mês)
✅ Sustentável pós Free Tier ($39.15/mês)
✅ Dev-only simplificado

Desvantagens:
❌ Single-AZ RDS (RF-005 compromised)
❌ Manual backup (ops overhead)
❌ Manual monitoring (sem CloudWatch)
❌ Sem autoscaling
❌ Mais complexo operacionalmente
```

---

## 📊 Comparação Técnica Detalhada

### RDS PostgreSQL

| Aspecto | ADR-009 | ADR-010 |
|---------|---------|---------|
| **Tier** | Aurora Serverless 1 ACU | db.t3.micro |
| **Custo** | $68.40/mês | $0/mês (Free) |
| **HA** | ✅ Multi-AZ | ❌ Single-AZ |
| **RTO** | ~30s (failover) | >30min (manual) |
| **Storage** | 100-500GB auto-scale | 20GB limit |
| **Cold Start** | ~30s | Immediate |
| **Escalabilidade** | Automática | Manual |
| **PostgreSQL Version** | 10 (Limited) | 14 (Full) |
| **RF-005 Compliance** | ✅ Yes | ❌ No (risk) |
| **MVP Adequacy** | ✅ Ideal for 10-50 clientes | ✅ Adequate for 10-20 |

### DocumentDB / MongoDB

| Aspecto | ADR-009 | ADR-010 |
|---------|---------|---------|
| **Service** | DocumentDB managed | Local MongoDB |
| **Custo** | $100/mês | $0/mês (Free) |
| **Backup** | Automated | Manual |
| **Replication** | Managed | None |
| **Monitoring** | CloudWatch | Manual logs |
| **Upgrade Path** | Already managed | Manual migration |
| **Ops Overhead** | Low | High |
| **MVP Adequacy** | ✅ Ideal | ✅ Adequate |

### Compute (Master API + Collector)

| Aspecto | ADR-009 | ADR-010 |
|---------|---------|---------|
| **Status** | Pending decision | EC2 t2/t3 (Free) |
| **Master API** | Lambda or ECS? | EC2 t3.micro ($0) |
| **Collector** | Lambda or ECS? | EC2 t2.micro ($0) |
| **Autoscaling** | Depends choice | Manual |
| **Cold Start** | Possible | None (always-on) |
| **Ops Overhead** | Minimal | Moderate |
| **MVP Adequacy** | ✅ Yes | ✅ Yes |

### Networking

| Aspecto | ADR-009 | ADR-010 |
|---------|---------|---------|
| **NAT Gateway** | $32/mês | Removed (IGW free) |
| **VPC Endpoints** | $21.60/mês | Not needed |
| **Public Subnets** | Optional | Required (EC2 public) |
| **Security** | Private (better) | Public (acceptable) |
| **iService Access** | Via NAT | Direct IGW |
| **Ops Complexity** | Simple | Simple |

### Observabilidade

| Aspecto | ADR-009 | ADR-010 |
|---------|---------|---------|
| **CloudWatch Logs** | $2.80/mês | $0 (removed) |
| **Alarms** | 3 alarms | Manual checks |
| **Metrics** | Centralized | Local logs |
| **Ops Overhead** | Low | Medium |

---

## 🔄 Timeline & Scaling

### ADR-009 Approach

```
Mês 1-3: $232/mês
  └─ Fixed architecture
  └─ Costs stable

Mês 13+: $232/mês (no change, already scaled)
  └─ Unnecessary overpayment for MVP

Escalação: Mês 6 if 50+ customers
  └─ Adicionar read replicas
  └─ Increase compute
```

### ADR-010 Approach ✅ RECOMENDADO

```
Mês 1-12: $13.65/mês (Free Tier máximo)
  └─ Manual ops acceptable
  └─ Perfect for MVP

Mês 13: $39.15/mês (still very cheap!)
  └─ After Free Tier expires
  └─ Still 6x cheaper than ADR-009

Escalação Gatilho: Mês 12 (~20 customers)
  └─ Upgrade RDS to db.t3.small ($45/mês)
  └─ Migrate MongoDB to DocumentDB ($100/mês)
  └─ Total ~$200/mês (similar to original ADR-009 at growth)

Production (50+ customers): Mês 18+
  └─ Aurora Serverless ($68/mês)
  └─ DocumentDB ($100/mês)
  └─ Multi-AZ + HA ($90+ extra)
  └─ Total ~$350+/mês
```

---

## 🎯 Recomendação

### ✅ RECOMENDADO: ADR-010 (Free Tier Optimization)

**Justificativas**:

1. **Custo**: $13.65/mês (94% economía) vs $232/mês
   - Permite pivots de produto sem preocupação orçamentária
   - Validar MVP sem gastar fortune

2. **Sustentabilidade**: $39.15/mês após Free Tier (Mês 13)
   - Ainda muito barato
   - Tempo suficiente para validar model before major investment

3. **Tempo ao Mercado**: Mais rápido
   - EC2 provisioning = horas (vs Aurora/DocumentDB = 15min)
   - Simples infrastructure = menos debugging

4. **Flexibilidade**: Easier pivots
   - Se precisar mudar tech stack, já documentado
   - Staging fácil de adicionar depois

5. **Trade-offs Aceitáveis para MVP**:
   - Single-AZ: OK (manual backup como mitigação)
   - Manual ops: OK (3 devs podem gerenciar EC2s)
   - Sem autoscaling: OK (10-20 clientes não precisa)

### ⚠️ Trade-off Aceitação

**ADR-010 pede aceitação explícita de**:

- ❌ RF-005 "sempre disponível" = SINGLE-AZ (RTO > 30min)
  - Mitigação: Daily manual backups, monitoring
  - Escalada: Multi-AZ aos 13 meses

- ❌ Manual backup (não automatic)
  - Mitigação: Cron job (daily snapshots to S3)
  - Escalada: DocumentDB managed aos 13 meses

- ❌ Manual ops (sem managed services)
  - Mitigação: Infrastructure as Code (terraform/CDK for EC2)
  - Escalada: Gradual migration to managed services

- ❌ Sem autoscaling (horizontal scale manual)
  - Mitigação: EC2 Auto Scaling Group (configure but not enable yet)
  - Escalada: Enable autoscaling when 50+ customers

---

## 🚨 Se Escolher ADR-009 (Managed Services)

**Aceitação necessária**:
- Orçamento: $232/mês vs $13.65/mês (17x mais caro)
- Waste: Overpaying para features não usadas no MVP
- Timeline: Possível atraso de QA (complexidade extra)

**Vantagem única**: RF-005 HA nativo (Multi-AZ).

---

## 📋 Decision Matrix

| Critério | ADR-009 | ADR-010 | Winner |
|----------|---------|---------|--------|
| **Custo MVP** | $232 | $13.65 | ✅ ADR-010 |
| **Custo Pós Free Tier** | $232 | $39.15 | ✅ ADR-010 |
| **HA/RF-005** | ✅ Multi-AZ | ❌ Single-AZ | ✅ ADR-009 |
| **Ops Complexity** | Low | Medium | ✅ ADR-009 |
| **Time to Provision** | 15-20min | 5-10min | ✅ ADR-010 |
| **Flexibility** | Medium | High | ✅ ADR-010 |
| **Sustainability** | Forever $232 | $39.15 after 12mo | ✅ ADR-010 |
| **Future Upgrade Path** | Already scaled | Clear path to $200 | ✅ ADR-010 |

---

## 📞 Orchestrator Decision Required

### Option 1: Approve ADR-010 (Rekomendado)
```
✅ Cost: $13.65/mês
✅ Trade RF-005 multi-AZ for cost
✅ Accept manual ops until Mês 13
→ Proceed with Free Tier optimization
```

### Option 2: Approve ADR-009 (Conservative)
```
✅ Cost: $232/mês (within budget negotiation)
✅ Maintain RF-005 Multi-AZ HA
✅ Minimal ops overhead
→ Proceed with managed services
```

### Option 3: Hybrid (Compromise)
```
✅ ADR-010 BUT upgrade RDS to db.t3.small ($45/mês)
✅ Keep Single-AZ MongoDB (local)
✅ Total: ~$58/mês
✅ Better RDS capacity, less ops for DB
```

---

## Recomendação Final

### 🎯 **GO WITH ADR-010** (Free Tier Optimization)

**Rationale**:
1. MVP needs lean operations, not enterprise HA
2. $13.65/mês enables rapid iteration
3. Clear upgrade path when product-market fit validated
4. RF-005 trade-off acceptable with manual backup mitigation
5. More sustainable long-term (scalable without cost explosion)

**Approval Conditions**:
- [ ] Orchest rator approves Single-AZ risk
- [ ] Orchestrator approves manual ops overhead
- [ ] Backend Engineer confirms EC2 deployment viable
- [ ] Platform Engineer confirms backup automation ready

---

**Documento preparado por**: aws-architect  
**Data**: 2026-08-29, 15:40 UTC-3  
**Status**: Aguardando decisão Orchestrator (ADR-009 vs ADR-010)
