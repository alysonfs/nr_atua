# ADR-039 - Idempotência do Template e das Mensagens da Triagem

## Status

**Accepted** — aprovado pelo Product Owner em 2026-10-13.

## Contexto

A fila, o canal WhatsApp e as chamadas entre componentes podem reenviar
eventos depois de timeout, reinício ou falha transitória. Sem uma estratégia
explícita, a mesma OS pode receber o template mais de uma vez, uma mensagem
pode ser processada repetidamente e anexos podem ser duplicados.

Uma garantia distribuída de `exactly-once` entre MongoDB, Master API,
armazenamento e WhatsApp não é realista. O sistema precisa admitir
`at-least-once` e impedir efeitos duplicados observáveis.

## Decisão proposta

Adotar entrega `at-least-once` com deduplicação idempotente:

1. O início da triagem e o template usam uma chave estável composta pelo
   tenant, integração e OS.
2. A Master API registra o aceite idempotente antes de emitir o comando de
   envio.
3. Cada comando ao canal recebe uma chave de idempotência estável.
4. Mensagens e mídias recebidas carregam o identificador original do canal,
   `channel_idempotency_key` e `correlation_id`.
5. A Master API deduplica o evento no escopo do tenant antes de alterar
   checklist, contadores ou estado.
6. Reentrega de evento já concluído retorna o resultado conhecido sem repetir
   o efeito.
7. Falha transitória preserva a mesma chave durante a retentativa.

O contrato lógico não define nesta ADR a tabela, coleção ou índice físico que
materializará a deduplicação.

## Motivos

- Corresponde às garantias reais de Change Streams e integrações HTTP.
- Evita duplicidade de contato com o consumidor.
- Permite recuperação após falhas sem transação distribuída.
- Mantém rastreabilidade entre comando, resposta e evento de entrega.

## Alternativas consideradas

### `Exactly-once` distribuído

Foi rejeitado por exigir coordenação transacional entre tecnologias e um
serviço externo que não oferece essa garantia ponta a ponta.

### Deduplicação apenas no canal

Foi rejeitada porque não protege a máquina de estados, os anexos nem o
processamento da LLM.

### Chave nova em cada retentativa

Foi rejeitada porque transforma retentativa em novo efeito e permite
duplicidade.

### Deduplicação somente por texto e horário

Foi rejeitada por ser heurística e poder descartar mensagens legítimas.

## Consequências

### Positivas

- Template inicial observavelmente único por OS elegível.
- Eventos repetidos não alteram contadores nem estado.
- Retentativas seguras e rastreáveis.
- Contrato independente do armazenamento físico.

### Negativas

- Exige retenção de chaves de deduplicação por período compatível com o fluxo.
- Concorrência precisa ser resolvida atomicamente pela Master API.
- Uma chave construída incorretamente pode bloquear um envio legítimo.
- Políticas de expiração e reabertura futura precisarão definir novo escopo de
  idempotência.

## Agentes envolvidos

- `orchestrator`
- `software-architect`

## Data

2026-10-13

## Relações

- Complementa o ADR-037.
- Aplica-se ao contrato Canal ↔ Master API.
- Não substitui ADRs existentes.
