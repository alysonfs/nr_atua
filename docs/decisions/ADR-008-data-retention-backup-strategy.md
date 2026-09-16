# ADR-008 - Data Retention, Backup Strategy e Disaster Recovery (5 anos)

## Status

Proposed

## Contexto

ADR-007 define infraestrutura AWS (RDS, DocumentDB, KMS) para MVP ATUA.

Requisito RS-003 especifica: "Dados de cliente e OS preservados enquanto conta ativa, removidos após 5 anos inatividade".

RDS e DocumentDB possuem backup automático de 35 dias apenas. Necessário definir:
- Estratégia de retenção longa (5 anos)
- Custo mínimo (Glacier)
- Validação periódica (testes de restore)
- Soft delete vs. hard delete (GDPR/LGPD)
- RTO/RPO por ambiente

## Decisão

### Camadas de Backup

#### Camada 1: Backup Operacional (35 dias)
**RDS PostgreSQL**
- Automated daily backups: 35 dias retention
- Backup window: 03:00-04:00 UTC (fora de pico)
- Multi-AZ: Backups replicados automaticamente
- Recovery: Point-in-time restore até 35 dias antes
- Custo: Incluso na instância RDS

**DocumentDB**
- Automatic cluster backup: 35 dias retention (incluso)
- Recovery: Point-in-time restore até 35 dias
- Custo: Incluso no cluster

**Objetivo**: Recuperação rápida de erros ou exclusões acidentais.

#### Camada 2: Backup Manual (Arquivo Mensal)
**RDS Snapshots**
- Manual snapshot: Executado pelo menos 1x/mês
- Automation: Via AWS Lambda + EventBridge (futuro) ou manual
- Retention: Mínimo 1 snapshot/mês por 60 meses (5 anos)
- Local storage: Mantido em região primária (sa-east-1)
- Custo: ~$5-8/mês (storage de 60 snapshots × 100GB)

**DocumentDB Snapshots**
- Manual snapshot: API call via AWS CLI/SDK
- Retention: Idem RDS
- Custo: Idem RDS

**Objetivo**: Snapshots históricos para auditoria e recuperação de períodos arbitrários.

#### Camada 3: Arquivo de Longa Retenção (Glacier Deep Archive)
**S3 Lifecycle Strategy**
- Copiar snapshots RDS para S3 Glacier Deep Archive após 90 dias
- Retenção: 5 anos (1825 dias)
- Storage class: S3 Glacier Deep Archive
- Lifecycle rule: Move após 90 dias, delete após 5 anos
- Encryption: SSE-S3 padrão
- Custo: ~$1-4/TB/mês (vs. S3 Standard: $23/TB/mês)

**Snapshot Export (futuro)**
- Exportar snapshots RDS para Parquet em S3 (para analytics)
- Retention: Idem snapshots
- Custo: ~$0.01 por GB exportado

**Objetivo**: Conformidade com RS-003, custo mínimo, acesso para auditoria/descoberta legal.

### Estratégia de Deleção

#### Soft Delete (Aplicação)
**Implementação em PostgreSQL**
```sql
-- Schema de auditoria
CREATE SCHEMA audit;

-- Coluna deleted_at em tabelas críticas
ALTER TABLE users ADD COLUMN deleted_at TIMESTAMP NULL;
ALTER TABLE tenants ADD COLUMN deleted_at TIMESTAMP NULL;
ALTER TABLE integrations ADD COLUMN deleted_at TIMESTAMP NULL;

-- View para queries "normais"
CREATE VIEW users_active AS
  SELECT * FROM users WHERE deleted_at IS NULL;

-- Trigger de auditoria
CREATE TABLE audit.deleted_users AS
  SELECT user_id, email, deleted_by, deleted_reason, deleted_at
    FROM users WHERE deleted_at IS NOT NULL;

-- RLS (Row Level Security) para soft delete
ALTER TABLE users ENABLE ROW LEVEL SECURITY;
CREATE POLICY users_soft_delete ON users
  FOR SELECT USING (deleted_at IS NULL);
```

**Benefícios**
- Recuperação fácil: UPDATE ... SET deleted_at = NULL
- Auditoria: Histórico completo preservado
- GDPR/LGPD: Diferença entre "removido" e "hard deleted"
- Sem perda de referential integrity (FKs)

**Timing**: Soft delete imediato (quando user solicita)

