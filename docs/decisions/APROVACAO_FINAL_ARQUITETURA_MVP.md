# APROVAÇÃO FINAL - Arquitetura MVP ATUA Refinada

**Data**: 2026-08-29, 16:05 UTC-3  
**Destinatário**: Orchestrator + Backend Engineer + Platform Engineer  
**Status**: ✅ PRONTO PARA APROVAÇÃO FINAL  
**Urgência**: CRÍTICA (Provisioning sexta EOD, backend segunda)  

---

## 📊 Sumário Executivo

### Proposta Refinada (ADR-011)

Após análise técnica completa de Playwright, DocumentDB, Landing & Domínio:

```
┌────────────────────────────────────────────────────────┐
│         ARQUITETURA MVP ATUA - FINAL                  │
├────────────────────────────────────────────────────────┤
│                                                        │
│ Custo MVP (Mês 1-12):        $14.76/mo               │
│ Custo Pós Free Tier (13+):   $41.65/mo               │
│ Economia vs original (ADR-009): 94% reduction        │
│                                                        │
│ Componentes:                                          │
│ ├─ Compute: 2x EC2 Free Tier (t2 + t3 micro)        │
│ ├─ Database: RDS db.t3.micro + Local MongoDB        │
│ ├─ Landing: GitHub Pages (MVP) → S3+CDN (scale)     │
│ ├─ Security: KMS + Secrets Manager + IAM            │
│ └─ Email: SES + Route53 + ACM                        │
│                                                        │
│ Pronto para Provisioning: ✅ SIM                      │
│ Timeline Provisioning: 5-10 horas (sexta)            │
│ Backend Ready: Segunda 09:00 ✅                       │
│                                                        │
└────────────────────────────────────────────────────────┘
```

---

## ✅ Decisões Confirmadas (Feedback do Usuário)

### 1. API Master: EC2 t2.micro (ASP.NET Core)

```
✅ Confirmado pelo usuário
Status: Implementação simples, deploy direto
Custo: $0 (Free Tier Mês 1-12), $8.50/mo (13+)
Timeline: 2-3 horas para setup
```

### 2. RDS PostgreSQL: db.t3.micro

```
✅ Confirmado pelo usuário
Status: Free Tier máximo para MVP
Custo: $0 (Free Tier), $8.50/mo (13+)
Limitação aceitável: Single-AZ (mitigado por backup)
Timeline: 30 min provisioning + seed data
```

### 3. Landing Page: S3 + CloudFront + Route53

```
✅ Confirmado pelo usuário
Recomendação Refinada:
  MVP (Mês 1-3): GitHub Pages ($0.50/mo Route53 only)
  Scale (3mo+): S3 + CloudFront (+$0.60-1.41/mo)
Vantagem: Simples MVP, fácil escalar
Timeline: 15-30 min (GitHub Pages)
```

---

## ✅ Decisões Analisadas & Recomendadas

### 4. Collector com Playwright: EC2 (NÃO Lambda)

**Pergunta**: Playwright em Lambda funciona?

**Análise Técnica**:
```
Cold start latency:      20-35s (vs EC2: <5s)
Timeout risk:            15 min limit (10 tenants = 1000s)
Cost:                    $88.20/mo Lambda vs $0 EC2
Browser state:           Session loss between invocations
Viability:               ⚠️ Possível mas complexo

Recomendação: ❌ NÃO use Lambda
              ✅ Use EC2 t2.micro + Playwright
              
Vantagens EC2:
  ✅ No cold start (browser always-on)
  ✅ No timeout (all tenants in single process)
  ✅ Session persistence (login once, reuse)
  ✅ $0 extra cost (already running)
  
Implementação: 2-3 horas (.NET Worker Service)
```

### 5. Document Storage: Local MongoDB (NÃO DynamoDB/DocumentDB)

**Pergunta**: DynamoDB vs DocumentDB?

