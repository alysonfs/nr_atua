# ADR-029 - Agendador Recorrente de Coleta: BackgroundService interno na Master API

## Status

Accepted

## Contexto

RF-025 (Coleta Recorrente Configurável) introduz o campo
`RecurrentCollectionIntervalMinutes` na entidade `Integration`
(`apps/api/Atua.Api/Domain/Integrations/Integration.cs`), configurável por
tenant+provider via Office, com mínimo de 5 minutos, máximo de 1440 minutos e
padrão de 15 minutos. RF-025 deliberadamente deixa em aberto **como** o
sistema cria periodicamente novos `ImmediateCollectionCommand`s respeitando
esse intervalo, para cada integração cujo `CollectorActivation` esteja
`Active` — e remete essa decisão a um ADR próprio. Esta é essa decisão.

ADR-003 já previa "execução recorrente a cada 15 minutos" como comportamento
de produto desde o desenho original do Agente Coletor. ADR-007 e ADR-010
haviam cogitado três mecanismos para esse agendamento, mas sem uma decisão
definitiva registrada: EventBridge Scheduler + SQS/Lambda, cron no
sistema operacional da EC2, ou (implicitamente, via "EC2 + cron" que na
prática significava um processo contínuo) um serviço interno de longa
duração. Essas ADRs antecediam a implementação de ADR-020/ADR-021, quando o
contrato de `CollectorActivation`/`ImmediateCollectionCommand` ainda não
existia.

### Contexto técnico verificado (código e infraestrutura atuais)

* **`CollectorActivationService.ActivateAsync`**
  (`apps/api/Atua.Api/Application/Integrations/CollectorControl/CollectorActivationService.cs`)
  cria hoje exatamente um `ImmediateCollectionCommand` `Pending` no momento da
  ativação manual, dentro da mesma transação que ativa o `CollectorActivation`.
  Não existe hoje nenhum mecanismo que crie comandos subsequentes — a coleta
  recorrente ainda não está implementada.
* **`ImmediateCollectionCommand`**
  (`.../Domain/Integrations/CollectorControl/ImmediateCollectionCommand.cs`)
  já modela o ciclo de vida completo (`Pending → Claimed →
  Succeeded|Failed`, `Pending → Cancelled`) e um índice único parcial garante
  no máximo um comando `Pending` por `IntegrationId` (ADR-020). Este invariante
  já resolve, sem trabalho adicional, o problema de duplicar comandos se o
  agendador rodar mais de uma vez para a mesma integração antes de o comando
  anterior ser concluído.
* **`CollectorActivation`**
  (`.../Domain/Integrations/CollectorControl/CollectorActivation.cs`) expõe
  `Status` (`Inactive`/`Active`) por integração — é a fonte de verdade de
  "quais integrações devem ter coleta recorrente agendada agora".
* **`ClaimTimeoutJob`**
  (`apps/api/Atua.Api/Infrastructure/Jobs/ClaimTimeoutJob.cs`), registrado em
  `Program.cs` via `builder.Services.AddHostedService<ClaimTimeoutJob>()`, já
  é um `BackgroundService` rodando dentro do próprio processo da Master API.
  Ele acorda a cada 5 minutos, abre um `IServiceScopeFactory` scope, resolve um
  serviço de aplicação (`ImmediateCollectionCommandService`) e faz uma
  varredura simples no PostgreSQL para expirar comandos `Claimed` órfãos. É um
  precedente direto, já em produção, de que a infraestrutura atual tolera e já
  opera esse padrão sem custo ou complexidade operacional adicional.
* **Infraestrutura AWS** (`infra/lib/atua-compute-stack.ts`,
  `infra/lib/atua-network-stack.ts`): o ambiente MVP roda em **duas instâncias
  EC2 `t3.micro`** (Master API e Collector Worker), sem Auto Scaling Group,
  sem Load Balancer, sem EventBridge Scheduler nem Lambda provisionados. A
  Master API roda como **um único processo, uma única instância**. Não há
  hoje nenhum risco de múltiplas réplicas do mesmo `BackgroundService`
  disputando o mesmo trabalho.
