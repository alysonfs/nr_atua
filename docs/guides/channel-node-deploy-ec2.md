# Deploy do Channel WhatsApp na EC2 (produção)

Este guia descreve como publicar e operar o Channel WhatsApp
(`apps/channel-node`, Node 22/TS + Baileys) na **mesma instância EC2 da
API** (`atua-api-master`), conforme decisão do PO e
[ADR-049](../decisions/ADR-049-pareamento-whatsapp-qr-office.md) e
[ADR-050](../decisions/ADR-050-deploy-channel-node-ec2-api.md). Não há
instância nem volume EBS dedicados ao Channel: ele roda como um processo
systemd adicional (`atua-channel`) ao lado de `atua-api`, no disco raiz da
mesma EC2.

Para desenvolvimento/teste local (contra a API via túnel SSH), use
[channel-node-teste-local.md](./channel-node-teste-local.md) em vez deste
guia.

## Pré-requisitos

- `AtuaComputeStack` no ar (`make status`), topologia atual ou colocada —
  ambas têm `atua-api-master` como alvo.
- Chave SSH local em `~/.ssh/atua-mvp-key.pem` (`make retrieve-keypair`).
- Seu IP público autorizado no Security Group da instância (porta 22).
- `pnpm` instalado localmente (os scripts rodam
  `pnpm --filter @atua/channel-node build` e
  `pnpm --filter @atua/channel-node deploy --prod`).
- Migrations inbound aplicadas e uma integração de tenant já provisionada
  com as credenciais de serviço descritas abaixo.
- `AWS_PROFILE` explícito em todo comando (ex.: `AWS_PROFILE=moldato`).

## Credenciais de serviço necessárias

O Channel autentica cada chamada à API com um token de serviço (bearer),
um por escopo. Os cinco escopos usados pelo Channel
(ver `apps/api/Atua.Api/Infrastructure/Security/ServiceCredentialAuthenticationHandler.cs`
e `apps/channel-node/src/config.ts`):

| Escopo                       | Variável de ambiente                             | Obrigatório |
|-------------------------------|---------------------------------------------------|:-----------:|
| `channel.command.claim`       | `ChannelWorker__ClaimServiceToken`                 | Sim |
| `channel.command.complete`    | `ChannelWorker__CompleteServiceToken`              | Sim |
| `channel.message.ingest`      | `ChannelWorker__MessageIngestServiceToken`         | Sim |
| `channel.media.ingest`        | `ChannelWorker__MediaIngestServiceToken`           | Sim |
| `channel.session.report`      | `ChannelWorker__SessionReportServiceToken`         | Não (ADR-049 — sem ele, o Office não exibe o estado da sessão/QR, mas o Channel opera normalmente) |

### Como as credenciais são geradas (comandos, NÃO executados por este guia)

Os scripts abaixo vivem em `infra/scripts/` e gravam o hash SHA-256 do
token no Postgres (tabela `service_credentials`) e o token em texto puro
**apenas** em `~/.config/atua/channel/*.env` (fora do repositório,
permissão 0600). Rode cada um primeiro com `--dry-run`, confirme os
UUIDs, e só então com `--confirm-write`. **Esta tarefa não executa esses
comandos** — eles exigem aprovação explícita e acesso de escrita ao
Postgres de produção.

```bash
# claim + complete + message.ingest (script existente, cobre os 3 escopos)
infra/scripts/seed-channel-credentials.sh \
  --tenant-id <uuid> --integration-id <uuid> --dry-run
infra/scripts/seed-channel-credentials.sh \
  --tenant-id <uuid> --integration-id <uuid> --confirm-write

# media.ingest (escopo adicional, script dedicado)
infra/scripts/add-channel-media-ingest-credential.sh \
  --tenant-id <uuid> --integration-id <uuid> --dry-run
infra/scripts/add-channel-media-ingest-credential.sh \
  --tenant-id <uuid> --integration-id <uuid> --confirm-write

# session.report (opcional, ADR-049 — status do QR no Office)
infra/scripts/add-channel-session-report-credential.sh \
  --tenant-id <uuid> --integration-id <uuid> --dry-run
infra/scripts/add-channel-session-report-credential.sh \
  --tenant-id <uuid> --integration-id <uuid> --confirm-write
```

Cada script exige que a integração já tenha exatamente uma credencial
ativa de `channel.command.claim` e `channel.command.complete` (os dois
primeiros scripts resolvem essa dependência na ordem acima) e recusa
sobrescrever uma credencial já existente do mesmo escopo.

Depois de gerados, carregue os quatro/cinco arquivos no shell que vai
rodar `make deploy-channel` (nunca na EC2 diretamente nem commitados):

