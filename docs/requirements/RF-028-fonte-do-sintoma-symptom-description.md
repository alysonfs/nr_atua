# RF-028 - Sintoma da OS: `symptomDescription` como Fonte, Preservando `symptom` Bruto

Status: `Proposto`

**Data:** 2026-09-26

## História de Usuário

Como usuário do Office analisando uma OS,
eu quero que o campo "Sintoma" exibido reflita o `symptomDescription`
relatado pelo provedor,
para que a informação seja consistente e utilizável, em vez de depender de
uma cadeia de campos alternativos que hoje mistura conceitos diferentes.

Como equipe de produto planejando features futuras,
nós queremos preservar o valor bruto do campo `symptom` do provedor,
para que ele fique disponível para uso em iniciativas futuras ainda não
definidas, mesmo que hoje não seja exibido.

## Contexto e motivação

O adapter atual (`IServiceProviderInteractionOrderAdapter.ExtractSymptom`,
`apps/collector/Atua.Collector/Consumer/IServiceProviderInteractionOrderAdapter.cs`)
popula `WorkOrder.Symptom` percorrendo uma lista de campos candidatos em
ordem de prioridade (`symptom`, `symptomDescription`, `notes`, `note`,
`remark`, `closeDescription`, `returnReason`, `reassigningReason`,
`errorMessage`, `suggestedSolution`, `pendingReason`) e usando o primeiro
valor não vazio. Essa heurística foi criada porque, nas amostras
observadas até a Fase 6 (QA), `symptom`/`symptomDescription` vinham
vazios — mas ela tem dois problemas:

1. Mistura conceitos: `pendingReason` é o **substatus** da OS (RF-031),
   não um sintoma. Se `pendingReason` estiver preenchido e
   `symptomDescription` também, o fallback atual pode escolher o campo
   errado dependendo da ordem.
2. Descarta o dado bruto de `symptom`: como o fallback escolhe apenas um
   valor, se `symptomDescription` vier preenchido, o valor de `symptom`
   (quando também preenchido) nunca é persistido — mesmo sendo,
   segundo a cliente, "peça fundamental para implementações futuras".

## Objetivo

Separar duas fontes de dado hoje conflated em um único campo: o sintoma
"tratado"/descritivo (`symptomDescription`) e o sintoma bruto do provedor
(`symptom`), persistindo ambos.

## Escopo

### O que É RF-028

- Novo campo de domínio para armazenar o valor bruto de `symptom` do
  provedor, sem heurística de fallback.
- `WorkOrder.Symptom` passa a ser populado exclusivamente a partir de
  `symptomDescription` (sem fallback para os demais campos candidatos
  atuais).
- Remoção de `pendingReason` da lista de candidatos de sintoma (é dado de
  substatus — ver RF-031).
- Migration de banco (nova coluna).

### O que NÃO é RF-028

- Reprocessamento retroativo de OS já coletadas (o valor de `Symptom`
  hoje gravado, vindo do fallback, só é corrigido na próxima atualização
  natural daquela OS via coleta).
- Exibição do novo campo bruto na UI do Office (é armazenamento para uso
  futuro, sem tela associada nesta fase).

## Requisitos funcionais

### RF-028.1 - Novo campo `SymptomRaw`

Adicionar campo `WorkOrder.SymptomRaw` (nullable, string), persistido em
toda coleta a partir do campo bruto `symptom` do provedor, sem qualquer
fallback para outros campos.

### RF-028.2 - `Symptom` passa a vir só de `symptomDescription`

O campo já existente `WorkOrder.Symptom` deixa de usar
`SymptomCandidateFields` e passa a ser populado exclusivamente pelo valor
de `symptomDescription` do provedor (`null`/vazio quando o provedor não
enviar o campo).

### RF-028.3 - Remoção de `pendingReason` da extração de sintoma

O campo `pendingReason` não deve mais alimentar nenhum campo de sintoma —
ele passa a ser extraído e tratado exclusivamente como substatus (RF-031).

## Critérios de aceite

1. Uma OS cujo payload traga `symptomDescription` preenchido tem
   `WorkOrder.Symptom` igual a esse valor, independentemente do conteúdo
   de `symptom`, `notes`, `pendingReason` etc.
2. Uma OS cujo payload traga `symptom` preenchido (com ou sem
   `symptomDescription`) tem `WorkOrder.SymptomRaw` igual ao valor bruto
   de `symptom`.
3. Uma OS cujo payload não traga nenhum dos dois campos tem `Symptom` e
   `SymptomRaw` nulos (sem cair em `notes`/`remark`/etc.).
4. `pendingReason` nunca aparece em `Symptom` nem em `SymptomRaw`.

## Decisões (product-analyst, 2026-09-26)

- **Nome do campo confirmado:** `WorkOrder.SymptomRaw` — segue o padrão
  já usado para dados brutos do provedor (`ProviderCreatedAt`,
  `ProviderUpdatedAt`, `ServiceRequestId`).
- **Sem correção retroativa:** o valor de `Symptom` já persistido (vindo
  da heurística de fallback antiga) não recebe script de correção em
  massa. Ele é corrigido naturalmente na próxima coleta de cada OS,
  coerente com o escopo já declarado em "O que NÃO é RF-028" e com o
  princípio de coleta incremental do ATUA (RF-017).