* **ADR-010** (AWS MVP Free Tier Optimization) estabelece preferência
  explícita por manter custo baixo no MVP evitando novos serviços gerenciados
  quando uma alternativa mais simples resolve o mesmo problema dentro do
  Free Tier ou de custo marginal zero.

## Decisão

### Mecanismo escolhido: `RecurrentCollectionSchedulerJob`, um `BackgroundService` interno à Master API

Adota-se um novo `BackgroundService`, análogo em desenho a `ClaimTimeoutJob`,
registrado no mesmo processo da Master API via `AddHostedService`. Este job:

1. Acorda em um intervalo de varredura fixo e curto (**1 minuto** — mais fino
   que o mínimo de negócio de 5 minutos de RF-025.4, para que o atraso entre
   "o intervalo venceu" e "o comando é criado" seja desprezível frente ao
   próprio intervalo configurado).
2. A cada execução, abre um `IServiceScopeFactory` scope e resolve um novo
   serviço de aplicação `RecurrentCollectionSchedulerService` (nome sugerido),
   colocado ao lado de `CollectorActivationService` no módulo
   `Integrations/CollectorControl`.
3. O serviço de aplicação consulta, em uma única query, todas as integrações
   com `CollectorActivation.Status == Active`, faz `JOIN` com `Integration`
   para obter `RecurrentCollectionIntervalMinutes`, e com o último
   `ImmediateCollectionCommand` de cada integração (por `RequestedAtUtc`
   decrescente).
4. Para cada integração `Active` cujo último comando tenha
   `RequestedAtUtc + RecurrentCollectionIntervalMinutes <= now` **e** que não
   possua comando em `Pending` ou `Claimed` no momento, cria um novo
   `ImmediateCollectionCommand` `Pending`, reaproveitando o construtor já
   existente na entidade — o mesmo caminho usado hoje pela ativação manual.
5. O índice único parcial de ADR-020 (`Pending` único por `IntegrationId`)
   continua sendo a garantia final contra duplicação, mesmo que a consulta do
   passo 3 tenha uma pequena janela de corrida; uma eventual violação de
   unicidade é tratada como no-op (a integração é revisitada na próxima
   varredura), nunca como erro fatal do job.
6. Integrações recém-ativadas (sem nenhum comando anterior) são tratadas como
   já vencidas na primeira varredura após a ativação — mas isso não duplica
   trabalho, porque `ActivateAsync` já cria o primeiro comando
   sincronamente (RF-008/ADR-020); o job apenas assume a partir do segundo
   ciclo.
7. Integrações `Inactive` são ignoradas pela consulta — nenhum comando é
   criado apenas por decorrer o tempo enquanto o Agente está desativado
   (RF-025.6).
8. A alteração do intervalo (RF-025.7) não exige nenhuma lógica adicional no
   job: como o "próximo vencimento" é sempre recalculado a partir de
   `RequestedAtUtc` do último comando **mais o valor atual** de
   `RecurrentCollectionIntervalMinutes` (lido a cada varredura, nunca
   armazenado em cache), o novo intervalo passa a valer automaticamente na
   próxima varredura, sem necessidade de recalcular ou reagendar nada
   explicitamente.

### Por que esta opção

* **Reaproveita um padrão já em produção.** `ClaimTimeoutJob` já prova que um
  `BackgroundService` interno, com varredura periódica e escopo de DI por
  execução, funciona no ambiente atual sem infraestrutura nova. O novo job
  segue exatamente essa forma, minimizando superfície de código novo e
  conhecimento operacional novo para a equipe.
* **Custo marginal zero.** Não introduz Lambda, EventBridge Scheduler, SQS,
  nem processos externos. Roda no mesmo `t3.micro` que já hospeda a Master
  API, dentro do Free Tier/orçamento já aprovado (ADR-010).
* **Menor complexidade operacional.** Não há novo componente para monitorar,
  implantar ou depurar separadamente; os logs do job aparecem no mesmo
  `journalctl`/CloudWatch da API, como já ocorre com `ClaimTimeoutJob`.
* **Compatível com o invariante de domínio existente.** O índice único
  parcial de `ImmediateCollectionCommand` (ADR-020) já foi desenhado para
  tolerar concorrência entre criações de comando; o scheduler não precisa de
  nenhum mecanismo de lock distribuído adicional no cenário atual de
  instância única.
