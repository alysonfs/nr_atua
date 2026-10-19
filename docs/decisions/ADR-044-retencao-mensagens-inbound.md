# ADR-044 - Retenção de Mensagens Inbound da Triagem

## Status

**Accepted** — política definida pelo Product Owner em 2026-10-18.

## Contexto

O ADR-043 introduziu a persistência de mensagens inbound em
`triagem_channel_messages`. Esses registros podem conter texto e telefone do
consumidor, além de identificadores e metadados relacionados à triagem.
O requisito geral RS-003 determina preservação dos dados enquanto a conta
estiver ativa e remoção mediante solicitação do cliente ou após mais de cinco
anos de inatividade.

## Decisão

- Manter o registro completo da mensagem por cinco anos a partir de
  `ReceivedAtUtc`.
- Definir `ExpiresAtUtc` como `ReceivedAtUtc + 5 anos`.
- Excluir fisicamente o registro um ano após `ExpiresAtUtc`, totalizando até
  seis anos desde o recebimento.
- Aplicar RS-003 como precedência: solicitação de exclusão ou inatividade da
  conta por mais de cinco anos pode antecipar a remoção. O fluxo geral de
  exclusão de dados do tenant ainda precisa incluir
  `triagem_channel_messages`; esse fluxo não está implementado neste
  incremento.
- A política cobre os registros e o texto armazenados no PostgreSQL. A
  retenção de anexos/mídia em S3 permanece pendente.

## Implementação e operação

Um job da Master API verifica diariamente as mensagens elegíveis e as exclui
em lotes. Como o job executa dentro do processo da API, a remoção pode atrasar
se a aplicação estiver desligada. O ambiente `make up`/`make down` é de
desenvolvimento e validação; a operação de produção precisa garantir execução
periódica contínua ou definir um mecanismo agendado equivalente.

A remoção no banco primário não apaga cópias já presentes em snapshots ou
backups. A retenção de backups deve ser verificada separadamente antes da
produção.

## Consequências

- `ExpiresAtUtc` pode ser calculado deterministicamente a partir da data de
  recebimento, inclusive para registros existentes no momento da migration.
  O preenchimento desses registros usa UTC explicitamente, independentemente
  do fuso da sessão PostgreSQL.
- O índice por `ExpiresAtUtc` permite localizar registros para purge sem
  varrer a tabela inteira.
- O purge em lotes limita o volume de trabalho de cada operação.
- Mensagens, anexos e backups têm políticas de retenção distintas; apenas a
  política das mensagens no banco primário está definida neste ADR.

## Agentes envolvidos

- Product Owner
- `orchestrator`
- `backend-engineer`
- `qa-engineer`

## Data

2026-10-18

## Relações

- Complementa o ADR-043.
- Deve respeitar o requisito RS-003.
- Não define retenção de anexos/mídia nem de backups.