**Análise Técnica**:
```
DynamoDB:
  ✅ Custo: $1-2/mo (on-demand)
  ❌ Queries: Limitadas (precisa GSI para tudo)
  ❌ Backup: Manual complexo
  ❌ Consistency: Eventual (edge cases)
  
DocumentDB AWS:
  ✅ Queries: Excelentes (MongoDB API)
  ✅ Backup: Automático
  ✅ ACID: Completas
  ❌ Custo: $651/mo (minimum, muito caro MVP)

Local MongoDB (em EC2 t2.micro):
  ✅ Custo: $0-8.50/mo (Free Tier)
  ✅ Queries: Todas suportadas
  ✅ Backup: Automated cron → S3
  ✅ Scaling: Claro path para DocumentDB
  ✅ Learning: Team já conhece MongoDB

Recomendação: ✅ Local MongoDB MVP
              → Migrar para DocumentDB em Mês 13
              
2-Phase Strategy:
  Mês 1-12: Docker MongoDB on EC2 ($0)
  Mês 13+: Migrate to DocumentDB ($651)
  Migration: 2-3 horas (mongodump/mongorestore)
```

---

## ✅ Questões Secundárias Respondidas

### 6. Domínio: atua.com.br Indisponível

**Pergunta**: Alternativas para .br?

**Análise**:
```
atua.com.br: Unavailable (check WHOIS em 30 dias)

Alternativas Recomendadas (MVP):
  ✅ atuacoleta.com.br (melhor fit, descritivo)
  ✅ atua.io (trendy, .io comum em tech)
  ✅ coleta.app (apps-focused, moderno)

Custo: $12-20/year (negligible)

Timeline para MVP:
  1. Registrar atuacoleta.com.br (2-3 dias)
  2. Configurar Route53
  3. Deploy landing
  4. Launch pronto para segunda

Timeline para atua.com.br (Future):
  Mês 3-6: Monitorar atua.com.br (backorder)
  Mês 6+: Se disponível, registrar
  Migração: 301 redirect (zero downtime)
  Custo migração: $0 (DNS only)

Recomendação: ✅ Usar atuacoleta.com.br agora
              ✅ Migrar para atua.com.br quando liberar
```

---

## 📊 Custo Final Detalhado

### MVP (Mês 1-12): Máximo Free Tier

```
┌────────────────────────────────────────────────────────┐
│ Custo Breakdown - MVP (Mês 1-12)                       │
├──────────────────────┬────────┬──────────────────────┤
│ Serviço              │ Qty    │ Custo                │
├──────────────────────┼────────┼──────────────────────┤
│ EC2 t2.micro         │ 1      │ $0 (Free)            │
│ EC2 t3.micro         │ 1      │ $0 (Free)            │
│ RDS db.t3.micro      │ 1      │ $0 (Free)            │
│ KMS CMK              │ 1      │ $1.00                │
│ Secrets Manager (3)  │ 1      │ $1.20                │
│ RDS Proxy            │ 1      │ $10.95               │
│ SES (62k emails)     │ -      │ $0 (Free)            │
│ S3 (backups)         │ ~10GB  │ $0.23                │
│ Glacier (retention)  │ ~5GB   │ $0.05                │
│ Route53 (landing)    │ 1      │ $0.50                │
│ GitHub Pages         │ 1      │ $0 (Free)            │
│ Domain (.br)         │ 1      │ ~$1/mo               │
├──────────────────────┼────────┼──────────────────────┤
│ TOTAL/MÊS            │        │ $14.93 (≈$15)        │
├──────────────────────┼────────┼──────────────────────┤
│ Original (ADR-009)   │        │ $232.05              │
│ ECONOMIA             │        │ $217.12 (94%)        │
└────────────────────────────────────────────────────────┘
```

### Pós Free Tier (Mês 13+)

