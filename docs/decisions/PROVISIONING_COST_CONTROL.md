# Procedimentos de Provisionamento - Controle Rigoroso de Custos

**Projeto**: ATUA MVP  
**Data**: 2026-08-29  
**Status**: CRITICAL - Implementação obrigatória  
**Orçamento**: Apertado - SEM ESPAÇO PARA TESTES PESADOS  

---

> **Nota de reconciliação (2026-08-30):** as referências e estimativas deste
> procedimento para Aurora, DocumentDB e NAT Gateway são históricas e estão
> obsoletas. Foram substituídas pela infraestrutura descartável implementada
> em CDK e pela decisão atual ADR-012. O Budget vigente é de **US$ 5/mês**,
> com alerta de forecast em 80%. O conteúdo abaixo é preservado para
> rastreabilidade.

## ⚠️ RESTRIÇÃO CRÍTICA

**Orçamento MVP**: $232/mês é APERTADO.
- Sem cartão de crédito extra
- Sem fundo de contingência
- Sem espaço para "testes exploratórios"

**Resultado**: Qualquer teste de carga, loop de requisições, ou recurso deixado rodando pode:
- ❌ Exceder budget em horas
- ❌ Bloquear provisionamento
- ❌ Cancelar backend implementation

**Responsabilidade**: Platform Engineer + Backend Engineer

---

## 1. O que NUNCA fazer durante Provisionamento

### ❌ Testes de Carga

```bash
# ❌ NÃO FAZER NUNCA
ab -n 10000 -c 100 https://master-api.atua.internal/api/health
siege -c 100 -r 100 https://master-api.atua.internal
LoadRunner test scripts
JMeter test plans
```

**Impacto**:
- 10.000 requisições SES = $1.00 custo (dentro limite)
- Mas 1 MILHÃO de requisições = $100 (11% orçamento mensal)
- Query loops em RDS podem causar storage spike

---

### ❌ Deixar Recursos Rodando "Testando"

```bash
# ❌ NÃO FAZER NUNCA
while true; do
  aws rds describe-db-instances  # Contínuo = custos de API
  sleep 5
done

# ❌ NÃO FAZER NUNCA
docker run -d my-test-container # Esqueceu de parar = bills até notificar AWS
```

**Impacto**:
- 1 EC2 t2.micro rodando por 1 mês não previsto = $8.50
- 1 Lambda invocação/segundo por 24h = ~100k invocations = $2.00 (pequeno, mas soma)
- CloudWatch logs de teste = $0.50/GB (rapidamente)

---

### ❌ Múltiplas Requisições de Teste

```bash
# ❌ NÃO FAZER NUNCA - Enviar 1000 emails de teste
for i in {1..1000}; do
  aws ses send-templated-email \
    --to test-$i@example.com \
    --template confirmation-email
done
# Resultado: 1000 emails = $0.10 + reputação ruim de domínio

# ❌ NÃO FAZER NUNCA - Queries massivas em DocumentDB
db.orders.find({tenant_id: "test"}).limit(100000)  # PESADO

# ❌ NÃO FAZER NUNCA - Decrypt masivo em KMS
for i in {1..20000}; do
  aws kms decrypt --ciphertext-blob ...  # Chega ao limite do free tier
done
```

**Impacto**:
- SES: 1000 emails = quase limite de reputação
- DocumentDB: Queries grandes = I/O, pode exceder budget
- KMS: 20k requests/mês free tier → já está cheio

---

### ❌ Snapshots ou Backups de Teste

```bash
# ❌ NÃO FAZER NUNCA - Criar snapshots "para validar"
aws rds create-db-snapshot --db-instance-identifier atua-postgres --db-snapshot-identifier test-snap-1
aws rds create-db-snapshot --db-instance-identifier atua-postgres --db-snapshot-identifier test-snap-2
aws rds create-db-snapshot --db-instance-identifier atua-postgres --db-snapshot-identifier test-snap-3
# Resultado: 3 snapshots × 100GB = $0.69 (mínimo, mas teste)

# ✅ OK - Usar automated backup do RDS (já incluído)
```

---

### ❌ Provisionar Recursos Extras "Só para Testar"

