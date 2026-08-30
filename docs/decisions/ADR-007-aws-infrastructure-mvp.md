# ADR-007 - AWS Infrastructure para MVP (KMS, Secrets Manager, RDS, DocumentDB)

## Status

Proposed

## Contexto

O MVP ATUA requer armazenamento de dados críticos (usuários, tenants, credenciais, histórico de OS) com proteção de segredos (ADR-004), retenção de 5 anos (RS-003) e isolamento de tenant (RS-002).

A arquitetura de backend (ADR-003) define:
- **Master API**: ASP.NET Core, acesso único aos dados
- **Collector Worker**: .NET Worker Service, coleta recorrente a cada 15 min
- **Armazenamento**: PostgreSQL (transacional) + MongoDB (operacional)

A infraestrutura AWS deve suportar este modelo sem custos excessivos (<$500/mês dev/staging) e ser preparada para escalabilidade futura.

## Decisão

### Estratégia de Segurança

**AWS KMS (Key Management Service)**
- 1 Customer Master Key (CMK) por ambiente (dev, staging, prod)
- Alias: `alias/atua-mvp-key`
- Rotação automática: Anual
- Uso: Criptografia at-rest de RDS, DocumentDB, Secrets Manager
- Acesso: Apenas Master API e Collector Worker via IAM roles
- VPC Endpoint: Sim (economizar NAT para decryption calls)

**AWS Secrets Manager**
- 3 segredos por ambiente:
  1. `atua/root`: Bootstrap token (uso único, manual)
  2. `atua/rds-postgres`: Credenciais PostgreSQL + host/port
  3. `atua/documentdb`: Credenciais DocumentDB + connection string
- Criptografia: Via KMS CMK
- Rotação automática: 30 dias (RDS + DocumentDB), manual (ROOT)
- Estrutura JSON: `{ "username": "...", "password": "...", "host": "...", "port": ... }`

### Armazenamento Transacional

**AWS Aurora Serverless PostgreSQL** (Atualizado via ADR-009 Cost Analysis)
- **Engine**: PostgreSQL 10 (AWS managed)
- **Topologia**: Multi-AZ (sempre incluído no Serverless)
- **Capacity**: 1 ACU (auto-scale 0.5-16 ACU conforme demanda)
- **Storage**: 100 GB, auto-scaling até 500 GB
- **Backup**: Automated retention 35 dias + manual snapshots mensais
- **Encryption at-rest**: Via KMS CMK
- **Encryption in-transit**: TLS 1.2 obrigatório
- **Network**: VPC privada, sem public endpoint
- **Connection Pool Management**:
  - Built-in connection pooling via RDS Proxy
  - Max connections: 100 (sufficient for MVP)
- **Databases**: 
  - `atua_prod`: Dados de produção
  - `atua_staging`: (futuro)
  - `atua_dev`: (local via Docker, não RDS)

**Motivo Aurora Serverless vs db.t3.medium Multi-AZ**:
- ADR-004 define "sempre disponível" (RF-005) → Aurora Serverless fornece Multi-AZ nativo
- Custo: Aurora $68.40/mês vs db.t3.medium $331.50/mês (79% reduction)
- Auto-scaling: Sem necessidade de reserva de capacity
- Trade-off: PostgreSQL 10 vs 14 (requer validação Entity Framework)
- Cold start: ~30 segundos (aceitável para MVP)
- Detalhes: Vide ADR-009 (AWS Cost Analysis)

### Armazenamento Operacional

**AWS DocumentDB (MongoDB 4.0 compatible)**
- **Engine**: MongoDB 4.0 (AWS managed)
- **Cluster**: `atua-cluster`
- **Instances**: 2x db.t3.medium (Primary + Read Replica)
- **Storage**: Managed (auto-scales, default backup nightly)
- **Backup**: Automated retention 35 dias
- **Encryption at-rest**: Via KMS CMK
- **Encryption in-transit**: TLS 1.2 via VPC
- **Network**: VPC privada, cluster security group
- **Collections**:
  - `orders`: OS do iService (denormalizado)
  - `observations`: Observações históricas por OS
  - `events`: Eventos de coleta, transições, erros
- **Índices** (performance para RF-010, RF-011):
  ```javascript
  db.orders.createIndex({ tenant_id: 1, status: 1 })
  db.orders.createIndex({ tenant_id: 1, updated_at: -1 })
  db.observations.createIndex({ order_id: 1, observed_at: -1 })
  db.observations.createIndex({ tenant_id: 1, order_id: 1 })
  db.events.createIndex({ tenant_id: 1, event_type: 1 })
  db.events.createIndex({ tenant_id: 1, created_at: -1 })
  db.events.createIndex({ event_id: 1 }, { unique: true })
  ```
- **TTL Index** (eventual 5-year deletion):
  ```javascript
  db.events.createIndex({ created_at: 1 }, { expireAfterSeconds: 157680000 })
  ```

**Motivo DocumentDB**: Queries e agregações sobre histórico (RF-010, RF-011) são mais eficientes em documentos do que JSON em PostgreSQL. Managed backup e replication sem operação manual.

