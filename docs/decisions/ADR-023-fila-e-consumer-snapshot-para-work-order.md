# ADR-023 - Fila e Consumer: Snapshot → work_order / work_order_history

## Status

Accepted

## Contexto

RF-016 e RF-017 redesenharam o modelo de persistência de Ordens de Serviço
do ATUA. O redesenho separou responsabilidades em três entidades:

- `work_order_snapshots` (MongoDB Atlas): dado bruto append-only, produzido
  exclusivamente pelo Worker Coletor a cada ciclo de coleta.
- `work_order` (PostgreSQL): estado atual de cada OS por tenant, mantido por
  upsert.
- `work_order_history` (PostgreSQL): histórico append-only de mudanças de
  status por OS por tenant.

A DP-016.2 (resolvida pelo produto em 2026-08-31) estabeleceu que um
**consumer de fila separado** é responsável por ler cada novo snapshot e
realizar o upsert em `work_order` + append condicional em
`work_order_history`. O Worker Coletor apenas insere o snapshot no MongoDB e
publica uma notificação na fila; ele não acessa PostgreSQL diretamente.

Esta ADR define a arquitetura desse mecanismo de fila + consumer, respondendo
às questões deixadas em aberto pela DP-016.2:

1. Tecnologia da fila.
2. Onde roda o consumer (topologia de deploy).
3. Contrato de dados e extração de campos do rawData.
4. Tratamento de falhas e reprocessamento.
5. Isolamento multi-tenant no consumer.

### Premissas de produto (não são decisões desta ADR)

As seguintes decisões foram tomadas pelo produto e são tratadas aqui apenas
como contexto:

- Status armazenado como string crua do provedor, sem enum, sem catálogo, sem
  validação de transição (DP-017.2 resolvida; DP-017.4 adiada pós-MVP).
- `work_order` e `work_order_history` ficam no PostgreSQL.
- `work_order_snapshots` fica no MongoDB Atlas (ADR-012).
- O Worker Coletor não acessa PostgreSQL de `work_order`/`work_order_history`
  diretamente (separação de responsabilidade definida em DP-016.2).
- `work_order_history` recebe entrada somente quando o status muda (DP-017.1
  resolvida).
- Idempotência de re-inserção via índice único `(tenant_id, provider_id,
  command_id)` em `work_order_snapshots` (DP-016.1 resolvida).

### Restrições de infraestrutura

- Orçamento: ~US$5/mês, atualmente ~US$2,20 fixos
  (RDS Postgres t3.micro + CMK + Secrets Manager).
- Infraestrutura existente: 2 EC2 t3.micro em sa-east-1; um roda a Master API
  (ASP.NET Core), outro roda o Worker Coletor (.NET Worker Service).
- MongoDB Atlas Free Tier (ADR-012): já em uso pelo Worker Coletor.
- Nenhum serviço de fila gerenciado provisionado até o momento.

---

## Decisão

### 1. Tecnologia da fila: MongoDB Change Streams (sem fila externa)

O mecanismo de notificação entre o Worker Coletor (produtor) e o consumer
(processador) será implementado via **MongoDB Change Streams** sobre a coleção
`work_order_snapshots`.

O consumer abre um Change Stream que monitora operações `insert` na coleção.
A cada novo documento inserido pelo Worker, o Change Stream entrega o evento
ao consumer, que então processa o snapshot (upsert + history) no PostgreSQL.

**Não será adicionada nenhuma fila externa** (SQS, RabbitMQ, Redis Streams,
etc.) neste momento.

#### Justificativa

| Critério              | MongoDB Change Streams                           | SQS                                              |
|-----------------------|--------------------------------------------------|--------------------------------------------------|
| Custo adicional       | $0 (já incluso no Atlas Free Tier)               | ~$0,40/mês (1M req free; volume MVP <1M), mas adiciona dependência e complexidade operacional |
| Infraestrutura nova   | Nenhuma                                          | Novo serviço AWS a provisionar, monitorar e pagar |
| Integração com código | Driver MongoDB C# já presente no Worker           | SDK AWS SQS novo no consumer                     |
| Reprocessamento       | Resume token (cursor persistido) permite retomar do ponto de falha | Visibility timeout + DLQ nativo |
| Ordenação             | Garantida por `_id` (UUIDv7 append-only)         | FIFO disponível, mas com custo adicional          |
| Operação              | Zero configuração extra                          | Fila, DLQ, políticas IAM, monitoramento adicional|
| Risco de lock-in      | Já dependemos do Atlas; não acrescenta           | Adiciona dependência AWS além das já existentes  |

