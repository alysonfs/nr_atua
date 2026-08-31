# RF-016 - Persistência de Snapshots Brutos de OS

Status: `Especificado`

**Data:** 2026-08-31

## Contexto e motivação

Este requisito substitui as partes de RF-009 (RF-009.5, RF-009.8, RF-009.9,
RN-009.9), RF-010 e RF-011 referentes ao modelo de persistência do Worker.

O modelo anterior assumia que o Worker mapeava campos do iService para uma
entidade de "observação" com `capturedAtUtc` e regras de imutabilidade por
campo. Esse mapeamento foi considerado prematuro: não há conhecimento suficiente
sobre o iService — e sobre provedores futuros — para desenhá-lo com confiança
antes de ter mais dados de produção. A decisão é **guardar o dado bruto primeiro,
mapear depois**.

## Objetivo

Definir como o Worker persiste o payload bruto de cada OS retornada pelo
provedor, sem interpretar nem mapear seus campos, garantindo que nenhuma
informação seja descartada antes de haver conhecimento suficiente para
estruturá-la.

## Usuário / Ator

- **Worker Coletor** (`apps/collector`): único produtor de snapshots.
- **Processos futuros de mapeamento**: consumidores futuros do rawData (fora
  do escopo deste requisito).

## Escopo

RF-016 cobre exclusivamente a persistência do dado bruto na coleção
`work_order_snapshots`. Não cobre o mapeamento do rawData em campos
estruturados, nem a atualização das entidades agnósticas de provedor
(`work_order` e `work_order_history` — ver RF-017).

### O que É RF-016

- Definição da entidade `work_order_snapshots` e suas regras de inserção.
- Regra de append-only: cada coleta gera um novo documento; não há upsert.
- Preservação integral do payload bruto sem mapeamento de campos.
- Identificadores mínimos necessários para rastreabilidade.

### O que NÃO é RF-016

- Mapeamento de campos do rawData em campos estruturados (trabalho futuro,
  condicionado a maior conhecimento dos provedores).
- Histórico de estados ou transições de status (RF-017).
- Consulta ou exibição de snapshots no Office (RF futuro).
- Interpretação da ausência de OS (RF-012).
- Coleta recorrente e seu scheduling (RF futuro).
- Escrita no iService (RF-013).

## Entidade: `work_order_snapshots`

```
work_order_snapshots {
  id:          UUID (UUIDv7, gerado pelo Worker, chave primária interna)
  tenant_id:   UUID
  provider_id: string (identificador externo da OS no provedor — ex.: workOrderId do iService)
  command_id:  UUID (identificador do comando de coleta que gerou este snapshot)
  rawdata:     documento/objeto sem schema formalizado — payload bruto do provedor,
               preservado exatamente como retornado
  created_at:  timestamp UTC de inserção
}
```

> **Nota sobre `provider_id`:** O campo `provider_id` usa `workOrderId` do
> iService como valor para o MVP (D7 resolvido, RF-009). O isolamento do
> mapeamento entre o campo do iService e `provider_id` deve ser mantido em
> um único componente do Worker (equivalente ao papel que `WorkOrderMapper`
> exercia anteriormente).

## Requisitos funcionais

### RF-016.1 - Append-only: cada coleta gera um novo snapshot

Para cada OS retornada pelo provedor em uma coleta, o Worker deve inserir um
novo documento em `work_order_snapshots`. Não deve haver upsert: múltiplas
coletas da mesma OS geram múltiplos documentos, um por coleta.

### RF-016.2 - Preservação integral do rawData

O campo `rawdata` deve conter o payload exatamente como retornado pelo
provedor, sem omissão de campos, transformação de valores, normalização de
tipos ou interpretação de semântica. O schema de `rawdata` não é formalizado
pelo ATUA.

### RF-016.3 - Identificadores obrigatórios

Cada documento deve conter:
- `id`: UUIDv7 gerado pelo Worker no momento da inserção.
- `tenant_id`: identificador do tenant proprietário da OS.
- `provider_id`: identificador externo da OS no provedor (ex.: `workOrderId`
  do iService para o MVP).
