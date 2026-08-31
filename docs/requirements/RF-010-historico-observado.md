# RF-010 - Histórico Observado

Status: `Pendente`

## Objetivo

Garantir que o histórico de uma OS no ATUA contenha exclusivamente estados
observados pelo sistema a partir da primeira coleta, sem retroação a eventos
anteriores a ela e sem fabricação de observações.

## Escopo

Este requisito define as regras de integridade temporal do histórico de OS.
Ele complementa RF-009 (que produz a observação inicial) e é consumido por
RF-011 (que acrescenta observações em coletas posteriores).

O mecanismo de persistência subjacente — coleções `work_order_snapshots` e
`work_order_observations` no MongoDB — já está implementado em
`WorkOrderRepository`, `WorkOrderSnapshotDocument` e
`WorkOrderObservationDocument`. Este requisito formaliza o contrato de
negócio que esse mecanismo precisa respeitar.

### O que é RF-010

- Regra de integridade: o campo `capturedAtUtc` de uma observação deve
  refletir o instante real da coleta que a gerou.
- Regra de origem: observações só podem ser criadas pelo Agente Coletor a
  partir de uma coleta efetiva.
- Regra de imutabilidade: uma vez inserida, uma observação não pode ser
  alterada, retroagida nem removida individualmente.
- Proibição de fabricação: datas de abertura, encerramento ou outros
  metadados fornecidos pelo iService sobre a OS não podem ser usados como
  `capturedAtUtc` de uma observação — eles são metadados do provedor, não
  registros do ATUA.

### O que NÃO é RF-010

- Definição de como observações são criadas em coletas recorrentes (RF-011).
- Interpretação da ausência de OS (RF-012).
- Exibição do histórico no Office (RF futuro).
- Paginação ou limites de consulta do histórico (decisão pendente).

### Fronteira crítica com RF-009

RF-009 (seção "Fronteira crítica com RF-010") já estabelece que datas
retornadas pelo iService sobre a OS são metadados do provedor e não
observações do ATUA. RF-010 eleva essa fronteira à condição de regra de
negócio verificável.

## Requisitos funcionais

### RF-010.1 - Início do histórico na primeira coleta

O histórico de uma OS começa no `capturedAtUtc` da observação gerada por
RF-009 (coleta inicial). Nenhum evento anterior a esse instante deve constar
no histórico do ATUA.

### RF-010.2 - Imutabilidade das observações

Observações persistidas na coleção `work_order_observations` são imutáveis.
Após inserção, nenhum campo pode ser alterado ou retroagido.

### RF-010.3 - Proibição de fabricação de datas

O campo `capturedAtUtc` de qualquer observação deve ser preenchido com o
instante real da execução da coleta que gerou aquela observação. É vedado
utilizar datas oriundas do iService (ex.: data de abertura, data de
encerramento da OS) como `capturedAtUtc`.

### RF-010.4 - Origem exclusiva via coleta

Observações somente podem ser criadas pelo Agente Coletor durante uma coleta
efetiva. Não é permitido inserir observações manualmente via API interna,
Office ou qualquer outro mecanismo externo ao fluxo de coleta.

## Regras de negócio

| Número   | Regra                                                                                                       |
|----------|-------------------------------------------------------------------------------------------------------------|
| RN-010.1 | O histórico de uma OS começa na primeira coleta; nenhum evento anterior pode ser registrado.                |
| RN-010.2 | Observações são imutáveis após inserção — nenhum campo pode ser alterado ou retroagido.                     |
| RN-010.3 | `capturedAtUtc` deve refletir o instante real da coleta; datas do iService são vedadas como origem.         |
| RN-010.4 | Observações só podem ser criadas pelo Agente Coletor durante uma coleta efetiva.                            |

## Critérios de aceite

1. Dado que a coleta inicial de uma OS foi executada no instante T,
   quando o histórico dessa OS for consultado,
   então a observação mais antiga deve ter `capturedAtUtc` igual a T,
   e não deve existir nenhuma observação com `capturedAtUtc` anterior a T.

2. Dado que uma observação foi persistida com `capturedAtUtc` = T,
   quando qualquer processo tentar alterar o valor de `capturedAtUtc` dessa
   observação,
   então a alteração deve ser rejeitada e a observação deve permanecer
   inalterada.

3. Dado que o iService retornou uma OS com campo `dataAbertura` = D (data
   de abertura anterior à primeira coleta),
   quando o Coletor persistir a observação dessa OS,
   então `capturedAtUtc` deve ser o instante da coleta, não `dataAbertura`.

4. Dado que uma OS foi observada apenas na coleta inicial,
   quando o histórico dessa OS for consultado,
   então deve conter exatamente uma observação — a observação inicial gerada
   por RF-009.

## Dependências

- RF-009 (implementado parcialmente — lado Worker pendente): produz a
  observação inicial de cada OS; a fronteira de integridade temporal é
  definida na seção "Fronteira crítica com RF-010" de RF-009.
- `WorkOrderObservationDocument`: campo `CapturedAtUtc` é imutável por
  design (`init`-only); índice único em `(tenantId, providerOrderId,
  commandId)` garante idempotência.
- `WorkOrderRepository.InsertObservationsAsync`: usa `InsertMany` com
  `IsOrdered = false`; duplicatas (mesmo `commandId`) são silenciadas pelo
  índice único.

## Impactos

- RF-011 depende diretamente desta especificação para saber o que constitui
  uma nova observação em coletas recorrentes.
- Qualquer RF futuro de exibição do histórico no Office herda as restrições
  de integridade definidas aqui.

## Decisões pendentes

### DP-010.1 — Formato e campos expostos do histórico ao Office

**Situação:** Os documentos de observação persistem `rawData` (BsonDocument
com o payload bruto do iService). Não está definido quais campos do `rawData`
são expostos ao Office, qual é a estrutura do objeto de histórico na resposta
de API, nem se há paginação ou limite de observações por OS.

**Impacto se não decidido:** o RF de exibição do histórico não pode ser
especificado e a API de consulta não pode ser implementada.

**Aguarda:** decisão de produto (usuário) sobre campos expostos; decisão de
arquitetura sobre paginação.

### DP-010.2 — Comportamento de observações com `providerOrderId` ausente

**Situação:** `WorkOrderRepository` já registra um `LogWarning` e descarta
OS sem `providerOrderId` durante a persistência. Não está definido se esse
descarte deve ser registrado como evento auditável ou se basta o log.

**Impacto se não decidido:** descarte silencioso pode dificultar diagnóstico
operacional.

**Aguarda:** decisão de produto sobre necessidade de auditoria de descartes.

## Fora do escopo

- Coleta recorrente e detecção de mudança de status (RF-011).
- Interpretação da ausência de OS (RF-012).
- Escrita no iService (RF-013).
- Exibição do histórico no Office (RF futuro).
- Paginação do histórico (decisão pendente DP-010.1).