O volume do MVP (poucos tenants, coletas pontuais) não justifica a
infraestrutura de uma fila gerenciada. O Atlas Free Tier já oferece Change
Streams com suporte a resume tokens, que é o primitivo suficiente para
garantir at-least-once delivery sem duplicação observável no destino
(idempotência tratada no consumer — ver seção 4).

**Alternativas a SQS descartadas por razões análogas:**
- **RabbitMQ/Redis Streams em EC2 extra**: adiciona uma terceira EC2 ou
  serviço gerenciado, aumentando custo e complexidade operacional além do
  orçamento disponível.
- **Polling com lock otimista sobre MongoDB**: funcional, mas mais verboso e
  sem a conveniência do push nativo; Change Streams é a forma canônica de
  reagir a escritas no MongoDB.
- **Outbox pattern com Postgres**: inverteria o fluxo (Worker precisaria
  acessar Postgres), violando a separação de responsabilidade definida em
  DP-016.2.

---

### 2. Localização do consumer: parte do Worker Coletor existente

O consumer de Change Streams rodará **como um `IHostedService` adicional
dentro do Worker Coletor** (`apps/collector/Atua.Collector`), no EC2 t3.micro
já dedicado a ele.

#### Justificativa

| Opção                            | Vantagem                              | Problema                                                        |
|----------------------------------|---------------------------------------|-----------------------------------------------------------------|
| Hosted service no Worker Coletor | Zero EC2 adicional; mesmo processo já conectado ao MongoDB | Worker e consumer co-habitam o mesmo processo |
| Hosted service na Master API     | Sem EC2 adicional                     | API não deve ter acesso direto ao MongoDB de snapshots (separação ADR-003); acoplamento indesejado |
| Novo serviço / nova EC2          | Separação máxima                      | Novo EC2 t3.micro ~$8,50/mês — inviável no orçamento           |
| AWS Lambda                       | Serverless, paga por uso              | LAMBDA_VIABILITY_ANALYSIS.md já analisou e rejeitou para o projeto; + custo de SQS como trigger |

O Worker Coletor já é o componente que produz snapshots e já tem conexão
MongoDB estabelecida (ADR-012). Adicionar um `IHostedService` de consumer ao
mesmo processo reutiliza a conexão existente e mantém o princípio de "menor
infraestrutura nova possível".

**Risco de co-habitação:** o consumer acessa PostgreSQL (para upsert em
`work_order`/`work_order_history`), enquanto o Worker de coleta não acessa
PostgreSQL. Isso significa que `apps/collector` passará a ter uma segunda
string de conexão — para o PostgreSQL — usada exclusivamente pelo consumer.

Este risco é aceito porque:
- O isolamento lógico entre "loop de coleta" e "consumer de snapshots" é
  mantido em código (dois `IHostedService` separados, sem dependência direta
  entre si).
- A alternativa de criar um novo serviço excederia o orçamento.
- No futuro, se o consumer crescer em complexidade, ele pode ser extraído para
  um processo independente sem impacto na arquitetura do Worker Coletor.

---

### 3. Contrato de dados e extração de campos do rawData

#### 3.1 Schema do documento `work_order_snapshots`

Conforme RF-016:

```
{
  id:          UUID (UUIDv7, gerado pelo Worker)
  tenant_id:   UUID
  provider_id: string  -- workOrderId do iService para o MVP
  command_id:  UUID
  rawdata:     object  -- payload bruto do provedor, sem schema formalizado
  created_at:  timestamp UTC
}
```

Índice único: `(tenant_id, provider_id, command_id)` — garante idempotência
de re-inserção (DP-016.1).

#### 3.2 Extração de `status` e outros campos do rawData

O consumer precisa extrair pelo menos o `status` do rawData para decidir se
deve inserir em `work_order_history` (RF-017.2) e qual valor persistir
(RF-017.3).

**Decisão:** o consumer utilizará um **SnapshotAdapter por provedor** — uma
interface leve com implementação por provedor — responsável por extrair campos
estruturados do rawData. Para o MVP, existe apenas o adaptador para iService.

```
interface ISnapshotAdapter {
    string? ExtractStatus(JsonDocument rawdata);
    // Campos adicionais podem ser acrescentados conforme necessário.
}

class IServiceSnapshotAdapter : ISnapshotAdapter {
    // Extrai o campo "woStatus" do payload do iService (resolvido em DP-023.1).
}
```

O `provider_id` já vem estruturado no documento (campo de primeiro nível,
mapeado pelo Worker conforme RF-016.6); o consumer não precisa extraí-lo do
rawData.

