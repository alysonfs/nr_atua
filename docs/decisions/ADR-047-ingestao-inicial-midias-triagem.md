# ADR-047 - Ingestão Inicial de Mídias Inbound da Triagem

## Status

**Accepted** — direção definida por `software-architect` e `aws-architect`,
com escopo e regras de produto confirmados pelo Product Owner em 2026-10-21.

## Contexto

O [ADR-043](ADR-043-ingestao-message-received-e-transicao-em-coleta.md)
define a ingestão de mensagens textuais e deixa mídias fora de escopo. O
[ADR-046](ADR-046-pipeline-midia-transcricao-audio-triagem.md) define a
direção do pipeline de mídia e transcrição, mas não autorizava sua
implementação.

O Product Owner autorizou implementar o primeiro incremento, limitado ao
recebimento, validação e armazenamento de mídia inbound. Esta autorização
não abrange transcrição, alterações operacionais na AWS, provisionamento,
IAM, credenciais de serviço ou deploy.

## Decisão

### Contrato e autorização

- O Channel envia um evento `MediaReceived` para
  `POST /api/internal/triagens/canal/midias`, em `multipart/form-data`, com
  metadados JSON e um arquivo binário.
- O evento inclui os campos do inbound textual (`contractVersion`,
  `channelMessageId`, `channelIdempotencyKey`, `correlationId`,
  `senderPhone`, `conversationKind`, `fromMe`, `receivedAtUtc`) e os campos
  da mídia (`mediaKind`, `contentType`, `sizeBytes`, `sha256`, `isVoiceNote`,
  `durationSeconds` opcional e `caption` opcional).
- Uma mensagem com mídia é enviada somente como `MediaReceived`; o Channel
  não duplica o mesmo evento na rota de mensagens textuais.
- A rota exige o escopo exclusivo `channel.media.ingest`. Tenant e
  integração são obtidos exclusivamente dos claims da credencial autenticada.
- Eventos de grupo e enviados pelo próprio posto são ignorados, segundo as
  regras existentes do Channel.

### Tipos, tamanho e validação

- São aceitos áudio, imagem, vídeo e documentos PDF. Outros documentos e
  figurinhas não são aceitos.
- O limite é de 16 MiB por arquivo.
- A API valida metadados, limite, tipo declarado e assinatura do conteúdo,
  além de recalcular o SHA-256; o hash informado pelo Channel não é confiado.
- Arquivos rejeitados por tamanho ou tipo não são exibidos ao operador nesta
  etapa. O resultado técnico deve ser registrado sem conteúdo ou dados
  pessoais nos logs.

### Associação, idempotência e estado

- A associação usa o mesmo resolvedor por tenant, integração e telefone do
  inbound textual.
- Se não houver exatamente uma triagem correspondente, a API pode registrar
  o resultado e os metadados técnicos, mas não persiste o binário.
- Havendo uma única triagem, o binário é persistido mesmo se ela estiver fora
  dos estados `CONTATO_INICIADO` e `EM_COLETA`.
- Uma mídia válida recebida em `CONTATO_INICIADO` move a triagem para
  `EM_COLETA`. Outros estados não são alterados pela chegada da mídia.
- A idempotência usa `(tenantId, channelIdempotencyKey)`. Uma repetição com o
  mesmo conteúdo retorna o resultado já conhecido; a mesma chave com
  conteúdo diferente resulta em conflito.

### Persistência e retenção

- A Master API controla os metadados e o armazenamento do binário em S3
  privado. A implementação usa uma abstração de storage com adaptador S3 e
  implementação fake para testes locais; o SDK da AWS não vaza para os
  contratos de domínio.
- As chaves de objetos ficam sob o prefixo
  `tenants/{tenantId}/inbound-media/{mediaId}` e não incluem telefone ou
  outros dados pessoais do consumidor.
- O objeto é gravado de forma idempotente (`PutIfAbsent`); conflito e falha
  transitória são resultados distintos.
