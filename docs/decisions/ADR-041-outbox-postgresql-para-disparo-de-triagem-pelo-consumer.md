# ADR-041 - Outbox PostgreSQL para o Disparo de Início de Triagem pelo Consumer

## Status

**Accepted** — aprovado pelo Product Owner em 2026-10-14.

## Contexto

O ADR-037 definiu que o MongoDB Change Streams é o mecanismo de fila/gatilho
da triagem: um worker consome o fluxo de `provider_interactions` e "solicita
à Master API o início idempotente da triagem", mas deixou em aberto *como*
essa solicitação atravessa o limite de processo entre o Collector e a Master
API.

O `ProviderInteractionConsumerWorker` do Collector observa globalmente
`provider_interactions` via Change Streams e processa documentos de múltiplos
tenants no mesmo processo. `WorkOrderPgRepository.ProcessInteractionAsync`
resolve o `integrationId`, faz upserts e históricos de `work_order`, e avança
o resume token do Change Stream — tudo na mesma transação PostgreSQL. Depois
disso, o documento Mongo correspondente é apagado (ADR-030), o que torna essa
transação o único ponto em que a elegibilidade de uma OS para iniciar
triagem é conhecida com certeza.

A rota `POST /api/internal/triagens/start`, recém-criada, exige
`ServiceCredential` com escopo `triage.start`, e cada credencial de serviço
fixa `tenantId`+`integrationId` nos claims — seguindo o mesmo princípio de
identidade fixa por credencial já usado em `collector.command.claim`
(ADR-020/ADR-021). Isso é incompatível com um consumer único que observa
globalmente interações de todos os tenants: não existe uma única credencial
capaz de representar "qualquer tenant, qualquer integração" sem se tornar,
na prática, uma credencial privilegiada cross-tenant.

Chamar essa rota **depois** de `ProcessInteractionAsync` committar é inseguro:
o resume token já avançou e o documento Mongo já foi apagado; uma falha na
chamada HTTP perde o disparo sem nenhum mecanismo de reentrega. Chamar
**antes** de committar é inviável: a `WorkOrder` ainda não existe do ponto de
vista da Master API, e o endpoint não a encontra.

### Precedente já existente no código

O ADR-031 resolveu um problema estruturalmente idêntico: o Consumer do
Collector, dentro da mesma transação de upsert de `work_orders`, decide que
uma ação precisa ser executada por outro processo (buscar detalhe de uma OS),
sem poder fazer uma chamada síncrona no meio da transação. A solução adotada
não foi um novo comando assíncrono nem uma chamada push — foi persistir a
decisão como uma coluna (`NeedsDetailFetch`) na mesma transação Postgres, e
deixar que um mecanismo de *pull* já existente (`claim`) a leia depois.

Collector e Master API já compartilham o mesmo banco PostgreSQL (dois
serviços `.NET`/`systemd` distintos, possivelmente colocados na mesma EC2 —
ADR-034 —, ambos conectando ao mesmo RDS). A Master API já hospeda
`BackgroundService`s internos no próprio processo (`ClaimTimeoutJob`,
`RecurrentCollectionSchedulerJob` — ADR-029), confirmando que esse é o padrão
operacional já estabelecido para trabalho assíncrono nesta fase do projeto,
sem fila gerenciada externa, compatível com a operação sob demanda do
ADR-036.

## Decisão

Introduzir uma tabela de outbox no PostgreSQL compartilhado, gravada **na
mesma transação** em que `WorkOrderPgRepository.ProcessInteractionAsync`
realiza o upsert de `work_order`, o append em `work_order_history` e o avanço
do resume token.

Quando a regra de elegibilidade (definida pelo domínio de triagem, fora do
escopo deste ADR) identificar que uma OS deve disparar o início de triagem,
o Consumer insere uma linha em `triagem_trigger_outbox` contendo, no mínimo,
`tenant_id`, `integration_id`, identificador externo da OS, timestamp de
ocorrência e status de processamento (`Pending` por padrão). Essa escrita é
parte atômica da mesma transação Postgres — se a transação for revertida, a
linha de outbox não existe; se for committada, a linha existe junto com o
estado que a originou.

Um novo `BackgroundService` hospedado **na Master API** (seguindo o padrão do
ADR-029) drena periodicamente `triagem_trigger_outbox`, selecionando linhas
`Pending`, e chama o `StartTriagemUseCase` **diretamente, em processo**, sem
HTTP. Isso elimina a necessidade de qualquer credencial de serviço
cross-tenant: a drenagem roda dentro do próprio limite de confiança da
Master API, que já é a autoridade de tenant/integração. Após o processamento
bem-sucedido (incluindo deduplicação idempotente conforme ADR-039), a linha é
marcada `Processed`; falhas marcam a linha para nova tentativa com backoff,
preservando a mesma chave de idempotência.

A rota `POST /api/internal/triagens/start` deixa de ser usada pelo fluxo
automático. Ela é mantida como rota **interna de uso manual/operacional**
(suporte, QA, reprocessamento pontual), ainda protegida por
`ServiceCredential` com escopo `triage.start` e identidade fixa de
tenant/integração por credencial — sem alteração de contrato de segurança.

