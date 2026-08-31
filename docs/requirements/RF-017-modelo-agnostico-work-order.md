# RF-017 - Modelo Agnóstico de OS: work_order e work_order_history

Status: `Especificado`

**Data:** 2026-08-31

## Contexto e motivação

Este requisito complementa RF-016 e substitui as partes de RF-010 e RF-011
referentes ao histórico de estados de OS.

O modelo anterior acumulava "observações" com o payload bruto do provedor e
derivava a "transição de status" por comparação entre observações consecutivas.
O novo modelo separa responsabilidades:

- **RF-016** guarda o dado bruto, sem mapeamento.
- **RF-017** (este documento) define a camada de status agnóstico de provedor:
  um registro atual por OS (`work_order`) e um histórico append-only de status
  mapeados (`work_order_history`).

A separação é intencional: quando houver mais provedores conhecidos, o mapeamento
de status poderá ser ajustado sem alterar a coleção de dados brutos.

## Objetivo

Definir as entidades `work_order` e `work_order_history`, que mantêm o estado
atual e o histórico de status de cada OS em termos agnósticos de provedor,
alimentadas a partir dos snapshots brutos definidos em RF-016.

## Usuário / Ator

- **Worker Coletor** (ou processo separado — ver DP-016.2): produtor de registros
  em `work_order` e `work_order_history`.
- **Office / API de consulta**: consumidor futuro desses dados para exibição.

## Escopo

RF-017 cobre as entidades `work_order` e `work_order_history`, o enum de status
agnóstico de provedor, e as regras de manutenção dessas entidades a partir dos
snapshots. Não cobre o dado bruto (`work_order_snapshots` — RF-016) nem a
camada de exibição no Office (RF futuro).

### O que É RF-017

- Definição das entidades `work_order` e `work_order_history`.
- Definição do enum de status agnóstico de provedor.
- Regras de upsert em `work_order` e append em `work_order_history` quando
  um novo snapshot é processado.
- Rastreabilidade entre snapshot bruto e registro de histórico.

### O que NÃO é RF-017

- Dado bruto do provedor (RF-016).
- Mapeamento detalhado de campos do rawData (trabalho futuro).
- Regras de transição de estados (o sistema registra o status observado; não
  valida se a transição é permitida — isso é trabalho futuro).
- Exibição de OS ou histórico no Office (RF futuro).
- Scheduling de coleta recorrente (RF futuro).
- Interpretação de ausência de OS (RF-012).
- Escrita no iService (RF-013).
- Notificações de mudança de status (trabalho futuro — ver DP-017.1).

## Entidades

### `work_order`

```
work_order {
  id:          UUID (UUIDv7, chave primária interna do ATUA)
  tenant_id:   UUID
  provider_id: string (identificador externo da OS no provedor)
  status:      enum WorkOrderStatus
  created_at:  timestamp UTC — instante em que a OS foi vista pela primeira vez
  updated_at:  timestamp UTC — instante da atualização mais recente
}
```

Representa o **estado atual** de uma OS, mantido por upsert: uma linha por OS
por tenant. `provider_id` é a chave de identidade da OS no provedor (ex.:
`workOrderId` do iService).

### `work_order_history`

```
work_order_history {
  id (implícito ou chave composta):
  work_order_id:          UUID (FK → work_order.id)
  work_order_snapshot_id: UUID (FK → work_order_snapshots.id — rastreabilidade)
  tenant_id:              UUID
  provider_id:            string
  status:                 enum WorkOrderStatus
  created_at:             timestamp UTC — instante de inserção deste registro
  updated_at:             timestamp UTC
}
```

Representa o **histórico de status observados**. É append-only: cada vez que
um snapshot gera um processamento, um novo registro é inserido aqui, mesmo que
o status não tenha mudado em relação ao registro anterior.

> **Nota:** a decisão de inserir uma entrada por snapshot (independentemente de
> mudança de status) ou apenas quando o status muda é uma **decisão pendente**
> — ver DP-017.1.

### Enum `WorkOrderStatus`

```
WorkOrderStatus {
  Designado,
  EmProcessamento,
  Pendente,
  Concluido,
  Cancelado
}
```

**Origem dos valores:** observados no iService (contagens nas capturas reais de
produção: Designado, Em Processamento, Pendente, Concluído, Cancelado). Tratados
como enum inicial de status agnóstico de provedor — o mapeamento de outros
provedores futuros para este enum é trabalho futuro.

