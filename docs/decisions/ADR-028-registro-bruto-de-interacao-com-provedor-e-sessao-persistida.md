# ADR-028 - Registro Bruto de Interação com o Provedor e Sessão Persistida

## Status

Accepted

## Contexto

RF-016/RF-017/ADR-023 definiram e implementaram (validado em produção em
2026-09-02) o modelo atual: `work_order_snapshots` (1 documento MongoDB por
OS individual, contendo o `rawdata` daquela OS), observado por Change
Stream pelo `SnapshotConsumerWorker`, que projeta o estado agnóstico de
provedor em `work_order`/`work_order_history` no PostgreSQL.

O usuário (product owner) identificou uma lacuna real nesse modelo: ele
preserva o dado bruto **por OS**, mas não preserva o registro bruto da
**interação** do Worker Coletor com o provedor como unidade auditável —
não há registro de login, nem de "esta chamada retornou N OS, com sucesso
ou falha, nesta data/hora, com estes parâmetros de request". Ele quer
poder estudar a situação das ordens de serviço através do ATUA, sem
precisar acessar o iService diretamente, e para isso precisa enxergar
exatamente o que cada requisição de coleta retornou.

Paralelamente, o usuário confirmou dois requisitos adicionais que
contradizem decisões existentes:

1. **Persistência de sessão do provedor é requisito, não otimização
   futura.** Durante a POC, a sessão CAS era mantida para evitar login
   repetido a cada ciclo. Isso importa de forma crescente: com mais
   tenants usando o ATUA, múltiplos logins simultâneos/repetidos podem
   gerar rate-limit, bloqueio ou ruído indesejado junto ao CAS da Midea.
   Isso contradiz o **ADR-022** (Postergação da persistência de sessão
   CAS), que assumia baixo volume de tenants como justificativa para
   adiar a implementação.

2. **`work_order_snapshots` deixa de existir no MongoDB.** A gestão da OS
   unitária (estado atual + histórico) permanece exclusivamente no
   PostgreSQL (`work_order`/`work_order_history`), mas alimentada por uma
   nova fonte bruta no Mongo — o registro de interação, que pode conter N
   OS por documento. Isso contradiz a granularidade "1 documento por OS"
   definida em **RF-016**.

Uma exploração real foi conduzida antes de formalizar esta decisão
(prerequisito colocado pelo próprio processo de análise arquitetural):

- Um bug crítico no log de diagnóstico local (`.txt`, Development-only)
  foi corrigido — `AsyncLocal<CycleContext?>` não sobrevivia a handlers de
  evento assíncronos do Playwright (`page.Response += ...`), descartando
  silenciosamente o log de tudo após `ClaimAsync`. Corrigido substituindo
  por `ConcurrentDictionary<Guid, string>` indexado por `CommandId`.
- Uma coleta real completa foi capturada com sucesso contra o iService da
  Natal Refrigeração (comando `01a092d7-...`, 673KB de log, 41 eventos):
  confirmando 7 trocas de `queryWorkOrder` (uma por status/página) + 2
  trocas de `queryOneWorkOrder` (enriquecimento de OS "designadas"), com
  paginação real ocorrendo em alguns status.
- O usuário navegou manualmente até a tela "Pesquisa de Ordem de Serviço"
  do portal iService (`#/wom/views/serviceExecution/workOrderQuery/index`),
  distinta da tela "Visão por Status" usada hoje pelo Collector, expondo
  57 colunas configuráveis e 17 filtros — bem mais campos do que os hoje
  coletados/enriquecidos pelo Collector.
- Um curl real capturado do browser do usuário confirmou que o mesmo
  endpoint já usado pelo Collector
  (`POST /web/iservice-wom/workOrder/queryWorkOrderList`) aceita
  `woStatus: ""` + `woStatusCond: "me"` combinado com
  `creationDateFrom`/`creationDateTo` explícitos, retornando **todas as OS
  do período, de qualquer status, numa única estratégia paginada por
  data** — alternativa às ~5-7 chamadas por status que o Collector faz
  hoje.

## Decisão

### 1. `work_order_snapshots` é descontinuada

A coleção deixa de ser escrita pelo Worker Coletor. `WorkOrderRepository`,
`IWorkOrderRepository` e `WorkOrderSnapshotDocument` são removidos (ver
RF-022, que substitui RF-016).

