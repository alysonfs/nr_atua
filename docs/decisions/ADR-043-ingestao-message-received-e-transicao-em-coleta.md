# ADR-043 - Ingestão de MessageReceived e Transição para EM_COLETA

## Status

**Accepted** — contrato aprovado em 2026-10-17. Mensagens ambíguas são
persistidas sem vínculo automático e aguardam resolução manual futura.

## Contexto

O ADR-042 definiu como o Channel busca e conclui comandos de saída
(`SendTemplate`/`SendMessage`) por `claim`/`complete`, sem expor porta de
entrada. A arquitetura da Fase 1 (seção 5.2) já previu os eventos de entrada
`MessageReceived`, `MediaReceived`, `DeliveryStatusUpdate` e `ChannelFailure`,
e o ADR-039 já definiu o princípio de idempotência para mensagens recebidas,
mas nenhum dos dois define:

- o transporte concreto pelo qual o Channel entrega `MessageReceived` à
  Master API;
- o formato mínimo do evento;
- como a Master API associa uma mensagem recebida a uma triagem, já que o
  Channel não conhece `TriagemId` para tráfego de entrada (só conhece
  `TriagemId` para comandos que ele mesmo reivindicou);
- o que torna uma mensagem recebida uma "mensagem aceita" capaz de mover a
  triagem de `CONTATO_INICIADO` para `EM_COLETA`;
- como tratar grupos, mensagens do próprio número conectado e mensagens sem
  conteúdo substantivo (vazias ou protocolares).

O plano de implementação (`PLANO-triagem-templates-channel-baileys.md`,
seção 6) lista `MessageReceived` com deduplicação como o próximo incremento
depois do envio inicial, seguido da transição `CONTATO_INICIADO` →
`EM_COLETA`. Este ADR cobre exatamente essa fronteira, sem entrar em mídia
(S3), checklist determinístico ou LLM — itens explicitamente adiados pelo
plano.

## Decisão

### 1. Transporte: push do Channel para a Master API

O Channel continua sem porta de entrada (ADR-042 permanece válido). Para
eventos de entrada, o Channel passa a fazer uma segunda chamada de saída,
simétrica ao papel já desempenhado por `complete`: ele relata à Master API o
que recebeu do WhatsApp, em vez de a Master API buscar algo do Channel.

Novo endpoint interno:

```text
POST /api/internal/triagens/canal/mensagens
```

- Protegido por um escopo próprio, `channel.message.ingest`, com credencial de
  serviço exclusiva — não reutiliza `channel.command.claim` nem
  `channel.command.complete` (mesmo princípio de isolamento de escopos do
  ADR-042 e do Incremento 3 do plano).
- Tenant e integração vêm exclusivamente dos claims da credencial
  autenticada, nunca do corpo da requisição — mesmo princípio do ADR-042.
- Não há `claim`/lease neste endpoint: cada mensagem recebida é um fato já
  ocorrido no WhatsApp: o Channel apenas relata, a Master API decide o efeito.
  Não existe "reivindicar" uma mensagem recebida.

### 2. Formato mínimo do evento

O corpo da requisição carrega, no mínimo, os campos já exigidos pelo envelope
comum (arquitetura, seção 5.1) e pelo ADR-039, aplicados a este evento:

- `contractVersion`;
- `channelMessageId`: identificador da mensagem atribuído pelo WhatsApp (ex.:
  `key.id` do Baileys);
- `channelIdempotencyKey`: chave estável de deduplicação — recomenda-se
  derivá-la do par `(remoteJid, channelMessageId, fromMe)`, pois o
  identificador do WhatsApp sozinho não garante unicidade entre conversas
  diferentes;
- `correlationId`;
- `senderPhone`: telefone do remetente em dígitos internacionais, já
  normalizado pelo Channel (mesma normalização usada para o destinatário no
  envio, sem o sufixo de domínio do JID);
- `conversationKind`: `"individual"` ou `"group"` — ver seção 4;
- `fromMe`: booleano — mensagem originada no próprio número conectado (ver
  seção 4);
