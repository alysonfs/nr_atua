# ADR-042 - Polling com Claim/Complete para o Canal WhatsApp

## Status

**Accepted** — aprovado pelo Product Owner em 2026-10-15.

## Contexto

A arquitetura da triagem define comandos da Master API para o canal e eventos
do canal para a API, mas ainda não define como o processo separado do canal
recebe trabalho. O incremento inicial usa `ITriagemChannel` e
`FakeTriagemChannel` em processo para validar o fluxo, sem representar o
transporte definitivo.

Na fase de desenvolvimento, o canal deve rodar localmente e a AWS permanece
sob demanda. Um modelo de push exigiria que a Master API alcançasse uma porta
exposta pelo processo local, com configuração de rede, NAT ou túnel. Isso
também transferiria ao canal responsabilidades de disponibilidade de servidor.

O projeto já usa o padrão de credencial de serviço com identidade fixa de
tenant e integração, além de `claim`, lease e `complete` para trabalho
assíncrono. O transporte deve preservar o isolamento multi-tenant, a chave de
idempotência e a correlação definidos no ADR-039.

## Decisão

O processo do canal buscará comandos na Master API por polling, sem expor uma
porta de entrada.

O contrato terá dois endpoints internos:

- `POST /api/internal/triagens/canal/claim`, protegido pelo escopo
  `channel.command.claim`;
- `POST /api/internal/triagens/canal/complete`, protegido pelo escopo
  `channel.command.complete`.

Tenant e integração serão obtidos exclusivamente dos claims da credencial de
serviço. O corpo das requisições não poderá ampliar esse escopo.

A Master API persistirá o comando de canal na mesma transação que registra o
aceite idempotente da triagem. O comando carregará, no mínimo, versão do
contrato, tipo, identificadores da triagem e da OS, identificador e versão do
template, chave de idempotência estável, `correlation_id`, status, contagem de
tentativas e lease.

O `claim` fará a transição atômica de um comando elegível para o estado
reivindicado, impedindo entrega concorrente a duas instâncias. O lease
expirado tornará o comando elegível novamente sem alterar sua chave de
idempotência.

A resposta de `claim` também levará o snapshot de `ContactPhone` da OS,
capturado quando a triagem é aceita. Todas as retentativas do comando mantêm
esse mesmo destinatário, mesmo que o telefone da OS seja atualizado depois.
O telefone é dado pessoal: não deve aparecer em logs.

O `complete` aceitará resultados estruturados:

- sucesso: conclui o comando e move a triagem de `AGUARDANDO_ENVIO` para
  `CONTATO_INICIADO`;
- falha definitiva: conclui o comando e move a triagem para
  `FALHA_DE_CONTATO`;
- falha transitória: incrementa a contagem e agenda nova tentativa;
- quinta falha transitória consecutiva: conclui o comando e move a triagem
  para `FALHA_DE_CONTATO`.

O canal consultará novos comandos em intervalo de até 30 segundos. A política
técnica de backoff pode reduzir chamadas durante indisponibilidade, desde que
não transforme falha transitória em sucesso nem altere a chave de
idempotência.

`ITriagemChannel` e `FakeTriagemChannel` permanecem como stub transitório de
desenvolvimento e testes. O caminho automático será migrado para um port de
persistência de comandos. A remoção ou transferência definitiva do stub para
os testes ocorrerá quando o processo separado do canal estiver operacional.

## Motivos

- O canal só realiza conexões de saída e não precisa expor servidor local.
- O desenvolvimento funciona com a AWS desligada.
- Reutiliza autenticação, claim, lease e complete já conhecidos pelo projeto.
- Mantém a Master API como autoridade da máquina de estados.
- Permite múltiplas instâncias do canal sem entrega concorrente do mesmo
  comando.
- Preserva idempotência e recuperação após reinício.

## Alternativas consideradas

### HTTP push da Master API para o canal

Rejeitado porque exige endpoint alcançável no processo do canal, complica o
desenvolvimento local e aumenta o acoplamento de rede.

### MongoDB como fila de saída

Rejeitado porque o MongoDB Change Streams permanece como gatilho de entrada da
triagem. Usá-lo também como barramento de saída duplicaria mecanismos de
persistência e lease sem necessidade.

### Manter chamada síncrona em processo

Rejeitado como transporte definitivo porque não representa um processo de
canal independente nem sobrevive aos limites operacionais previstos para o
Baileys.

## Consequências

### Positivas

- Nenhuma porta de entrada precisa ser exposta pelo canal.
- Comandos pendentes sobrevivem a reinícios.
- Retry, lease, correlação e auditoria ficam observáveis na Master API.
- Credenciais permanecem isoladas por tenant e integração.

### Negativas

- O envio pode aguardar até 30 segundos antes do claim.
- Uma nova entidade persistente e endpoints internos serão necessários.
- O resultado do envio deixa de ser síncrono para o caso de uso de início.
- Leases expirados e tentativas precisam de manutenção operacional.

## Agentes envolvidos

- `orchestrator`
- `software-architect`
- Product Owner

## Data

2026-10-15

## Relações

- Complementa o ADR-037.
- Complementa o ADR-039.
- Complementa o ADR-040.
- Complementa o ADR-041.
- Não substitui decisões existentes.