* **Sem necessidade de recalcular explicitamente ao alterar o intervalo**
  (RF-025.7): como o vencimento é sempre `último comando + intervalo atual`,
  a nova leitura do campo já é a "recalibração" — não há estado de
  agendamento duplicado (ex.: um "próximo timestamp" persistido
  separadamente) que precisasse ser invalidado.

### Escopo desta decisão

Esta ADR decide **apenas o mecanismo de criação periódica de
`ImmediateCollectionCommand`s dentro da Master API**. Não decide:

* como o Worker (`apps/collector`) processa esses comandos — isso é regido
  por ADR-021 (`claim`/`complete`) sem alteração.
* qualquer lógica de ajuste adaptativo de intervalo — fora de escopo de
  RF-025 e desta ADR.
* comportamento caso o volume de integrações ativas cresça a ponto de a
  varredura de 1 minuto se tornar cara — ver "Riscos" abaixo.

## Alternativas consideradas

* **EventBridge Scheduler + Lambda**: rejeitada para o MVP atual. Exigiria
  provisionar um novo serviço gerenciado (Lambda, possivelmente IAM
  adicional, e triggers do EventBridge), além de lidar com o problema já
  identificado em ADR-010 de acesso de Lambda a RDS/PostgreSQL privado
  (NAT Gateway ou RDS Proxy público, ambos com custo e complexidade
  adicionais). O ganho de escalabilidade não se justifica no volume atual
  (dezenas de tenants), e contraria a preferência de ADR-010 por evitar
  novos serviços gerenciados quando uma alternativa simples resolve o mesmo
  problema dentro do custo já aprovado. Fica como opção futura caso o volume
  de integrações ativas cresça a ponto de justificar desacoplar o
  agendamento do processo da API.