- `receivedAtUtc`: instante em que o WhatsApp entregou a mensagem ao Channel;
- `kind`: `"text"` ou `"empty"` neste incremento. `MediaReceived` permanece um
  evento e um contrato à parte (fora de escopo aqui); uma mensagem que só
  contém mídia, sem texto, não é relatada por este endpoint — o Channel só
  envia aqui o que tem conteúdo textual relatável.
- `text`: conteúdo textual, quando `kind = "text"`.

O contrato **não** transporta `triagemId`: o Channel não o conhece para
tráfego de entrada. A Master API resolve a triagem a partir de
`(tenantId, integrationId, senderPhone)`, conforme a seção 3.

Dado de retenção: o campo `senderPhone` é dado pessoal. Não deve aparecer em
logs do Channel nem da Master API (mesma regra já aplicada a
`RecipientPhone` no ADR-042). Pode ser persistido, pois a correlação
conversa↔OS depende dele (premissa do épico, seção 7).

### 3. Resolução da triagem pelo telefone do remetente

A Master API localiza a triagem elegível para o evento buscando, no escopo
do tenant e da integração autenticados, uma triagem cujo
`RecipientPhone` snapshot (ADR-042) corresponda ao `senderPhone` normalizado
e cujo status seja `CONTATO_INICIADO` ou `EM_COLETA`.

- Nenhuma triagem correspondente: o evento é reconhecido (200), sem efeito
  algum sobre qualquer triagem. Não é um erro — pode ser número desconhecido,
  mensagem tardia após expiração, ou ruído. A Master API pode registrar o
  evento para fins de suporte, mas sem vinculá-lo a uma triagem.
- Exatamente uma triagem correspondente: o evento é processado contra ela
  (seção 4).
- Mais de uma triagem correspondente simultaneamente ativa para o mesmo
  telefone: nenhuma triagem é selecionada. O evento é persistido como
  `Ambigua`, sem vínculo a uma OS/triagem e sem transição de estado. A lista
  de triagens candidatas fica registrada para resolução manual futura no
  painel do operador; alerta e resolução no painel não fazem parte deste
  incremento.

Este incremento não introduz `MediaReceived`, checklist ou LLM. Para uma
triagem já em `EM_COLETA`, uma mensagem aceita subsequente é apenas
deduplicada e registrada; ela não altera o checklist nem produz novos
efeitos de estado neste incremento. Triagens em `LIBERADA_AO_POOL` ou
`ASSUMIDA_POR_OPERADOR` recebendo mensagem seguem as regras já resolvidas no
épico (seção 9, pergunta 11) — permanecem fora deste incremento e devem ser
implementadas quando o pool e o painel do operador consumirem este mesmo
evento.

### 4. Critério de "mensagem aceita" (fronteira `CONTATO_INICIADO` → `EM_COLETA`)

Recomendação do `software-architect`:

Uma mensagem recebida só é candidata a mover a triagem de
`CONTATO_INICIADO` para `EM_COLETA` quando, simultaneamente:

1. **Não é de grupo.** `conversationKind = "individual"`. O vínculo
   conversa↔OS é 1:1 pelo número cadastrado na OS (premissa do épico, seção
   7); uma conversa de grupo não corresponde a essa relação, e o remetente
   dentro de um grupo (`participant`) não é necessariamente o consumidor
   titular do número. O Channel não deve nem relatar mensagens de grupo a
   este endpoint; e, em defesa de profundidade, a Master API rejeita
   (sem efeito, sem erro — apenas ack) qualquer evento com
   `conversationKind = "group"` caso chegue mesmo assim.
2. **Não é `fromMe`.** Mensagens enviadas pelo próprio número conectado do
   posto (ex.: o operador responde manualmente pelo aplicativo WhatsApp, fora
   do fluxo de `SendMessage` da Master API) não são fala do consumidor e não
   fazem parte da coleta. `fromMe = true` é sempre reconhecido (ack) e nunca
   aciona transição ou checklist.