> **Nota sobre ADR:** a Otto perguntou se este enum deve ir para um ADR. A
> recomendação da analista é: **sim, registrar em ADR**, porque é uma decisão de
> modelo de dados com impacto em múltiplos componentes (Worker, API, Office) e
> com consequências para provedores futuros. Aguarda confirmação do orchestrator
> — ver **DP-017.2**.

## Requisitos funcionais

### RF-017.1 - Upsert em `work_order` ao processar um snapshot

Quando um snapshot de uma OS for processado, o sistema deve criar ou atualizar
o registro correspondente em `work_order`:

- Se a OS não existir (`tenant_id` + `provider_id` ainda não cadastrados):
  inserir novo registro com o status mapeado do snapshot e `created_at` =
  instante atual.
- Se a OS já existir: atualizar `status` e `updated_at` com os valores do
  snapshot processado.

### RF-017.2 - Append em `work_order_history` ao processar um snapshot

Quando um snapshot de uma OS for processado, o sistema deve inserir um novo
registro em `work_order_history` com o status mapeado e a referência ao
snapshot (`work_order_snapshot_id`). Registros anteriores de histórico não
devem ser alterados.

### RF-017.3 - Mapeamento de status: agnóstico de provedor

O status armazenado em `work_order` e `work_order_history` deve ser um valor
do enum `WorkOrderStatus`, não um valor bruto do provedor. O mapeamento entre
o valor do provedor (ex.: string do iService) e o enum é responsabilidade do
componente que processa o snapshot (Worker ou processo separado — ver DP-016.2).

### RF-017.4 - Rastreabilidade: `work_order_snapshot_id`

Cada registro em `work_order_history` deve referenciar o `id` do snapshot
(`work_order_snapshot_id`) que originou aquele registro, permitindo auditoria
e reconciliação futura entre dado bruto e dado mapeado.

### RF-017.5 - OS sem status mapeável são descartadas com log

Se o status retornado pelo provedor não puder ser mapeado para nenhum valor
do enum `WorkOrderStatus`, a OS não deve gerar registro em `work_order` nem
em `work_order_history`. O descarte deve ser registrado em log com nível
`Warning`.

### RF-017.6 - Identidade da OS: `tenant_id` + `provider_id`

A chave de identidade da OS em `work_order` é `(tenant_id, provider_id)`.
Dois tenants distintos podem ter OSs com o mesmo `provider_id` sem conflito.

## Regras de negócio

| Número   | Regra                                                                                                                                        |
|----------|----------------------------------------------------------------------------------------------------------------------------------------------|
| RN-017.1 | Cada snapshot processado resulta em upsert em `work_order` (estado atual) e append em `work_order_history` (histórico).                     |
| RN-017.2 | `work_order_history` é append-only; registros anteriores não são alterados nem removidos.                                                    |
| RN-017.3 | O status em `work_order` e `work_order_history` é um valor do enum `WorkOrderStatus`; valores brutos do provedor não são armazenados aqui.   |
| RN-017.4 | Cada registro de `work_order_history` referencia o `work_order_snapshot_id` que o originou.                                                  |
| RN-017.5 | OS com status não mapeável são descartadas (sem inserção) com log `Warning`.                                                                 |
| RN-017.6 | A identidade da OS em `work_order` é `(tenant_id, provider_id)`; isolamento entre tenants é garantido pelo `tenant_id`.                     |

## Critérios de aceite

1. Dado que a OS X (tenant T, provider_id P) não existe em `work_order`,
   quando um snapshot dessa OS com status "Designado" for processado,
   então deve ser inserido um registro em `work_order` com
   `status = Designado`, e um registro em `work_order_history` referenciando
   o `work_order_snapshot_id` correspondente.

2. Dado que a OS X já existe em `work_order` com `status = Designado`,
   quando um snapshot da mesma OS com status "Em Processamento" for processado,
   então `work_order.status` deve ser atualizado para `EmProcessamento`,
   e um novo registro deve ser inserido em `work_order_history` com
   `status = EmProcessamento`, sem alterar registros anteriores do histórico.

3. Dado que a OS X já existe em `work_order` com `status = Designado`,
   quando um snapshot da mesma OS com o mesmo status "Designado" for processado,
   então o comportamento de `work_order_history` (inserir ou não nova entrada
   quando o status não mudou) segue a decisão **DP-017.1**.

