# ADR-037 - Fila da Triagem WhatsApp com Reuso de Change Streams

## Status

**Accepted** — aprovado pelo Product Owner em 2026-10-13.

## Contexto

O épico de triagem WhatsApp precisa reagir à elegibilidade de uma OS e iniciar
o contato uma única vez de forma observável e recuperável. O projeto já usa
MongoDB Change Streams, resume token e processamento `at-least-once` no
ADR-023. Introduzir outra tecnologia de fila aumentaria custo e operação
durante a Fase 1.

O mecanismo precisa preservar isolamento por tenant, suportar reprocessamento
e não permitir que o Collector envie mensagens diretamente ao consumidor.

## Decisão proposta

Reutilizar MongoDB Change Streams como mecanismo da fila da triagem.

O Collector publica um gatilho durável quando uma OS se torna elegível. Um
worker separado consome esse fluxo, preserva o resume token e solicita à
Master API o início idempotente da triagem.

A Master API continua como autoridade do estado. O evento da fila não autoriza
o worker a alterar diretamente a máquina de estados nem a enviar o template
sem a decisão idempotente da API.

A entrega é `at-least-once`. Duplicidades são tratadas conforme o ADR-039.

## Motivos

- Reutiliza infraestrutura, driver e experiência operacional existentes.
- Evita custo e IaC adicionais para o MVP.
- Resume token permite retomada após interrupção.
- Mantém o Collector desacoplado das regras da conversa.
- É compatível com operação sob demanda em desenvolvimento.

## Alternativas consideradas

### Amazon SQS

Oferece recursos gerenciados de fila e DLQ, mas adiciona serviço, IAM, IaC,
monitoramento e um segundo padrão de mensageria sem necessidade comprovada
para o volume da Fase 1.

### Redis Streams ou RabbitMQ

Exigiriam nova infraestrutura contínua e aumentariam custo e carga
operacional.

### Polling

É funcional, mas perde o mecanismo reativo e de retomada já adotado no
ADR-023.

### Envio direto pelo Collector

Foi rejeitado porque acopla coleta, regra de negócio e efeito externo, além de
retirar da Master API a autoridade da triagem.

## Consequências

### Positivas

- Nenhuma nova fila gerenciada.
- Padrão operacional conhecido.
- Reprocessamento com resume token.
- Evolução posterior para outra fila sem alterar o contrato de domínio.

### Negativas

- Dependência operacional do MongoDB Atlas.
- Change Streams não oferece DLQ nativa.
- O consumer precisa de observabilidade, política de retentativa e tratamento
  explícito de eventos irrecuperáveis.
- Escala e limites do Atlas devem ser reavaliados antes da produção.

## Agentes envolvidos

- `orchestrator`
- `software-architect`

## Data

2026-10-13

## Relações

- Reutiliza o padrão do ADR-023.
- Complementa o ADR-039.
- Não substitui ADRs existentes.
