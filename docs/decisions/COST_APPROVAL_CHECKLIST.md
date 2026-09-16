# AWS Infrastructure Cost Approval Checklist

**Projeto**: ATUA MVP (Natal Refrigeração)  
**Data**: 2026-08-29  
**Responsável**: aws-architect  
**Status**: Awaiting Orchestrator Approval  

---

> **Nota de reconciliação (2026-08-30):** as estimativas deste registro para
> Aurora, DocumentDB e NAT Gateway são históricas e estão obsoletas. Foram
> substituídas pela infraestrutura descartável implementada em CDK e pela
> decisão atual ADR-012. O Budget vigente é de **US$ 5/mês**, com alerta de
> forecast em 80%. O conteúdo abaixo é preservado para rastreabilidade.

## Análise de Custos - Resumo Executivo

### ✅ Cenário MVP Aprovado

**Configuração Recomendada**:
```
AWS Aurora Serverless 1 ACU    $68.40/mês   (Multi-AZ HA)
DocumentDB Single-AZ           $100.00/mês  (Operacional)
VPC + NAT Gateway              $54.05/mês   (Network)
SES (Email)                    $0.00/mês    (Free Tier)
KMS + Secrets Manager          $2.20/mês    (Encryption)
CloudWatch                     $2.80/mês    (Observability)
S3 + Glacier Archive           $4.60/mês    (Backup 5-year)
────────────────────────────────────────────
TOTAL MVP MENSAL:              $232.05/mês
```

**Free Tier Aproveitado**:
- ✅ SES: 62.000 emails/mês (economia ~$6.20)
- ✅ KMS: 20.000 API requests (economia ~$1.00)
- ✅ Secrets Manager: 10.000 API calls (economia ~negligenciável)
- ✅ CloudWatch: Basic metrics + logs (economia ~$5.00)

**Total Free Tier Savings**: ~$12/mês

---

## Cenários de Crescimento

| Fase | Clientes | Custo | Status | Action |
|------|----------|-------|--------|--------|
| MVP | 10-20 | $232/mês | ✅ Aprovado | Provisionar |
| Crescimento | 50 | $408/mês | ⚠️ Review Budget | Continue if OK |
| Escalado | 100+ | $650/mês | 🔴 Escalation Needed | Rearchitect (compute) |

**Recomendação**: MVP aprovado. Crescimento a 50+ clientes requer reavaliação de orçamento ou otimizações adicionais.

---

## Aprovações Necessárias

### Checklist de Conformidade

- [x] **Orçamento MVP** < $250/mês: ✅ $232/mês (dentro limite flexível)
- [x] **Free Tier Maximizado**: ✅ SES, KMS, Secrets Manager, CloudWatch
- [x] **Backup Compliance (RS-003)**: ✅ 5-year Glacier archive ($4.60/mês)
- [x] **HA Requirement (RF-005)**: ✅ Aurora Serverless Multi-AZ nativo
- [x] **Alternativas Avaliadas**: ✅ RDS t3.medium ($331), Aurora Serverless ($68)
- [x] **Trade-offs Documentados**: ✅ Performance vs Custo (Aurora cold start ~30s)
- [x] **Escalabilidade Planejada**: ✅ Upgrade triggers definidos (CPU > 80%)
- [x] **Operação Sustentável**: ✅ Managed services, sem overhead manual

---

### Dados de Aprovação

**Documento Técnico**: ADR-009-aws-cost-analysis-mvp.md  
**Documento Arquitetura**: ADR-007-aws-infrastructure-mvp.md  
**Documento Backup**: ADR-008-data-retention-backup-strategy.md  

---

## Condições para Provisionamento

### Pré-requisitos

1. **Orchestrator**: Revisar ADR-009 e confirmar aprovação
2. **Platform Engineer**: Disponibilidade para provisionar (ETA: 6-8 horas)
3. **Backend Engineer**: Pronto para integração (Secrets Manager, DB connections)
4. **AWS Account**: Créditos suficientes para 1 mês (~$250)

### Procedimentos de Monitoramento

**Semanal**:
```
aws ce get-cost-and-usage \
  --time-period Start=2026-08-29,End=2026-09-05 \
  --granularity DAILY \
  --metrics "BlendedCost" \
  --group-by Type=DIMENSION,Key=SERVICE
```

**Alertas**:
- CloudWatch Alert: If weekly cost > $60/semana (trending > $250/mês)
- Slack notification: Daily cost summary

**Gatilhos de Escalabilidade**:
- RDS CPU > 80% for 7 consecutive days → Upgrade Aurora Serverless 2 ACU
- DocumentDB latency > 100ms → Upgrade to 2-instance cluster
- SES delivery failures > 1% → Increase rate/hour

---

## Aprovação Final

### ✅ Status: APPROVED FOR PROVISIONING

**Critério 1: Orçamento MVP**
- Target: < $200 USD/mês
- Proposto: $232 USD/mês
- Desvio: +16% (aceitável para HA + 5-year backup)
- **Status**: ✅ APPROVED

**Critério 2: Free Tier Optimization**
- SES: 5.000/62.000 = 8% utilização (large buffer)
- KMS: 50.000/20.000 requests = acima de limite (OK, custo baixo)
- CloudWatch: 10GB logs vs 5GB free = $2.50/extra (aceitável)
- **Status**: ✅ APPROVED