**Seleção do adaptador:** o consumer seleciona o adaptador correto com base no
campo `provider_type`, adicionado ao documento `work_order_snapshots`
(resolvido em DP-023.2). O Worker grava esse campo no momento da coleta
(mesmo ponto onde já grava `provider_id`); o consumer faz um lookup direto
(`provider_type` → `ISnapshotAdapter`) sem depender de configuração externa
ou de estado do tenant.

#### 3.3 Schema relacional: `work_order` e `work_order_history`

Conforme RF-017:

**Tabela `work_order`** (PostgreSQL):

```sql
CREATE TABLE work_order (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   UUID NOT NULL,
    provider_id VARCHAR NOT NULL,
    status      VARCHAR NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_work_order_tenant_provider UNIQUE (tenant_id, provider_id)
);
CREATE INDEX ix_work_order_tenant ON work_order (tenant_id);
```

**Tabela `work_order_history`** (PostgreSQL):

```sql
CREATE TABLE work_order_history (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    work_order_id           UUID NOT NULL REFERENCES work_order(id),
    work_order_snapshot_id  UUID NOT NULL,  -- referência ao _id do MongoDB
    tenant_id               UUID NOT NULL,
    provider_id             VARCHAR NOT NULL,
    status                  VARCHAR NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_work_order_history_work_order ON work_order_history (work_order_id);
CREATE INDEX ix_work_order_history_tenant ON work_order_history (tenant_id);
```

> **Nota:** `work_order_snapshot_id` é um UUID armazenado como campo de
> referência, mas **não é uma FK com constraint de banco** — a FK cruzaria
> bancos diferentes (PostgreSQL ↔ MongoDB), o que não é suportado. A
> rastreabilidade é mantida por convenção de aplicação.

---

### 4. Falhas e reprocessamento

#### 4.1 Resume token (at-least-once delivery)

O consumer persiste o **resume token** do Change Stream após cada processamento
bem-sucedido. Em caso de reinício do processo (crash, deploy, OOM), o consumer
retoma o Change Stream a partir do último token persistido, garantindo que
nenhum evento seja perdido.

**Onde persistir o resume token:** em uma coleção MongoDB dedicada
(`work_order_snapshots_consumer_state`) ou em uma tabela PostgreSQL simples
(`consumer_state`). A escolha preferida é **PostgreSQL** (tabela simples), pois
o consumer já terá conexão aberta ao Postgres; evita criar uma coleção MongoDB
extra para metadados operacionais.

```sql
CREATE TABLE consumer_state (
    consumer_id  VARCHAR PRIMARY KEY,
    resume_token JSONB,
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

O consumer persiste o resume token **na mesma transação** que o upsert em
`work_order` + insert em `work_order_history`. Isso garante que o token
avance somente se a escrita no Postgres for confirmada — sem risco de "perder"
um evento que foi marcado como processado mas falhou na escrita relacional.

#### 4.2 Idempotência do consumer

O consumer pode receber o mesmo snapshot mais de uma vez (ex.: resume token
persistido antes do commit da transação em caso de crash antes do commit).

A idempotência é garantida pela chave única `(tenant_id, provider_id)` em
`work_order` (upsert via `INSERT ... ON CONFLICT DO UPDATE`) e pela lógica
de comparação de status antes de inserir em `work_order_history`:

- Upsert em `work_order`: idempotente por design (ON CONFLICT DO UPDATE).
- Insert em `work_order_history`: precedido por leitura do status atual em
  `work_order`; só insere se o status mudou. Reprocessar o mesmo snapshot com
  o mesmo status não gera entrada duplicada no histórico.

#### 4.3 Falha persistente (Postgres indisponível)

Se o PostgreSQL estiver indisponível:

- O consumer não avança o resume token.
- O Change Stream permanece pausado (consumer em retry com backoff
  exponencial, configurável).
- O Worker Coletor continua inserindo snapshots normalmente no MongoDB; não
  há perda de dados brutos.
- Quando o Postgres voltar, o consumer retoma do resume token e processa os
  snapshots acumulados em ordem.

O Change Stream do MongoDB Atlas Free Tier mantém o oplog por pelo menos
24 horas. Para o volume do MVP, esse janela é mais que suficiente para
absorver qualquer indisponibilidade temporária do Postgres.

#### 4.4 Descarte com log (RF-017.5)

Se o consumer não conseguir extrair o campo de status do rawData (campo
ausente ou vazio), o snapshot é **descartado sem inserção** em `work_order`
ou `work_order_history`. O descarte é registrado em log `Warning` com
`snapshot_id`, `tenant_id` e `command_id`. O resume token avança normalmente
(o snapshot foi "processado", mesmo que sem escrita).

---

### 5. Isolamento multi-tenant no consumer

O isolamento é garantido por múltiplos mecanismos em camadas:

1. **Chave de identidade com `tenant_id`:** toda leitura e escrita no
   PostgreSQL inclui `tenant_id` nas condições WHERE e nos valores inseridos.
   A restrição única `(tenant_id, provider_id)` em `work_order` garante que
   dois tenants distintos com o mesmo `provider_id` produzem registros
   independentes (RF-017.6).

2. **Sem compartilhamento de estado em memória entre tenants:** o consumer
   processa snapshots sequencialmente (ou com paralelismo limitado por worker,
   se implementado futuramente); não há cache ou estado em memória que misture
   dados de tenants diferentes.

3. **Dados de sessão e credenciais:** o consumer não acessa credenciais do
   iService nem dados de sessão CAS — ele processa apenas o snapshot já
   gravado no MongoDB, que por RF-016.7 não contém credenciais.

---

## Fluxo ponta a ponta

```
Worker Coletor (EC2 t3.micro)
  |
  | 1. Coleta OS do iService via Playwright
  | 2. Monta documento work_order_snapshots
  |    (id, tenant_id, provider_id, command_id, rawdata, created_at)
  | 3. Insert MongoDB Atlas → work_order_snapshots
  |    └─ Índice único (tenant_id, provider_id, command_id) previne duplicata
  |
  └──────────────────────────────────────────────────────────────────────┐
                                                                         │
                                                    MongoDB Change Stream │
                                                    (insert em           │
                                                     work_order_snapshots│
                                                     detectado)          │
                                                                         │
