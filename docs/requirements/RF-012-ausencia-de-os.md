# RF-012 - Ausência de OS

Status: `Pendente`

## Objetivo

Definir o comportamento do sistema quando uma OS conhecida não aparece em
uma coleta — impedindo que ausências transitórias sejam interpretadas
automaticamente como cancelamento, conclusão ou exclusão da OS.

## Escopo

RF-012 trata exclusivamente da interpretação da ausência de uma OS no
conjunto retornado por uma coleta. Ele não define o que ocorre quando uma OS
aparece com status diferente (RF-011), nem o comportamento de OS nunca antes
vistas.

### O que é RF-012

- Regra de interpretação negativa: ausência em uma coleta não é evidência
  de mudança de estado.
- Regra de preservação: OS previamente observadas não devem ter seu estado
  alterado nem ser marcadas como canceladas/concluídas/excluídas com base
  exclusivamente na ausência.
- Registro da ausência: a coleta em que a OS não apareceu pode ser registrada
  para fins de diagnóstico, mas não como mudança de estado da OS.

### O que NÃO é RF-012

- Definição de quando o ATUA pode concluir que uma OS foi encerrada (esse
  critério depende de evidência positiva — a OS aparecer com status Concluído
  ou Cancelado numa coleta futura).
- Mecanismo de alerta por OS há N coletas ausente (RF futuro).
- Exclusão de OS do sistema (RF futuro, dependente de política de retenção
  RF-009.7).

## Requisitos funcionais

### RF-012.1 - Ausência não implica mudança de estado

Quando uma OS previamente observada não aparece em uma coleta, o sistema não
deve alterar o estado atual dessa OS nem registrar qualquer transição de
status.

### RF-012.2 - Preservação do snapshot existente

O documento de snapshot de uma OS ausente em uma coleta deve permanecer
inalterado: o estado atual, `capturedAtUtc` e `commandId` devem continuar
refletindo a última coleta em que a OS foi observada.

### RF-012.3 - Ausência não gera observação de estado

A ausência de uma OS em uma coleta não deve gerar uma nova observação na
coleção `work_order_observations`. Observações registram aparições reais da
OS, não ausências.

### RF-012.4 - Razões conhecidas de ausência

As razões pelas quais uma OS pode não aparecer em uma coleta incluem, entre
outras: filtro de janela temporal do iService, instabilidade temporária do
portal, OS fora dos 5 status suportados naquele momento, ou paginação não
coberta. Nenhuma dessas razões justifica inferir mudança de estado.

## Regras de negócio

| Número   | Regra                                                                                                                     |
|----------|---------------------------------------------------------------------------------------------------------------------------|
| RN-012.1 | Ausência de OS em uma coleta não altera o estado atual nem o histórico dessa OS.                                          |
| RN-012.2 | O snapshot de uma OS ausente permanece com os valores da última coleta em que ela foi observada.                          |
| RN-012.3 | Ausência não gera observação — observações registram apenas aparições reais da OS.                                        |
| RN-012.4 | Apenas evidência positiva (OS aparecendo com status Concluído ou Cancelado) pode registrar esses estados no ATUA.         |

## Critérios de aceite

1. Dado que a OS 123 foi observada na coleta C1 com status Designado,
   quando a coleta C2 não retornar a OS 123 (ausência),
   então o snapshot da OS 123 deve permanecer com status Designado e
   `commandId` de C1,
   e nenhuma nova observação deve ser inserida em `work_order_observations`
   para a OS 123 com `commandId` de C2.

2. Dado que a OS 123 esteve ausente nas coletas C2, C3 e C4,
   quando a coleta C5 retornar a OS 123 com status Em Processamento,
   então o snapshot da OS 123 deve ser atualizado para Em Processamento com
   `commandId` de C5,
   e uma nova observação com `capturedAtUtc` de C5 deve ser inserida,
   e as coletas C2, C3 e C4 não devem gerar observações para a OS 123.

3. Dado que a OS 123 esteve ausente em todas as coletas após C1,
   quando o histórico da OS 123 for consultado,
   então deve conter apenas a observação de C1,
   sem qualquer registro de "ausência" ou mudança de estado inferida.

## Dependências

- RF-009 (coleta inicial): estabelece o conjunto inicial de OS conhecidas.
- RF-010 (histórico observado): reforça que observações são append-only e
  originadas apenas de aparições reais.
- RF-011 (atualização de estado): define o que acontece quando a OS aparece
  — RF-012 é o complemento para quando ela não aparece.
- `WorkOrderRepository.UpsertSnapshotsAsync`: o upsert opera apenas sobre OS
  presentes no `CollectionResult`; OS ausentes simplesmente não são
  processadas, o que satisfaz RF-012.1 e RF-012.2 por design.

## Impactos

- RF futuro de alerta por OS cronicamente ausente dependerá da contagem de
  coletas sem aparição, que este requisito preserva implicitamente (ausência
  não apaga o snapshot, que permanece com o `commandId` da última aparição).

## Decisões pendentes

### DP-012.1 — Registro explícito de ausência para diagnóstico

**Situação:** RF-012.3 proíbe gerar observações de estado por ausência. Não
está definido se o sistema deve registrar, em algum outro lugar (ex.: log
estruturado, campo no snapshot como `lastSeenCommandId`), que a OS não
apareceu em determinadas coletas.

**Impacto se não decidido:** sem registro de ausência, não é possível
construir alertas ou diagnósticos de "OS há N coletas ausente" sem consultar
a ausência de observações — o que é possível, mas menos eficiente.

**Aguarda:** decisão de produto sobre necessidade de diagnóstico de ausência.

### DP-012.2 — Critério de conclusão por evidência positiva vs. ausência prolongada

**Situação:** RF-012 define que ausência não implica conclusão. Não está
definido se, após N coletas consecutivas sem aparição, o sistema pode assumir
que a OS foi concluída ou cancelada fora dos status suportados.

**Impacto se não decidido:** OS históricas podem acumular indefinidamente
com status desatualizado se nunca mais aparecerem com status Concluído ou
Cancelado.

**Aguarda:** decisão de produto sobre política de envelhecimento de OS.

## Fora do escopo

- Alerta por OS ausente por N coletas consecutivas (RF futuro).
- Exclusão ou arquivamento automático de OS (RF futuro, política RF-009.7).
- Interpretação de OS que aparecem com status diferente (RF-011).
- Escrita no iService (RF-013).