- `command_id`: identificador do comando de coleta que gerou este snapshot,
  permitindo rastrear qual ciclo de coleta produziu cada registro.

### RF-016.4 - Rastreabilidade temporal via `created_at`

Cada documento deve registrar o instante UTC de inserção em `created_at`.
Este campo é preenchido pelo sistema no momento da gravação e não pode ser
retroagido nem fabricado. Não é permitido usar datas provenientes do provedor
como `created_at`.

### RF-016.5 - OS sem `provider_id` válido são descartadas com log

Se o Worker não conseguir extrair um `provider_id` válido do payload de uma
OS, esse payload deve ser descartado sem inserção. O descarte deve ser
registrado em log com nível `Warning`, incluindo o `command_id` e, se
disponível, qualquer campo identificador presente no payload.

### RF-016.6 - Isolamento do mapeamento de campos do provedor

O mapeamento entre o campo do provedor (ex.: `workOrderId` do iService) e o
campo `provider_id` do ATUA deve estar isolado em um único componente do
Worker. Nenhuma outra parte do código deve referenciar o nome do campo do
provedor diretamente.

### RF-016.7 - Credenciais e dados de sessão proibidos no rawData persistido

Credenciais do iService e dados de sessão CAS não devem ser incluídos no
`rawdata` persistido, mesmo que o provedor os devolva no payload de resposta.

## Regras de negócio

| Número   | Regra                                                                                                                              |
|----------|------------------------------------------------------------------------------------------------------------------------------------|
| RN-016.1 | Cada coleta de uma OS gera exatamente um novo documento em `work_order_snapshots`; não há upsert nem deduplicação por OS.         |
| RN-016.2 | O campo `rawdata` preserva o payload bruto do provedor integralmente, sem mapeamento, transformação ou interpretação de campos.    |
| RN-016.3 | `created_at` reflete o instante real de inserção; datas do provedor não podem ser usadas como `created_at`.                       |
| RN-016.4 | OS sem `provider_id` válido são descartadas com log `Warning`; não geram documento.                                               |
| RN-016.5 | O mapeamento entre campo do provedor e `provider_id` é isolado em um único componente; nenhum outro componente o referencia.      |
| RN-016.6 | Credenciais e dados de sessão CAS são vedados no `rawdata` persistido.                                                            |

## Critérios de aceite

1. Dado que o Worker coletou a OS X na coleta C1 e na coleta C2,
   quando ambas as coletas forem processadas,
   então `work_order_snapshots` deve conter exatamente dois documentos para
   a OS X — um com `command_id` de C1 e outro com `command_id` de C2 —
   sem que nenhum documento seja sobrescrito ou removido.

2. Dado que o provedor retornou um payload com campos A, B e C para a OS X,
   quando o Worker persistir o snapshot,
   então o `rawdata` do documento deve conter os campos A, B e C com os
   valores exatos retornados pelo provedor, sem omissão, renomeação ou
   transformação.

3. Dado que o Worker está inserindo um snapshot no instante T,
   quando o documento for gravado,
   então `created_at` deve refletir o instante T (ou imediatamente próximo),
   e nenhum campo de data do provedor deve ser usado como `created_at`.

4. Dado que o provedor retornou uma OS sem campo `workOrderId` (ou equivalente
   configurado como `provider_id`),
   quando o Worker tentar persistir essa OS,
   então nenhum documento deve ser inserido em `work_order_snapshots`,
   e uma entrada de log `Warning` deve ser gerada com o `command_id`.

5. Dado que o Worker processou N OS em uma coleta,
   quando a persistência for concluída,
   então `work_order_snapshots` deve conter exatamente N novos documentos,
   todos com o mesmo `command_id` da coleta.

6. Dado que a mesma coleta (mesmo `command_id`) for re-executada por falha
   parcial do Worker,
   quando o Worker tentar inserir os snapshots novamente,
   então o comportamento esperado de idempotência de re-execução deve ser
   definido — ver **DP-016.1** abaixo.

## Decisões pendentes

