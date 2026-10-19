# Teste local do Channel Node

Este guia descreve como executar o Channel Node localmente contra a API de
teste já publicada por meio do túnel SSH local. É um procedimento de
desenvolvimento/teste, não um deploy de produção nem uma instalação na EC2.

Essa topologia segue a decisão de desenvolvimento em
[ADR-035](../decisions/ADR-035-topologia-dedicada-always-on-para-baileys.md)
e a persistência local de sessão definida em
[ADR-038](../decisions/ADR-038-persistencia-sessao-baileys-dev-local-producao-ebs.md).
Uma única instância deve usar cada sessão WhatsApp. A fila inbound é em memória;
interromper o processo durante o processamento pode exigir reentrega da mensagem.

## Pré-requisitos

- Túnel SSH local aberto pelo operador, encaminhando a API para
  `127.0.0.1:18080`.
- API de teste disponível pelo túnel em `http://127.0.0.1:18080`.
- Migrations inbound aplicadas e integração de teste provisionada com os
  escopos `channel.command.claim`, `channel.command.complete` e
  `channel.message.ingest`.
- Node.js, pnpm e dependências do monorepo instalados.
- Arquivos locais de credenciais criados pelos scripts operacionais:
  - `~/.config/atua/channel/channel.env`
  - `~/.config/atua/channel/message-ingest.env`
- Acesso ao aparelho que receberá mensagens e autorização para iniciar o
  atendimento de teste. Antes de iniciar, confirme que não existe outro
  processo Channel ativo usando a mesma conta/sessão e que não há comandos
  pendentes que possam enviar mensagens inesperadas.

Os arquivos de credenciais contêm tokens de serviço. Não os versione, não
imprima seus valores e não os envie por chat. Se ainda não existirem, pare e
obtenha autorização explícita antes de executar um script que grave no RDS.

## Preparar ambiente

Execute na raiz do repositório:

```bash
source "$HOME/.config/atua/channel/channel.env"
source "$HOME/.config/atua/channel/message-ingest.env"

export ChannelWorker__ApiBaseUrl="http://127.0.0.1:18080"
export ChannelWorker__SessionPath="$HOME/.local/share/atua/channel-session"
export ChannelWorker__PrintQr=false

mkdir -p "$ChannelWorker__SessionPath"
chmod 700 "$ChannelWorker__SessionPath"
```

Confirme que o túnel está ativo antes de iniciar o Channel:

```bash
curl --fail --silent --show-error --max-time 5 http://127.0.0.1:18080/health
```

O resultado esperado é HTTP 200 com `{"status":"healthy"}`. Se o túnel não
estiver ativo, inicie-o em um terminal privado e mantenha-o aberto durante o
teste. Não envie bearer tokens por HTTP público.

`ChannelWorker__SessionPath` precisa ser absoluto, ficar fora do repositório e
ser protegido como material de autenticação do WhatsApp. A sessão é local à
máquina e não deve ser compartilhada entre processos.

## Validar e iniciar

Na raiz do repositório, valide e compile o Channel:

```bash
pnpm --filter @atua/channel-node check
pnpm --filter @atua/channel-node test
pnpm --filter @atua/channel-node build
```

Se os comandos passarem, inicie o processo em primeiro plano:

```bash
pnpm --filter @atua/channel-node exec node dist/main.js
```

Mantenha o terminal aberto enquanto durar o teste. Para encerrar normalmente,
use `Ctrl+C` e aguarde a mensagem de encerramento. Não apague o diretório da
sessão ao parar o processo.

Se esta máquina ainda não tiver uma sessão pareada, o QR pode ser habilitado
somente para o pareamento inicial:

```bash
export ChannelWorker__PrintQr=true
```

Escaneie o QR apenas no aparelho autorizado e não compartilhe uma captura ou
saída do terminal que o contenha. Após parear, encerre o processo, remova a
variável da sessão do shell (`unset ChannelWorker__PrintQr`) e inicie
novamente com o padrão `false`.

## Verificação do teste inbound

1. Aguarde o log `whatsapp_session_connected`.
2. Envie uma mensagem de teste do número autorizado para o WhatsApp do Posto.
3. Confirme nos logs que o Channel recebeu e encaminhou a mensagem, sem copiar
   texto, telefone, tokens ou conteúdo de mídia para o chat.
4. Use o procedimento autorizado de observabilidade do ambiente para confirmar
   o registro inbound e a associação/transição esperada da triagem.
5. Se houver erro ou resultado ambíguo, pare e investigue antes de reiniciar
   ou reenviar mensagens. A ingestão é idempotente pela chave do evento, mas
   a fila local não é durável.

## Limites

- Este procedimento não instala nem reinicia serviço na EC2 e não aplica
  migrations ou grava credenciais no banco.
- O Channel deve usar o túnel local; não configure bearer tokens contra um
  endpoint HTTP público.
- Não execute o Channel simultaneamente em duas máquinas com a mesma conta
  WhatsApp.
- A execução local depende de conectividade contínua entre a máquina do
  desenvolvedor e a API. Não representa uma topologia de produção.
- Produção continua bloqueada pela topologia always-on, recuperação da sessão,
  operação contínua, revisão jurídica/LGPD e demais itens registrados no
  [plano da triagem](../implementation/PLANO-triagem-templates-channel-baileys.md).