### 2. Nova coleção `provider_interactions` é a única fonte bruta no MongoDB

Documento por interação (request/response) com o provedor: login, cada
chamada de listagem (`list_query`, por página, cobrindo N OS de qualquer
status — ver decisão 6 sobre estratégia de busca), e cada chamada de
enriquecimento por OS (`detail_query`). É a única coleção observada pelo
Change Stream do `SnapshotConsumerWorker` (filtrando `interaction_type`
diferente de `login`, que não carrega OS).

O schema exato de `provider_interactions` (campos de `request`/`orders`)
é tratado como o mais estável possível dado o conhecimento atual, mas
**não é fechado como imutável** — normalizações adicionais podem ser
necessárias à medida que mais variação de payload for observada em
produção. Ver RF-022 para a definição formal do schema no momento desta
decisão.

### 3. Nova coleção `provider_sessions` para reuso de sessão do provedor

Documento único e mutável por `(tenant_id, provider_type)`, contendo o
estado de sessão do Playwright (`storage_state`: cookies + origins),
criado/atualizado a cada login bem-sucedido, com TTL conservador (valor
inicial a calibrar por observação real — não há dado empírico de duração
da sessão CAS da Midea no momento desta decisão).

Não é observada pelo Change Stream — é estado operacional do Worker, não
dado de negócio. Não é um log; é sobrescrita por chave natural.

O Worker deve, antes de logar:
1. buscar sessão salva válida (não expirada, `valid = true`);
2. se existir, tentar hidratar o `BrowserContext` com ela e validar com
   uma checagem barata (ex.: acessar rota autenticada, checar ausência de
   redirect para login);
3. se a checagem passar, pular o login CAS;
4. caso contrário (ausente, expirada, inválida, ou falha na checagem),
   executar login CAS normalmente e sobrescrever a sessão salva.

Se qualquer request autenticado falhar durante o ciclo (ex.: 401 ou
redirect inesperado para login), o Worker marca a sessão como inválida,
refaz login uma única vez, e registra o evento de login (sucesso/falha)
em `provider_interactions` como interação do tipo `login`.

Ver RF-023 para a definição formal.

### 4. Desvio explícito do ADR-004/ADR-021 D9-B quanto à segurança da sessão

O **ADR-004** previa persistência de sessão CAS cifrada, via PostgreSQL/KMS,
sob custódia da API (não do Worker) — desenho nunca implementado. O
**ADR-021 (D9-B)** e o **ADR-022** reforçaram que o Worker não deve ter
acesso a cifra nem ao Postgres/KMS diretamente, adiando a persistência de
sessão até que fosse revisitada sob esse desenho.

Esta decisão **não segue esse desenho reservado**. O Worker já escreve
diretamente no MongoDB (padrão estabelecido por RF-016/ADR-023 para o dado
bruto de OS); persistir `provider_sessions` no mesmo MongoDB, pelo mesmo
Worker, é consistente com esse padrão de acesso já aceito — não introduz
um novo acesso a Postgres/KMS pelo Worker, portanto não viola literalmente
o ADR-021 D9-B (que trata especificamente de Postgres/KMS).

Isso não elimina o risco de segurança em si: `storage_state` contém
cookies de sessão, que são material sensível equivalente a credencial de
curto prazo. A mitigação nesta fase é operacional, não criptográfica:
- a coleção `provider_sessions` não é exposta por nenhuma API pública;
- o acesso ao MongoDB Atlas já é restrito à infraestrutura do ATUA (mesmo
  perímetro de segurança usado hoje para `work_order_snapshots`, que já
  continha dado de negócio do cliente);
- o `.txt` de diagnóstico local (Development-only) nunca deve conter o
  conteúdo de `storage_state` em texto claro — isso é reforçado
  explicitamente em RF-023.

Cifrar `storage_state` no MongoDB (ex.: com uma chave gerenciada fora do
Worker) fica registrado como melhoria futura, sujeita aos mesmos critérios
de revisão já definidos no ADR-022 (volume de tenants, sinal de
rate-limit, ou expansão do modelo de ameaça). Esta ADR formaliza que essa
melhoria **não bloqueia** o MVP.

### 5. `SnapshotConsumerWorker` decompõe array por documento

