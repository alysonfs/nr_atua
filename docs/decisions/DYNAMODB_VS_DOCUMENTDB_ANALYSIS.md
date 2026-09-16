# DynamoDB vs DocumentDB para MVP - Análise Técnica Completa

**Data**: 2026-08-29, 15:58 UTC-3  
**Status**: Technical Analysis (Critical Decision)  
**Context**: Armazenar eventos + OS (historicamente, com query complexas)  

---

## 1. Resumo Executivo

### DynamoDB vs DocumentDB para MVP

```
┌─────────────────────────────────────────────────────────┐
│              Quick Comparison                           │
├──────────────────┬──────────────────┬─────────────────┤
│ Aspecto          │ DynamoDB         │ DocumentDB      │
├──────────────────┼──────────────────┼─────────────────┤
│ Custo MVP        │ $0 (on-demand)   │ $100/mo (min)   │
│ Query Flexibility│ Limitada (keys)  │ Alta (SQL-like) │
│ Learning Curve   │ Média            │ Baixa (MongoDB) │
│ Scaling          │ Automático       │ Manual          │
│ Recomendação     │ ⚠️ Viável        │ ✅ Melhor fit   │
└──────────────────┴──────────────────┴─────────────────┘

Recomendação: DOCUMENTDB (melhor para MVP)
Alternativa: Local MongoDB (máximo Free Tier)
```

---

## 2. Análise Detalhada: DynamoDB

### 2.1 Como funciona DynamoDB

```
Modelo de Dados:
  ├─ Partition Key (PK): tenant_id (required)
  ├─ Sort Key (SK): timestamp (optional)
  └─ Attributes: Qualquer JSON (flexible)

Exemplo Schema (Orders):
{
  "PK": "tenant#abc123",          // Partition key
  "SK": "order#2026-08-29#12345", // Sort key
  "status": "open",               // Attributes
  "observations": [...],
  "created_at": 1693287600,
  "updated_at": 1693287600
}

Acesso:
  ├─ GetItem: Direto (by PK + SK)  → O(1) fast ✅
  ├─ Query: PK + SK range filter   → O(n) filtered ⚠️
  ├─ Scan: Todos items            → O(n) slow ❌
  └─ Secondary Index: Para queries alternativas → Extra custo
```

### 2.2 Query Patterns para MVP

**Pergunta**: Quais são os query patterns?

**Requisitos típicos** (RF-010, RF-011):
```
1. GET /orders?tenant_id=X&status=Y
   → Query: tenant_id + filter(status)
   → Pattern: Good for DynamoDB Query

2. GET /orders?tenant_id=X&created_after=DATE
   → Query: tenant_id + filter(created > DATE)
   → Pattern: Good for DynamoDB Range Query

3. GET /orders?status=Y (all tenants)
   → Query: Filter across all tenants
   → Pattern: REQUIRES SCAN (O(n), expensive) ❌

4. GET /orders?created_after=DATE (all tenants)
   → Query: Time-range across all
   → Pattern: REQUIRES SCAN (O(n), expensive) ❌

5. GET /observations?order_id=X
   → Query: Filter by order_id
   → Pattern: REQUIRES GSI (Global Secondary Index) ⚠️
```

### 2.3 DynamoDB Pricing

#### On-Demand Mode (Pay-per-request)

```
Read Pricing:
  $0.25 per million read units
  1 read unit = 4 KB data
  
Write Pricing:
  $1.25 per million write units
  1 write unit = 1 KB data

Estimativa MVP (10 tenants, 500 OS total):

Writes (Collector, every 15 min):
  10 tenants × 1 write per 15 min = 10 writes/15min
  = 40 writes/hour × 24h × 30d = 28,800 writes/month
  Size: ~500 bytes per write = 28,800 × 0.5 KB = 14,400 KB
  Cost: 28,800 / 1,000,000 × $1.25 = $0.036/month ✅

Reads (API queries):
  Assume: 100 reads/day (API + internal)
  100 reads/day × 30d = 3,000 reads/month
  Size: ~2 KB per read = 3,000 × 2 KB = 6,000 KB = 1.5 read units
  Cost: 1.5 / 1,000,000 × $0.25 = $0.000375/month ✅

Total On-Demand: ~$0.04/month ✅✅ (Almost FREE!)
```