#### Hard Delete (Automation)
**Stored Procedure Agendada**
```sql
-- Executa 1x/dia (00:00 UTC)
CREATE PROCEDURE hard_delete_aged_records()
LANGUAGE plpgsql
AS $$
BEGIN
  -- Hard delete após 5 anos
  DELETE FROM audit.deleted_users
   WHERE deleted_at < CURRENT_TIMESTAMP - INTERVAL '5 years';
  
  -- Cascade: Remover related data
  DELETE FROM user_sessions
   WHERE user_id IN (SELECT user_id FROM audit.deleted_users);
  
  DELETE FROM tenant_audit_logs
   WHERE tenant_id IN (
     SELECT tenant_id FROM tenants WHERE deleted_at < CURRENT_TIMESTAMP - INTERVAL '5 years'
   );
  
  -- Log de deleção
  INSERT INTO audit.deletion_log (table_name, records_deleted, deleted_at)
  VALUES ('users', ROW_COUNT(), CURRENT_TIMESTAMP);
END;
$$;

-- Agendamento via pg_cron (extensão) ou AWS DMS
SELECT cron.schedule('hard_delete_aged_records', '0 0 * * *', 'CALL hard_delete_aged_records()');
```

**Timing**: 5 anos após soft delete

**Observação**: Hard delete é irreversível. Verificação manual antes de executar em produção.

#### DocumentDB Retention
**TTL Index (Auto-expiration)**
```javascript
// Events: Auto-delete após 5 anos
db.events.createIndex(
  { created_at: 1 },
  { expireAfterSeconds: 157680000 }  // 5 years in seconds
);

// Observations: Auto-delete após 5 anos
db.observations.createIndex(
  { created_at: 1 },
  { expireAfterSeconds: 157680000 }
);
```

**Vantagem**: Sem stored procedure, DocumentDB deleta automaticamente.

**Limitação**: TTL index não garante deleção exata no tempo; pode levar até 1 hora após expiry.

### RTO/RPO por Ambiente

| Evento | RTO (Recovery Time Objective) | RPO (Recovery Point Objective) | Método |
|--------|------|------|--------|
| Erro de aplicação (corrupção de dados) | 1 hora | 35 dias | RDS restore + Manual snapshot |
| Falha de AZ (RDS) | <1 min | Síncrono (Multi-AZ) | RDS failover automático |
| Falha de instância | 5 min | 35 dias | RDS snapshot |
| Perda de região (disaster recovery) | 24-48 horas | 90 dias | Glacier restore |
| Deleção acidental de tenant | 1 hora | 35 dias | Soft delete recovery |

### Testes de Restauração

**Mensal (Staging)**
```bash
#!/bin/bash
# 1. Selecionar snapshot recente
aws rds describe-db-snapshots --db-instance-identifier atua-prod --query 'DBSnapshots[0]'

# 2. Restore para test DB
aws rds restore-db-instance-from-db-snapshot \
  --db-instance-identifier atua-staging-restore \
  --db-snapshot-identifier <SNAPSHOT_ID>

# 3. Validar conectividade
psql -h <RESTORED_ENDPOINT> -U atua_app -d atua_prod -c "SELECT COUNT(*) FROM users;"

# 4. Limpar
aws rds delete-db-instance --db-instance-identifier atua-staging-restore
```

**Semestral (Disaster Recovery)**
- Restore de snapshot em região alternativa (us-east-1)
- Teste de failover cross-region
- Documentação de procedimento

**Anual (Compliance)**
- Restore completo de backup Glacier
- Validação de integridade dos dados
- Teste de RTO estimado
- Documentação em ADR-009 (futuro)

### Custos Estimados

#### Backup Operacional (35 dias)
- RDS storage: Incluso (~$10/mês)
- DocumentDB: Incluso
- **Total: $0**

#### Snapshots Manuais (60 snapshots × 100GB)
- S3 Standard (primeira 90 dias): ~$0.023/GB/mês × 6000GB = $138/mês
- Transição após 90 dias: Gratuita
- **Total primeiro ano: ~$70/mês (média)**
- **Total anos 2-5: ~$5-10/mês (Glacier Deep Archive)**

#### Restore (teste anual)
- Restore de snapshot: Gratuito
- Novo RDS instance (1 dia): ~$4
- **Total: ~$4/evento**