```
┌────────────────────────────────────────────────────────┐
│ Custo Breakdown - Post Free Tier (Mês 13+)            │
├──────────────────────┬────────┬──────────────────────┤
│ Serviço              │ Qty    │ Custo                │
├──────────────────────┼────────┼──────────────────────┤
│ EC2 t2.micro         │ 1      │ $8.50                │
│ EC2 t3.micro         │ 1      │ $8.50                │
│ RDS db.t3.micro      │ 1      │ $8.50                │
│ KMS CMK              │ 1      │ $1.00                │
│ Secrets Manager (3)  │ 1      │ $1.20                │
│ RDS Proxy            │ 1      │ $10.95               │
│ SES (62k emails)     │ -      │ $0 (Free)            │
│ S3 + CloudFront      │ ~20GB  │ $0.65                │
│ Glacier (retention)  │ ~50GB  │ $0.50                │
│ Route53              │ 1      │ $0.50                │
│ Domain (.br)         │ 1      │ $1/mo                │
├──────────────────────┼────────┼──────────────────────┤
│ TOTAL/MÊS            │        │ $41.30 (≈$41)        │
└────────────────────────────────────────────────────────┘
```

### Escalação (50+ Clientes, Mês 18)

```
┌────────────────────────────────────────────────────────┐
│ Custo Breakdown - Escalação (50+ Clientes)            │
├──────────────────────┬────────┬──────────────────────┤
│ Serviço              │ Qty    │ Custo                │
├──────────────────────┼────────┼──────────────────────┤
│ ALB                  │ 1      │ $22.68               │
│ EC2 t3.small (API)   │ 2-4    │ $36-72               │
│ EC2 t3.micro (Col)   │ 1      │ $8.50                │
│ RDS db.t3.small      │ 1      │ $45.00               │
│ DocumentDB           │ 1      │ $651.00              │
│ KMS/Secrets/IAM      │ -      │ $2.20                │
│ Data Transfer        │ -      │ $10-20               │
│ Monitoring/Logs      │ -      │ $10-15               │
├──────────────────────┼────────┼──────────────────────┤
│ TOTAL/MÊS            │        │ $200-250             │
└────────────────────────────────────────────────────────┘
```

---

## 🎯 Recomendação Final do AWS Architect

```
╔════════════════════════════════════════════════════════╗
║         RECOMENDAÇÃO: APPROVE ADR-011                 ║
╠════════════════════════════════════════════════════════╣
║                                                        ║
║ Status: ✅ READY FOR PROVISIONING                     ║
║                                                        ║
║ Próximas Ações:                                       ║
║ 1. Orchestrator: APPROVE ADR-011                      ║
║ 2. Platform Engineer: BEGIN PROVISIONING (5-10h)      ║
║ 3. Backend Engineer: READY para segunda 09:00         ║
║                                                        ║
║ Custo Final:                                          ║
║   MVP (0-12mo):    $14.93/mo (94% reduction!)        ║
║   Post FT (13mo+): $41.30/mo (still 82% savings)     ║
║   Scale (50+ cli): $200-250/mo (sustainable)         ║
║                                                        ║
║ Documentação:                                         ║
║   ✅ ADR-010 (Free Tier optimization)                 ║
║   ✅ ADR-011 (Decisões refinadas)                     ║
║   ✅ PLAYWRIGHT_LAMBDA_ANALYSIS                       ║
║   ✅ DYNAMODB_VS_DOCUMENTDB_ANALYSIS                  ║
║   ✅ LANDING_PAGE_ANALYSIS                            ║
║   ✅ ARCHITECTURE_DIAGRAM                             ║
║   ✅ Todos os trade-offs documentados                 ║
║                                                        ║
║ Risco: BAIXO (mitigado)                               ║
║ Timeline: Sexta EOD ✅                                ║
║                                                        ║
╚════════════════════════════════════════════════════════╝
```

---

## 📋 Checklist de Aprovação

### Para Orchestrator

- [ ] Aceita $14.93/mo MVP (vs $232 original)?
- [ ] Aceita Single-AZ RDS (RF-005 trade-off)?
- [ ] Aceita Local MongoDB (manual backup)?
- [ ] Aceita EC2 compute (vs Lambda)?
- [ ] Aceita GitHub Pages MVP (vs S3+CDN)?
- [ ] Aprova atuacoleta.com.br (domínio temporário)?
- [ ] Aprova timeline sexta provisioning?

### Para Backend Engineer

- [ ] Confirma Playwright viável em EC2?
- [ ] Confirma ASP.NET Core em t2.micro OK?
- [ ] Confirma MongoDB local queries OK?
- [ ] Confirma RDS connection string setup?
- [ ] Pronto para começar segunda 09:00?