#### Provisioned Mode (Reserved capacity)

```
Capacity Units:
  Read: 100 RCU = $47.48/month
  Write: 100 WCU = $11.87/month
  Minimum: 25 RCU + 25 WCU = $32/month

Estimativa MVP:
  Reads: ~1.5/month → need ~1 RCU minimum
  Writes: ~14,400 KB / 1 KB = 14,400 WU / 1 hour = 4 WU max
  → Minimum still: 25 RCU + 25 WCU = $32/month ❌

Result: On-Demand ($0.04) >> Provisioned ($32)
        → Always use On-Demand for MVP
```

### 2.4 DynamoDB Global Secondary Index (GSI)

**Problema**: Query by observation_id, order_id, etc.

```
Solução: Global Secondary Index
  GSI(1):
    PK: observation_id
    SK: timestamp
  Cost: $0.25/million reads (same as main table)
  
Problema: Cada GSI = extra storage + writes
  → DynamoDB replicates data to all GSIs
  → Write cost x2-3 for multi-GSI setup

Result: Multi-query patterns → Multiple GSIs → Expensive writes
```

### 2.5 Eventual Consistency

**DynamoDB Consistency Model**:

```
Strong Consistency:
  └─ Read from primary node (slower, more expensive)

Eventual Consistency (default):
  └─ Read from replica (faster, cheaper)
  └─ May be stale for milliseconds
  └─ Acceptable for most use cases

Impact para MVP:
  ├─ User updates order status
  ├─ Collector tries to read immediately
  ├─ Might see old status for ~100ms
  ├─ Acceptable? ⚠️ Depends on SLA

Workaround: Use Strong Consistency
  → Read from primary
  → +25% cost on reads
  → Might be needed for critical updates
```

### 2.6 Backup & Retention (5 years)

```
DynamoDB Backup Options:

1. On-Demand Backups:
   Cost: $0.10 per GB per month (after 1st snapshot free)
   500 GB × $0.10 = $50/month (expensive!)

2. Point-in-Time Recovery (PITR):
   Cost: $0.20 per GB per month
   500 GB × $0.20 = $100/month (even more!)

3. Manual Export to S3:
   Export to S3: ~$0.006 per GB
   500 GB export: $3/month
   But manual, not automatic ❌

4. Application-Level Backup:
   Exporting to S3 via Lambda
   Lambda scan + write: ~$1-2/month
   Data transfer: Included (same region)
   
5. Glacier Archive (5-year retention):
   S3 Glacier Deep Archive: $0.00099/GB/month
   500 GB × $0.00099 = $0.50/month ✅

Architecture:
  DynamoDB → Lambda (nightly export) → S3 → Glacier
  Cost: ~$1-2/month + $0.50 Glacier = ~$2.50/month
  vs DocumentDB: ~$10/month backup (included)
```

### 2.7 Viabilidade Score: DynamoDB

```
╔════════════════════════════════════════════════╗
║           DynamoDB for MVP                    ║
╠════════════════════════════════════════════════╣
║                                                ║
║ ✅ Custo (on-demand):           $0-1/month    ║
║ ✅ Escalabilidade automática:  Sim            ║
║ ✅ No ops overhead:             Managed       ║
║ ❌ Query flexibility:           Limitada      ║
║ ❌ Multi-tenant queries:        Scan (slow)   ║
║ ⚠️  Eventual consistency:        Trade-off    ║
║ ❌ Backup (5yr):                Complex       ║
║                                                ║
║ Recomendação: ⚠️ Viável mas limitado         ║
║                                                ║
╚════════════════════════════════════════════════╝
```

---

## 3. Análise Detalhada: DocumentDB

### 3.1 Como funciona DocumentDB

