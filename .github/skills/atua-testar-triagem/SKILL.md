---
name: atua-testar-triagem
description: 'Use quando o usuario pedir "testar triagem", "teste de triagem", "E2E da triagem", "triagem sintetica" ou "testar o pool". Executa o E2E repetivel da triagem via WhatsApp com e2e-triagem-synthetic.sh contra o ambiente remoto (Channel na EC2 da API): envio inicial, resposta do consumidor, pool do operador no Office e limpeza somente da OS sintetica autorizada.'
---

# Testar triagem via WhatsApp

Execute a partir da raiz do repositorio usando o
[script existente](../../../infra/scripts/e2e-triagem-synthetic.sh).
Nao copie nem reimplemente o script e nao rode SQL avulso no lugar dele.

## Escopo e seguranca

- O teste grava uma interacao sintetica no MongoDB Atlas real, processada pelo
  Consumer do Collector, pela API e pelo Channel (`atua-channel`, rodando na
  EC2 da API — ADR-050). Envia uma mensagem real de WhatsApp.
- Etapas cobertas:
  1. **Envio:** OS `assigned` -> outbox -> comando -> WhatsApp ->
     triagem `ContatoIniciado`.
  2. **Resposta (opcional):** o numero de teste responde com texto e/ou midia
     -> mensagem/midia vinculadas -> triagem `EmColeta`. Midia vai para o S3.
  3. **Pool (opcional):** sem a LLM, nada leva a triagem a `LiberadaAoPool`;
     use `release-to-pool` (somente OS `E2E-`) e valide no Office.
  Nao cobre consulta ao iService nem a conducao pela LLM.
- Destinatario de teste autorizado: `+5584994241248`.
  WhatsApp do Posto: `+5584994077677`.
- Preserve as travas da API e do Channel. Nunca libere backlog de OS reais,
  altere status de OS reais ou mude telefone/tenant para fazer o teste passar.
- Nao imprima connection strings, tokens, URI do Atlas, conteudo de secrets
  nem o QR de pareamento. Nao use `set -x`.
- Um pedido explicito para testar autoriza uma injecao nesse destinatario e o
  `release-to-pool` dessa OS. Se o pedido for apenas consulta ou explicacao,
  nao injete. Antes de novo envio apos falha ou resultado ambiguo, obtenha
  autorizacao.

## Pre-requisitos

1. Leia o estado operacional no
   [plano de implementacao](../../../docs/implementation/PLANO-triagem-templates-channel-baileys.md).
   Nao aplique migrations como parte do E2E; se o script falhar por tabela
   ou coluna ausente, pare e informe.
2. Confirme `bash`, `aws`, `python3`, `uuidgen`, `mongosh` e `psql`, e a
   presenca de `Postgres__ConnectionString` sem mostrar seu valor.
3. Use `AWS_PROFILE=moldato` e `AWS_REGION=sa-east-1`. Nao crie infraestrutura.
4. Ambiente remoto no ar (nao ha mais tunel SSH nem Channel local):
   - API publica saudavel, sem credenciais na requisicao:

     ```bash
     curl --fail --silent --show-error --max-time 5 https://api.atyno.com.br/health
     ```

     Exija HTTP 200 e `{"status":"healthy"}`. Se falhar, o IP da EC2 pode ter
     mudado: use `make sync-api-domain` (skill `atua-deploy`) com autorizacao.
   - Channel ativo e pareado. Confira com a skill `atua-ec2-exec`:

     ```bash
     AWS_PROFILE=moldato bash .github/skills/atua-ec2-exec/scripts/ec2-exec.sh \
       "systemctl is-active atua-channel; sudo journalctl -u atua-channel -n 30 --no-pager -o cat | grep -E 'whatsapp_session_connected|channel_session_state_reported|SessionFatalError' | tail -5"
     ```

     Exija `active` e `whatsapp_session_connected` (ou estado `connected`).
     Se o estado for `qr_pending`, peca ao usuario para ler o QR em
     Office -> Configuracoes -> WhatsApp (OWNER/ADMIN) e espere a conexao.
     Nunca rode outro Channel local com a mesma sessao.
5. Confira overrides `E2E_TENANT_ID` e `E2E_TEST_PHONE`; pare se divergirem do
   cenario autorizado.