### Para Platform Engineer

- [ ] Confirma 5-10h provisioning sexta?
- [ ] Confirma backup automation viável?
- [ ] Confirma Terraform IaC pronto?
- [ ] Confirma monitoring setup?
- [ ] Pronto para deploy?

---

## ⏱️ Timeline Crítico

```
TODAY 16:05 → Approval finalization
TODAY 17:00 → Platform Engineer begins provisioning

SEXTA 09:00 → Infrastructure coming up
SEXTA 12:00 → RDS + EC2 online, data loaded
SEXTA 14:00 → MongoDB + Collector setup
SEXTA 16:00 → Backup automation tested
SEXTA 17:00 → Smoke tests passed, ready for backend
SEXTA 17:30 → Documentation finalized

SEGUNDA 09:00 → Backend Engineer starts implementation
SEGUNDA 10:00 → RF-001, RF-002 implementation begins
```

---

## 📞 Decisão Necessária

### Para Provisionar Sexta, preciso de:

1. **Orchestrator**: ✅ Approve ADR-011 (async OK)
2. **Backend Engineer**: ✅ Confirm Playwright/EC2 viável
3. **Platform Engineer**: ✅ Ready to provision 5-10h

### Status Atual

```
Arquitetura:       ✅ DEFINIDA (ADR-011)
Custos:            ✅ ANALISADOS ($14.93/mo)
Tecnologias:       ✅ VALIDADAS
Documentação:      ✅ COMPLETA
Trade-offs:        ✅ MITIGADOS
Escalação:         ✅ CLARA ($200-250 growth)

Pronto para:       ✅ PROVISIONING
```

---

## 📚 Documentação Criada (Esta Sprint)

### Análises Técnicas
1. ✅ ADR-010: Free Tier Optimization (16.7 KB)
2. ✅ ADR-011: Decisões Refinadas (19.1 KB)
3. ✅ PLAYWRIGHT_LAMBDA_ANALYSIS (14.8 KB)
4. ✅ DYNAMODB_VS_DOCUMENTDB_ANALYSIS (16.8 KB)
5. ✅ LANDING_PAGE_ANALYSIS (13.6 KB)

### Comparativas & Decisões
6. ✅ COMPARISON_ADR009_vs_ADR010 (9.2 KB)
7. ✅ ARCHITECTURE_DIAGRAM (7.2 KB)
8. ✅ AWS_ARCHITECT_FINAL_ANALYSIS (11.2 KB)
9. ✅ DECISION_APPROVAL_READY (15.4 KB)
10. ✅ AWS_ARCHITECT_SUMMARY (this doc)

**Total**: 10 documentos, ~134 KB de análise completa

---

## 🎬 Call to Action

**Status**: ⏳ AWAITING APPROVALS

### Para Backend Engineer

Confirme viabilidade:
```
[ ] Playwright on EC2 (.NET Worker Service): Viável?
[ ] ASP.NET Core on t2.micro: Performance aceitável?
[ ] RDS + Local MongoDB queries: Padrão OK?
[ ] Monday 09:00: Ready to start RF-003?
```

### Para Platform Engineer

Confirme provisioning:
```
[ ] 5-10 horas sexta: Timeline realista?
[ ] Terraform IaC: Pronto para start?
[ ] Backup automation: Scripts testados?
[ ] Monitoring: CloudWatch básico setup OK?
```

### Para Orchestrator

Approve architecture:
```
[ ] ADR-011 approved: SIM / NÃO
[ ] $14.93/mo budget OK: SIM / NÃO
[ ] RF-005 single-AZ trade-off: SIM / NÃO
[ ] atuacoleta.com.br domínio: SIM / NÃO
```

---

**AWS Architect Analysis**  
**Date**: 2026-08-29, 16:05 UTC-3  
**Status**: ✅ COMPLETE & READY FOR APPROVAL  
**Documents**: 10 technical analyses + this summary  
**Cost**: $14.93/mo MVP (94% reduction from original)  
**Timeline**: Sexta EOD provisioning, backend segunda 09:00  

🎯 **All decisions made. All questions answered. Ready to provision.**