Consumer (IHostedService no mesmo processo do Worker)                    │
  |                                                                       │
  | 4. Recebe evento do Change Stream ◄──────────────────────────────────┘
  | 5. Seleciona SnapshotAdapter pelo campo provider_type do documento
  |    (DP-023.2)
  | 6. Extrai status = adapter.ExtractStatus(rawdata)  → campo "woStatus"
  |    └─ status ausente/vazio → log Warning, avança token, finaliza
  |
  | 7. Abre transação PostgreSQL:
  |    a. UPSERT work_order (tenant_id, provider_id, status, updated_at)
  |       → se nova OS: insere com created_at = now()
  |       → se OS existente com status diferente: atualiza status + updated_at
  |       → se OS existente com mesmo status: atualiza apenas updated_at
  |    b. Lê status anterior (resultado do UPSERT ou SELECT anterior)
  |       → se status mudou (ou OS é nova): INSERT work_order_history
  |         (work_order_id, work_order_snapshot_id, tenant_id, provider_id,
  |          status, created_at)
  |       → se status igual: nenhum insert em work_order_history
  |    c. UPDATE consumer_state (resume_token = token_atual)
  |    d. COMMIT
  |
  | 8. Em caso de erro:
  |    → ROLLBACK (token não avança)
  |    → Backoff exponencial + retry
  |    → Worker Coletor continua operando normalmente