```bash
source ~/.config/atua/channel/channel.env            # claim + complete
source ~/.config/atua/channel/message-ingest.env
source ~/.config/atua/channel/media-ingest.env
source ~/.config/atua/channel/session-report.env     # opcional
```

(Os nomes exatos dos arquivos dependem de como `seed-channel-credentials.sh`
grava — confirme com `--dry-run` antes.)

## Latência de envio e configuração persistente

`make deploy-channel` grava `ChannelWorker__PollingIntervalSeconds=2`
por padrão no arquivo de ambiente da instância. Para sobrescrever esse
intervalo, exporte a variável antes do deploy. Assim, a configuração é
reaplicada após `make down` / `make up`, sem edição manual na EC2.

Após um ingest inbound bem-sucedido, o Channel também antecipa buscas de
comandos nos instantes definidos por `ChannelWorker__EarlyClaimDelaysSeconds`
(padrão `0,3,6,10`, em segundos contados após o ingest). O valor `-`
desativa as buscas antecipadas. Os ciclos são serializados; o polling
normal continua como recuperação. A confirmação de envio pelo WhatsApp
não garante que o celular já decifrou ou exibiu a mensagem: a mensagem
“Aguardando mensagem” deve ser investigada separadamente pelos logs de
sessão e reenvio do Baileys.

A API recebe sua configuração `TriagemLlm__*` pelo UserData do CDK,
incluindo modelo, orçamento e allowlists de tenant e telefone de teste.
A recriação da EC2 reaplica esses valores; as credenciais do Channel
continuam sendo reinjetadas pelo deploy, nunca pelo CDK.

## Sequência de publicação

### 1. Primeira instalação (ou após um `make up` em instância recriada)

```bash
cd infra
export AWS_PROFILE=moldato

source ~/.config/atua/channel/channel.env
source ~/.config/atua/channel/message-ingest.env
source ~/.config/atua/channel/media-ingest.env
source ~/.config/atua/channel/session-report.env   # opcional

make deploy-channel
```

O que acontece (ver `.github/skills/atua-deploy/scripts/deploy-channel.sh`):

1. Compila o Channel (`pnpm --filter @atua/channel-node build`) e gera um
   bundle de produção autocontido (`pnpm ... deploy --prod`).
2. Publica o bundle em
   `s3://atua-<ACCOUNT_ID>-releases/channel-latest/` — para que um futuro
   `make up` (instância recriada do zero) já restaure automaticamente o
   runtime/bundle/unit via UserData (ver seção "Efeito do `make down`"
   abaixo).
3. Via SSH, bootstrap idempotente na EC2: instala Node 22
   (`dnf install nodejs22`) se ausente, cria o usuário de sistema sem
   privilégio `atua-channel`, cria `/opt/atua-channel/app` (dono
   `ec2-user`) e `/var/lib/atua-channel/session` (dono `atua-channel`,
   modo `700`).
4. Envia a unit systemd (`infra/systemd/atua-channel.service`, fonte de
   verdade) e habilita o serviço (`Restart=always`).
5. Grava `/etc/atua-channel.env` (modo `600`, nunca impresso no terminal
   nem versionado) com os tokens exportados e
   `ChannelWorker__ApiBaseUrl=http://127.0.0.1:80` (a API escuta na porta
   80 na mesma instância — loopback, não passa pelo Security Group).
6. Envia o bundle para `/opt/atua-channel/app/` (layout
   `/opt/atua-channel/app/dist/main.js` — **não achatar**, ver nota de
   `SessionPath` abaixo).
7. Reinicia `atua-channel` e mostra o status.

### 2. Redeploy de uma nova versão de código (serviço já instalado)

```bash
cd infra
export AWS_PROFILE=moldato
make redeploy-channel
```

Não reescreve tokens nem a unit systemd — apenas builda, publica no S3,
sincroniza o bundle e reinicia o processo. A sessão Baileys
(`/var/lib/atua-channel/session`) não é tocada.

### 3. Pareamento via Office (ADR-049)

Depois do primeiro `make deploy-channel` bem-sucedido (ou depois de
qualquer `make down` + `make up` + `make deploy-channel`):

1. Acompanhe os logs: `make logs-channel-follow`.
2. Aguarde o estado `qr_pending` ser reportado (log
   `whatsapp_session_connected` ainda não aparece).
3. Acesse o Office como **OWNER** ou **ADMIN** do tenant → Configurações →
   tela de status do WhatsApp.
4. Escaneie o QR exibido no aparelho autorizado em até ~60 segundos (o QR
   expira — `ReportWhatsAppSessionStateUseCase.QrTimeToLive`); se expirar,
   a tela volta a "aguardando novo QR" e um novo QR é reportado
   automaticamente pelo Channel.