3. **Tem conteúdo substantivo.** `kind = "text"` com `text` não vazio após
   remoção de espaços. Mensagens vazias, protocolares, de recibo, reação ou
   indicação de digitação não chegam a este endpoint como `kind = "text"`;
   quando o Channel as repassar mesmo assim (ex.: para fins de observação
   futura), `kind = "empty"` nunca aciona transição.

Quando as três condições são satisfeitas e a triagem correspondente está em
`CONTATO_INICIADO`, a Master API chama `Triagem.IniciarColeta(now)` (já
implementado no domínio) na mesma operação que registra a deduplicação do
evento. Esta é a mesma transição já modelada pela classe `Triagem` — este
ADR define apenas o fato de entrada que a aciona, não uma transição nova.

Mídia (foto/vídeo sem legenda) **não é decidida por este ADR**: ela chega por
`MediaReceived`, um evento e contrato ainda não implementados. Quando esse
incremento existir, mídia sem legenda deverá contar como mensagem aceita
pelo mesmo motivo que texto conta (é conteúdo substantivo do consumidor), mas
a implementação e o contrato ficam para o incremento de mídia/S3, conforme o
plano.

### 5. Idempotência e deduplicação

Segue o princípio geral do ADR-039, aplicado a este evento:

- Deduplicação pelo par `(tenantId, channelIdempotencyKey)`.
- Reentrega do mesmo evento (reconexão do Channel, retry de rede) retorna o
  resultado já conhecido sem repetir a transição nem contar a mensagem duas
  vezes.
- A implementação materializa o evento em `triagem_channel_messages`, com
  restrição única em `(TenantId, ChannelIdempotencyKey)`. Eventos ambíguos
  armazenam a lista de triagens candidatas em `CandidateTriagemIds`, sem
  associação automática a qualquer delas.
- A operação de deduplicar + (quando aplicável) transicionar é atômica: duas
  entregas concorrentes do mesmo evento não podem produzir duas transições.

### 6. Autenticidade e autorização

- Credencial de serviço com escopo exclusivo `channel.message.ingest`,
  seguindo o mesmo padrão de token hash-only no PostgreSQL já usado para
  `channel.command.claim`/`channel.command.complete`.
- A credencial é distinta das demais e é provisionada pelo script de
  credenciais do Channel.
- Tenant e integração vêm somente dos claims da credencial — o corpo da
  requisição não amplia esse escopo, mesma regra do ADR-042.
- Não há reuso de token entre os três escopos do Channel.

### 7. Semântica de resposta e retry

- `200 OK` com confirmação mínima (eco do `channelMessageId`, resultado,
  triagem vinculada quando única e quantidade de candidatas quando ambíguo)
  — sempre que o evento foi processado, inclusive quando o resultado é "sem
  efeito".
- `400` para payload malformado (campos obrigatórios ausentes).
- `401` para credencial ausente ou inválida.
- Não há `404` nem `409` neste endpoint: não existe uma entidade reivindicada
  por chave que possa "não existir" ou estar "em posse de outro lease" — a
  ausência de triagem correspondente é um resultado de negócio válido (ver
  seção 3), não um erro HTTP.
- O Channel deve reenviar com a mesma `channelIdempotencyKey` em caso de
  falha de rede ou `5xx`. Erros `4xx` não devem ser repetidos indefinidamente
  pelo Channel — indicam payload ou credencial inválidos, não falha
  transitória.
- Este endpoint não tem lease nem `claim`: múltiplas instâncias do Channel
  (se existirem no futuro) podem relatar mensagens concorrentemente sem
  disputa de posse, pois cada relato é independente e a deduplicação ocorre
  pela chave do evento, não por reivindicação exclusiva.

## Motivos

- Mantém o Channel exclusivamente como emissor de conexões de saída, sem
  introduzir uma porta de entrada nem contradizer o ADR-042.
- Reaproveita o padrão de autorização e isolamento de escopo já validado nos
  outros dois endpoints do canal.
- Resolve a triagem pelo telefone porque é a única informação disponível ao
  Channel para tráfego de entrada, e porque o épico já define esse vínculo
  como premissa de produto (seção 7).
- Mantém a Master API como única autoridade da máquina de estados: o Channel
  relata fatos, nunca decide transição.
