# Arquitetura: Triagem automatizada de OS via WhatsApp (Fase 1)

Status: `ARCHITECTURE_DEFINED`

**Data:** 2026-10-13

Referências:

- [Épico da triagem WhatsApp](../features/triagem-whatsapp-fase-1.md)
- [ADR-023 — Fila e consumer de snapshots](../decisions/ADR-023-fila-e-consumer-snapshot-para-work-order.md)
- [ADR-035 — Topologia dedicada para o Baileys](../decisions/ADR-035-topologia-dedicada-always-on-para-baileys.md)
- [ADR-036 — Operação sob demanda](../decisions/ADR-036-operacao-sob-demanda-make-up-down-controle-de-creditos.md)
- [ADR-037 — Fila da triagem](../decisions/ADR-037-fila-triagem-whatsapp-reuso-change-streams.md)
- [ADR-038 — Persistência da sessão Baileys](../decisions/ADR-038-persistencia-sessao-baileys-dev-local-producao-ebs.md)
- [ADR-039 — Idempotência da triagem](../decisions/ADR-039-idempotencia-envio-template-e-processamento-mensagens-triagem.md)
- [ADR-040 — Persistência PostgreSQL da triagem](../decisions/ADR-040-persistencia-postgresql-do-agregado-triagem.md)
- [ADR-041 — Outbox PostgreSQL para disparo da triagem](../decisions/ADR-041-outbox-postgresql-para-disparo-de-triagem-pelo-consumer.md)
- [ADR-042 — Polling do canal com claim/complete](../decisions/ADR-042-polling-claim-complete-para-canal-whatsapp.md)

Este documento define a arquitetura lógica da Fase 1. Não define modelo
físico de dados, prompts da LLM nem detalhes internos da biblioteca Baileys.

## 1. Princípios

1. A Master API é a autoridade da triagem e da máquina de estados.
2. O canal WhatsApp transporta mensagens e mídias, mas não decide regras de
   negócio nem mantém o estado autoritativo da triagem.
3. O operador humano sempre assume o atendimento. A LLM não cancela, agenda,
   classifica SubStatus nem conclui a OS.
4. Entregas assíncronas são tratadas como `at-least-once`, com deduplicação.
   A arquitetura não promete `exactly-once`.
5. Todo processamento preserva o isolamento por tenant.
6. Anexos e dados pessoais só podem ser acessados pelos componentes que
   precisam deles, com rastreabilidade.

## 2. Componentes e responsabilidades

### 2.1 Collector

- Obtém as OS do sistema legado.
- Identifica a condição funcional que torna uma OS elegível para iniciar a
  triagem.
- Publica o gatilho durável consumido pelo worker da triagem.
- Não envia mensagens ao consumidor e não controla a conversa.

### 2.2 Fila e worker da triagem

- Reutilizam MongoDB Change Streams e o padrão de resume token do ADR-023,
  conforme proposto no ADR-037.
- Consomem o gatilho de OS elegível.
- O Consumer grava uma intenção na outbox PostgreSQL na mesma transação do
  upsert da OS e do avanço do resume token.
- Um `BackgroundService` da Master API drena a outbox e solicita, em processo,
  o início idempotente da triagem, conforme o ADR-041.
- Encaminham comandos de saída ao canal sem assumir a autoridade do estado.
- Reprocessam falhas transitórias sem produzir efeitos duplicados observáveis.

### 2.3 Master API

- É a única autoridade para criar a triagem, validar o checklist e realizar
  transições de estado.
- Mantém a correlação entre tenant, OS, triagem, mensagens e anexos.
- Decide quando emitir `SendTemplate` e `SendMessage`.
- Consulta a LLM, valida sua saída e aplica as regras determinísticas do
  produto.
- Autoriza a assunção exclusiva pelo primeiro operador.
- Produz as notificações in-app e a atualização do contador do pool.
- Mantém a auditoria das alterações feitas pelo operador.

### 2.4 Canal WhatsApp

- Mantém a conexão com o WhatsApp e a sessão do número.
- Busca comandos de envio na Master API por polling, com intervalo de até 30
  segundos, e os reivindica por lease antes de executar.
- Reporta o resultado por `complete`, preservando a chave de idempotência e a
  correlação recebidas.