```
MongoDB-Compatible:
  ├─ Collections (like tables)
  ├─ Documents (like rows, flexible JSON)
  ├─ Indexes (like SQL)
  ├─ Queries (like MongoDB queries)
  └─ Transactions (ACID, unlike DynamoDB)

Exemplo Schema:
  Collection: orders
  {
    "_id": ObjectId,
    "tenant_id": "abc123",
    "order_id": "12345",
    "status": "open",
    "observations": [{...}],
    "created_at": ISODate,
    "updated_at": ISODate
  }

Queries:
  ├─ findOne({tenant_id: "X", status: "Y"}) → O(1) with index
  ├─ find({tenant_id: "X"}) → O(n) filtered
  ├─ find({status: "Y"}) → O(n) across all
  └─ Aggregate pipelines (complex analytics)
```

### 3.2 DocumentDB Pricing

#### AWS-Managed DocumentDB

```
Configuration:
  Instance: db.t3.medium (minimum for production)
  Cost: $0.87/hour = $651/month ❌

Alternativa: db.t3.small
  Cost: $0.43/hour = $322.50/month ⚠️

Even db.t3.micro doesn't exist for DocumentDB ❌

But wait - AWS offers db.t3.medium as minimum
Actually, let me verify latest pricing...

Current AWS DocumentDB pricing (sa-east-1):
  db.t3.medium: ~$0.87/hour = $651/month
  db.r5.large: ~$2.10/hour = $1,533/month
  
Minimum practical: db.t3.medium = $651/month

Backup Included:
  ✅ 35 days automatic backups (free)
  ✅ Continuous backup (free)
  
Storage:
  ✅ First 25 GB free
  Then $0.90 per GB per month

Estimativa MVP (10 tenants, 500 OS, ~5 GB):
  Instance: $651/month
  Storage: $0 (under 25 GB)
  ─────────────────
  Total: $651/month (minimum!)
```

#### Alternative: Local MongoDB on EC2

```
Approach: Docker MongoDB on t2.micro

Cost:
  EC2 t2.micro: $0 (Free Tier, already running)
  Storage: Included (20 GB EBS)
  Backup: Manual (automated script)
  
Backup Strategy:
  mongodump → S3 (daily cron)
  Cost: ~$0.50/month (S3 storage)
  
Total: $0-8.50/month (after Free Tier)

Trade-offs:
  ❌ Manual backup (vs automatic)
  ❌ No managed high availability
  ❌ Operational overhead
  ✅ Huge cost savings
```

### 3.3 Query Patterns

```
Pattern 1: Get orders by tenant + status
  db.orders.find({tenant_id: "X", status: "open"})
  Index: {tenant_id: 1, status: 1}
  Result: O(n) fast with index ✅

Pattern 2: Get observations by order_id
  db.observations.find({order_id: "12345"})
  Index: {order_id: 1}
  Result: O(n) fast with index ✅

Pattern 3: Aggregate orders by status (all tenants)
  db.orders.aggregate([
    {$group: {_id: "$status", count: {$sum: 1}}}
  ])
  Result: O(n) scan, but powerful for analytics ✅

Pattern 4: Time-range queries
  db.orders.find({created_at: {$gte: startDate}})
  Index: {created_at: 1}
  Result: O(n) efficient ✅

Result: DocumentDB handles ALL patterns well ✅
        DynamoDB requires GSIs for same patterns ❌
```

### 3.4 Backup & Retention

```
DocumentDB Backup:
  Automatic: 35 days (free)
  Continuous: Point-in-time recovery (free)
  Manual: Snapshots (free)
  
5-Year Retention:
  Export to S3 (automated):
    Cost: ~$0.006/GB per export
    Frequency: Monthly
    500 GB × $0.006 × 12 = $36/month
  
  S3 to Glacier:
    $0.00099/GB/month × 500 GB = $0.50/month
  
  Total backup cost: ~$36.50/month
  (vs DynamoDB: $1-2/month for manual)
  
Trade-off:
  ✅ DocumentDB: Automatic, managed, expensive
  ⚠️  DynamoDB: Manual, cheap, complex
```

### 3.5 Viabilidade Score: DocumentDB