`ISnapshotAdapter.ExtractStatus(BsonDocument) -> string?` (1 OS por
documento) é substituído por `ExtractOrders(BsonArray) -> IEnumerable<ExtractedOrder>`
(N OS por documento). Um documento de `provider_interactions` do tipo
`list_query`/`detail_query` gera de 0 a N upserts em `work_order` e de 0 a
N inserts condicionais em `work_order_history`, todos numa única
transação Postgres, com o mesmo resume token do Change Stream avançado ao
final.

A responsabilidade de descartar OS sem `provider_id` extraível (hoje
RF-016.5, aplicada no momento da escrita pelo Worker) migra inteiramente
para o consumer/adapter — o Worker grava tudo cru, sem filtrar; quem
decide o que é válido para projeção é o consumer (ver RF-022 e RF-017).

### 6. Índice de idempotência muda de nível

De `(tenant_id, provider_id, command_id)` (por OS, usado hoje em
`work_order_snapshots`) para identidade da própria interação:
- `list_query`: `(tenant_id, command_id, interaction_type, request.page)`
- `detail_query`: `(tenant_id, command_id, interaction_type, request.provider_order_id)`

A idempotência **por OS** não desaparece — permanece garantida no
PostgreSQL pelo upsert já existente em `work_order` por
`(tenant_id, provider_id)`. Reprocessar o mesmo documento de interação
(ex.: replay de resume token) não duplica OS no Postgres; o índice Mongo
serve apenas para não duplicar o registro da interação em si.

### 7. Estratégia de busca migra de "por status" para "por data" (sem filtro de status)

`FetchWorkOrdersForStatusAsync`/`FetchWorkOrdersPageAsync` (hoje ~5-7
chamadas por ciclo, uma por status) são substituídas por uma única
estratégia paginada contra `queryWorkOrderList` com `woStatus: ""` +
`woStatusCond: "me"` + `creationDateFrom`/`creationDateTo` do período
desejado, conforme confirmado no curl real capturado. Isso reduz o número
de interações por ciclo e é coerente com o modelo de `provider_interactions`
(um documento `list_query` pode legitimamente conter OS de status
variados, já que não há mais um filtro de status por chamada).

A migração deve ser validada lado a lado (mesmo conjunto de OS retornado
pela estratégia antiga e pela nova) antes do cutover definitivo — ver
Fase 3 do plano de implementação em
`docs/implementation/PLANO-refactor-provider-interactions-collector.md`.

## Motivos

- Preserva o registro de interação como unidade auditável, atendendo à
  necessidade real do usuário de estudar as OS sem depender de acesso
  direto ao iService.
- Reduz login CAS repetido, mitigando risco de rate-limit/bloqueio junto
  ao provedor à medida que mais tenants forem adicionados — requisito
  explícito do usuário, não otimização prematura.
- Mantém a separação de responsabilidades já estabelecida em ADR-023
  (Worker grava bruto no Mongo; consumer projeta agnóstico de provedor no
  Postgres), apenas mudando a granularidade da fonte bruta.
- Reduz o número de chamadas por ciclo de coleta (busca por data em vez
  de por status), com base em endpoint já confirmado como funcional.

## Alternativas consideradas

**Manter `work_order_snapshots` e adicionar `provider_interactions` como
coleção aditiva, sem tocar no pipeline existente.** Rejeitada
explicitamente pelo usuário: ele não quer duplicar a gestão de OS
individual em duas coleções Mongo — a granularidade por OS deve viver
exclusivamente no Postgres (`work_order`/`work_order_history`), não mais
em nenhuma coleção Mongo.

**Persistir `provider_sessions` na mesma coleção que `provider_interactions`
(log único).** Rejeitada: um log append-only não serve como estado
mutável reutilizável — obrigaria buscar "o último login válido" num
histórico crescente em vez de um lookup direto por chave natural, além de
reter desnecessariamente mais cópias de material sensível de sessão.

**Seguir o desenho reservado do ADR-022 (endpoints internos da API com
cifra via KMS/Postgres) para a sessão.** Rejeitada nesta fase: exigiria
novos endpoints, lógica de cifra/decifra na API e tráfego de cookies
sensíveis por HTTP — complexidade desproporcional ao estágio de MVP,
quando o Worker já tem acesso direto e aceito ao MongoDB para o mesmo tipo
de dado bruto.

**Resolver a estratégia de busca por data em uma decisão separada,
independente desta.** Considerada, mas o usuário optou por decidir as
duas coisas juntas (persistência + estratégia de busca), pois ambas
afetam o mesmo ponto do código (`IServiceCollectorService`) e o schema de
`provider_interactions` depende de como a busca é feita.