- Publica na Master API eventos de mensagem, mídia, entrega e falha.
- Não consulta a LLM, não altera o estado da triagem e não acessa regras do
  posto.

Em desenvolvimento, o processo roda localmente com arquivos de sessão no
disco. Ele só realiza conexões de saída e não expõe uma porta para a Master
API. `ITriagemChannel` e `FakeTriagemChannel` são stubs transitórios do
incremento anterior, não o transporte de produção. A topologia de produção
permanece relacionada ao ADR-035 e ao ADR-038.

### 2.5 LLM

- É chamada somente pela Master API.
- Interpreta a descrição livre, identifica itens pendentes e propõe respostas
  dentro do fluxo definido pelo produto.
- Não é fonte de verdade para estado, autorização ou contadores de tentativa.
- Não sugere peças na primeira implementação. Essa capacidade depende de um
  catálogo confiável.

### 2.6 Painel do operador

- Acessa somente a Master API.
- Exibe o pool compartilhado do posto, checklist, anexos, transcrição e
  responsável atual.
- Permite ao primeiro operador assumir a triagem com exclusividade.
- Após a assunção, permite enviar e receber texto e anexos e editar os campos
  produzidos pela LLM, com auditoria.

## 3. Fluxo principal

1. O Collector identifica uma OS elegível durante o consumo do Change Stream
   e grava a intenção na outbox, na mesma transação do estado da OS e do
   resume token.
2. O worker interno da Master API drena a outbox e solicita o início da
   triagem diretamente ao caso de uso.
3. A Master API aceita a solicitação uma única vez por OS elegível e persiste
   o comando do template inicial na mesma transação.
4. O canal reivindica o comando por polling, envia o template e reporta o
   resultado pelo `complete`.
5. Mensagens e mídias recebidas são encaminhadas à Master API.
6. A Master API deduplica o evento, armazena os anexos, consulta a LLM quando
   aplicável e atualiza a triagem.
7. Ao completar o checklist, a Master API libera a triagem ao pool e notifica
   os operadores autorizados.
8. O primeiro operador que assumir obtém exclusividade. A LLM deixa de
   responder nesse atendimento.

O texto do template Midea e os textos dos lembretes de 24 e 36 horas dependem
de revisão do PO antes da implementação e validação de envios reais.

## 4. Autoridade da máquina de estados

A Master API controla os nove estados definidos pelo produto:

| Estado | Responsabilidade arquitetural |
|---|---|
| `AGUARDANDO_ENVIO` | Triagem aceita, aguardando execução do envio inicial |
| `CONTATO_INICIADO` | Envio inicial confirmado, aguardando resposta |
| `EM_COLETA` | Conversa automatizada ativa |
| `INFORMACOES_SUFICIENTES` | Checklist mínimo validado |
| `LIBERADA_AO_POOL` | Disponível aos operadores autorizados |
| `ASSUMIDA_POR_OPERADOR` | Atendimento exclusivo do operador |
| `EXPIRADA` | Checklist incompleto após 48 horas; permanece visível no pool |
| `FALHA_DE_CONTATO` | Falha definitiva no contato inicial; permanece visível no pool |
| `AGUARDANDO_INTERVENCAO` | LLM interrompida após o limite de tentativas inválidas ou desvios |

`EXPIRADA` e `AGUARDANDO_INTERVENCAO` são caminhos independentes.
Uma triagem expirada não transita automaticamente para
`AGUARDANDO_INTERVENCAO`.

Eventos do canal são fatos de transporte. Eles só alteram a máquina de
estados depois de validados pela Master API.

Cinco falhas transitórias consecutivas no envio inicial encerram a retentativa
automática e movem a triagem para `FALHA_DE_CONTATO`.

## 5. Contrato Canal ↔ Master API

### 5.1 Envelope comum

Eventos e comandos carregam, no mínimo:

- versão do contrato;
- identificador do tenant;
- identificador da triagem e da OS;
- identificador da conexão ou número lógico do canal;
- `correlation_id`;
- chave de idempotência;
- data e hora do fato em UTC;
- tipo e payload específico;
- erro estruturado, quando aplicável, com código, indicação de retentativa e
  descrição segura.

O contrato não transporta credenciais de sessão do WhatsApp. Logs não devem
expor conteúdo de mensagens, documentos ou tokens.

### 5.2 Eventos do canal para a API

