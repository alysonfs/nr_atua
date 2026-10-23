# Channel Node (WhatsApp) — comportamento multi-tenant

Estado: vigente na fase 1 (2026-10-23). Complementa a
[arquitetura da triagem](./triagem-whatsapp-fase-1-arquitetura.md), o
[ADR-042](../decisions/ADR-042-polling-claim-complete-para-canal-whatsapp.md),
o [ADR-049](../decisions/ADR-049-pareamento-whatsapp-qr-office.md) e o
[ADR-050](../decisions/ADR-050-deploy-channel-node-ec2-api.md).

## Resumo

**Um processo do Channel atende exatamente um tenant, uma integração e um
número de WhatsApp.** O Channel não sabe qual é o tenant: ele não tem
`TenantId` na configuração. Quem define o tenant é a API, a partir das
credenciais de serviço que o processo apresenta.

## Como o tenant é determinado

1. Cada credencial de serviço (`service_credentials`) pertence a um par
   **tenant + integração** e tem um escopo único:

   | Variável do Channel | Escopo |
   | --- | --- |
   | `ChannelWorker__ClaimServiceToken` | `channel.command.claim` |
   | `ChannelWorker__CompleteServiceToken` | `channel.command.complete` |
   | `ChannelWorker__MessageIngestServiceToken` | `channel.message.ingest` |
   | `ChannelWorker__MediaIngestServiceToken` | `channel.media.ingest` |
   | `ChannelWorker__SessionReportServiceToken` | `channel.session.report` (opcional) |

2. A API autentica o token e injeta as claims `tenant_id` e
   `integration_id`. Todos os endpoints internos do Channel filtram por esse
   par:
   - `claim` só entrega comandos de envio desse tenant e integração;
   - mensagens e mídias recebidas são vinculadas apenas a triagens desse par;
   - o estado do QR é guardado por tenant + integração e só OWNER/ADMIN desse
     tenant o leem no Office.
3. Os cinco tokens precisam ser distintos e do **mesmo** tenant e integração.
   Misturar tokens de tenants diferentes no mesmo processo faria o número de
   um posto enviar mensagens de outro — é responsabilidade de quem provisiona
   (scripts `infra/scripts/add-channel-*-credential.sh`) não fazer isso.

## O que fica isolado por processo

| Recurso | Onde | Observação |
| --- | --- | --- |
| Sessão Baileys (pareamento) | `ChannelWorker__SessionPath` | Diretório próprio por processo; perde-se no `make down`. |
| Número de WhatsApp | Sessão pareada pelo QR | O número é definido por quem lê o QR, não por configuração. |
| Credenciais | `/etc/atua-channel.env` (0600) | Um env file por processo. |
| Fila de mídia | Memória do processo | Capacidade `ChannelWorker__MediaQueueCapacity` (padrão 20). |
| Originais de mensagens outbound para retry | Memória do processo | Até 256 mensagens por sessão, por no máximo 1 hora. |

## Recuperação de mensagens outbound

O Channel fornece ao Baileys o callback `getMessage`, que recupera o payload
original quando um aparelho solicita retransmissão. Sem esse callback, o
default do Baileys 6.7.24 retorna `undefined`: um pedido de retry não consegue
recuperar a mensagem e pode deixar o aparelho em "Aguardando mensagem".
Isso demonstra uma lacuna no código anterior, não comprova por si só a causa
de todos os episódios observados.

Decisão explícita do PO em 2026-10-23: manter esses originais **somente em
memória**, por no máximo **1 hora**, com limite de **256 mensagens**. O limite
remove a mensagem mais antiga; consultas e novas tentativas enquanto a entrada
existe não estendem sua validade. A expiração remove a entrada mesmo sem novo
tráfego. Nenhum texto é persistido em arquivo pelo cache.

- O cache pertence à sessão do processo, nunca é global entre tenants.
- A consulta exige o ID e um destinatário/alias PN ou LID conhecido, sem
  disponibilizar o conteúdo para outro destinatário.
- Reconexões do socket no mesmo processo mantêm o cache. Encerramento,
  logout ou reinício do processo o perdem; `make down` também.
- Se o aparelho pedir retry depois da expiração, da remoção por capacidade
  ou de um reinício, o original não estará disponível. Essa limitação foi
  aceita pelo PO na escolha de memória em vez de arquivo privado por sessão.
- O pedido de retry não cria outra OS ou comando; o SDK retransmite usando
  o mesmo identificador da mensagem.

O retorno `Completed`/`Success` do comando confirma **ACK do servidor
WhatsApp**, não entrega legível nem leitura pelo consumidor. O cache viabiliza
recuperação, mas não constitui prova de entrega. Eventos duráveis de entrega
e leitura ainda não são persistidos na API.

Os diagnósticos de retry (`whatsapp_retry_requested`,
`whatsapp_retry_message_available`, `whatsapp_retry_message_unavailable`,
`whatsapp_retry_original_message_missing`, `whatsapp_retry_send_failed`,
`whatsapp_retry_limit_reached`) e alertas genéricos do SDK não imprimem texto,
QR, JID, telefone, chaves ou stack do SDK. Uma entrada disponível indica que
o original foi fornecido ao SDK, não que sua retransmissão foi entregue.

## Situação atual (1 tenant)

- Um único serviço systemd `atua-channel`, na EC2 da API, para o tenant
  `01a0888b-acd8-773b-93b7-7362973d7ea8` e sua integração iService.
- Consumo observado: cerca de 64 MB de RAM por processo.

## Como ficaria com N tenants (direção, não implementado)

Caminho mais simples, sem mudar código:

1. Transformar o unit em template `atua-channel@<slug>.service`, com
   `EnvironmentFile=/etc/atua-channel/<slug>.env` e
   `ChannelWorker__SessionPath=/var/lib/atua-channel/<slug>/session`.
2. Para cada novo tenant: criar as cinco credenciais, gravar o env file,
   habilitar a instância do unit e pedir ao OWNER/ADMIN para ler o QR no
   Office.
3. Ajustar os scripts `deploy-channel`/`redeploy-channel`/`logs-channel`
   para receber o `<slug>`.

Limites e pontos de atenção:

- **Memória:** cada processo soma ~64 MB; a t3.micro (~1 GB, hoje sem swap)
  comporta poucos tenants junto da API. Acima de ~3–5 tenants, mover o
  Channel para instância própria (decisão do `aws-architect`).
- **Vários números por tenant:** a premissa do épico (números por pool ou
  campanha) **não é atendida hoje**: as credenciais são por
  tenant + integração, não por número. Exigiria um identificador de número
  na credencial e no roteamento — tema de ADR futuro.
- **Um processo multiplexado** (várias sessões Baileys num único Node) reduz
  memória, mas mistura falhas e credenciais de tenants; só vale com volume
  maior. Não recomendado na fase 1.
- **`make down`** apaga todas as sessões; cada tenant precisa reler o QR.
