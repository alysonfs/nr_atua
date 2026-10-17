# ADR-040 - Persistência PostgreSQL do Agregado de Triagem

## Status

**Accepted** — aprovado pelo Product Owner em 2026-10-13.

## Contexto

A Master API é a autoridade da máquina de estados da triagem. O Incremento 2
definiu `ITriagemRepository.GetOrAddAsync` como uma operação atômica baseada
na chave estável formada por tenant, integração e identificador externo da OS,
conforme o ADR-039.

O próximo incremento precisa persistir o agregado e impedir que gatilhos
repetidos ou concorrentes criem mais de uma triagem para a mesma chave.

O projeto já utiliza:

- PostgreSQL e EF Core para o estado autoritativo dos agregados da Master API;
- MongoDB Atlas e Change Streams como entrada bruta e mecanismo de fila;
- S3 para os binários dos anexos da triagem.

O pool de operadores também precisa consultar e assumir triagens com
consistência.

## Decisão proposta

Persistir o agregado `Triagem` no PostgreSQL da Master API por meio do EF
Core.

A chave de idempotência composta por tenant, integração e identificador
externo da OS terá restrição única no banco. A implementação de
`GetOrAddAsync` tentará inserir a triagem candidata e, diante de concorrência,
tratará a violação dessa restrição buscando e retornando a instância já
persistida, sem substituir seu estado.

Não serão usados lock apenas em memória nem transação distribuída.

O MongoDB permanece responsável pelo gatilho e pela entrega `at-least-once`
definidos no ADR-037; não será a fonte de verdade da triagem. Os anexos
permanecem no S3.

Mensagens, transcrição e auditoria pertencem ao limite da Master API. Seu
modelo físico detalhado será definido nos incrementos correspondentes, sem
alterar a responsabilidade estabelecida neste ADR.

## Motivos

- Mantém o estado e as transições sob a autoridade da Master API.
- Reutiliza infraestrutura, EF Core, observabilidade e operação existentes.
- A restrição única fornece arbitragem atômica entre processos concorrentes.
- Facilita as consultas do pool e a assunção exclusiva pelo operador.
- Evita consistência distribuída entre PostgreSQL e MongoDB para o mesmo
  agregado.
- Não adiciona custo ou serviço de infraestrutura.

## Alternativas consideradas

### MongoDB Atlas

Persistir o agregado no MongoDB aproximaria o estado do mecanismo de fila,
mas criaria um segundo padrão para os agregados autoritativos da Master API e
complicaria as consultas e transações do pool.

### Abordagem híbrida

Manter parte do agregado ou a conversa no MongoDB e apenas a chave de
idempotência no PostgreSQL exigiria coordenar dois sistemas para uma única
operação de negócio, sem necessidade comprovada na Fase 1.

### Deduplicação somente em memória

Foi rejeitada porque não funciona entre processos e é perdida em reinícios.

### Lock distribuído

Foi rejeitado porque a restrição única do PostgreSQL já resolve a arbitragem
com menor complexidade.

## Consequências

### Positivas

- Idempotência durável após reinícios.
- Concorrência resolvida no banco.
- Estado avançado preservado em reentregas.
- Isolamento multi-tenant expresso na própria chave única.
- Nenhuma infraestrutura adicional.

### Negativas

- O PostgreSQL continua sendo ponto único de disponibilidade do estado da
  aplicação, risco já existente para outros agregados.
- A implementação precisa distinguir violação da restrição de idempotência de
  outros erros de persistência.
- Será necessária migration e configuração de mapeamento EF Core.
- O crescimento futuro de mensagens e auditoria poderá exigir particionamento
  ou estratégia de retenção.

## Agentes envolvidos

- `orchestrator`
- `software-architect`
- `backend-engineer` (implementação futura)

## Data

2026-10-13

## Relações

- Complementa o ADR-037.
- Materializa a atomicidade definida no ADR-039.
- Não altera o papel do MongoDB estabelecido no ADR-023.
- Não altera o armazenamento de anexos em S3.