## Executar e acompanhar

1. Consulte testes existentes, sem limpa-los:

   ```bash
   AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
     bash infra/scripts/e2e-triagem-synthetic.sh status
   ```

   Se houver OS `E2E-` antiga, informe o usuario antes de injetar outra.

2. Injete uma unica OS e guarde o ID `E2E-...` retornado. Nos comandos
   seguintes, substitua `E2E-ID` pelo ID exato desta execucao
   (`^E2E-[A-Za-z0-9-]+$`).

   ```bash
   AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
     bash infra/scripts/e2e-triagem-synthetic.sh inject
   ```

3. Consulte apenas esse ID a cada 5 a 10 segundos, por no maximo 2 minutos:

   ```bash
   AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
     bash infra/scripts/e2e-triagem-synthetic.sh status E2E-ID
   ```

   Esperado: OS `assigned`, outbox `Processed`, comando `Completed`, triagem
   `ContatoIniciado`, sem erros. Informe retentativas. Zero linhas logo apos a
   injecao nao comprova sucesso nem falha.

4. `Completed` e ACK do servidor nao comprovam entrega. Use `ask_user` para
   confirmar que chegou exatamente uma mensagem em `+5584994241248` e que ela
   aparece no aparelho do Posto. So declare o envio aprovado apos isso.

5. **Resposta (se o usuario quiser testar o pool):** peca, via `ask_user`, que
   o numero de teste responda com um texto e uma foto. Acompanhe com `status`
   ate `mensagens_recebidas >= 1` e triagem `EmColeta`.

6. **Pool:** avance somente esta OS:

   ```bash
   AWS_PROFILE=moldato AWS_REGION=sa-east-1 \
     bash infra/scripts/e2e-triagem-synthetic.sh release-to-pool E2E-ID
   ```

   Exija 1 linha com triagem `LiberadaAoPool`. O comando so atua em OS `E2E-`
   do tenant da trava e em triagens `ContatoIniciado`/`EmColeta`.

7. Peca ao usuario, via `ask_user`, para validar no Office (`/triage-pool`),
   com um campo para cada item:
   - a OS aparece na lista (a lista atualiza por polling a cada 30 s; recarregar
     a pagina mostra na hora);
   - no detalhe, a conversa (transcricao) aparece em ordem;
   - no detalhe, o anexo (foto) abre — URL pre-assinada do S3;
   - "Assumir" funciona e a triagem sai do pool;
   - autoriza remover a OS sintetica.

   Registre como pendente cada item nao confirmado; nao conclua pelo banco.

## Falhas e diagnostico

- Preserve o ID e os dados em caso de erro, timeout ou mensagem nao recebida.
  Informe a etapa, status e codigo de erro.
- Logs do Channel: `make logs-channel` (em `infra/`, skill `atua-deploy`) ou
  `atua-ec2-exec`. Procure `whatsapp_message_server_ack`,
  `channel_inbound_message_reported`, `channel_inbound_media_reported`,
  rejeicoes e desconexoes. Nao exponha telefone/JID, conteudo nem QR.
- `401` do Channel para a API: credencial de servico ausente ou revogada; use a
  skill `atua-seed-collector-credentials` apenas para verificar. Nao gere ou
  rotacione secrets sem autorizacao.
- Anexo nao abre no Office (403 do S3): confira se a `apiRole` tem
  `s3:GetObject` no bucket de midia (statement `MediaTriagemBucketObjectAccess`
  em `infra/lib/atua-compute-stack.ts`).
- Nao reinjete automaticamente, nao reabra comando concluido e nao altere o
  banco fora do script para forcar um resultado.

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
- O cleanup remove registros relacionais (inclusive mensagens e midias) e os
  objetos de midia no S3. Nao remove mensagens dos aparelhos nem interacoes no
  Atlas. Nao limpe enquanto um comando de envio ainda estiver processando.
- Informe ID, resultado tecnico por etapa, confirmacoes humanas, retentativas e
  limpeza. Registre evidencias no plano, distinguindo fatos de pendencias.
- Nao reinicie o `atua-channel`, nao faca commit ou push como efeito colateral
  do teste.