```

---

## Impactos em RF-016 e RF-017

### RF-016

Nenhuma alteração textual necessária. Esta ADR complementa RF-016 definindo a
arquitetura do consumer que consome os snapshots ali especificados.

**Ponto de atenção para o product-analyst/orchestrator:** RF-016 menciona
"publica uma mensagem (evento de novo snapshot) em uma fila" na DP-016.2
resolvida. Com a escolha de Change Streams (sem fila externa), o Worker
**não publica explicitamente em nenhuma fila** — ele apenas insere no MongoDB
e o Change Stream entrega o evento ao consumer automaticamente. O texto da
DP-016.2 pode ser ajustado para refletir isso, mas a intenção de
desacoplamento permanece intacta. **Esta edição é escopo do product-analyst.**

### RF-017

Nenhuma alteração textual necessária. Esta ADR concretiza a arquitetura do
consumer descrito em RF-017 (DP-016.2 como dependência) e define o schema
relacional das tabelas `work_order` e `work_order_history`.

---

## Decisões pendentes (DPs em aberto)

Nenhuma. As duas DPs identificadas nesta ADR foram resolvidas pelo usuário em
2026-08-31 — ver seção "Decisões resolvidas" abaixo.

## Decisões resolvidas

### DP-023.1 — Nome do campo de status no payload do iService

**Situação:** o `IServiceSnapshotAdapter` precisava do nome exato do campo de
status no JSON retornado pelo iService.

**Decisão do usuário (2026-08-31):** o campo é `woStatus`.

### DP-023.2 — Mecanismo de seleção do SnapshotAdapter por provedor

**Situação:** o consumer precisa saber qual `ISnapshotAdapter` usar para cada
snapshot.

**Opções identificadas:**
- (a) Campo `provider_type` adicionado ao documento `work_order_snapshots`
  (exige alteração em RF-016).
- (b) Lookup por `tenant_id` → configuração de integração no PostgreSQL
  (o consumer consulta qual provedor está associado ao tenant).
- (c) Hard-coded para iService no MVP, com TODO para generalizar.

**Decisão do usuário (2026-08-31):** opção (a) — campo `provider_type`
adicionado ao documento `work_order_snapshots`. **Requer alteração em RF-016**
(escopo do `product-analyst`/`orchestrator`) para incluir o campo no contrato
do documento.

---

## Motivos

- **Change Streams** eliminam a necessidade de uma fila externa (SQS,
  RabbitMQ), mantendo o custo dentro do orçamento (~US$0 adicional).
- **Consumer no Worker Coletor** evita nova EC2 (~US$8,50/mês) e reutiliza a
  conexão MongoDB já existente.
- **Resume token no PostgreSQL** (mesma transação que os upserts) garante
  consistência entre "o que foi processado" e "o que foi marcado como
  processado", sem depender de atomicidade cruzada entre dois bancos.
- **Idempotência no consumer** (ON CONFLICT + comparação de status) torna o
  sistema seguro para reprocessamento sem duplicação de histórico.
- **SnapshotAdapter por provedor** isola o conhecimento sobre o schema do
  rawData em um único componente — mesma filosofia do `WorkOrderMapper`
  preservado em RF-016.6.

## Alternativas consideradas

### AWS SQS como fila

Custo adicional mínimo (~US$0,40/mês no volume MVP), mas acrescenta um novo
serviço AWS a provisionar, monitorar e pagar. Nenhuma vantagem funcional
relevante em relação a Change Streams para o volume e confiabilidade
esperados no MVP. Rejeitada por complexidade operacional desproporcional.

### Polling periódico sobre `work_order_snapshots`

O consumer consultaria o MongoDB periodicamente buscando snapshots não
processados (ex.: com campo `processed = false`). Funcional, mas exige
campo adicional em RF-016, lógica de locking para evitar processamento
concorrente e introduz latência artificial. Change Streams é a abordagem
canônica e mais simples para reagir a inserções no MongoDB.

### Consumer na Master API

Violaria a separação entre "API pública" e "processamento interno de
snapshots" (ADR-003). A Master API não deve conhecer o schema de
`work_order_snapshots` nem ter acesso direto à coleção MongoDB de snapshots.
Rejeitada.

### Novo serviço / nova EC2

Solução arquiteturalmente mais limpa (separação máxima), mas inviável no
orçamento atual (~US$8,50/mês por EC2 t3.micro). Pode ser reconsiderada
quando o volume justificar o custo.

## Consequências

**Positivas:**
- Nenhuma nova infraestrutura AWS necessária.
- Custo adicional: $0 (dentro do Atlas Free Tier).
- Worker Coletor passa a ser também o consumer de snapshots — dois
  `IHostedService` no mesmo processo, com responsabilidades distintas.
- Reprocessamento seguro via resume token + idempotência no consumer.
- Isolamento multi-tenant garantido por `tenant_id` em todas as operações.

**Negativas / trade-offs:**
- O Worker Coletor adquire uma segunda conexão de banco (PostgreSQL) via o
  consumer, aumentando levemente o acoplamento do processo.
- Change Streams requerem que o MongoDB esteja em modo replica set (Atlas Free
  Tier já está em 3-node replica — sem custo adicional).
- O oplog do Atlas Free Tier tem janela de retenção limitada; interrupções
  longas do consumer (>24h) podem exigir reprocessamento manual. Aceitável
  para o MVP.
- DPs 023.1 e 023.2 bloqueiam a implementação do adapter; precisam ser
  resolvidas antes do início da implementação do consumer.

## Agentes envolvidos

- Usuario/product-analyst: decisões de produto em RF-016 e RF-017 (contexto).
- software-architect: definição desta arquitetura.
- backend-engineer: implementação do consumer, adapter e schema PostgreSQL.
- aws-architect: sem impacto de infraestrutura adicional; ciente de que o
  Worker Coletor passa a usar PostgreSQL.

## Data

2026-08-31

## Substitui

Nenhum ADR anterior. Complementa ADR-003 (define consumer de snapshots como
parte do Worker Coletor), ADR-012 (usa Change Streams do Atlas já adotado) e
ADR-021 (acrescenta consumer ao Worker Coletor do RF-009).
