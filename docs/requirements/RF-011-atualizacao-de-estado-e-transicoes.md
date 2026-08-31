# RF-011 - Atualização de Estado e Transições

Status: `Pendente`

## Objetivo

Garantir que cada coleta recorrente mantenha o estado atual de cada OS
atualizado e registre uma nova observação no histórico quando o status
observado mudar, sem criar duplicatas nem perder o histórico anterior.

## Escopo

RF-011 define o comportamento de coletas recorrentes em relação à persistência
de OS já conhecidas. A coleta recorrente em si (scheduling, intervalo, comando
recorrente) não está no escopo deste requisito — esse mecanismo é RF futuro.
RF-011 especifica o que acontece com a persistência quando uma coleta
recorrente ocorre.

Os dois documentos de persistência são:

- `work_order_snapshots`: estado atual de cada OS — upsert idempotente por
  `(tenantId, providerOrderId)`.
- `work_order_observations`: registro imutável de cada aparição da OS em uma
  coleta — índice único em `(tenantId, providerOrderId, commandId)`.

Essa estrutura já está implementada em `WorkOrderRepository`. RF-011 formaliza
o contrato de negócio que ela precisa satisfazer.

### O que é RF-011

- Manutenção do estado atual: para cada OS conhecida, o snapshot deve
  refletir o estado observado na coleta mais recente.
- Registro de transição: quando o status de uma OS mudar entre coletas, uma
  nova observação deve ser acrescentada ao histórico.
- Persistência sem duplicatas: o mesmo evento (mesmo `commandId` para a mesma
  OS) não deve gerar documentos duplicados.

### O que NÃO é RF-011

- Scheduling e intervalo da coleta recorrente (RF futuro).
- Interpretação da ausência de OS (RF-012).
- Escrita no iService (RF-013).
- Exibição de transições no Office (RF futuro).

## Requisitos funcionais

### RF-011.1 - Manutenção do estado atual (snapshot)

Para cada OS retornada em uma coleta, o sistema deve atualizar o documento
correspondente na coleção `work_order_snapshots` com o estado observado nessa
coleta, preservando o `providerOrderId` e o `tenantId` como chave de
identidade.

### RF-011.2 - Registro de nova observação por coleta

Para cada OS retornada em uma coleta, o sistema deve inserir exatamente uma
nova observação na coleção `work_order_observations`, identificada pelo
`commandId` da coleta. O índice único em `(tenantId, providerOrderId,
commandId)` garante idempotência: reexecução do mesmo comando não gera
duplicatas.

### RF-011.3 - Detecção de mudança de status (nota de implementação)

O histórico acumula uma observação por coleta por OS. A interpretação de que
houve "transição de status" decorre da comparação entre observações
consecutivas — não há campo de "status anterior" ou "evento de transição"
explícito nos documentos atuais. Toda coleta que retorna uma OS gera uma
observação; a comparação de status entre observações é responsabilidade da
camada de consulta ou exibição.

> **Decisão pendente DP-011.1:** ver seção abaixo — se o produto requerer
> detecção explícita de transição no momento da persistência (ex.: campo
> `statusChanged: true`), isso impacta o modelo de dados.

### RF-011.4 - Preservação do histórico anterior

A atualização do snapshot de uma OS não deve remover nem alterar observações
anteriores. O histórico é append-only.

### RF-011.5 - Identidade estável da OS

A chave de identidade da OS é `providerOrderId` = `workOrderId` do iService
(D7 resolvido, validado empiricamente com ~100 capturas reais de produção,
2026-08-25/26, 23 OS distintas). O mapeamento está isolado em
`WorkOrderMapper.ExtractProviderOrderId` — nenhum outro componente deve
referenciar o campo do iService diretamente.

### RF-011.6 - Status suportados

Somente OS nos cinco status suportados geram snapshots e observações:

1. Designado (`assigned`)
2. Em Processamento (`accepted` no iService)
3. Pendente (`pending`)
4. Concluído (`closed`)
5. Cancelado (`cancelled`)

OS em outros status são ignoradas, conforme RF-009.3.

## Regras de negócio

| Número   | Regra                                                                                                                      |
|----------|----------------------------------------------------------------------------------------------------------------------------|
| RN-011.1 | Cada coleta atualiza o snapshot da OS com o estado mais recentemente observado.                                            |
| RN-011.2 | Cada coleta insere exatamente uma observação por OS; o mesmo `commandId` não gera duplicatas.                              |
| RN-011.3 | O histórico é append-only; observações anteriores não são alteradas nem removidas ao atualizar o snapshot.                 |
| RN-011.4 | A identidade da OS é `providerOrderId` (`workOrderId` do iService); mapeamento isolado em `WorkOrderMapper`.               |
| RN-011.5 | Apenas os 5 status suportados geram persistência; OS em outros status são ignoradas.                                       |