- Restringe o critério de "mensagem aceita" ao que já é observável sem LLM —
  evita antecipar regras de checklist ou interpretação de conteúdo que
  pertencem a incrementos futuros.

## Alternativas consideradas

### Channel expõe `TriagemId` reconstruindo-o localmente a partir de um cache de conversas

Rejeitada. Exigiria que o Channel mantivesse estado próprio sobre qual
conversa pertence a qual triagem, duplicando uma responsabilidade que já é da
Master API (arquitetura, princípio 1: "A Master API é a autoridade da
triagem e da máquina de estados"). Também reintroduziria um estado que se
perderia a cada reinício do processo do Channel.

### Master API faz polling de mensagens recebidas, em vez do Channel relatar

Rejeitada. Mensagens recebidas só existem no processo do Channel, que é quem
mantém a sessão Baileys; não há onde a Master API faria polling. O relato do
Channel para a API (como já ocorre em `complete`) é a única direção possível
sem o Channel expor uma porta.

### Qualquer mensagem (inclusive de grupo ou `fromMe`) conta como aceita

Rejeitada. Contrariaria a premissa de vínculo 1:1 pelo número cadastrado e
abriria uma transição de estado a partir de tráfego que não representa o
consumidor respondendo à triagem.

### Considerar mídia sem legenda neste mesmo incremento

Rejeitada por ora. Avaliar mídia exigiria definir `MediaReceived`,
armazenamento e metadados — exatamente o escopo que o plano reserva para o
próximo incremento. Antecipar essa regra aqui obrigaria a decidir também
contrato de mídia sem necessidade imediata.

## Consequências

### Positivas

- `MessageReceived` ganha um transporte concreto, compatível com a topologia
  de egress-only do Channel.
- A transição `CONTATO_INICIADO` → `EM_COLETA` passa a ter um gatilho de
  entrada bem definido, sem exigir LLM ou checklist antecipados.
- Grupos e mensagens do próprio número ficam explicitamente fora da máquina
  de estados, evitando falsos positivos de "consumidor respondeu".
- O novo endpoint é testável de forma determinística (ack, dedup, resolução
  por telefone, critério de aceitação), sem depender do Baileys real.

### Negativas

- Mais um escopo e uma credencial de serviço a provisionar e rotacionar.
- A resolução por telefone introduz uma ambiguidade real quando o mesmo
  consumidor tem mais de uma triagem ativa simultânea; por decisão do PO,
  essas mensagens ficam sem associação até resolução manual no futuro painel.
- Mensagens e texto são dados pessoais; a política de retenção no banco
  primário está definida no ADR-044. Retenção de anexos/backups e exclusão
  antecipada pelo fluxo geral de dados do tenant ainda precisam ser cobertas
  antes da produção.
- O critério "sem grupo, sem `fromMe`, com texto" ainda não cobre mídia; o
  primeiro contato por foto/vídeo sem texto continuará em `CONTATO_INICIADO`
  até o incremento de mídia existir. Isso é uma limitação temporária
  conhecida, não uma falha deste ADR.

## Decisão do Product Owner — ambiguidade por telefone

Em 2026-10-17, o Product Owner escolheu não resolver automaticamente quando
mais de uma triagem da mesma tenant/integração estiver ativa para o mesmo
telefone. A Master API persiste o evento como `Ambigua`, sem selecionar nem
vincular a mensagem a uma triagem e sem avançar estado. A lista de candidatas
é persistida para a resolução manual futura pelo pool/painel; a interface e
notificação para essa resolução ficam fora deste incremento.

Enquanto o painel não existir, o evento ambíguo fica retido sem ação
automática. Isso é um bloqueio operacional conhecido para esse caso, não um
motivo para escolher uma OS arbitrariamente.

## Agentes envolvidos

- `orchestrator`
- `software-architect`
- Product Owner (decisão de ambiguidade, 2026-10-17)

## Data

2026-10-17

## Relações

- Complementa o ADR-037.
- Complementa o ADR-039.
- Complementa o ADR-040.
- Complementa o ADR-042.
- Não substitui decisões existentes.