#### Totais Estimados
| Período | Custo |
|---------|-------|
| Ano 1 | $70/mês (snapshots em S3 Standard + transitions) |
| Anos 2-5 | $5-10/mês (Glacier Deep Archive) |
| Contingenciado (restore) | $4-10/ano |

**Comparação com observabilidade gerenciada**: Backup é 40x mais barato que DataDog ($500+/mês).

## Alternativas Rejeitadas

### 1. Sem Backup de Longa Retenção
❌ Viola RS-003 (5 anos retenção).
✅ Snapshots manuais + Glacier implementam RS-003.

### 2. Backup em S3 Standard Indefinidamente
❌ Custo: $23/TB/mês × 5 anos = ~$1380 (vs. Glacier: ~$50).
✅ S3 Glacier Deep Archive reduz custo 25x.

### 3. Replicação Cross-Region
❌ Custo: Data transfer $0.02/GB + replicação complexa.
✅ Snapshots em Glacier: Single region, mais barato.

### 4. Backup Manual Indefinido (sem Lifecycle)
❌ Operação manual, propenso a erros.
✅ Lifecycle automática via S3 policy.

### 5. Sem Hard Delete (retenção indefinida)
❌ GDPR/LGPD violation (dados não são removidos).
✅ Hard delete após 5 anos, soft delete imediato.

### 6. Hard Delete Imediato (sem soft delete)
❌ Impossível recuperar dados acidental/legalmente.
✅ Soft delete permite recuperação, hard delete após 5 anos.

## Motivos

1. **Conformidade**: RS-003 explicitamente requere 5 anos de retenção
2. **Custo mínimo**: Glacier Deep Archive é 25x mais barato que S3 Standard
3. **GDPR/LGPD**: Soft delete atende direito ao esquecimento, hard delete após período
4. **Auditoria**: Snapshots históricos permitem descoberta legal
5. **RTO/RPO definido**: Testes de restore garantem procedure documentado
6. **Sem operação manual**: S3 Lifecycle + TTL index automatizam deleção
7. **Recuperação rápida**: 35 dias de backup automático para erros operacionais

## Consequências

### Positivas
- ✅ 5 anos de retenção sem custo excessivo (~$5-10/mês após ano 1)
- ✅ Soft delete permite recuperação rápida
- ✅ Hard delete autentica GDPR/LGPD compliance
- ✅ Testes periódicos garantem restore viável
- ✅ Auditoria completa (deltas temporais preservados)

### Negativas / Trade-offs
- ⚠️ **Snapshots manuais**: Requer automação (Lambda + EventBridge)
  - Mitigação: Implementar no primeiro deploy
  
- ⚠️ **Restore de Glacier**: 1-5 horas (expedited) ou 12+ horas (standard)
  - Mitigação: Testes mensais para validar RTO
  
- ⚠️ **Hard delete irreversível**: Sem rollback após 5 anos
  - Mitigação: Backup final antes de hard delete
  
- ⚠️ **Soft delete overhead**: Coluna `deleted_at` em todas as tabelas
  - Mitigação: RLS automatiza; overhead mínimo (<1%)

## Implementação Sequencial

### Fase 1: MVP (Go-live)
- ✅ RDS + DocumentDB automated 35-day backup
- ✅ Soft delete schema (deleted_at coluna)
- ✅ Manual snapshot mensal (via checklist)
- ✅ TTL index em DocumentDB

### Fase 2: Stabilization (1-2 semanas após go-live)
- [ ] Lambda + EventBridge para snapshots automáticos
- [ ] S3 Glacier Lifecycle policy
- [ ] Hard delete stored procedure (agendada 5 anos adiante)
- [ ] Teste mensal de restore

### Fase 3: Compliance (3-6 meses)
- [ ] Audit trail completo (audit schema)
- [ ] GDPR/LGPD documentation
- [ ] Disaster recovery runbook
- [ ] Annual restore test schedule

## Referências

- ADR-007: AWS Infrastructure para MVP
- ADR-004: Identidade, sessões, segredos de integração
- RS-003: Data Retention Requirement
- AWS S3 Glacier: https://docs.aws.amazon.com/AmazonS3/latest/userguide/storage-class-intro.html
- AWS RDS Backups: https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_PIT.html
- PostgreSQL UNLOGGED tables (futuro): Performance vs. durability trade-off