* **Cron no sistema operacional da EC2 chamando a API**: rejeitada. Exigiria
  expor um endpoint HTTP interno adicional apenas para ser chamado pelo
  cron, duplicando parte da superfície de autenticação interna já definida
  em ADR-017/ADR-020 sem necessidade, além de mover a lógica de
  agendamento para fora do controle de versão e do ciclo de deploy da
  aplicação (scripts de cron mantidos separadamente na instância, como já
  ocorre com os scripts em `infra/systemd/`, mas sem os mesmos testes
  automatizados que um `BackgroundService` em C# permite). Não há benefício
  claro sobre o `BackgroundService` interno, e a operação fica mais frágil
  (depende de configuração correta da EC2 fora do artefato de deploy da API).
* **Timer por integração (um `Task`/timer individual criado na ativação)**:
  rejeitada. Multiplicaria o número de tarefas em memória proporcionalmente
  ao número de integrações ativas, exigiria lógica própria de
  criação/cancelamento de timer ao ativar/desativar/alterar intervalo, e
  duplicaria estado (o "próximo vencimento" passaria a existir tanto em
  memória quanto implicitamente no banco), aumentando o risco de
  dessincronização após um restart do processo. A varredura periódica
  centralizada é mais simples de raciocinar e já é statelessa entre
  reinícios (o estado vive inteiramente no PostgreSQL).

## Consequências

### Impactos de implementação (para backend-engineer)

* **Novo arquivo de domínio/aplicação**: `RecurrentCollectionSchedulerService`
  (ou nome equivalente), no módulo
  `Application/Integrations/CollectorControl/`, responsável pela consulta de
  integrações vencidas e criação dos comandos `Pending`. Deve reutilizar a
  mesma `AtuaDbContext` e o mesmo padrão de `TimeProvider` já usado em
  `CollectorActivationService` para permitir testes determinísticos.
* **Novo arquivo de infraestrutura**: `RecurrentCollectionSchedulerJob` em
  `Infrastructure/Jobs/`, seguindo exatamente o mesmo esqueleto de
  `ClaimTimeoutJob.cs` (loop com `Task.Delay`, `IServiceScopeFactory`,
  tratamento de `OperationCanceledException` e `Exception` genérica com log).
* **`Program.cs`**: uma linha adicional,
  `builder.Services.AddHostedService<RecurrentCollectionSchedulerJob>();`,
  ao lado do registro existente de `ClaimTimeoutJob`.
* **Sem alteração necessária em `CollectorActivationService.cs`**: o
  contrato de ativação/desativação manual permanece exatamente como está;
  o scheduler é um consumidor adicional do mesmo modelo de domínio, não uma
  alteração do fluxo de ativação.
* **Sem alteração necessária na infraestrutura AWS** (`infra/lib/*.ts`):
  nenhum novo serviço gerenciado, IAM role, rede ou custo adicional a
  provisionar. O job roda dentro do processo já existente da Master API na
  EC2 já provisionada.
* Uma migration pode ser necessária apenas se a consulta do passo 3 exigir
  um índice adicional (ex.: índice composto em `ImmediateCollectionCommand`
  por `(IntegrationId, RequestedAtUtc DESC)`) para manter a varredura
  eficiente à medida que o volume de comandos históricos cresce — decisão
  de índice específica cabe ao backend-engineer na implementação, com base
  em medição real de plano de consulta.

### Riscos e mitigação

* **Múltiplas instâncias da Master API no futuro**: se o MVP evoluir para
  Auto Scaling Group com mais de uma instância da API, múltiplos
  `BackgroundService` idênticos rodariam simultaneamente, todos tentando
  criar o mesmo comando para a mesma integração vencida. O índice único
  parcial de `ImmediateCollectionCommand` (ADR-020) já impede duplicação de
  dados, mas causaria tentativas de escrita concorrentes desperdiçadas
  (não incorretas, apenas ineficientes). Caso a arquitetura evolua para
  múltiplas instâncias, este ADR deve ser revisitado para introduzir um
  lock distribuído (ex.: `pg_advisory_lock` por ciclo de varredura) ou migrar
  para o mecanismo externo (EventBridge Scheduler) descartado acima. Não é
  um problema hoje, porque a Master API roda em instância única.
* **Atraso de até ~1 minuto entre vencimento e criação do comando**: aceitável
  frente ao intervalo mínimo de negócio de 5 minutos (RF-025.4); não afeta
  nenhum critério de aceite de RF-025.
- **Crescimento do volume de integrações ativas**: a consulta de varredura
  cresce linearmente com o número de integrações `Active`. Para o volume
  esperado no MVP (dezenas de tenants), o custo é desprezível. Se o volume
  crescer para milhares de integrações ativas, a estratégia de índice e o
  intervalo de varredura devem ser revisados — não é um risco atual.
* **Falha silenciosa do job**: segue o mesmo padrão de tratamento de exceção
  de `ClaimTimeoutJob` (log de erro, sem derrubar o processo); uma falha
  isolada em uma varredura não impede a próxima. Deve-se garantir
  observabilidade mínima (log de quantos comandos foram criados por
  varredura, como já ocorre em `ClaimTimeoutJob` para expirações).

### Compatibilidade

Esta decisão não altera nenhum contrato público (Office, Worker) nem o
modelo de domínio de `ImmediateCollectionCommand`/`CollectorActivation`. É
aditiva: um novo consumidor do mesmo modelo já existente, seguindo o mesmo
padrão de `BackgroundService` já em produção.

## Agentes envolvidos

* software-architect (Sérgio): mecanismo de agendamento e justificativa.
* backend-engineer: implementação de `RecurrentCollectionSchedulerService`,
  `RecurrentCollectionSchedulerJob` e registro em `Program.cs`.
* qa-engineer: validação de que integrações `Inactive` nunca geram comando,
  que a alteração de intervalo é respeitada na próxima varredura, e que não
  há duplicação de comandos `Pending` sob concorrência.
* aws-architect: nenhuma ação necessária nesta fase; deve ser consultado
  apenas se uma futura revisão migrar o mecanismo para EventBridge Scheduler
  (ver "Riscos").

## Data

2026-09-17

## Substitui

Não aplicável. Resolve a decisão pendente deixada em aberto por ADR-003,
ADR-007 e ADR-010 ("como orquestrar a coleta recorrente"), e complementa
ADR-020/ADR-021 sem alterar seus contratos.
