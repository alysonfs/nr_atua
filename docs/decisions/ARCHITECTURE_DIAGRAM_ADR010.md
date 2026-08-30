# ATUA MVP Architecture Diagram - ADR-010 (Free Tier Optimized)

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         ATUA MVP - Free Tier                            │
│                    Region: sa-east-1 (São Paulo)                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────┐    │
│  │ VPC (10.0.0.0/16)                                              │    │
│  │                                                                │    │
│  │  ┌──────────────────────────────────────────────────────┐    │    │
│  │  │ AVAILABILITY ZONE 1a (Single AZ - MVP Trade-off)    │    │    │
│  │  │                                                      │    │    │
│  │  │ Public Subnet (10.0.1.0/24)                        │    │    │
│  │  │ ┌────────────────────────────────────────────────┐ │    │    │
│  │  │ │ Internet Gateway (IGW) - FREE               │ │    │    │
│  │  │ │                                               │ │    │    │
│  │  │ │ EC2 t2.micro (MongoDB + Collector)          │ │    │    │
│  │  │ │ ├─ Docker MongoDB 5.0                        │ │    │    │
│  │  │ │ ├─ .NET Worker Service (Collector)          │ │    │    │
│  │  │ │ ├─ Systemd (always-on, every 15 min)        │ │    │    │
│  │  │ │ └─ IP: 10.0.1.X (Public IP for iService)   │ │    │    │
│  │  │ │                                               │ │    │    │
│  │  │ │ EC2 t3.micro (Master API)                    │ │    │    │
│  │  │ │ ├─ ASP.NET Core Kestrel                      │ │    │    │
│  │  │ │ ├─ Monolito (no refactoring needed)          │ │    │    │
│  │  │ │ ├─ Systemd service                           │ │    │    │
│  │  │ │ └─ Security Group: TCP 443 inbound (users)   │ │    │    │
│  │  │ │                                               │ │    │    │
│  │  │ └────────────────────────────────────────────────┘ │    │    │
│  │  │                                                      │    │    │
│  │  │ Private Subnet (10.0.2.0/24)                       │    │    │
│  │  │ ┌────────────────────────────────────────────────┐ │    │    │
│  │  │ │ RDS db.t3.micro (PostgreSQL)                  │ │    │    │
│  │  │ │ ├─ Storage: 20GB (EBS gp2)                    │ │    │    │
│  │  │ │ ├─ Backup: 35 days automated                  │ │    │    │
│  │  │ │ ├─ Security Group: TCP 5432 (EC2 + Lambda)   │ │    │    │
│  │  │ │ └─ Encryption: TLS                            │ │    │    │
│  │  │ │                                               │ │    │    │
│  │  │ │ RDS Proxy (Public, managed)                   │ │    │    │
│  │  │ │ ├─ Connection pooling for Lambda (future)     │ │    │    │
│  │  │ │ ├─ Endpoint: rds-proxy.c9akciq32.rds...     │ │    │    │
│  │  │ │ └─ Cost: $10.95/mo                           │ │    │    │
│  │  │ │                                               │ │    │    │
│  │  │ └────────────────────────────────────────────────┘ │    │    │
│  │  │                                                      │    │    │
│  │  └──────────────────────────────────────────────────────┘    │    │
│  │                                                                │    │
│  └────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘

AWS Account:
├─ KMS CMK (1 per environment)
│  └─ Encryption key for secrets & backups
│
├─ Secrets Manager
│  ├─ atua/root (bootstrap credential)
│  ├─ atua/rds-postgres (RDS password)
│  └─ atua/mongodb (MongoDB password - if needed)
│
├─ S3 Bucket
│  ├─ Daily RDS snapshots
│  ├─ Daily EBS snapshots (MongoDB)
│  └─ Glacier transition (35+ days, ADR-008)
│
├─ SES (Email)
│  └─ 62k emails/month (Free Tier)
│
└─ IAM Roles
   ├─ ec2-role (EC2 → RDS, Secrets Manager)
   └─ lambda-role (future use)
```

---

## Data Flow

### 1. Collector Workflow (Every 15 minutes)

```
Systemd Timer (every 15 min)
    ↓