4. Dado que o provedor retornou uma OS com status não presente no enum
   `WorkOrderStatus`,
   quando o processamento tentar mapear o status,
   então nenhum registro deve ser criado em `work_order` ou `work_order_history`,
   e uma entrada de log `Warning` deve ser gerada.

5. Dado um registro em `work_order_history`,
   quando o `work_order_snapshot_id` for consultado,
   então deve existir um documento correspondente em `work_order_snapshots`
   com aquele `id`.

6. Dado dois tenants distintos (T1 e T2) com OSs de mesmo `provider_id` P,
   quando ambas forem persistidas,
   então `work_order` deve conter dois registros distintos — um para (T1, P)
   e outro para (T2, P) — sem conflito.

## Decisões pendentes

### DP-017.1 — Inserção em `work_order_history` a cada snapshot ou apenas em mudança de status

**Situação:** Não está definido se `work_order_history` deve receber uma entrada
para cada snapshot processado (mesmo sem mudança de status) ou apenas quando o
status observado difere do último registro de histórico.

**Impacto:**
- Inserção a cada snapshot: histórico mais denso, rastreabilidade total de
  quando a OS foi vista em cada coleta, mas volume de dados potencialmente alto
  em coletas recorrentes frequentes.
- Inserção apenas em mudança: histórico mais enxuto, focado em transições, mas
  perde a informação de "quantas coletas viram a OS sem mudança de status".

**Aguarda:** decisão de produto.

### DP-017.2 — Enum `WorkOrderStatus` em ADR ou apenas em RF

**Situação:** O enum `WorkOrderStatus` é uma decisão de modelo de dados com
impacto em múltiplos componentes (Worker, API, Office) e consequências para
provedores futuros. A analista recomenda registrá-lo em ADR, mas aguarda
confirmação do orchestrator.

**Aguarda:** confirmação do orchestrator (Otto).

### DP-017.3 — Tecnologia de persistência de `work_order` e `work_order_history`

**Situação:** Não está definido se `work_order` e `work_order_history` vivem em
MongoDB (junto com `work_order_snapshots`) ou em PostgreSQL (junto com as demais
entidades relacionais do ATUA). O schema estruturado e relacional dessas
entidades (FK, upsert condicional, enum) é mais natural em PostgreSQL; o dado
bruto de `work_order_snapshots` é mais natural em MongoDB.

**Aguarda:** decisão do `software-architect`.

### DP-017.4 — Validação de transições de status

**Situação:** RF-017 não valida se uma transição de status é permitida (ex.:
de `Concluido` para `Designado`). O sistema apenas registra o que o provedor
reporta. Não está definido se o ATUA deve rejeitar transições inválidas no MVP.

**Aguarda:** decisão de produto.

## Dependências

- RF-016 (persistência de snapshots brutos): produz os snapshots que alimentam
  as entidades deste requisito.
- DP-016.2: define quem dispara o processamento snapshot → work_order/history
  (Worker síncrono ou processo separado).
- ADR-021 (D7): define `workOrderId` como `provider_id` para o iService no MVP.
- RF-009 (coleta inicial): define o fluxo de coleta que origina os primeiros
  snapshots.

## Impactos

- RF-012 (ausência de OS) é compatível: OS ausentes em uma coleta simplesmente
  não geram novo snapshot, portanto não disparam atualização de `work_order` nem
  inserção em `work_order_history`. O último estado registrado persiste.
- RF futuro de exibição de OS no Office dependerá das entidades definidas aqui.
- RF futuro de coleta recorrente definirá o que acontece com `work_order` e
  `work_order_history` quando a mesma OS aparecer em coletas subsequentes —
  RF-017.1 e RF-017.2 já cobrem o comportamento esperado, mas o critério de
  aceite RF-017.3 fica pendente até DP-017.1 ser resolvido.

## Fora do escopo

- Dado bruto do provedor (RF-016).
- Mapeamento de campos do rawData além do status (trabalho futuro).
- Exibição no Office (RF futuro).
- Scheduling de coleta recorrente (RF futuro).
- Notificações de mudança de status (trabalho futuro).
- Escrita no iService (RF-013).
- Regras de retenção de dados (RF-009.7 — já definidas).
