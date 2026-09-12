# RF-017 - Modelo Agnóstico de OS: work_order e work_order_history

Status: `Implementado` (origem do dado alterada — ver nota)

**Data:** 2026-08-31 (especificado) · **2026-09-02** (confirmado implementado e
validado em produção) · **2026-09-12** (nota de atualização — ver ADR-028)

> **Nota de atualização (2026-09-12):** as entidades `work_order` e
> `work_order_history` e suas regras (upsert por mudança de status,
> string crua sem enum, histórico só em mudança) **permanecem válidas
> integralmente**. O que muda, por decisão do **ADR-028**: a fonte bruta
> deixa de ser `work_order_snapshots` (RF-016, superseded) e passa a ser
> `provider_interactions` (RF-022) — um documento pode conter N OS, não
> apenas 1. Consequentemente, `ISnapshotAdapter.ExtractStatus` (1 OS por
> chamada) é substituído por `ExtractOrders` (N OS por chamada), e a regra
> de descarte por `provider_id` inválido (antes aplicada pelo Worker em
> RF-016.5) passa a ser aplicada aqui, pelo consumer, no momento da
> extração — nenhuma outra regra de RF-017 muda.

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

RF-017 cobre as entidades `work_order` e `work_order_history`, mantendo o
status como string crua do provedor (sem enum), e as regras de manutenção
dessas entidades a partir dos snapshots. Não cobre o dado bruto
(`work_order_snapshots` — RF-016) nem a camada de exibição no Office (RF
futuro).

### O que É RF-017