- `MessageReceived`: texto recebido, identificador da mensagem do canal e
  referência à conversa.
- `MediaReceived`: metadados da mídia e conteúdo encaminhado para ingestão
  controlada pela API.
- `DeliveryStatusUpdate`: confirmação, entrega, leitura ou falha de uma
  mensagem enviada.
- `ChannelFailure`: indisponibilidade ou erro operacional do canal.

### 5.3 Comandos da API para o canal

- `SendTemplate`: envio do template inicial fixo aprovado pelo PO.
- `SendMessage`: envio de respostas da LLM, lembretes aprovados ou mensagens
  do operador.

O processo do canal usa:

- `POST /api/internal/triagens/canal/claim`, com escopo
  `channel.command.claim`, para obter um comando elegível e adquirir seu
  lease;
- `POST /api/internal/triagens/canal/complete`, com escopo
  `channel.command.complete`, para informar sucesso, falha transitória ou
  falha definitiva.

Tenant e integração vêm somente da credencial de serviço. Lease expirado
torna o comando elegível novamente, preservando a chave de idempotência. O
canal devolve o resultado correlacionado; timeout não é interpretado como
falha definitiva antes da política de retentativa. Na quinta falha transitória
consecutiva, a Master API encerra o comando e move a triagem para
`FALHA_DE_CONTATO`, conforme o ADR-042.

## 6. Idempotência e consistência

- O gatilho de início usa uma chave estável por tenant, integração e OS.
- A Master API registra o aceite antes de emitir o efeito externo.
- Reentregas do mesmo gatilho retornam o resultado já conhecido ou continuam
  o processamento pendente.
- Cada comando de saída tem chave de idempotência estável.
- Eventos recebidos são deduplicados pelo identificador do canal e pela chave
  de idempotência no escopo do tenant.
- `correlation_id` permite rastrear comando, resposta do canal e evento de
  entrega.
- Falhas transitórias podem ser repetidas; falhas definitivas são convertidas
  em fatos para decisão da Master API.

Os detalhes estão formalizados no ADR-039.

O agregado e a chave de idempotência são persistidos no PostgreSQL da Master
API, com restrição única composta para arbitrar chamadas concorrentes,
conforme o ADR-040. O MongoDB permanece apenas como fila/gatilho.

A passagem entre o Consumer global e a Master API usa a outbox transacional do
ADR-041. A rota `POST /api/internal/triagens/start` permanece disponível
somente para suporte, QA e reprocessamento manual com credencial escopada.

## 7. Anexos e interação com a LLM

- O canal encaminha a mídia à Master API; a API controla o armazenamento
  durável no S3 e associa o anexo à triagem.
- A LLM recebe somente o conteúdo ou referência temporária estritamente
  necessária à tarefa.
- O painel obtém anexos por autorização da Master API, sem acesso público ao
  bucket.
- Prazo de retenção, exclusão, base legal e região de processamento continuam
  bloqueios de produção.
- Uma mídia rejeitada continua pendente. Após três tentativas inválidas do
  mesmo item, a API move a triagem para `AGUARDANDO_INTERVENCAO`.

## 8. Notificações e pool

- A Fase 1 tem um pool compartilhado por posto.
- A Master API produz notificação in-app em tempo real e atualiza o contador
  do pool para liberação, expiração, falha de contato e nova mensagem antes
  da assunção.
- Não há push web no escopo definido.
- A assunção é exclusiva e deve ser protegida contra concorrência.

## 9. Desenvolvimento e produção

### Desenvolvimento

- Baileys local, com número de teste.
- Sessão em arquivos locais.
- Stack AWS operada sob demanda conforme ADR-036.

### Produção

- Operação contínua e topologia do canal permanecem `Proposed` no ADR-035.
- Persistência em volume EBS permanece proposta no ADR-038.
- Parecer jurídico/LGPD, contrato com fornecedor de LLM e política de
  retenção são bloqueios.
- Sugestão de peças permanece desabilitada até existir catálogo confiável.

## 10. Decisões ainda pendentes

- Revisão do template Midea e dos textos dos lembretes pelo PO.
- Catálogo de peças e sintomas.
- Política de retenção e tratamento LGPD.
- Operação always-on e responsabilidade pelo custo de produção.
- Contrato e fornecedor da LLM.