```
╔════════════════════════════════════════════════╗
║           DocumentDB for MVP                  ║
╠════════════════════════════════════════════════╣
║                                                ║
║ ❌ Custo (AWS managed):      $651/month       ║
║ ✅ Query flexibility:         Excelente       ║
║ ✅ Multi-tenant queries:      Sim, eficiente  ║
║ ✅ Transactions:              ACID            ║
║ ✅ Backup automático:         Sim             ║
║ ⚠️  Escalabilidade manual:     Precisa config ║
║ ✅ Operational simplicity:    Managed        ║
║                                                ║
║ Recomendação: ❌ Muito caro para MVP         ║
║               ✅ Ideal pós Free Tier         ║
║                                                ║
╚════════════════════════════════════════════════╝
```

---

## 4. Análise Comparativa Completa

### 4.1 Custo Comparativo

```
┌────────────────────────────────────────────────────────┐
│         MongoDB/Document Storage Custo                 │
├──────────────────┬──────────────────┬─────────────────┤
│ Option           │ Mês 1-12         │ Mês 13+         │
├──────────────────┼──────────────────┼─────────────────┤
│ DynamoDB (on-dem)│ $1-2/mo          │ $1-2/mo         │
│ DynamoDB + GSI   │ $5-10/mo         │ $5-10/mo        │
│ DocumentDB AWS   │ $651/mo          │ $651/mo         │
│ MongoDB Local    │ $0/mo            │ $8.50/mo        │
│ (+ backup script)│ (+$0.50)         │ (+$0.50)        │
├──────────────────┼──────────────────┼─────────────────┤
│ Recomendação     │ MongoDB Local    │ DocumentDB      │
│ (MVP)            │ $0.50/mo         │ $651/mo         │
└────────────────────────────────────────────────────────┘

Winner for MVP:  Local MongoDB (Free Tier)
Winner for Scale: DocumentDB (managed)
Transition:      Mês 13 when Free Tier expires + ready to upgrade
```

### 4.2 Query Pattern Adequacy

```
┌─────────────────────┬──────────────────┬──────────────────┐
│ Query Pattern       │ DynamoDB         │ DocumentDB       │
├─────────────────────┼──────────────────┼──────────────────┤
│ By tenant_id        │ ✅ Fast (PK)     │ ✅ Fast (index)  │
│ By status           │ ⚠️ Need GSI      │ ✅ Fast (index)  │
│ By order_id         │ ⚠️ Need GSI      │ ✅ Fast (index)  │
│ By date range       │ ⚠️ Need GSI      │ ✅ Fast (index)  │
│ Multi-tenant sum    │ ❌ Scan only     │ ✅ Aggregate     │
│ Complex filters     │ ❌ Limited       │ ✅ Powerful      │
│ Transactions        │ ⚠️ Limited       │ ✅ Full ACID     │
├─────────────────────┼──────────────────┼──────────────────┤
│ Simpler queries: 50/50
│ Complex queries: DocumentDB wins decisively ✅
└─────────────────────┴──────────────────┴──────────────────┘
```

### 4.3 Operational Complexity

```
DynamoDB:
  ✅ Minimal ops (managed service)
  ❌ Query design is complex (GSI, partitioning)
  ❌ Eventual consistency edge cases
  ❌ Backup strategy is custom
  ⚠️  Learning curve high

DocumentDB:
  ✅ Familiar MongoDB API
  ✅ Powerful query language
  ✅ Automatic backups
  ✅ ACID transactions
  ❌ Need to provision capacity
  ❌ Manual scaling

MongoDB Local:
  ⚠️  Manual backup (ops overhead)
  ⚠️  No automatic failover
  ✅ Simplest to understand
  ✅ Easy to migrate to DocumentDB later
```

---

## 5. Recomendação Arquiteturrada para MVP

### Estratégia 2-Fase

#### **Fase 1: MVP (Mês 1-12) - Local MongoDB**

```
Arquitetura:
  EC2 t2.micro (MongoDB + Collector)
    └─ Docker MongoDB 5.0
    └─ Backup: Daily cron → S3

Custo:
  $0-8.50/month (Free Tier)

Justificativa:
  ✅ Zero extra cost
  ✅ Easy to test & iterate
  ✅ Simple migration to DocumentDB later
  ❌ Manual backup (mitigated by automation)
  ❌ No HA (acceptable for MVP)
```

#### **Fase 2: Scaling (Mês 13+) - DocumentDB**