5. Confirme `connected` no Office e `whatsapp_session_connected` no log.

Se a credencial `channel.session.report` não tiver sido gerada/injetada,
o Office mostra `state: "unknown"` — o pareamento ainda pode ser feito
lendo o QR impresso no terminal via SSH
(`ChannelWorker__PrintQr=true`, apenas para esse fim pontual, nunca
deixado ligado em produção).

### 4. Logs

```bash
make logs-channel           # últimas ~200 linhas
make logs-channel-follow    # tempo real (Ctrl+C para sair)
```

## Efeito do `make down` / `make up`

- `make down` destrói a stack Compute inteira (EC2 **e** EBS das duas
  instâncias, API e Collector-base) — decisão deliberada do PO, **sem**
  volume persistente dedicado ao Channel. Isso apaga
  `/var/lib/atua-channel/session` junto com o disco raiz: a sessão
  Baileys é perdida e o pareamento deve ser refeito via QR (passo 3
  acima) na próxima subida.
- `make up` recria a EC2 e o UserData da CDK
  (`channelBootstrapCommands` em `infra/lib/atua-compute-stack.ts`)
  restaura automaticamente, sem intervenção manual: runtime Node 22,
  usuário `atua-channel`, diretórios, unit systemd, e o bundle mais
  recente publicado em `s3://atua-<ACCOUNT_ID>-releases/channel-latest/`
  (se `make deploy-channel` já tiver sido rodado ao menos uma vez antes).
- **O que NÃO é restaurado automaticamente**: `/etc/atua-channel.env`
  (os tokens). Isso é proposital — reutilizar a mesma permissão de leitura
  do bucket de releases para restaurar o bundle não exige IAM novo, mas
  restaurar segredos exigiria acesso a Secrets Manager/SSM Parameter
  Store, uma permissão IAM nova fora do escopo desta tarefa. Por isso, o
  serviço fica **habilitado mas parado** depois de um `make up` "frio"
  (sem tokens) — rode `make deploy-channel` novamente (passo 1) para
  reinjetar os tokens e iniciar o processo.

Resumo da sequência completa após um `make down` seguido de `make up`:

```bash
make up                 # EC2 recriada; API volta; Channel instalado mas parado (sem tokens)
make deploy-channel      # reinjeta tokens, inicia o serviço
# acompanhar logs + reparear via Office (passo 3 acima)
```

## Nota técnica: por que o layout `/opt/atua-channel/app/`

`apps/channel-node/src/config.ts` calcula um `repositoryRoot` três níveis
acima de onde `dist/config.js` fisicamente reside em tempo de execução, e
rejeita qualquer `ChannelWorker__SessionPath` que fique **dentro** dessa
raiz calculada (proteção contra a sessão acabar versionada/dentro da
árvore do código por engano). Com o bundle em
`/opt/atua-channel/app/dist/main.js`, a raiz calculada é `/opt` — e
`ChannelWorker__SessionPath=/var/lib/atua-channel/session` fica
genuinamente fora dela. **Não achate** essa estrutura (ex.: bundle direto
em `/opt/atua-channel/dist/`): a raiz calculada viraria `/` e qualquer
`SessionPath` possível seria rejeitado como "dentro do repositório".

## Riscos e memória

- A instância da API é `t3.micro` (1 GiB RAM). Medição real (via SSH,
  somente leitura) antes desta mudança: ~916 MB totais, ~517 MB
  disponíveis, API usando ~124 MB, sem swap configurado. O Channel
  (Node + Baileys, sem Chromium) deve adicionar algo entre ~100-250 MB —
  dentro da folga medida, mas **sem muita margem adicional** para picos
  simultâneos de API + Channel sob carga. Por isso o arquivo de swap
  (antes só usado na topologia colocada com o Collector) passou a ser
  criado **incondicionalmente** nesta mudança de CDK (ver ADR-050) — rede
  de segurança contra OOM-kill, não uma expectativa de uso rotineiro de
  swap.
- `UserData` da instância está em ~12,4 KB (topologia padrão) / ~13,7 KB
  (topologia colocada), ambos abaixo do limite de 16 KiB da AWS — a
  colocada tem menos folga (~2,7 KB) se mais comandos de bootstrap forem
  adicionados no futuro.
- Reinício do processo da API ou da instância não afeta a sessão do
  Channel/Baileys diretamente (processos systemd independentes), mas
  qualquer recriação da instância (`make down` + `make up`) apaga a
  sessão, como descrito acima.
- `channel.session.report` é o único novo escopo introduzido por este
  trabalho; não há endpoint HTTP exposto pelo Channel (ele é só cliente
  HTTP/WebSocket outbound), então não há exposição de rede nova — nenhuma
  alteração de Security Group foi necessária.