O MongoDB Change Streams continua sendo o mecanismo de entrada do gatilho de
triagem, conforme ADR-037; este ADR define apenas o estágio interno entre o
commit da transação do Consumer e a chamada ao caso de uso de domínio da
Master API.

## Motivos

- Reaproveita um padrão já validado no código (ADR-031) para o mesmo tipo de
  problema (efeito decidido numa transação do Collector, executado por outro
  componente).
- Preserva atomicidade: a decisão de disparar a triagem nunca existe sem o
  estado de `work_order` que a originou, nem vice-versa.
- Elimina a necessidade de uma credencial de serviço cross-tenant, mantendo
  o modelo de identidade fixa por credencial (ADR-017/ADR-018/ADR-020).
- Não introduz fila gerenciada nem infraestrutura nova — reaproveita o
  PostgreSQL já compartilhado e o padrão de `BackgroundService` interno já
  hospedado na Master API (ADR-029), compatível com a operação sob demanda
  (ADR-036).
- Mantém a Master API como única autoridade de início/deduplicação da
  triagem (ADR-039/ADR-040); o Collector continua sem conhecer a máquina de
  estados de triagem.
- Fornece retry natural e observável (linha pendente com status e contagem
  de tentativas), sem depender de reprocessar um documento Mongo que já foi
  apagado (ADR-030) nem de recuar o resume token.

## Alternativas consideradas

### B — Mover/adicionar consumer Mongo na Master API

Duplicaria a infraestrutura de Change Streams/resume token já existente no
Collector, exigindo coordenar dois consumidores do mesmo fluxo (ou particionar
o resume token entre eles). Desloca o problema de limite de confiança para o
lado oposto sem resolvê-lo: o problema real não é "quem lê o Mongo", é como
um efeito decidido numa transação Postgres do Collector chega à Master API
com atomicidade. Rejeitada por complexidade não justificada.

### C — Quebrar a transação do Consumer e chamar o endpoint antes de avançar o resume token

Introduz uma falha distribuída clássica: se a chamada HTTP falhar, o token
não avança e o documento é reprocessado indefinidamente; se a chamada
suceder mas o commit local falhar depois, o efeito duplica sem reentrega
visível. Rejeitada por violar o invariante de atomicidade já estabelecido
pelos ADR-023/ADR-028.

### D — Credencial global privilegiada aceitando tenant/integration no corpo da requisição

Equivale, na prática, a uma credencial de serviço cross-tenant/"super-admin",
contrariando o princípio — já aplicado em ADR-017/ADR-018/ADR-020 — de que
nenhuma credencial de serviço deve aceitar identificadores de tenant
fornecidos pelo chamador como autorização. Rejeitada por regressão de
segurança.

## Consequências

### Positivas

- Nenhuma infraestrutura nova de mensageria; reaproveita PostgreSQL e o
  padrão de `BackgroundService` interno já existente.
- Atomicidade garantida entre o estado de `work_order` e a intenção de
  disparar triagem.
- Nenhuma credencial de serviço cross-tenant é necessária.
- Retry e observabilidade naturais via status da linha de outbox.
- A rota HTTP manual permanece disponível para operação/QA sem reintroduzir
  o problema de segurança original.

### Negativas

- Introduz uma nova tabela e uma nova migration (`triagem_trigger_outbox`),
  com esquema, índices e política de retenção/limpeza a definir na
  implementação.
- Introduz um novo `BackgroundService` na Master API, que precisa de
  observabilidade própria (métricas de linhas pendentes, idade da linha mais
  antiga, falhas de drenagem) — sem isso, um travamento silencioso da
  drenagem atrasaria todas as triagens sem sinal visível.
- Cria mais um lugar (além do resume token do Change Stream) onde "at least
  once" precisa ser respeitado; a implementação deve reaproveitar
  estritamente a chave de idempotência do ADR-039 para evitar duplicidade.
- Acopla a Master API a uma tabela escrita por outro processo (o Collector),
  o que exige que qualquer mudança futura de schema dessa tabela seja
  coordenada entre os dois projetos (mesmo risco de divergência documentado
  no ADR-032 para `WorkOrderTerminalStatuses`, com o mesmo critério de
  reavaliação: se um terceiro consumidor desse schema aparecer, considerar
  extrair um contrato compartilhado).

## Agentes envolvidos

- `orchestrator`
- `software-architect`
- `backend-engineer` (implementação futura: migration, outbox writer no
  Consumer, `BackgroundService` de drenagem na Master API)

## Data

2026-10-14

## Relações

- Complementa o ADR-037 (mantém Change Streams como mecanismo de entrada do
  gatilho; não o substitui).
- Respeita a idempotência definida no ADR-039.
- Respeita a autoridade do estado de triagem no PostgreSQL definida no
  ADR-040.
- Reaproveita o padrão de decisão-na-transação-do-Consumer estabelecido no
  ADR-031.
- Reaproveita o padrão de `BackgroundService` interno na Master API
  estabelecido no ADR-029.
- Não altera o modelo de credenciais de serviço com identidade fixa definido
  em ADR-017/ADR-018/ADR-020; a rota `POST /api/internal/triagens/start`
  passa a ser de uso interno manual/operacional, preservando esse modelo.