.NET Worker Service (Collector)
    ├─ Get tenant list from RDS PostgreSQL
    ├─ For each tenant:
    │  ├─ Call iService API (GET /orders?tenant=XXX)
    │  ├─ Parse response
    │  ├─ Store in RDS (via EC2 connection)
    │  └─ Store in MongoDB (localhost connection)
    └─ Log results

Network Path:
  EC2 (10.0.1.X) → IGW → Internet → iService API
  EC2 (10.0.1.X) → localhost:27017 → MongoDB (local)
  EC2 (10.0.1.X) → RDS Proxy → RDS (10.0.2.X)
```

### 2. Master API Workflow (Request-Response)

```
Client (User/Frontend)
    ↓ (HTTPS)
Security Group (TCP 443)
    ↓
EC2 t3.micro (10.0.1.Y)
    ├─ ASP.NET Core Kestrel (Port 5000)
    ├─ Request processing
    └─ Data access:
       ├─ Query RDS via EC2 connection
       │  └─ Connection pooling (persistent)
       ├─ Query MongoDB locally
       │  └─ Localhost connection
       └─ Other services (Secrets Manager, KMS)

Response Path:
  EC2 (10.0.1.Y) → Security Group → Client
```

### 3. Backup Workflow (Daily)

```
Cron Job (02:00 UTC, daily)
    ↓
AWS Backup Agent (on EC2)
    ├─ RDS: Create snapshot
    │  └─ Upload to S3 (automatic)
    └─ EBS: Create snapshot
       └─ Upload to S3 (automatic)

    ↓ (35+ days later)

S3 Lifecycle Policy
    └─ Transition to Glacier Deep Archive
       └─ Long-term retention (ADR-008)
```

---

## Network Security

### Security Groups Configuration

```
EC2 Public Security Group (Master API + Collector)
├─ Inbound:
│  ├─ TCP 443 from 0.0.0.0/0 (HTTPS from users)
│  ├─ TCP 22 from 10.0.0.0/8 (SSH admin, if needed)
│  └─ All other inbound: DENY
│
└─ Outbound:
   ├─ TCP 443 to 0.0.0.0/0 (HTTPS to iService API)
   ├─ TCP 5432 to 10.0.2.0/24 (PostgreSQL to RDS)
   ├─ UDP 53 to 0.0.0.0/0 (DNS)
   └─ All other outbound: DENY

RDS Security Group
├─ Inbound:
│  └─ TCP 5432 from 10.0.1.0/24 (PostgreSQL from EC2)
│
└─ Outbound:
   └─ All: DENY (RDS doesn't initiate connections)
```

### Encryption

```
Data at Rest:
├─ RDS: EBS encryption via KMS CMK
├─ EBS (MongoDB): Encryption via KMS CMK
├─ S3 (Backups): Encryption via KMS CMK
└─ Secrets Manager: KMS CMK encryption

Data in Transit:
├─ EC2 ↔ RDS: TLS (PostgreSQL over SSL)
├─ EC2 ↔ MongoDB: Localhost (no encryption needed)
├─ EC2 ↔ KMS: TLS
├─ EC2 ↔ Secrets Manager: TLS
└─ Client ↔ EC2: TLS (HTTPS only)
```

---

## Monitoring & Observabilit

### Metrics & Alarms (Manual AWS Console)

```
EC2 Monitoring:
├─ CPU Utilization (alert if > 60%)
├─ Network In/Out (egress to iService)
└─ Disk Space (MongoDB storage)

RDS Monitoring:
├─ CPU Utilization (alert if > 50%)
├─ Database Connections
└─ Storage Space

Manual Checks (via AWS Console):
├─ Daily: Check backup completion
├─ Daily: Verify EC2 status
└─ Weekly: Review CloudWatch metrics

Application Logging:
├─ Collector: systemd journalctl
├─ Master API: stdout/stderr to journalctl
└─ MongoDB: /var/log/mongodb/mongod.log
```

---

**Diagram prepared by**: aws-architect  
**Date**: 2026-08-29  
**Status**: ✅ ADR-010 Architecture  