### Email Transacional

**AWS SES (Simple Email Service)**
- **Region**: us-east-1 (melhor disponibilidade)
- **Identity**: Domínio verificado (DKIM + SPF)
- **Mode**: Production (não Sandbox)
- **Rate Limit**: 14 emails/segundo (suficiente para MVP)
- **Templates**:
  1. `confirmation-email`: Confirmação de cadastro (RF-002)
  2. `trial-expiring-soon`: Aviso trial expirando (RF-003)
  3. (futuro: `password-reset`, `welcome`, etc)
- **Tracking**: Bounces + Complaints via SNS (futura integração)

**Motivo**: Serviço gerenciado, sem servidor de email próprio. Integração nativa com .NET.

### Rede e Isolamento

**VPC**
- **CIDR**: 10.0.0.0/16
- **AZ**: sa-east-1a, sa-east-1b (dual AZ)
- **DNS**: Hostnames + resolution habilitados

**Subnets Públicas** (para NAT Gateway, futuro ALB):
- 10.0.0.0/24 (sa-east-1a)
- 10.0.1.0/24 (sa-east-1b)

**Subnets Privadas** (RDS, DocumentDB, Master API):
- 10.0.10.0/24 (sa-east-1a)
- 10.0.11.0/24 (sa-east-1b)

**NAT Gateway**
- 1 em subnet pública (sa-east-1a com Elastic IP)
- Outbound: Collector Worker → iService, Master API → SES
- Custo: ~$32/mês + data transfer

**Security Groups**
```
rds-postgres:
  Ingress: TCP 5432 (Master API SG + Collector SG)
  Egress: Nenhum (não necessário)

documentdb-cluster:
  Ingress: TCP 27017 (Master API SG + Collector SG)
  Egress: Nenhum

master-api:
  Ingress: TCP 80, 443 (0.0.0.0/0 futuro ALB)
  Egress: TCP 443 (KMS, Secrets Manager, SES, iService, RDS, DocumentDB)

collector-worker:
  Ingress: Nenhum
  Egress: TCP 443 (KMS, Secrets Manager, iService)
          TCP 5432 (RDS)
          TCP 27017 (DocumentDB)
```

**VPC Endpoints** (evitar NAT para AWS services):
- `secretsmanager.sa-east-1`
- `kms.sa-east-1`
- `ses.us-east-1` (SES em us-east-1)

### Identity & Access Management

**Master API Execution Role** (`MasterApiExecutionRole`)
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "kms:Decrypt",
      "Resource": "arn:aws:kms:sa-east-1:ACCOUNT:key/KEY_ID",
      "Condition": {
        "StringEquals": {
          "kms:ViaService": [
            "secretsmanager.sa-east-1.amazonaws.com",
            "rds.sa-east-1.amazonaws.com"
          ]
        }
      }
    },
    {
      "Effect": "Allow",
      "Action": "secretsmanager:GetSecretValue",
      "Resource": [
        "arn:aws:secretsmanager:sa-east-1:ACCOUNT:secret:atua/rds-postgres*",
        "arn:aws:secretsmanager:sa-east-1:ACCOUNT:secret:atua/documentdb*"
      ]
    },
    {
      "Effect": "Allow",
      "Action": "ses:SendTemplatedEmail",
      "Resource": "*"
    }
  ]
}
```

**Collector Worker Execution Role** (`CollectorWorkerExecutionRole`)
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "kms:Decrypt",
      "Resource": "arn:aws:kms:sa-east-1:ACCOUNT:key/KEY_ID"
    },
    {
      "Effect": "Allow",
      "Action": "secretsmanager:GetSecretValue",
      "Resource": [
        "arn:aws:secretsmanager:sa-east-1:ACCOUNT:secret:atua/rds-postgres*",
        "arn:aws:secretsmanager:sa-east-1:ACCOUNT:secret:atua/documentdb*"
      ]
    }
  ]
}
```

### Retenção de Dados (RS-003: 5 anos)

**RDS Automated Backup**
- Retention: 35 dias
- After 35 days: Manual snapshot mensal para arquivo
- Archive: Copiar snapshot para S3 Glacier Deep Archive
- Lifecycle: Move após 90 dias, retenção 5 anos (1825 dias)
- Custo: ~$5-10/mês (após primeiro ano)

**DocumentDB Automated Backup**
- Retention: 35 dias (incluso)
- Manual snapshots: API call mensal
- Archive: Idem RDS

**Deletion Policy**
- Soft delete: Coluna `deleted_at` no PostgreSQL
- Hard delete: Stored procedure agendada para 5 anos após `deleted_at`
- Audit: Registrar quando dados foram removidos (schema `audit.`)

## Alternativas Rejeitadas

### 1. Credenciais em Variáveis de Ambiente
❌ Risco de exposição em logs, snapshots e versionamento de código.
✅ Secrets Manager implementa rotação e auditoria.

### 2. Credenciais em Parameter Store
❌ Parameter Store não suporta rotação automática (Secrets Manager sim).
✅ Secrets Manager é a escolha apropriada para segredos de aplicação.