```bash
# ❌ NÃO FAZER NUNCA
aws ec2 run-instances --instance-type t2.medium  # Para "test node"
aws rds create-db-instance --db-instance-class db.t3.small  # "Staging test"

# Resultado: Recursos duplicados não destruídos = bill contínua
```

---

## 2. O que FAZER - Validação Mínima

### ✅ Padrão Validação MVP

**Template de Teste Válido**:

```
1️⃣ Provisionar recurso
2️⃣ Conectar (1 tentativa)
3️⃣ Criar dado de teste (1 registro)
4️⃣ Ler dado de teste (1 query)
5️⃣ Deletar dado de teste
6️⃣ Desconectar
7️⃣ Destruir recurso de teste (se separado)
```

**Tempo Total**: < 5 minutos por recurso  
**Custos**: Negligenciáveis (< $0.01)

---

### ✅ RDS Aurora Serverless - Validação

```bash
# 1. Conectar
psql -h aurora-endpoint.rds.amazonaws.com \
  -U atua_app \
  -d atua_prod \
  -c "SELECT version();"  # 1 query apenas

# 2. Criar tabela teste (se schema não existe)
psql -h aurora-endpoint.rds.amazonaws.com \
  -U atua_app \
  -d atua_prod \
  -c "CREATE TABLE test_validation (id UUID, data TEXT);"

# 3. Insert 1 registro
psql -h aurora-endpoint.rds.amazonaws.com \
  -U atua_app \
  -d atua_prod \
  -c "INSERT INTO test_validation VALUES (gen_random_uuid(), 'test');"

# 4. Select 1 registro
psql -h aurora-endpoint.rds.amazonaws.com \
  -U atua_app \
  -d atua_prod \
  -c "SELECT COUNT(*) FROM test_validation;"

# 5. Cleanup
psql -h aurora-endpoint.rds.amazonaws.com \
  -U atua_app \
  -d atua_prod \
  -c "DROP TABLE test_validation;"

# Total: 5 queries, ~30 segundos, cost ~$0.00
```

**Checklist**:
- [x] SELECT version() → Aurora running
- [x] CREATE TABLE → RDS writable
- [x] INSERT → Data persistence OK
- [x] SELECT COUNT(*) → Query latency acceptable
- [x] DROP TABLE → Cleanup done

---

### ✅ DocumentDB - Validação

```bash
# 1. Connect
mongosh --username atua_app \
  --password "$MONGODB_PASSWORD" \
  --authenticationDatabase admin \
  mongodb+srv://atua-cluster.docdb.amazonaws.com

# 2. Use database
use atua_prod

# 3. Insert 1 document
db.orders.insertOne({
  _id: ObjectId(),
  tenant_id: "00000000-0000-0000-0000-000000000001",
  status: "TESTING",
  created_at: new Date(),
  created_by: "provisioning-validation"
})

# 4. Query 1 document
db.orders.findOne({status: "TESTING"})

# 5. Cleanup
db.orders.deleteOne({status: "TESTING"})

# Total: 3 operations, ~20 segundos, cost ~$0.00
```

**Checklist**:
- [x] Connect → DocumentDB running
- [x] Insert → Writable
- [x] Query → Latency OK
- [x] Delete → Cleanup done

---

### ✅ SES - Validação

```bash
# 1. Validate domain (already done via console)
aws ses get-account-sending-enabled

# 2. Send 1 test email ONLY
aws ses send-templated-email \
  --source noreply@atua.example.com \
  --destination ToAddresses=backend-engineer@atua.com \
  --template confirmation-email \
  --template-data '{"confirm_url": "https://atua.example.com/confirm/test"}'

# 3. Check delivery (wait 30 seconds)
aws ses get-account-sending-enabled

# Total: 1 email, ~1 minuto, cost $0.0001
```

**Checklist**:
- [x] SendTemplatedEmail successful
- [x] Email received in inbox
- [x] Template rendering OK

---

### ✅ KMS - Validação