## Consequências

- Reescreve um pipeline (`WorkOrderRepository`, `SnapshotConsumerWorker`,
  `ISnapshotAdapter`) já implementado e validado em produção — risco de
  regressão real, não cosmético. Mitigado por implementação faseada (ver
  `docs/implementation/PLANO-refactor-provider-interactions-collector.md`):
  captura aditiva primeiro, cutover só após validação ponta a ponta.
- Transações Postgres maiores por documento processado (N OS por commit
  em vez de 1) — sem problema no volume atual do MVP; requer atenção se o
  volume crescer.
- TTL de `provider_sessions.expires_at` sem dado empírico de duração real
  da sessão CAS — começar conservador e ajustar por observação em
  produção.
- Ponto de escrita da interação bruta sobe para dentro do loop de coleta
  em `IServiceCollectorService` (não é troca de repositório por baixo de
  uma interface estável — é alteração estrutural no fluxo de coleta
  Playwright).
- Cifra de `storage_state` fica como melhoria futura, não bloqueante,
  sujeita aos mesmos critérios de revisão do ADR-022.
- **Decisão de produto confirmada (2026-09-12):** os documentos existentes
  em `work_order_snapshots` serão apagados após o cutover (Fase 5 do plano
  de implementação) — não há retenção como resíduo histórico. A remoção
  da coleção em si (drop) fica a cargo da Fase 5, após confirmação de que
  `work_order`/`work_order_history` no Postgres já refletem o estado
  completo alimentado pela nova fonte.

## Glossário — Pipeline de projeção

**Pipeline de projeção**: mecanismo pelo qual dados brutos gravados pelo
Worker Coletor no MongoDB são transformados em estado estruturado e
consultável no PostgreSQL, sem que o Worker Coletor acesse o Postgres
diretamente. Compreende três estágios encadeados: (1) inserção do
documento bruto na coleção Mongo de origem; (2) captação desse evento via
MongoDB Change Streams pelo `SnapshotConsumerWorker`, que seleciona o
adapter apropriado por `provider_type` e extrai os dados relevantes do
rawdata; (3) gravação do resultado no Postgres como upsert em
`work_order` e append condicional em `work_order_history`, na mesma
transação em que o resume token é avançado. O termo "projeção" designa
especificamente essa transformação de dado bruto agnóstico de schema em
modelo estruturado agnóstico de provedor — não inclui a captura em si
(responsabilidade do Collector) nem a exibição futura desses dados
(Office/API).

## Agentes envolvidos

- Usuário: identificação da lacuna real, requisitos de sessão persistida
  e descontinuação de `work_order_snapshots`, validação/rejeição das
  alternativas propostas.
- software-architect ("Sérgio"): análise inicial e desenho de
  `provider_interactions`/`provider_sessions` (turnos 0-2, em background),
  avaliação de impacto no pipeline existente — conteúdo aproveitado
  integralmente nesta ADR.
- orchestrator ("Otto"): redação final desta ADR e dos RFs associados,
  assumida diretamente após o agente `software-architect` estagnar em 22
  tool calls concluídas sem produzir nova saída por mais de 17h (mesmo
  valor do contador em leituras separadas por ~20min — sinal de hang, não
  de progresso lento). Custo registrado aqui para calibrar orçamento de
  prompt em subagentes futuros: tarefas amplas sem critério de parada
  explícito ("PARE após N tool calls e retorne parcial") tendem a não
  convergir nesta ferramenta, que não expõe `max_tool_calls`/
  `max_wall_time` como parâmetro forçado.

## Data

2026-09-12

## Substitui

RF-016 (integralmente — ver RF-022).

## Altera

ADR-022 (a persistência de sessão deixa de estar postergada; o desenho
reservado nele descrito — endpoints internos cifrados via API/KMS — não é
seguido nesta fase, mas permanece registrado como referência para revisão
futura caso os critérios de segurança/volume exijam cifra).
ADR-023 (o mecanismo de Change Stream e a separação Worker/consumer
permanecem válidos; muda apenas a coleção observada e a granularidade do
documento).
RF-017 (a origem dos dados muda de `work_order_snapshots` para
`provider_interactions`; a regra de descarte por `provider_id` inválido
migra do produtor para o consumer).