## Critérios de aceite

1. Dado que a OS 123 foi coletada na coleta C1 com status Designado,
   quando a coleta C2 retornar a OS 123 com status Em Processamento,
   então o snapshot de OS 123 deve refletir o status Em Processamento,
   e o histórico deve conter exatamente duas observações (C1: Designado,
   C2: Em Processamento), em ordem cronológica crescente de `capturedAtUtc`.

2. Dado que a OS 123 foi coletada na coleta C1,
   quando a coleta C2 retornar a OS 123 com o mesmo status,
   então o snapshot deve ser atualizado (novo `commandId` e `capturedAtUtc`),
   e o histórico deve conter duas observações (uma por coleta),
   e as observações de C1 não devem ser alteradas.

3. Dado que o mesmo `commandId` é processado duas vezes para a OS 123
   (reexecução por falha parcial),
   quando a segunda execução tentar inserir a observação,
   então nenhuma observação duplicada deve ser criada.

4. Dado que a OS 123 possui histórico com N observações,
   quando a coleta CN+1 ocorrer,
   então o histórico deve conter exatamente N+1 observações,
   e as N observações anteriores devem permanecer inalteradas.

5. Dado que o iService retornou uma OS em um status não suportado,
   quando o Coletor processar a coleta,
   então essa OS não deve gerar snapshot nem observação.

## Dependências

- RF-009 (coleta inicial): produz o primeiro snapshot e a primeira observação
  de cada OS; RF-011 descreve o comportamento das coletas subsequentes.
- RF-010 (histórico observado): define a imutabilidade e a integridade
  temporal das observações que RF-011 acrescenta.
- RF-012 (ausência de OS): define o que ocorre quando uma OS não aparece em
  uma coleta — complementar a este requisito.
- `WorkOrderRepository.UpsertSnapshotsAsync`: upsert por
  `(tenantId, providerOrderId)` — já implementado.
- `WorkOrderRepository.InsertObservationsAsync`: insert idempotente por
  `(tenantId, providerOrderId, commandId)` — já implementado.
- D7 resolvido: `providerOrderId` = `workOrderId` (inteiro do iService),
  mapeamento em `WorkOrderMapper`.

## Impactos

- RF futuro de exibição do histórico no Office depende do modelo de
  observações append-only definido aqui.
- Se DP-011.1 for resolvido adicionando campo de detecção explícita de
  transição, haverá impacto no schema de `WorkOrderObservationDocument`.

## Decisões pendentes

### DP-011.1 — Detecção explícita de transição no momento da persistência

**Situação:** O modelo atual acumula uma observação por coleta por OS,
independentemente de o status ter mudado. A "transição" é inferida pela
camada de consulta comparando observações consecutivas. Não há campo
`previousStatus`, `statusChanged` ou evento de transição explícito nos
documentos.

**Impacto se não decidido:** se o produto precisar de notificações em tempo
real quando uma OS mudar de status (ex.: alerta ao técnico ou gestor), a
detecção na camada de consulta pode ser insuficiente ou tardia. Se a detecção
for necessária na persistência, o schema de `WorkOrderObservationDocument`
precisará ser estendido.

**Aguarda:** decisão de produto sobre necessidade de notificação ou detecção
explícita de transição.

### DP-011.2 — Intervalo e comando da coleta recorrente

**Situação:** RF-011 especifica o comportamento de persistência de coletas
recorrentes, mas o mecanismo de scheduling (intervalo, tipo de comando,
máquina de estados) não está definido.

**Impacto se não decidido:** RF-011 não pode ser implementado sem o
mecanismo de disparo da coleta recorrente.

**Aguarda:** RF futuro de coleta recorrente (scheduling).

### DP-011.3 — Comportamento em caso de desempate de status duplicado entre coletas

**Situação:** ADR-021 (D8) define desempate quando a mesma OS aparece em
múltiplos status na mesma coleta. Não está definido o que ocorre se, entre
duas coletas, o iService retornar a mesma OS em dois status distintos dentro
da mesma janela de coleta recorrente.

**Aguarda:** confirmação se o cenário é possível no iService e, se for,
qual é o comportamento esperado.

## Fora do escopo

- Scheduling e intervalo da coleta recorrente (RF futuro).
- Interpretação da ausência de OS entre coletas (RF-012).
- Escrita no iService (RF-013).
- Exibição de transições no Office (RF futuro).
- Notificações de mudança de status (dependente de DP-011.1).
