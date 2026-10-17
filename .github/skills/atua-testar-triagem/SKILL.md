---
name: atua-testar-triagem
description: 'Use quando o usuario pedir "testar triagem", "teste de triagem", "E2E da triagem" ou "triagem sintetica". Executa o E2E repetivel de envio inicial via WhatsApp com e2e-triagem-synthetic.sh, acompanha o resultado, confirma recebimento nos aparelhos e limpa somente a OS sintetica autorizada.'
---

# Testar triagem via WhatsApp

Execute a partir da raiz do repositorio usando o
[script existente](../../../infra/scripts/e2e-triagem-synthetic.sh).
Nao copie nem reimplemente o script.

## Escopo e seguranca

- O teste grava uma interacao sintetica no MongoDB Atlas real, processada pelo
  Consumer do Collector, API e Channel Node/Baileys. Envia uma mensagem real.
- Cobre OS `assigned` -> outbox -> comando -> WhatsApp -> `ContatoIniciado`.
  Nao cobre consulta ao iService, respostas do consumidor, LLM ou midias.
- Destinatario de teste autorizado: `+5584994241248`.
  WhatsApp do Posto: `+5584994077677`.
- Preserve as travas da API e do Channel. Nunca libere backlog de OS reais,
  altere status de OS reais ou mude telefone/tenant para fazer o teste passar.
- Nao imprima connection strings, tokens, URI do Atlas ou conteudo de secrets.
  Nao use `set -x` nem envie credenciais para a API publica em HTTP.
- Um pedido explicito para testar autoriza uma injecao nesse destinatario.
  Se o pedido for apenas consulta, explicacao ou criacao desta skill, nao injete.
  Antes de novo envio apos falha ou resultado ambiguo, obtenha autorizacao.

## Pre-requisitos

1. Leia o script e o estado operacional no
   [plano de implementacao](../../../docs/implementation/PLANO-triagem-templates-channel-baileys.md).
   Antes de executar `status`, `inject` ou `cleanup`, confirme com o
   responsavel que as migrations
   `20261017183609_AddTriagemChannelInboundMessages` e
   `20261017184547_AddTriagemChannelMessageCandidateGinIndex` foram aplicadas
   no ambiente. O script consulta `triagem_channel_messages`; se as migrations
   ainda nao foram aplicadas, pare e solicite autorizacao para a preparacao do
   ambiente. Nao aplique migrations como parte do E2E.
2. Confirme disponibilidade de `bash`, `aws`, `python3`, `uuidgen`, `mongosh`
   e `psql`, e presenca de `Postgres__ConnectionString` sem mostrar seu valor.
   O script utiliza tambem `infra/scripts/lib-pg-conn.sh`.
3. Use o profile e a Region configurados. No ambiente de teste documentado:
   `AWS_PROFILE=moldato` e `AWS_REGION=sa-east-1`. Nao crie infraestrutura.
4. API e Consumer devem estar ativos, com templates e credenciais de servico
   configurados. O Channel deve estar pareado com o WhatsApp do Posto.
5. Confira o tunel SSH local sem credenciais na requisicao:

   ```bash
   curl --fail --silent --show-error --max-time 5 http://127.0.0.1:18080/health
   ```

   Exija HTTP 200 e `{"status":"healthy"}`. O Channel deve apontar para
   `http://127.0.0.1:18080`. Se faltar tunel ou Channel, solicite ao usuario
   que os mantenha abertos em seu terminal privado conforme o plano.
   Nao abra outra sessao Baileys nem reinicie processos sem autorizacao.
6. Confira as travas atuais e eventuais overrides `E2E_TENANT_ID` e
   `E2E_TEST_PHONE`. Pare se divergirem do cenario autorizado.

## Executar e acompanhar

1. Consulte os testes existentes, sem limpa-los:

   ```bash
   AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
     bash infra/scripts/e2e-triagem-synthetic.sh status
   ```

2. Injete uma unica OS:

   ```bash
   AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
     bash infra/scripts/e2e-triagem-synthetic.sh inject
   ```

   Guarde o ID `E2E-...` retornado. Para comandos seguintes, substitua
   `E2E-ID` pelo ID exato desta execucao, nunca pelo ID de outro teste.
   IDs aceitos seguem `^E2E-[A-Za-z0-9-]+$`.

3. Consulte apenas esse ID em intervalos de 5 a 10 segundos, por no maximo
   2 minutos:

   ```bash
   AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
     bash infra/scripts/e2e-triagem-synthetic.sh status E2E-ID
   ```

   Resultado esperado: OS `assigned`, outbox `Processed`, comando `Completed`,
   triagem `ContatoIniciado`, sem erros. Informe retentativas se ocorrerem.
   Zero linhas imediatamente apos a injecao nao comprova sucesso nem falha:
   o Consumer pode ainda estar processando.

4. `Completed` e ACK do servidor nao comprovam entrega no aparelho.
   Use `ask_user` para confirmar:
   - chegou exatamente uma mensagem em `+5584994241248`?
   - a mensagem enviada aparece na conversa do aparelho do Posto?
   - autoriza remover a OS sintetica deste teste?

   So declare o E2E de envio aprovado apos confirmar ambos os aparelhos.
   Se nao houver confirmacao, registre entrega como pendente.

## Falhas e diagnostico

- Preserve o ID e os dados em caso de erro, timeout ou mensagem nao recebida.
  Informe a etapa, status e codigo de erro; nao declare sucesso pelo banco.
- Consulte logs sanitizados do Channel para resolucao do destinatario,
  `whatsapp_message_server_ack`, rejeicao e desconexao. Nao exponha
  telefone/JID ou conteudo de mensagens nos logs.
- Para logs remotos, carregue a skill `atua-deploy` ou `atua-ec2-exec`.
  Para `401`, verifique credenciais; nao gere ou rotacione secrets sem
  autorizacao. Para `ChannelApiError`, confira tunel e health primeiro.
- Nao reinjete automaticamente, reabra comando concluido ou altere o banco
  para forcar um novo envio.

## Limpeza e encerramento

Depois da autorizacao do usuario, limpe somente o ID acompanhado:

```bash
AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
  bash infra/scripts/e2e-triagem-synthetic.sh cleanup E2E-ID
AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
  bash infra/scripts/e2e-triagem-synthetic.sh status E2E-ID
```

- Exija sucesso do comando e zero linhas na consulta final.
- Nunca execute `cleanup` sem ID: isso apaga todas as OS sinteticas do tenant.
- O cleanup remove registros relacionais, nao mensagens dos aparelhos nem
  interacoes ainda pendentes no Atlas. Nao limpe enquanto o Consumer ou um
  comando de envio ainda estiver processando.
- Informe ID, resultado tecnico, confirmacao humana, retentativas e limpeza.
  Registre evidencias no plano, distinguindo fatos de pendencias.
- Nao pare o tunel/Channel mantidos pelo usuario, faca commit ou push como
  efeito colateral do teste.