### DP-016.1 — Idempotência de re-execução do mesmo `command_id`

**Situação:** O modelo anterior garantia idempotência por índice único em
`(tenantId, providerOrderId, commandId)` na coleção `work_order_observations`.
No novo modelo, `work_order_snapshots` é append-only e não possui esse índice
de unicidade. Se o Worker falhar parcialmente e re-executar a mesma coleta com
o mesmo `command_id`, múltiplos snapshots com o mesmo `command_id` para a
mesma OS serão inseridos.

**Impacto:** Sem definição, re-execuções parciais produzem duplicatas
rastreáveis (têm `command_id` igual) mas não são bloqueadas. Isso pode ou não
ser aceitável dependendo da decisão de produto.

**Opções:**
- (a) Aceitar duplicatas por `command_id` — re-execução é rara e os duplicatas
  são identificáveis pelo mesmo `command_id`; o consumidor filtra.
- (b) Adicionar índice de unicidade em `(tenant_id, provider_id, command_id)` —
  retorna ao comportamento anterior de idempotência por comando, mas introduz
  uma restrição de schema que pode conflitar com o princípio de append-only
  irrestrito.
- (c) O Worker controla idempotência em memória (não reinsere OS já inseridas
  no ciclo atual).

**Aguarda:** decisão do usuário sobre comportamento aceitável em re-execução.

### DP-016.2 — Quem dispara o processamento snapshot → work_order / work_order_history

**Situação:** Após inserir um documento em `work_order_snapshots`, é necessário
mapear o status (agnóstico de provedor) para `work_order_history` (append) e
atualizar `work_order` (upsert) — ver RF-017. Não está definido quem dispara
esse processamento:

- (a) O próprio Worker, de forma síncrona, no mesmo ciclo de inserção do snapshot.
- (b) Um processo separado (ex.: change stream do MongoDB, job na API, consumer
  de fila) que reage à inserção do snapshot.

**Impacto:** A opção (a) torna o Worker responsável por dois passos de
persistência e exige que ele conheça a lógica de mapeamento de status. A opção
(b) desacopla os dois passos mas introduz latência e um novo componente de
infraestrutura. A decisão afeta a arquitetura do Worker e da API.

**Aguarda:** decisão do `software-architect`.

### DP-016.3 — Tecnologia de persistência de `work_order_snapshots`

**Situação:** O modelo anterior usava MongoDB Atlas (ADR-012) para
`work_order_snapshots`. O novo modelo mantém o nome da coleção e o dado bruto
(BsonDocument/JSON), o que é naturalmente compatível com MongoDB. No entanto,
dado que `work_order` e `work_order_history` (RF-017) têm schema mais
estruturado, não está decidido se essas entidades vivem no mesmo banco
(MongoDB) ou em bancos distintos.

**Aguarda:** decisão do `software-architect`.

## Dependências

- RF-009 (coleta inicial): define o fluxo de coleta que produz os snapshots.
- RF-017 (novo): define as entidades agnósticas de provedor que são alimentadas
  a partir dos snapshots.
- ADR-012: define MongoDB Atlas como banco de dados do Worker.
- ADR-021 (D7): define `workOrderId` como `provider_id` para o iService no MVP.

## Impactos

- O código existente `WorkOrderSnapshotDocument`, `WorkOrderObservationDocument`,
  `WorkOrderRepository` e `IWorkOrderRepository` será substituído pela
  implementação deste requisito. O `WorkOrderMapper` será preservado ou
  adaptado para manter o isolamento do mapeamento de campo do provedor
  (RF-016.6).
- RF-012 (ausência de OS) é compatível com este modelo: OS ausentes simplesmente
  não geram novos snapshots; o último snapshot da OS permanece como estava.

## Fora do escopo

- Mapeamento de campos do `rawdata` em campos estruturados (trabalho futuro —
  condicionado a maior conhecimento dos provedores).
- Histórico de status agnóstico de provedor (RF-017).
- Consulta ou exibição de snapshots no Office (RF futuro).
- Interpretação da ausência de OS (RF-012).
- Escrita no iService (RF-013).