```bash
# 1. Get KMS key info
aws kms describe-key --key-id alias/atua-mvp-key

# 2. Encrypt 1 string
PLAINTEXT="test-secret-value"
CIPHERTEXT=$(aws kms encrypt \
  --key-id alias/atua-mvp-key \
  --plaintext "$PLAINTEXT" \
  --query CiphertextBlob \
  --output text)

# 3. Decrypt 1 string
aws kms decrypt \
  --ciphertext-blob fileb://<(echo $CIPHERTEXT | base64 -d) \
  --query Plaintext \
  --output text | base64 -d

# Total: 2 API calls, ~10 segundos, cost $0.00
```

**Checklist**:
- [x] KMS key accessible
- [x] Encrypt/Decrypt working
- [x] Performance acceptable

---

### ✅ Secrets Manager - Validação

```bash
# 1. Get secret (already provisioned)
aws secretsmanager get-secret-value \
  --secret-id atua/root \
  --region sa-east-1

# 2. Parse JSON response
SECRET_JSON=$(aws secretsmanager get-secret-value \
  --secret-id atua/root \
  --query SecretString \
  --output text)
echo $SECRET_JSON | jq .

# Total: 2 API calls, ~5 segundos, cost ~$0.00
```

**Checklist**:
- [x] Secret accessible
- [x] JSON valid
- [x] Field names correct

---

### ✅ VPC Connectivity - Validação

```bash
# 1. Test RDS from Master API (security group OK)
docker run --rm \
  --network vpc-internal \
  postgres:14 \
  psql -h aurora-endpoint.rds.amazonaws.com \
       -U atua_app \
       -d atua_prod \
       -c "SELECT 1;"

# 2. Test DocumentDB from Collector (security group OK)
docker run --rm \
  --network vpc-internal \
  mongo \
  mongosh mongodb+srv://atua-cluster.docdb.amazonaws.com/atua_prod

# Total: 2 connections, ~30 segundos, cost ~$0.00
```

**Checklist**:
- [x] RDS security group allows Master API
- [x] DocumentDB security group allows Collector
- [x] Network latency acceptable

---

## 3. Checklist de Provisionamento Seguro

### Pré-Provisionamento

- [ ] AWS Billing console aberto (monitorar em tempo real)
- [ ] Slack alert configurado para custos > $10/hora
- [ ] Backend engineer revisor (alguém acompanhando)
- [ ] Todos procedimentos abaixo lidos e entendidos

### Durante Provisionamento

**VPC & Network (Esperado: < 2 minutos)**
```
Criar VPC 10.0.0.0/16
Criar 4 subnets
Criar NAT Gateway (começará a costar)
├─ ⏱️ 5min: NAT em "Pending"
├─ ⏱️ 10min: NAT "Available"
├─ ✅ Test: 1 EC2 no private subnet, ping 8.8.8.8 (via NAT)
├─ ✅ Cleanup: Destruir EC2 teste imediatamente
└─ ✅ Cost: ~$0.01 NAT, ~$0.00 data transfer (100 bytes ping)
```

**KMS (Esperado: 1 minuto)**
```
Criar CMK
Criar alias atua-mvp-key
├─ ✅ Test: 1 encrypt + 1 decrypt (2 API calls)
└─ ✅ Cost: ~$0.00
```

**Secrets Manager (Esperado: 2 minutos)**
```
Criar 3 secrets
  ├─ atua/root
  ├─ atua/rds-postgres
  └─ atua/documentdb
├─ ✅ Test: 1 GetSecretValue per secret (3 API calls)
└─ ✅ Cost: $1.20 (storage)
```

**RDS Aurora Serverless (Esperado: 10-15 minutos)**
```
Criar Aurora cluster
├─ ⏱️ 5min: "Creating"
├─ ⏱️ 10min: "Available"
├─ ✅ Test: 1 query via psql (5 total queries)
├─ ✅ Cleanup: No tables to drop (schema new)
└─ ✅ Cost: ~$0.04 (proportional time running)
```

**DocumentDB (Esperado: 10-15 minutos)**
```
Criar DocumentDB cluster
├─ ⏱️ 5min: "Creating"
├─ ⏱️ 10min: "Available"
├─ ✅ Test: 1 insert + 1 query + 1 delete
├─ ✅ Cleanup: Done (1 doc deleted)
└─ ✅ Cost: ~$0.04 (proportional time running)
```