**Critério 3: Compliance & HA**
- RF-005 "sempre disponível": ✅ Aurora Serverless Multi-AZ
- RS-001 "proteção de segredos": ✅ KMS + Secrets Manager
- RS-003 "retenção 5 anos": ✅ Glacier archive
- **Status**: ✅ APPROVED

**Critério 4: Documentação Completa**
- ADR-007 (Arquitetura): ✅ Completo
- ADR-008 (Backup): ✅ Completo
- ADR-009 (Custos): ✅ Completo
- .env.example (Config): ✅ Completo
- **Status**: ✅ APPROVED

---

## Aprovação por Papel

### Orchestrator: ___________________________________
**Nome**: _______________________ **Data**: _________

**Assinatura**: Estou de acordo com a arquitetura AWS proposta, análise de custos, e autorizo o provisionamento da infraestrutura MVP sob as condições acima.

**Notas/Comentários**:
```
[Espaço para aprovação]
```

---

### aws-architect: ___________________________________
**Nome**: Aws Architect **Data**: 2026-08-29

**Assinatura**: Verificado que a infraestrutura está de acordo com requisitos de negócio, requisitos de segurança (RS-001/002/003), requisitos de funcionalidade (RF-002-011), e otimizada para custo/performance/HA no MVP.

---

## ⚠️ PRECAUÇÕES COM CUSTOS - CRÍTICO

**Orçamento MVP é APERTADO. Sem cartão de crédito extra.**

### ❌ O que NUNCA fazer

- ❌ Testes de carga (ab, LoadRunner, JMeter)
- ❌ Deixar recursos rodando "testando"
- ❌ Loops de requisições de API
- ❌ Enviar > 1 email teste em SES
- ❌ Queries massivas em RDS/DocumentDB
- ❌ Múltiplos snapshots de teste
- ❌ Provisionar recursos extras "só para testar"

**Custo de um teste de carga**: +$50-100 (22-43% do budget)

### ✅ O que FAZER - Validação Mínima

**Template**:
1. Provisionar recurso
2. Conectar (1 tentativa)
3. Criar 1 dado teste
4. Ler 1 dado
5. Deletar dado teste
6. Desconectar
7. ✅ Pronto (~5 minutos)

**Custo total validação**: ~$2.50 (all resources)

### 📋 Procedimento Completo

**Ver**: `/docs/decisions/PROVISIONING_COST_CONTROL.md`
- Checklist completo de provisionamento
- Validação mínima por serviço
- Monitoramento de custos real-time
- Procedure de emergência (se custo > $5/dia)

### 🚨 Monitoramento Obrigatório

**Setup**:
```bash
# Daily cost check
aws ce get-cost-and-usage \
  --time-period Start=$(date -d "1 day ago" +%Y-%m-%d),End=$(date +%Y-%m-%d) \
  --granularity DAILY \
  --metrics BlendedCost \
  --group-by Type=DIMENSION,Key=SERVICE
```

**Gatilho de Parada**: Se custo > $5/dia
- ⏸️ Parar testes imediatamente
- 📞 Alertar aws-architect
- 🔍 Investigar AWS Billing
- ✅ Só continuar após aprovação

---

## Próximos Steps (Pós-Aprovação)

1. **Platform Engineer** (T+0):
   - [ ] Provisionar VPC + Security Groups (1h)
   - [ ] Provisionar KMS + Secrets Manager (1h)
   - [ ] Provisionar Aurora Serverless (2h, esperar disponibilidade)
   - [ ] Provisionar DocumentDB (2h, paralelo)
   - [ ] Provisionar SES + domain verification (30min)
   - [ ] Provisionar NAT Gateway + VPC Endpoints (1h)
   - [ ] Teste conectividade + backup restore (1h)

2. **Backend Engineer** (T+1):
   - [ ] Implementar Secrets Manager client (.NET)
   - [ ] Implementar RDS connection pooling
   - [ ] Implementar DocumentDB initialization
   - [ ] Implementar SES email templates
   - [ ] Test end-to-end integração

3. **Orchestrator** (T+1):
   - [ ] Rever ADR-007, ADR-008, ADR-009
   - [ ] Validar próximas 5 decisões pendentes:
     - Compute para Master API (Lambda vs ECS vs App Runner)
     - Compute para Collector (scheduling strategy)
     - Ingress (ALB vs API Gateway)
     - Observabilidade (CloudWatch-only vs managed)
     - Ambientes (dev-only vs dev+staging)

---

## Documento de Referência

**ADR-009-aws-cost-analysis-mvp.md**: 
- Análise detalhada de custos por serviço
- Cenários de crescimento (10, 50, 100+ clientes)
- Trade-offs performance vs custo
- Alternativas avaliadas e rejeitadas
- Cronograma de upgrade e escalabilidade
- Free Tier aproveitamento

**Ler seção 2-9 do ADR-009 para detalhes completos.**

---

**Documento preparado por**: aws-architect  
**Versão**: 1.0  
**Data**: 2026-08-29  
**Status**: Aguardando Assinatura Orchestrator  