- O marco de retenção é `receivedAtUtc`: cinco anos até expiração e mais um
  ano até elegibilidade para expurgo, conforme ADR-045. O job ativo da
  aplicação foi implementado para apagar primeiro o objeto e depois os
  metadados; não há regra S3 Lifecycle autoritativa.
- A transcrição não é iniciada neste incremento.

## Limites desta autorização

- É permitido implementar e testar localmente a API, o Channel, a
  abstração de storage, o adaptador S3 e os testes com storage fake.
- Em 2026-10-21, o Product Owner também autorizou continuar com as pendências
  de teste: a migration `20261021194516_AddTriagemChannelInboundMedia` foi
  aplicada somente ao banco de teste `atua-postgres-mvp` e o job de expurgo
  local foi implementado e aprovado pelo QA.
- Em 2026-10-22, o E2E com S3 foi aprovado. Por decisão do Product Owner
  para o MVP, sem role de teste dedicada, a API local rodou com o profile do
  desenvolvedor. Uma imagem real enviada pelo número de teste foi armazenada
  com `SSE=AES256` e a triagem passou para `EmColeta`. Depois, objeto, linha
  e OS sintética foram removidos. Não houve alteração IAM nem deploy.
  Evidências estão na seção 11 do plano de implementação.
- Até 2026-10-22, nenhuma alteração de banco de produção estava autorizada.
  Nessa data, o Product Owner autorizou a preparação da publicação da API e
  da migration em produção. A verificação read-only confirmou que a conexão
  local, a secret `atua/rds-postgres` usada pela API e o RDS
  `atua-postgres-mvp` apontam para o mesmo banco `atua`; a migration
  `20261021194516_AddTriagemChannelInboundMedia` já estava aplicada nele,
  portanto nenhuma instrução DDL foi executada nessa autorização.
- Em 2026-10-22, o deploy da infraestrutura concedeu à role da API somente
  `s3:PutObject` e `s3:DeleteObject` nos objetos do bucket persistente
  `atua-462991286554-media-triagem`.
- A API foi redeployada a quente em 2026-10-22 a partir do commit
  `eae1edc05e2fb609a83b397c06d6ffae9c3c1289`. O serviço ficou
  `active (running)`, o MD5 local/remoto coincidiu, `GET /health` retornou
  200 e a rota de mídia sem credenciais retornou 401. O E2E remoto de mídia
  permanece pendente.
- A transcrição continua condicionada aos gates do ADR-046, incluindo
  amostra autorizada e desidentificada, avaliação de qualidade e dados de
  volume/custo.

## Alternativas consideradas

- Reutilizar `channel.message.ingest`: rejeitada, pois a autorização de mídia
  é independente e não deve herdar permissões textuais.
- Persistir binário quando a associação com a triagem é ambígua: rejeitada
  para evitar armazenar mídia pessoal sem vínculo confirmado.
- Usar somente storage fake: rejeitada, pois não entrega o adaptador S3
  necessário ao destino do primeiro incremento.

## Consequências

- A triagem pode receber áudio, imagem, vídeo e PDF sem depender da
  transcrição.
- O operador continua sendo responsável por interpretar as mídias; esta
  decisão não altera checklist, agendamento, cancelamento ou conclusão da OS.
- O uso real do S3 depende de configurar posteriormente a identidade de
  runtime com menor privilégio e executar o teste de integração autorizado.
- O job de expurgo está implementado localmente, mas não foi implantado em
  serviço remoto. A política de backups e a validação jurídica da retenção
  permanecem pendentes.

## Agentes envolvidos

- Product Owner
- `orchestrator`
- `software-architect`
- `aws-architect`

## Data

2026-10-21

## Relações

- Complementa os [ADRs 043](ADR-043-ingestao-message-received-e-transicao-em-coleta.md),
  [045](ADR-045-retencao-anexos-e-transcricao-audios-triagem.md) e
  [046](ADR-046-pipeline-midia-transcricao-audio-triagem.md).