**SES (Esperado: 2 minutos)**
```
Criar domain identity
├─ ⏱️ Instant: Identity created
├─ ✅ Test: 1 email enviado
├─ ✅ Verify: Email recebido (30 sec)
└─ ✅ Cost: $0.0001 (1 email)
```

### Pós-Provisionamento

- [ ] Cleanup: Todos recursos de teste DESTRUÍDOS
- [ ] Verify: Nenhum recurso extra em "Pending" state
- [ ] Cost Review: AWS Billing dashboard < $1.00 total
- [ ] Documentation: Tomar screenshots de configuração final

---

## 4. Monitoramento de Custos - Real-Time

### Setup Imediato (Dia 0)

```bash
# Criar alarme CloudWatch
aws cloudwatch put-metric-alarm \
  --alarm-name "atua-mvp-daily-cost-alert" \
  --alarm-description "Alert if daily cost > $15" \
  --metric-name EstimatedCharges \
  --namespace AWS/Billing \
  --statistic Average \
  --period 300 \
  --threshold 15 \
  --comparison-operator GreaterThanThreshold \
  --alarm-actions arn:aws:sns:sa-east-1:ACCOUNT_ID:alerts

# Criar SNS topic para emails
aws sns create-topic --name atua-mvp-cost-alerts
aws sns subscribe \
  --topic-arn arn:aws:sns:sa-east-1:ACCOUNT_ID:atua-mvp-cost-alerts \
  --protocol email \
  --notification-endpoint your-email@atua.com
```

### Daily Review (Cada manhã)

```bash
# Check last 24 hours cost
aws ce get-cost-and-usage \
  --time-period Start=$(date -d "1 day ago" +%Y-%m-%d),End=$(date +%Y-%m-%d) \
  --granularity DAILY \
  --metrics BlendedCost \
  --group-by Type=DIMENSION,Key=SERVICE \
  --output table
```

**Expected Output**:
```
┌──────────────────┬──────────┐
│ SERVICE          │ COST     │
├──────────────────┼──────────┤
│ RDS              │ $2.28    │
│ DocumentDB       │ $3.33    │
│ NAT Gateway      │ $1.07    │
│ Other            │ $0.42    │
├──────────────────┼──────────┤
│ TOTAL            │ $7.10    │
└──────────────────┴──────────┘
```

**Ação se > $10/dia**:
1. Verificar Console se algo rodando
2. Parar qualquer teste
3. Alert ao aws-architect

---

## 5. Custo de Provisionamento Estimado

### Esperado (Com procedimentos corretos)

```
VPC + Subnets + SGs:         $0.00  (free)
NAT Gateway (1 hora):        $1.07  (hourly + data)
KMS CMK (1 mês proporcional): $0.02  (1 dia de $1.00)
Secrets Manager (3 secrets):  $1.20  (storage + API calls)
RDS Aurora (1 hora):         $0.09  (horário de $68/mês)
DocumentDB (1 hora):         $0.14  (horário de $100/mês)
SES (1 email):               $0.0001
CloudWatch (logs):           $0.05  (mínimo)
────────────────────────────────────
TOTAL PROVISIONAMENTO:       $2.57
```

**Status**: ✅ Bem dentro de orçamento

---

### Cenário Ruim (SEM Procedimentos)

```
Teste de carga SES:          +$50.00 (500k emails)
Teste de carga RDS:          +$10.00 (storage spike)
Lambda test invocations:     +$2.00 (200k invocations)
EC2 test instance left on:   +$8.50 (24h)
CloudWatch logs de teste:    +$5.00 (500GB)
Snapshots de teste:          +$2.00 (20 snapshots)
────────────────────────────────────
TOTAL SE NÃO SEGUIR:         +$77.50
```

**Status**: ❌ 34% do orçamento mensal em 1 dia

---

## 6. Procedimento de Emergência

**Se custo subir acima de $5/dia**:

1. ⏸️ **Pause Immediately**
   - Para todos os testes
   - Avisa via Slack #alerts
   - Não faz mais nada