### 3. MongoDB em EC2
❌ Requer backups manuais, patch management, operação 24/7.
✅ DocumentDB é gerenciado, backup automático, HA nativa.

### 4. RDS Single-AZ
❌ RTO > 30 min em falha de AZ. ADR-004 exige "sempre disponível" (RF-005).
✅ Multi-AZ: failover automático <1 min, mesmo custo de operação.

### 5. Observabilidade Gerenciada (DataDog, New Relic)
❌ Custo: $500+/mês, inviável no MVP.
✅ CloudWatch nativo: Logs, métricas, alarmes com Free Tier.

### 6. S3 para Dados Operacionais
❌ Latência inadequada para queries em tempo real.
✅ RDS + DocumentDB: latência ms, queries eficientes.

### 7. Backup em múltiplas regiões
❌ Custo de data transfer inter-região, replicação complexa.
✅ Retenção de snapshots em Glacier (single region) é suficiente para MVP.

## Motivos

1. **Menor privilégio**: IAM roles específicas, sem `Action: "*"` ou `Resource: "*"`
2. **Encriptação em camadas**: at-rest (KMS) + in-transit (TLS) + application-level (AES-256-GCM)
3. **Isolamento de tenant**: Security groups + VPC privada, sem exposição pública
4. **Gestão de segredos**: Rotação automática reduz risco de comprometimento
5. **Auditoria**: CloudTrail + audit logs na aplicação
6. **Custo**: ~$250/mês (50% abaixo do limite $500)
7. **Escalabilidade**: RDS auto-scaling, DocumentDB replication sem limite
8. **Recuperação**: Backup + snapshots + Glacier para RTO/RPO definido

## Consequências

### Positivas
- ✅ Dados protegidos em trânsito e em repouso
- ✅ Tenant isolados por design de rede + RBAC
- ✅ Retenção de 5 anos sem custo proibitivo
- ✅ HA nativa (failover automático RDS)
- ✅ Escalabilidade para 100+ tenants no MVP

### Negativas / Trade-offs
- ⚠️ **Complexidade operacional**: 6+ serviços AWS para gerenciar
  - Mitigação: IaC (Terraform/CDK) para reproducibilidade
  
- ⚠️ **Bootstrap token único**: Perda impossibilita criar primeiro ROOT user
  - Mitigação: Guardar em password manager, backup seguro
  
- ⚠️ **KMS key deletion**: 7-day deletion window, mas irreversível após
  - Mitigação: CloudTrail alerts, IAM policy para evitar deleção acidental
  
- ⚠️ **Secrets rotation failure**: Aplicação tenta usar senhas antigas
  - Mitigação: Teste mensal de rotation em staging antes de prod
  
- ⚠️ **RDS snapshot corruption**: Restauração falha
  - Mitigação: Teste semestral de restore
  
- ⚠️ **DocumentDB incompatibilidade MongoDB**: Algumas queries podem não funcionar
  - Mitigação: Testar durante desenvolvimento (planejado para ADR-006)

## Decisões Pendentes

1. **Qual serviço de computação para Master API?**
   - Opções: ECS Fargate, Lambda, App Runner, EC2
   - Impacto: Custo, latência, operação
   - Responsável: orchestrator → software-architect

2. **Qual serviço de computação para Collector Worker?**
   - Opções: ECS EC2, Lambda, EC2 standalone
   - Impacto: Escalabilidade, agendamento
   - Responsável: orchestrator → software-architect

3. **Como orquestrar Collector recorrente (15 min)?**
   - Opções: EventBridge Scheduler, SQS + Lambda, cron em EC2
   - Impacto: Custo, facilidade de operação
   - Responsável: orchestrator → software-architect

4. **ALB vs. API Gateway para Master API?**
   - ALB: Mais barato, melhor para tráfego constante
   - API Gateway: Rate limiting, cache, mais caro
   - Responsável: orchestrator → aws-architect

5. **Monitoramento centralizado desde o início?**
   - CloudWatch nativo vs. observabilidade gerenciada (futuro)
   - Responsável: orchestrator → software-architect

## Implicações para Implementação

### Master API
- Carregar secrets em runtime (aplicação) via Secrets Manager
- Não hardcodificar credenciais
- Executar dentro de VPC privada (SG: master-api)
- Rotation handler: Reconectar pools ao detectar rotação

### Collector Worker
- Idem Master API para secrets
- Executar dentro de VPC privada (SG: collector-worker)
- Output: Eventos para DocumentDB + logs para CloudWatch
- Agendamento: Externo (EventBridge ou job scheduler)

### Testes
- Teste de conectividade a RDS + DocumentDB
- Teste de secret rotation
- Teste de KMS encrypt/decrypt
- Teste de SES email sending
- Teste de restore de snapshot (anualmente)

## Referências

- ADR-003: Backend, Master API e Agente Coletor
- ADR-004: Identidade, sessões e segredos de integracao
- ADR-005: Tenancy, memberships e integracoes
- RS-001, RS-002, RS-003: Security requirements
- RF-002, RF-003, RF-005, RF-010, RF-011: Functional requirements