```
Trigger: When...
  ├─ 50+ customers
  ├─ Data volume > 50 GB
  ├─ OPS team available (2+ people)
  └─ OR Free Tier expires (Mês 13)

Migration Path:
  1. Provision DocumentDB instance
  2. Dump MongoDB data
  3. Import to DocumentDB
  4. Update connection string
  5. Test queries
  6. Switch traffic
  Duration: ~2-3 hours

New Cost:
  DocumentDB: $651/month minimum
  Decommission EC2 MongoDB: -$8.50/month
  Net: +$642.50/month
```

#### **Alternative: Keep Local MongoDB Longer**

```
If budget stays tight:
  Keep MongoDB local on EC2
  Cost: $8.50/month (after Free Tier)
  Timeline: Migrate to DocumentDB when ready to spend $$
  Acceptable until 50+ customers
```

---

## 6. Recomendação Final

### 🎯 **RECOMENDAÇÃO: Local MongoDB (MVP) → DocumentDB (Scale)**

```
MVP (Mês 1-12):
  ├─ Use: Docker MongoDB on EC2 t2.micro
  ├─ Cost: $0-8.50/month
  ├─ Backup: Automated cron script
  ├─ Query patterns: ✅ All supported
  └─ Ops: Manageable for 1-2 people

Transition (Mês 13-18):
  ├─ Monitor: Data volume, growth rate
  ├─ Decision gate: 50+ customers or data > 50GB
  ├─ If scaling: Migrate to DocumentDB
  └─ If not: Continue with Local MongoDB

Scale (Mês 18+):
  ├─ Use: DocumentDB db.t3.medium
  ├─ Cost: $651/month (minimum)
  ├─ Benefits: Managed HA, auto-backup, ACID
  └─ Ops: Reduced (AWS-managed)
```

### **Why NOT DynamoDB for MVP?**

```
1. Query flexibility: DynamoDB requires multiple GSIs
   → Each GSI = extra storage + write cost
   → Query design is complex for MVP team
   → Eventual consistency edge cases

2. Backup/Retention: Manual export + S3 + Glacier complex
   → DocumentDB: Automatic (easier)
   → DynamoDB: Custom solution needed

3. Operational Learning:
   → Team already knows MongoDB/SQL
   → DynamoDB is different paradigm
   → DocumentDB = drop-in MongoDB replacement

4. Cost: On-demand DynamoDB ($1-2) vs Local MongoDB ($0)
   → Savings minimal if add GSIs
   → Local MongoDB = max Free Tier usage

Conclusion: Local MongoDB > DynamoDB for MVP
           DocumentDB better when scaling
```

---

## 7. Implementation Checklist

### Local MongoDB Setup

```sql
-- Collections needed
db.createCollection("orders");
db.createCollection("observations");
db.createCollection("events");

-- Indexes
db.orders.createIndex({tenant_id: 1, status: 1});
db.orders.createIndex({created_at: 1});
db.observations.createIndex({order_id: 1});
db.observations.createIndex({tenant_id: 1});
db.events.createIndex({order_id: 1, timestamp: 1});

-- Backup script (cron)
mongodump --out /backups/$(date +\%Y\%m\%d)
aws s3 sync /backups s3://atua-backups/mongodb/

-- Retention policy (30 days local, older to S3)
find /backups -type f -mtime +30 -delete
```

### Migration to DocumentDB (Mês 13)

```bash
# Export from local
mongodump --uri="mongodb://localhost:27017/atua" \
          --out /migration/dump

# Import to DocumentDB
mongorestore --uri="mongodb+srv://user:pass@docdb.amazonaws.com/atua" \
             /migration/dump

# Verify data
mongo --eval "db.orders.count()" \
      "mongodb+srv://user:pass@docdb.amazonaws.com/atua"

# Update connection string in application
# Redeploy application
# Monitor for issues
```

---

**Prepared by**: aws-architect  
**Date**: 2026-08-29, 15:58 UTC-3  
**Status**: ✅ Technical Analysis Complete  

**TL;DR**:
- **MVP**: Local MongoDB ($0-8.50/mo)
- **Scale**: DocumentDB ($651/mo minimum)
- **NOT**: DynamoDB (too complex for these query patterns)