- Definição das entidades `work_order` e `work_order_history`.
- Definição do status como string crua do provedor, sem enum nem catálogo.
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
  status:      string (valor cru retornado pelo provedor, sem mapeamento)
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
  status:                 string (valor cru retornado pelo provedor)
  created_at:             timestamp UTC — instante de inserção deste registro
  updated_at:             timestamp UTC
}
```

Representa o **histórico de status observados**. É append-only, mas **não**
recebe uma entrada a cada snapshot processado: uma nova entrada só é inserida
quando o status observado difere do último registro de histórico para aquela
OS (ver DP-017.1, resolvida). Snapshots que reafirmam o mesmo status não
geram novo registro em `work_order_history` — apenas atualizam `updated_at`
em `work_order`.

### Status: string crua do provedor (sem enum, sem catálogo)

**Decisão (2026-08-31, resolve DP-017.2):** o `status` não é um enum fixo em
código. É armazenado exatamente como o provedor o retorna (string), sem
tradução para um vocabulário canônico comum.

**Motivo:** provedores diferentes têm conjuntos de status distintos e sem
correspondência garantida — ex.: iService tem um conjunto de status, um
provedor futuro (ex.: LG) pode ter outro conjunto parcialmente sobreposto e
parcialmente diferente. Um enum único no código exigiria decidir a priori um
mapeamento canônico entre vocabulários de provedores que ainda não são
conhecidos, o que reintroduziria o mesmo problema que motivou este redesenho
(ver RF-016: guardar o dado bruto primeiro, mapear depois).

**Consequência para os requisitos abaixo:** toda menção a "enum
`WorkOrderStatus`" e a "mapeamento de status" (RF-017.3, RF-017.5, RN-017.3,
RN-017.5, critério de aceite 4) fica sem efeito — não há mapeamento nem
descarte por status não reconhecido nesta fase. Um catálogo de status válidos
por provedor, ou um vocabulário canônico comum, pode ser desenhado no futuro
quando houver mais provedores conhecidos — fora de escopo deste RF.

## Requisitos funcionais

### RF-017.1 - Upsert em `work_order` ao processar um snapshot

Quando um snapshot de uma OS for processado, o sistema deve criar ou atualizar
o registro correspondente em `work_order`:

- Se a OS não existir (`tenant_id` + `provider_id` ainda não cadastrados):
  inserir novo registro com o status (string crua) do snapshot e `created_at` =
  instante atual.
- Se a OS já existir: atualizar `status` e `updated_at` com os valores do
  snapshot processado.

### RF-017.2 - Append em `work_order_history` apenas quando o status muda

Quando um snapshot de uma OS for processado, o sistema deve comparar o status
do snapshot com o status atual em `work_order`. Se forem diferentes (ou se a
OS ainda não existir em `work_order`), o sistema deve inserir um novo
registro em `work_order_history` com o status (string crua) e a referência
ao snapshot (`work_order_snapshot_id`). Se o status for igual ao atual,
nenhum registro é inserido em `work_order_history` — apenas `work_order` é
atualizado (RF-017.1). Registros anteriores de histórico não devem ser
alterados.

### RF-017.3 - Status armazenado como string crua, sem mapeamento

O status armazenado em `work_order` e `work_order_history` é a string exata
retornada pelo provedor no snapshot, sem tradução para um enum ou vocabulário
canônico comum entre provedores (ver seção "Status: string crua do provedor").

### RF-017.4 - Rastreabilidade: `work_order_snapshot_id`

Cada registro em `work_order_history` deve referenciar o `id` do snapshot
(`work_order_snapshot_id`) que originou aquele registro, permitindo auditoria
e reconciliação futura entre dado bruto e dado mapeado.

### RF-017.5 - OS sem status extraível são descartadas com log

Se o Worker não conseguir extrair um valor de status do payload do snapshot
(campo de status ausente ou vazio), a OS não deve gerar registro em
`work_order` nem em `work_order_history`. O descarte deve ser registrado em
log com nível `Warning`. Isso é diferente de "status não reconhecido": não há
mais lista de status válidos para reconhecer — o critério de descarte é
apenas ausência do valor.

### RF-017.6 - Identidade da OS: `tenant_id` + `provider_id`

A chave de identidade da OS em `work_order` é `(tenant_id, provider_id)`.
Dois tenants distintos podem ter OSs com o mesmo `provider_id` sem conflito.

## Regras de negócio

| Número   | Regra                                                                                                                                        |
|----------|----------------------------------------------------------------------------------------------------------------------------------------------|
| RN-017.1 | Cada snapshot processado resulta em upsert em `work_order` (estado atual sempre atualizado).                                                |
| RN-017.2 | `work_order_history` recebe uma nova entrada apenas quando o status do snapshot difere do status atual em `work_order` (ou na primeira vez que a OS é vista); é append-only e registros anteriores não são alterados nem removidos. |
| RN-017.3 | O status em `work_order` e `work_order_history` é a string crua retornada pelo provedor; não há enum nem vocabulário canônico comum.         |
| RN-017.4 | Cada registro de `work_order_history` referencia o `work_order_snapshot_id` que o originou.                                                  |
| RN-017.5 | OS sem valor de status extraível do snapshot são descartadas (sem inserção) com log `Warning`.                                               |
| RN-017.6 | A identidade da OS em `work_order` é `(tenant_id, provider_id)`; isolamento entre tenants é garantido pelo `tenant_id`.                     |

## Critérios de aceite

1. Dado que a OS X (tenant T, provider_id P) não existe em `work_order`,
   quando um snapshot dessa OS com status "Designado" for processado,
   então deve ser inserido um registro em `work_order` com
   `status = "Designado"` (string crua), e um registro em `work_order_history`
   referenciando o `work_order_snapshot_id` correspondente.

2. Dado que a OS X já existe em `work_order` com `status = "Designado"`,
   quando um snapshot da mesma OS com status "Em Processamento" for processado,
   então `work_order.status` deve ser atualizado para `"Em Processamento"`,
   e um novo registro deve ser inserido em `work_order_history` com
   `status = "Em Processamento"`, sem alterar registros anteriores do histórico.

3. Dado que a OS X já existe em `work_order` com `status = "Designado"`,
   quando um snapshot da mesma OS com o mesmo status "Designado" for processado,
   então `work_order.updated_at` deve ser atualizado, mas **nenhum** novo
   registro deve ser inserido em `work_order_history` (DP-017.1 resolvida:
   histórico só registra mudanças de status).

4. Dado que o provedor retornou uma OS sem valor de status no payload,
   quando o Worker tentar processar o snapshot,
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

## Implementação e validação (2026-09-02)

RF-017 foi implementado em `apps/collector/Atua.Collector` no commit `7d9ee28`
(a documentação ficou "Especificado" por engano até esta atualização —
o código já estava rodando em produção):

- `Consumer/SnapshotConsumerWorker.cs` — `IHostedService` que abre um
  **MongoDB Change Stream** sobre `work_order_snapshots`, com resume token
  persistido em `consumer_states` (Postgres) para retomar após reinícios.
- `Consumer/WorkOrderPgRepository.cs` — `ProcessSnapshotAsync`: para cada
  snapshot, extrai o status via `ISnapshotAdapter` (selecionado por
  `provider_type`); se ausente, descarta com log `Warning` e avança o
  token (RF-017.5); se presente, faz upsert em `work_orders` + append
  condicional em `work_order_histories` (só se o status mudou, DP-017.1) +
  persiste o resume token — tudo em uma única transação Postgres.
- Migration `AddWorkOrdersAndConsumerState` — cria as tabelas
  `work_orders`, `work_order_histories` e `consumer_states` (nomes de
  tabela em `snake_case`, colunas em `PascalCase` — convenção do EF Core
  usada no projeto inteiro).
- Registrado no DI em `Program.cs`.

**Validado ao vivo em produção** (via skill `atua-pg-inspect`,
`make pg-peek TABLE=work_orders` / `TABLE=work_order_histories`):

- `work_orders`: 196 registros reais (dado agnóstico de provedor,
  `status` como string crua do iService — ex.: `closed`, `assigned`).
- `work_order_histories`: 207 registros — mais que `work_orders` porque
  entradas se acumulam a cada mudança de status observada (RF-017.2), sem
  sobrescrever histórico anterior.
- `consumer_states` com `ResumeToken` não nulo, confirmando que o
  `SnapshotConsumerWorker` está avançando no Change Stream sem reprocessar
  do zero a cada restart.
- Logs (`[CONSUMER] SnapshotConsumerWorker iniciado`, `Abrindo Change
  Stream. ResumeToken=sim`) confirmam a consulta ao Change Stream ativa.

Os critérios de aceite 1-3 e 5-6 (upsert, append condicional,
rastreabilidade via `work_order_snapshot_id`, isolamento por tenant) foram
observados no comportamento real dos dados acima. O critério 4 (descarte
silencioso de OS sem status extraível) está implementado no código
(`ProcessSnapshotAsync`), mas não foi isolado em um cenário de teste
dedicado nesta validação — não há evidência de ocorrência real até o
momento (todas as OS coletadas do iService têm status).

## Decisões pendentes

### DP-017.1 — ~~Inserção em `work_order_history` a cada snapshot ou apenas em mudança de status~~ ✅ Resolvido (2026-08-31)

**Decisão do usuário:** apenas em mudança de status. `work_order_history`
recebe uma nova entrada somente quando o status observado no snapshot difere
do status atual em `work_order` (ver RF-017.2). Snapshots que reafirmam o
mesmo status atualizam apenas `work_order.updated_at`, sem gerar entrada de
histórico.

### DP-017.2 — ~~Enum `WorkOrderStatus` em ADR ou apenas em RF~~ ✅ Resolvido (2026-08-31)

**Decisão do usuário:** não há enum `WorkOrderStatus` — o status é armazenado
como string crua do provedor, sem mapeamento (ver seção "Status: string crua
do provedor" acima). Motivo: provedores diferentes têm conjuntos de status
distintos e sem correspondência garantida (ex.: iService tem um conjunto,
outro provedor futuro pode ter outro parcialmente sobreposto); um enum fixo
em código reintroduziria o problema que este redesenho pretendia evitar.
Nenhum ADR é necessário para um enum que não existe.

### DP-017.3 — ~~Tecnologia de persistência de `work_order` e `work_order_history`~~ ✅ Resolvido (2026-08-31)

**Decisão do usuário:** `work_order` e `work_order_history` ficam no
**PostgreSQL** (junto com as demais entidades relacionais do ATUA), separado
de `work_order_snapshots` (MongoDB — DP-016.3). Consistente com o schema
estruturado dessas entidades (FK entre `work_order_history.work_order_id` e
`work_order.id`, upsert condicional).

**Consequência de arquitetura:** o componente definido em DP-016.2 (consumer
de fila) precisa acesso a ambos os bancos — lê o evento de novo snapshot
(originado no MongoDB) e escreve em `work_order`/`work_order_history`
(PostgreSQL).

### DP-017.4 — Validação de transições de status

**Situação:** RF-017 não valida se uma transição de status é permitida (ex.:
de `Concluido` para `Designado`). O sistema apenas registra o que o provedor
reporta. Não está definido se o ATUA deve rejeitar transições inválidas no MVP.

**Decisão do usuário (2026-08-31):** adiada para depois do MVP. Sem
validação de transição por enquanto — o sistema registra qualquer status
reportado pelo provedor, mesmo que pareça retroceder. Revisitar quando
houver mais visibilidade sobre o comportamento real do iService/outros
provedores.

**Aguarda:** revisão pós-MVP.

## Dependências

- RF-016 (persistência de snapshots brutos): produz os snapshots que alimentam
  as entidades deste requisito.
- DP-016.2 (resolvida): consumer de fila dispara o processamento
  snapshot → work_order/history.
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
  RF-017.1 e RF-017.2 já cobrem o comportamento esperado (upsert sempre;
  histórico só em mudança de status, DP-017.1 resolvida).
- `work_order` e `work_order_history` vivem em PostgreSQL (DP-017.3 resolvida),
  separado do MongoDB usado por `work_order_snapshots` (RF-016/DP-016.3) —
  novo esquema relacional a ser desenhado pelo `software-architect`, incluindo
  migração/tabela e índices para `(tenant_id, provider_id)`.

## Fora do escopo

- Dado bruto do provedor (RF-016).
- Mapeamento de campos do rawData além do status (trabalho futuro).
- Exibição no Office (RF futuro).
- Scheduling de coleta recorrente (RF futuro).
- Notificações de mudança de status (trabalho futuro).
- Escrita no iService (RF-013).
- Regras de retenção de dados (RF-009.7 — já definidas).
