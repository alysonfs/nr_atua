# ADR-049 - Pareamento WhatsApp via QR a partir do Office

## Status

**Accepted** — implementado em 2026-10-22.

## Contexto

O Channel WhatsApp (`apps/channel-node`, Node/TS + Baileys) será publicado
na mesma EC2 da API. A cada `make down` a EC2 e seu EBS são **destruídos**,
o que apaga o diretório de credenciais da sessão Baileys. O tenant precisa
ler um novo QR Code para reparear o WhatsApp sempre que isso acontece.

Hoje o QR só é impresso no terminal (`qrcode-terminal`), inacessível para
quem administra o tenant a partir do Office. É preciso um caminho para que
**OWNER** e **ADMIN** do tenant leiam o QR pela tela de Configurações, sem
acesso ao terminal da EC2.

## Decisão

- O Channel reporta cada transição de estado da sessão Baileys —
  `qr_pending`, `connected`, `disconnected`, `logged_out` — para
  `POST /api/internal/channel/session`, autenticado com a mesma credencial
  de serviço (tenant + integração via claims do token) já usada em
  `TriagemChannelEndpoints`, com um escopo exclusivo
  `channel.session.report`. O payload traz o texto bruto do QR (nunca a
  imagem) e o instante UTC da transição; `phoneNumber` só é enviado junto
  de `connected`.
- A API guarda **apenas o último estado por tenant+integração**, em um
  `ConcurrentDictionary` singleton em memória
  (`InMemoryWhatsAppSessionStateStore`) — não há persistência em banco.
  Não se justifica uma tabela para um dado efêmero, de vida curta (o QR
  expira em segundos) e que é sempre substituído pelo próximo reporte; se
  isso mudar (por exemplo, múltiplas instâncias da API atrás de um load
  balancer sem afinidade), a store pode ser trocada por algo
  compartilhado (ex.: tabela ou cache distribuído) sem alterar os
  contratos de `IWhatsAppSessionStateStore`.
- O QR expira **~60 segundos** depois do instante reportado
  (`ReportWhatsAppSessionStateUseCase.QrTimeToLive`). Passado esse prazo, a
  consulta volta a refletir o mesmo estado (`qr_pending`) mas sem o campo
  `qr` — o Office deve voltar a mostrar "aguardando novo QR".
- O Office consulta o estado via
  `GET /api/tenants/{tenantId}/whatsapp-session`, autenticado por sessão de
  navegador (`BrowserSession`) e restrito a membros **OWNER/ADMIN** do
  tenant — mesma verificação de papel usada em
  `TriagemMessageTemplateService.ValidateAccessAsync`. Sem estado
  reportado ainda, a resposta é `state: "unknown"` (não é erro).
- **Resolução da integração WhatsApp do tenant**: o domínio de integrações
  não distingue "WhatsApp" de outros provedores por um campo explícito.
  Em vez de adicionar esse campo (mudança de modelo fora do escopo desta
  tarefa), a integração é identificada pelas `ServiceCredentials` ativas
  com o escopo `channel.command.claim` — o mesmo escopo que já autoriza o
  processo do Channel a reivindicar comandos daquela integração. Se o
  tenant tiver mais de uma integração com esse escopo, o endpoint exige
  `integrationId` explícito na query string (`400 integration_id_required`
  quando ambíguo); um `integrationId` que não pertença ao tenant resulta em
  `404 integration_not_found`. Essa é uma decisão pragmática — se o modelo
  de integrações vier a marcar o tipo de provedor explicitamente, a
  resolução deve passar a usar esse campo.
- O terminal continua imprimindo o QR via `qrcode-terminal`, mas isso
  passa a ser **opcional e só para desenvolvimento**
  (`ChannelWorker__PrintQr`, padrão ligado em dev). O conteúdo do QR nunca
  é escrito em log estruturado, nem no Channel nem na API.

## Consequências

- O tenant consegue reparear o WhatsApp sozinho pelo Office após qualquer
  `make down`, sem depender de alguém com acesso à EC2 para ler o
  terminal.
- O estado de pareamento não sobrevive a um restart do processo da API
  (store em memória). Isso é aceitável: o próprio Channel reconecta e
  reporta um novo estado (geralmente um novo `qr_pending`) logo depois de
  qualquer reinício, já que a sessão Baileys também se perde junto com o
  EBS.
- Se a API rodar com mais de uma instância sem afinidade de sessão, o
  reporte do Channel e a consulta do Office podem acabar em instâncias
  diferentes e o Office veria `state: "unknown"` mesmo com um reporte
  recente. Hoje a API roda em uma única instância (EC2), então isso não é
  um problema imediato — fica registrado como limitação conhecida da
  store em memória.
- Novo escopo de credencial de serviço (`channel.session.report`) e nova
  policy de autorização (`ChannelSessionReport`) devem ser provisionados
  junto com a credencial do Channel (fora do escopo desta implementação —
  ver pendências de infraestrutura/CDK, não alteradas aqui).

## Segurança do QR

- O QR nunca é logado (nem no Channel, nem na API) — apenas mantido em
  memória por até 60s e exposto no corpo da resposta HTTP sobre uma
  sessão de navegador autenticada.
- Apenas OWNER e ADMIN do tenant conseguem ler o QR; isolamento por tenant
  é obrigatório (a consulta nunca atravessa tenants).
- Passado o TTL, o QR deixa de ser servido mesmo que ninguém tenha
  reportado um novo estado — reduz a janela em que um QR capturado por
  alguém sem autorização ainda seria válido para parear uma sessão.