2. 🔍 **Investigate** (15 minutos)
   - AWS Billing → Last 24 hours
   - AWS Cost Explorer → By service
   - Identificar culprit

3. ❌ **Kill** (5 minutos)
   ```bash
   # Exemplo: SES enviando emails
   aws ses put-account-suppression-attributes \
     --suppression-attributes Reason=COMPLAINT
   
   # Exemplo: RDS rodando queries
   aws rds stop-db-instance --db-instance-identifier atua-postgres
   ```

4. 📞 **Escalate** (immediately)
   - Informar aws-architect + orchestrator
   - Não provisionar mais
   - Aguardar aprovação para continuar

5. ✅ **Cleanup** (1 hora)
   - Destruir recursos teste
   - Verificar cost trend
   - Só then reconhecer

---

## 7. Aprovação & Signature

### Platform Engineer

**Eu li e entendi as restrições de custo e concordo em:**

1. ✅ Não fazer testes de carga
2. ✅ Não deixar recursos rodando
3. ✅ Não fazer requisições em loop
4. ✅ Usar validação mínima (1 insert, 1 query, 1 delete)
5. ✅ Destruir recursos teste imediatamente
6. ✅ Monitorar custos diários
7. ✅ Pausar e escalar se custo > $5/dia

**Assinado**: ______________________ **Data**: _________

---

### Backend Engineer

**Eu li e entendo que:**

1. ✅ Não posso rodar testes de performance MVP
2. ✅ Não posso fazer integração testes com dados massivos
3. ✅ Devo usar dados de teste mínimos (< 100 records)
4. ✅ Devo limpar dados teste após validação
5. ✅ Sou responsável monitorar API logs para custos inesperados

**Assinado**: ______________________ **Data**: _________

---

### aws-architect

**Responsabilidades pós-provisionamento:**

1. ✅ Revisar AWS Billing diariamente (primeiro 7 dias)
2. ✅ Atualizar ADR-009 com custos reais vs estimado
3. ✅ Trigger alertas se custo trending acima $300/mês
4. ✅ Recomendar otimizações se budget excedido

**Assinado**: ______________________ **Data**: 2026-08-29

---

## 8. Checklists Rápidas

### ✅ Pré-Provisionamento (5 min)
- [ ] Ler este documento
- [ ] AWS Billing console aberto em outro tab
- [ ] Slack alerts ativado
- [ ] Backup de credentials guardado
- [ ] Nenhum teste planejado

### ✅ Provisionamento VPC (10 min)
- [ ] VPC criada
- [ ] 4 subnets criadas
- [ ] NAT Gateway criada
- [ ] 1 teste de conectividade
- [ ] EC2 teste destruído

### ✅ Provisionamento KMS+Secrets (5 min)
- [ ] 1 CMK criado
- [ ] 3 secrets criados
- [ ] 1 encrypt+decrypt test passed
- [ ] 1 GetSecretValue per secret test passed

### ✅ Provisionamento RDS (20 min)
- [ ] Aurora Serverless 1 ACU criado
- [ ] Aguardar "Available" status
- [ ] 5 queries de validação executadas
- [ ] Conectividade OK

### ✅ Provisionamento DocumentDB (20 min)
- [ ] Cluster criado
- [ ] Aguardar "Available" status
- [ ] 3 operações de validação (insert/query/delete)
- [ ] Conectividade OK

### ✅ Provisionamento SES (5 min)
- [ ] Domain verificado
- [ ] Templates criados
- [ ] 1 email teste enviado
- [ ] Email recebido com sucesso

### ✅ Pós-Provisionamento (10 min)
- [ ] Cleanup de todos recursos teste
- [ ] AWS Billing < $3.00 total
- [ ] Nenhum recurso em "Pending" state
- [ ] Screenshots documentação guardadas

---

## Referências

- ADR-009: AWS Cost Analysis MVP
- COST_APPROVAL_CHECKLIST: Aprovação de orçamento
- AWS Pricing: https://aws.amazon.com/pricing/

---

**Documento preparado por**: aws-architect  
**Data**: 2026-08-29  
**Versão**: 1.0  
**Status**: CRÍTICO - Implementação obrigatória antes de provisionamento
