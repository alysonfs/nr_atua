# Plano de implementação: templates, Channel e Baileys

Status: `APPROVED_FOR_IMPLEMENTATION`

Data de consolidação: 2026-10-16

## 1. Objetivo

Manter a memória executável dos próximos incrementos da triagem automatizada
via WhatsApp após o ADR-042, cobrindo:

- conclusão do transporte `claim`/`complete`;
- gestão de templates por tenant e integração;
- administração dos templates pelo Office;
- criação das credenciais próprias do Channel;
- substituição do worker C# de prova por Node.js/TypeScript com Baileys;
- teste ponta a ponta controlado com a integração iService.

Este documento não substitui o épico, a arquitetura nem os ADRs. Em caso de
divergência, as fontes de verdade continuam sendo:

- [Épico da triagem WhatsApp](../features/triagem-whatsapp-fase-1.md);
- [Arquitetura da Fase 1](../architecture/triagem-whatsapp-fase-1-arquitetura.md);
- [ADR-038 — persistência da sessão Baileys](../decisions/ADR-038-persistencia-sessao-baileys-dev-local-producao-ebs.md);
- [ADR-039 — idempotência](../decisions/ADR-039-idempotencia-envio-template-e-processamento-mensagens-triagem.md);
- [ADR-041 — outbox do gatilho](../decisions/ADR-041-outbox-postgresql-para-disparo-de-triagem-pelo-consumer.md);
- [ADR-042 — polling com claim/complete](../decisions/ADR-042-polling-claim-complete-para-canal-whatsapp.md);
- [ADR-043 — ingestão de MessageReceived e transição para EM_COLETA](../decisions/ADR-043-ingestao-message-received-e-transicao-em-coleta.md).

## 2. Decisões confirmadas

### 2.1. Processo do Channel

- O processo definitivo do Channel será implementado em Node.js/TypeScript.
- O processo Node executará diretamente:
  - polling da Master API;
  - `claim` do comando;
  - envio pelo Baileys;
  - `complete` com resultado estruturado.
- O worker C# atual é uma prova do contrato do ADR-042. Sua remoção fica
  condicionada à equivalência validada do Channel Node.js/TypeScript.
- Não será criado um sidecar Node chamado pelo worker C#, evitando dois
  processos e um contrato interno adicional.
- O Channel não exporá porta de entrada; realizará somente conexões de saída.

### 2.2. Sessão do Baileys

- O desenvolvimento usará um número exclusivo de WhatsApp para testes.
- A sessão será persistida em diretório externo ao repositório.
- O caminho será informado por variável de ambiente.
- Credenciais e arquivos de sessão não poderão ser versionados.
- A integração iService permanecerá desabilitada até o Channel estar
  conectado e pronto para o teste ponta a ponta.

### 2.3. Escopo de tenant e integração

Ambiente confirmado em 2026-10-16:

| Item | Valor |
|---|---|
| Tenant | Natal Refrigeração |
| Tenant ID | `01a0888b-acd8-773b-93b7-7362973d7ea8` |
| Integração | iService |
| Integration ID | `01a0888b-ad0d-760a-9327-bf7dfa5fd97d` |
| Provider ID | `00000000-0000-0000-0000-0000000000e1` |
| Estado observado | Integração desabilitada |

O estado da infraestrutura é apenas um registro operacional do momento da
consolidação, não uma garantia permanente:

- região AWS selecionada: `sa-east-1`;
- `AtuaComputeStack`: `CREATE_COMPLETE`;
- RDS: `available`.

### 2.4. Credenciais do Channel

- Serão criadas credenciais exclusivas para:
  - `channel.command.claim`;
  - `channel.command.complete`.
- As credenciais existentes do Collector não serão reutilizadas, alteradas
  nem revogadas.
- Tokens em texto puro serão armazenados somente em secrets ou variáveis de
  ambiente locais.
- Nenhum token será escrito neste documento ou em arquivos versionados.

## 3. Gestão de templates

### 3.1. Escopo

Os templates serão configurados por tenant e integração e persistidos no
PostgreSQL da Master API.

Tipos incluídos no primeiro incremento:

1. contato inicial;
2. lembrete de 24 horas;
3. lembrete de 36 horas.

### 3.2. Ciclo de vida

- Um template começa como rascunho.
- A publicação cria uma versão imutável.
- Alterar conteúdo publicado exige criar uma nova versão.
- Deve existir exatamente uma versão ativa por tipo, tenant e integração.
- A ativação de uma nova versão desativa a anterior na mesma operação
  transacional.
- Comandos já persistidos devem preservar a versão selecionada no momento de
  sua criação.

### 3.3. Autorização

- Somente administradores do tenant podem criar, editar, publicar ou ativar
  templates.
- Operadores não podem alterar templates.
- Tenant e integração devem ser validados pela Master API; o corpo da
  requisição não pode ampliar o escopo autorizado.

### 3.4. Office

A tela administrativa do Office deverá oferecer:

- listagem dos três tipos de template;
- indicação da versão ativa;
- criação e edição de rascunho;
- publicação e ativação;
- histórico de versões;
- preview com dados de exemplo;
- validação de placeholders;
- aviso de que versões publicadas são imutáveis.

Não entra neste incremento:

- botão de envio de mensagem de teste;
- pareamento por QR code no Office;
- monitoramento operacional da conexão Baileys.

### 3.5. Placeholders

Placeholders inicialmente aprovados:

| Placeholder | Origem | Regra |
|---|---|---|
| `{{nome_posto}}` | tenant | obrigatório |
| `{{fabricante}}` | provedor/integração | obrigatório |
| `{{numero_os}}` | OS externa | obrigatório |
| `{{produto}}` | OS | usar `seu produto` quando ausente |
| `{{itens_pendentes}}` | checklist da triagem | exclusivo dos lembretes |

O backend deve usar uma lista permitida por tipo de template. Placeholders
desconhecidos impedem a publicação.

## 4. Conteúdo inicial aprovado

O ATYNO é ferramenta interna do posto e não deve ser mencionado nas mensagens
ao consumidor.

### 4.1. Contato inicial

```text
📩 Triagem da OS {{numero_os}}

Olá! Somos da {{nome_posto}}, assistência técnica autorizada
{{fabricante}}. Recebemos a ordem de serviço do produto {{produto}} e
precisamos realizar uma triagem antes do atendimento, para aumentar a chance
de resolver o problema na primeira visita.

Por favor, envie:
✅ descrição detalhada do defeito, incluindo eventual código de erro;
📄 foto legível da nota fiscal;
🏷️ foto legível da etiqueta com modelo e número de série;
📷 pelo menos uma foto ou vídeo mostrando o defeito;
📍 endereço completo onde o produto está instalado.

Se possível, envie também um ponto de referência e a localização do endereço.

Você pode enviar as informações em mensagens separadas. Quanto mais detalhes
recebermos, mais rápido poderemos analisar o atendimento.

Se as informações obrigatórias não forem enviadas em até 48 horas, a análise
poderá atrasar e nossa equipe poderá entrar em contato para continuar o
atendimento. A ordem de serviço não será cancelada automaticamente por falta
dessas informações.
```

### 4.2. Lembrete de 24 horas

```text
Olá! Para continuarmos a triagem da OS {{numero_os}}, ainda precisamos
receber:
{{itens_pendentes}}

Você pode enviar os itens em mensagens separadas. Assim que recebermos as
informações obrigatórias, seguiremos com a análise.
```

### 4.3. Lembrete de 36 horas

```text
Olá! A triagem da OS {{numero_os}} continua incompleta. Ainda faltam:
{{itens_pendentes}}

O prazo de 48 horas termina em aproximadamente 12 horas. A ordem de serviço
não será cancelada automaticamente por falta dessas informações, mas a
ausência dos dados poderá atrasar o atendimento e nossa equipe poderá entrar
em contato.
```

## 5. Sequência de execução

### Incremento 0 — fechamento do ADR-042

- [ ] Versionar os arquivos restantes da criação atômica da triagem e do
      comando de canal.
- [ ] Versionar separadamente o ADR-042 e a arquitetura atualizada.
- [ ] Confirmar que um clone limpo compila sem depender de arquivos locais
      não versionados.
- [ ] Executar as suítes da API, Collector e Channel.

Critério de conclusão:

- nenhum arquivo funcional do ADR-042 permanece apenas no worktree;
- `claim` e `complete` continuam cobertos por testes;
- nenhuma mudança alheia é incluída nos commits.

### Incremento 1 — backend de templates

- [x] Modelar template e versão por tenant, integração e tipo.
- [x] Garantir uma única versão ativa por tipo e integração.
- [x] Implementar rascunho, publicação e ativação transacional.
- [x] Validar placeholders por tipo.
- [x] Preservar texto e versão selecionados no comando de canal.
- [x] Expor endpoints administrativos autorizados.
- [x] Criar migration e testes de concorrência, autorização e versionamento.

Critério de conclusão:

- uma versão publicada nunca é editada;
- publicar nova versão não altera comandos antigos;
- tenant diferente não consegue consultar nem alterar o template.

### Incremento 2 — tela de templates no Office

- [x] Criar seção administrativa em Configurações.
- [x] Listar tipos, versões e versão ativa.
- [x] Criar e editar rascunhos.
- [x] Publicar e ativar versões.
- [x] Exibir preview com dados de exemplo.
- [x] Validar placeholders antes da publicação.
- [x] Restringir alteração e publicação aos administradores do tenant.

Critério de conclusão:

- administrador consegue completar o fluxo sem editar banco ou configuração;
- operador não autorizado não acessa ações de escrita;
- preview usa o mesmo mecanismo de renderização do backend ou contrato
  equivalente validado.

Validação local concluída em 2026-10-16:

- API: 388 testes aprovados;
- Office: 167 testes aprovados;
- Channel C#: 11 testes aprovados;
- migration aplicada em PostgreSQL 16 com seed dos três templates para uma
  integração pré-existente.

### Incremento 3 — credenciais do Channel

- [x] Gerar tokens distintos para `claim` e `complete`.
- [x] Persistir somente os hashes no PostgreSQL.
- [x] Configurar os tokens no ambiente local protegido do Channel.
- [x] Validar autenticação e isolamento dos escopos.
- [x] Manter escopos do Channel separados dos do Collector; a rotação do
      Collector foi feita separadamente após autorização explícita.

Validação local isolada em 2026-10-16:

- `bash -n` passou para o seeder e seu helper PostgreSQL;
- o seeder foi executado contra PostgreSQL 16 descartável, criando uma
  credencial para cada scope; os hashes recalculados a partir dos tokens
  temporários corresponderam aos hashes persistidos, os tokens eram distintos
  e o arquivo de segredos tinha permissão `0600`;
- uma segunda execução foi recusada por duplicidade, sem inserir linhas
  adicionais; o arquivo temporário criado antes da falha foi removido;
- `ServiceCredentialScopeIsolationTests`: 6/6 aprovados;
- nenhuma credencial real foi criada e nenhum acesso à AWS/RDS foi realizado.
  Tokens e banco de teste foram descartados ao fim da validação.

Estado operacional em 2026-10-17:

- as duas credenciais reais do Channel foram criadas no RDS para a integração
  de teste; somente os hashes estão no banco;
- os tokens plaintext foram salvos em arquivo local fora do repositório com
  permissão `0600`;
- validação contra a API: `claim` válido retornou 204 sem comando pendente,
  `complete` válido chegou à validação de payload (400) sem alterar dados e
  credenciais com escopo cruzado foram recusadas (403);
- os escopos do Channel continuam ativos e separados dos do Collector.

O incremento permanece pendente até o Channel processar um comando real com o
socket WhatsApp conectado e concluir `complete`.

Critério de conclusão:

- Channel autentica nos dois endpoints;
- token com escopo de `claim` não executa `complete`, e vice-versa;
- nenhum segredo aparece em logs, commits ou documentação.

### Incremento 4 — Channel Node.js/TypeScript com Baileys

- [x] Criar worker Node.js/TypeScript.
- [x] Implementar polling e backoff.
- [x] Reutilizar os contratos `claim` e `complete` do ADR-042.
- [x] Integrar o envio do contato inicial pelo Baileys.
- [x] Carregar sessão a partir do diretório externo configurado.
- [x] Mapear sucesso, falha transitória e falha definitiva.
- [x] Preservar chave de idempotência e correlação.
- [x] Remover o sucesso fake do caminho operacional.
- [ ] Descontinuar o worker C# de prova após equivalência validada; a
      implementação C# ainda existe e não deve ser removida antes dessa
      validação.

Validação local em 2026-10-16:

- `pnpm --filter @atua/channel-node check` passou;
- `pnpm --filter @atua/channel-node test`: 23/23 testes aprovados;
- cinco testes de sessão com socket simulado cobrem reconexão e reset do
  backoff após conexão, `loggedOut` sem retry, timeout de envio, espera por
  persistência de credenciais no encerramento e permissão `0700` do diretório;
- a suíte detectou e motivou a correção do carregamento CommonJS de
  `qrcode-terminal` em runtime ESM no Node 22;
- `dotnet test tests/Atua.Api.Tests/Atua.Api.Tests.csproj`: 388/388 testes
  aprovados;
- `dotnet test tests/Atua.Channel.Tests/Atua.Channel.Tests.csproj`: 11/11
  testes aprovados;
- a migration do snapshot `RecipientPhone` foi aplicada e verificada em
  PostgreSQL 16 descartável; o container local foi parado e removido;
- nenhuma conexão AWS/RDS, sessão/QR real ou mensagem de WhatsApp foi usada.

Configuração local do Channel (fornecer valores apenas por ambiente/secrets,
sem versionar tokens):

- obrigatórias: `ChannelWorker__ApiBaseUrl`,
  `ChannelWorker__ClaimServiceToken`, `ChannelWorker__CompleteServiceToken`,
  `ChannelWorker__MessageIngestServiceToken` e `ChannelWorker__SessionPath`
  (caminho absoluto fora do repositório). Os três tokens de serviço devem ser
  distintos; integrações já provisionadas precisam receber o novo escopo
  `channel.message.ingest` antes de iniciar esta versão do Channel;
- opcionais: `ChannelWorker__PollingIntervalSeconds` (padrão 15, máximo 30),
  `ChannelWorker__SendTimeoutSeconds` (padrão 30, máximo 45) e
  `ChannelWorker__PrintQr` (padrão `false`). O QR pode conter material de
  autenticação; não o habilitar em terminal ou log compartilhado.

Pendente antes de concluir o incremento:

- validar conexão e comportamento do socket Baileys real, além de persistência
  da sessão entre reinicializações do processo;
- validar envio real para número de teste, reinício sem novo QR com sessão
  válida e atualização por `complete` em ponta a ponta;
- descontinuar o worker C# somente após equivalência validada.

O incremento continua incompleto até os critérios de conclusão serem
verificados. Os testes locais de ciclo de vida usam um socket simulado e não
substituem a validação com Baileys/WhatsApp real.

Critério de conclusão:

- Channel envia uma mensagem real para o número de teste;
- reiniciar o processo não exige novo QR code quando a sessão é válida;
- timeout não é reportado como sucesso;
- `complete` atualiza corretamente comando e triagem.

### Incremento 5 — preparação e estado operacional

Estado em 2026-10-17: OS de teste definida e autorização concedida pelo
product owner (janela: o dia todo). A trava temporária foi implantada e
validada antes do deploy.

Preparação executada:

- [x] Confirmar OS isolada e número vinculado sob controle do posto; não
      registrar o telefone neste plano.
- [x] Obter autorização operacional explícita para acesso AWS e envio real de
      uma mensagem de teste.
- [x] Aplicar no RDS as seis migrations pendentes de triagem; o schema agora
      inclui agregado, outbox, comandos e templates.
- [x] Criar credenciais Channel separadas para `claim` e `complete`; a
      autenticação ainda não foi validada.
- [x] Confirmar que a integração iService está desabilitada antes da
      preparação e planejar sua desativação ao final do teste.
- [x] Definir janela, operador responsável e procedimento de interrupção do
      Channel (o product owner escaneia o QR; o Channel Node roda localmente e
      é interrompido com Ctrl+C).
- [x] Implantar a trava temporária do E2E (abaixo) antes de habilitar a
      integração.

#### Estado operacional e próximos bloqueios

- API redeployada em 2026-10-17; serviço ativo e `/health` respondeu
  `healthy` na instância.
- No RDS foram aplicadas as seis migrations pendentes de triagem; as quatro
  tabelas de triagem existem e as três templates iniciais estão publicadas
  para a única integração.
- O Collector foi autorizado a ter suas credenciais giradas separadamente:
  credenciais antigas dos três escopos foram revogadas, uma nova por escopo
  permanece ativa e o arquivo de ambiente da EC2 foi atualizado sem exibir
  tokens. O binário atual foi publicado e o serviço voltou a receber respostas
  HTTP 200/204, sem novos 401.
- O endpoint de claim do Channel foi validado através de túnel SSH local
  (HTTP sobre loopback; não enviar tokens pela API pública em HTTP).
- A ativação do Collector está `Active`, mas não há comando de coleta
  pendente. A OS de teste existe no banco, porém seu status local ainda é
  `closed`; precisa ser atualizada pelo próximo ciclo antes de elegibilidade.
- Nenhuma triagem/outbox/comando existe para a OS de teste. O Channel ainda
  não foi iniciado nem pareado, a integração permanece sem coleta de teste e
  nenhuma mensagem foi enviada.

Próximos passos: o product owner escaneia o QR no terminal privado; depois
dispara uma coleta pelo fluxo autenticado do Office e confirma que a OS volta
como `assigned`. A allowlist impede que outras OS gerem triagens ou mensagens.

#### E2E repetível com OS sintética (2026-10-17)

A OS real de teste ficou `closed` e não volta para `assigned`; esperar uma OS
real elegível não é repetível. Decisão do product owner: usar OS sintéticas no
mesmo tenant/integração, com prefixo `E2E-` e limpeza após o teste.

- Script: `infra/scripts/e2e-triagem-synthetic.sh` (`inject [ID]`,
  `status [ID]`, `cleanup [ID]`). Injeta uma interação `detail_query` em
  `provider_interactions` (Atlas); o Consumer do Collector processa pelo
  fluxo real. `detail_query` grava `NeedsDetailFetch=false`, então o Collector
  não consulta o iService real para a OS fictícia. O Consumer apaga a
  interação do Atlas após processá-la.
- Trava: o claim da outbox passou a aceitar também `WorkOrderProviderId`
  iniciado por `TriagemTestSafeguard.SyntheticWorkOrderProviderIdPrefix`
  (`E2E-`). O destinatário continua fixo e o Channel mantém a segunda barreira.
- Fora do cenário: a consulta ao iService. Validar depois com uma OS real
  elegível, quando houver.
- Custo: sem infraestrutura nova; Baileys não cobra por mensagem.

Resultado da primeira execução (`E2E-20261017162812`):

- [x] OS gravada `assigned` pelo Consumer; outbox `Processed`.
- [x] Triagem criada (`AguardandoEnvio`) e comando `Pending`.
- [x] Channel fez `claim` e `complete` (`Completed`); triagem em
      `ContatoIniciado` em cerca de 20 segundos. Isso comprovou o processamento
      interno, não a entrega: o product owner informou que a mensagem não chegou
      nem apareceu como saída no aparelho do posto.
- [x] Reinjeção da mesma OS: continua 1 outbox, 1 triagem, 1 comando e 1
      histórico — sem segundo envio.
- [x] Os 2 itens reais `Pending` da outbox permaneceram retidos pela trava.
- [ ] Recebimento da primeira execução: não confirmado; product owner informou
      ausência da mensagem nos dois aparelhos. Ver reexecução corrigida abaixo.
- [x] Executar `cleanup` após diagnóstico e confirmação da reexecução.

Diagnóstico após a falha de recebimento:

- Túnel saudável e snapshot do destinatário correto no comando.
- O Channel aceitava o ID retornado por `sendMessage` como sucesso. Esse ID é
  gerado localmente e não comprova o aceite pelo servidor WhatsApp.
- Correção: consultar `onWhatsApp` para resolver o JID canônico do telefone,
  sem remover dígitos por heurística; aguardar o ACK do servidor correspondente
  ao ID da mensagem antes de enviar `Success` à API. Rejeição, desconexão ou
  timeout não podem concluir o envio como sucesso.
- Logs seguros: `whatsapp_recipient_resolved`, `whatsapp_message_server_ack`,
  `whatsapp_message_rejected` e `whatsapp_message_send_unconfirmed`; não
  registram telefone, JID, texto ou credenciais.
- ACK do servidor comprova aceite, não entrega nem leitura no aparelho.
- Type-check, build e 29 testes do Channel aprovados, incluindo ausência de
  ACK, rejeição, desconexão, JID canônico e consulta expirada sem envio tardio.
- A primeira OS foi preservada durante o diagnóstico; a correção foi verificada
  com outra OS sintética, sem reabrir o comando concluído.

Reexecução corrigida (`E2E-20261017-ACK-01`):

- [x] OS `assigned`, outbox `Processed`, comando `Completed`, triagem
      `ContatoIniciado`, sem retentativas.
- [x] Log `whatsapp_recipient_resolved` com `canonicalJidChanged=true`: o JID
      confirmado pelo WhatsApp difere do JID montado diretamente do telefone.
- [x] `whatsapp_message_server_ack` às 16:46:35 UTC, antes de
      `channel_command_completed` com `outcome=Success`, para a mesma mensagem.
- [x] Product owner confirmou recebimento no aparelho de teste e mensagem
      visível na saída do aparelho do posto.
- [x] Limpar as duas OS sintéticas após o diagnóstico e a confirmação.
      Consultas por ambos os IDs retornaram zero registros; os 2 itens reais
      `Pending` da outbox permaneceram intactos e protegidos pela trava.

Revalidação independente (`E2E-20261017165357`, 2026-10-17 ~13:54 BRT):

- [x] Channel Node: `tsc --noEmit` ok e 29/29 testes aprovados.
- [x] Comando `Pending` → `Claimed` → `Completed` (0 tentativas extras),
      triagem `ContatoIniciado`.
- [x] Product owner confirmou: exatamente 1 mensagem recebida em
      `+5584994241248` e mensagem visível na saída do aparelho do posto.
- [x] OS sintética removida; `status` sem registros `E2E-`.

Operação local necessária durante o teste: túnel SSH em
`127.0.0.1:18080` (verificar com `curl http://127.0.0.1:18080/health`) e
Channel Node rodando no terminal privado.

#### Trava temporária do E2E — REMOVER após validação

Risco identificado: o Collector grava intenção de triagem na outbox para
**toda** OS `assigned` coletada (inclusive as já existentes), e não havia
lista de OS permitidas nem chave de desligamento. Habilitar a integração
enviaria mensagem a consumidores reais.

Decisão do product owner (2026-10-17): durante a fase de testes, usar trava
com duas barreiras e telefone de destino fixo no código:

- API — `TriagemTestSafeguard.AllowedWorkOrderIds`: o claim da outbox só
  reivindica a OS de teste; as demais permanecem `Pending`, sem triagem e sem
  mensagem.
- API — `TriagemTestSafeguard.FixedRecipientPhone`: o snapshot do telefone do
  comando de canal usa sempre o número de teste, ignorando o telefone da OS.
  Desde 2026-10-24 (decisão do PO, testes simultâneos), a lista
  `AllowedRecipientPhones` tem 2 números: o comando usa o telefone da OS
  quando ele está na lista; senão, o número fixo. A IA também tem os 2 em
  `TriagemLlm__AllowedPhones` (CDK e, pontualmente, `/etc/atua-api.env`).
- Channel Node — `src/test-safeguard.ts`: qualquer destinatário fora da mesma
  lista é concluído como `PermanentFailure`
  (`TEST_RECIPIENT_NOT_ALLOWED`) sem envio.
- Script E2E: `E2E_TEST_PHONE` só aceita números dessa lista.

Remoção (obrigatória antes de qualquer uso real):

- [ ] Remover `TriagemTestSafeguard`, o filtro `@allowedWorkOrderIds` e o
      prefixo `@syntheticWorkOrderProviderIdPrefix` do `ClaimBatchSql` e
      restaurar `recipientPhone: workOrder.ContactPhone`.
- [ ] Remover `apps/channel-node/src/test-safeguard.ts` e sua verificação no
      worker; restaurar os testes afetados.
- [ ] Antes de remover, decidir o que fazer com as linhas `Pending` acumuladas
      na outbox de OS não liberadas (o backlog seria disparado de uma vez).
- [ ] Observação: o telefone fixo fica no histórico do git mesmo após a
      remoção.

### Incremento 5 — teste ponta a ponta controlado

- [ ] Iniciar API e dependências.
- [ ] Iniciar Channel Node e confirmar sessão conectada.
- [ ] Confirmar templates ativos para a integração iService.
- [ ] Habilitar a integração somente neste momento.
- [ ] Produzir uma OS de teste elegível.
- [ ] Confirmar outbox, criação da triagem e comando pendente.
- [ ] Confirmar `claim`, envio real e `complete`.
- [ ] Confirmar transição para `CONTATO_INICIADO`.
- [ ] Desabilitar novamente a integração ao terminar, se o ambiente não
      permanecer em teste.

Critério de conclusão:

- uma única mensagem inicial é observada para a OS;
- a mesma chave é preservada em qualquer retentativa;
- logs não contêm tokens, conteúdo sensível ou arquivos de sessão.

## 6. Próximos incrementos depois do envio inicial

Ordem recomendada:

1. [x] evento `MessageReceived` com deduplicação por tenant e chave estável;
2. [x] transição `CONTATO_INICIADO` → `EM_COLETA` para texto individual não
   vazio do número vinculado a exatamente uma triagem;
3. recebimento de mídias e armazenamento no S3 (evento `MediaReceived`),
   incluindo áudio original e transcrição persistida segundo a lógica das
   mensagens inbound;
4. checklist determinístico e contadores de tentativas;
5. integração da LLM;
6. lembretes de 24 e 36 horas;
7. expiração em 48 horas;
8. pool, notificações, resolução manual de mensagens ambíguas e assunção pelo
   operador;
9. respostas do operador pelo painel.

O contrato dos dois primeiros incrementos está registrado no
[ADR-043](../decisions/ADR-043-ingestao-message-received-e-transicao-em-coleta.md).
A implementação adiciona `POST /api/internal/triagens/canal/mensagens`,
escopo `channel.message.ingest` e persistência/deduplicação em
`triagem_channel_messages`. As migrations inbound foram aplicadas ao RDS de
teste em 2026-10-18, após autorização explícita.

Validação local em 2026-10-17:

- `dotnet build apps/api/Atua.Api/Atua.Api.csproj --no-restore`: aprovado,
  zero warnings e zero errors;
- `dotnet test tests/Atua.Api.Tests/Atua.Api.Tests.csproj --no-restore`:
  402/402 testes aprovados, incluindo duplicidade, transição, ambiguidade,
  associação ausente, mensagens de grupo/próprias/vazias e triagem já em
  `EM_COLETA`;
- `pnpm --filter @atua/channel-node check` e
  `pnpm --filter @atua/channel-node test`: aprovados, 33/33 testes;
- `bash -n` dos scripts E2E e de credenciais, e geração local do SQL
  idempotente das migrations: aprovados; nenhum script operacional foi
  executado contra banco real;
- migrations geradas:
  `20261017183609_AddTriagemChannelInboundMessages` e
  `20261017184547_AddTriagemChannelMessageCandidateGinIndex`. Naquela
  validação local, nenhuma havia sido aplicada em ambiente; ambas foram
  aplicadas posteriormente ao RDS de teste em 2026-10-18, após autorização.

O script
`infra/scripts/add-channel-message-ingest-credential.sh` oferece `--dry-run`
sem acesso ao banco e, com `--confirm-write`, adiciona somente o escopo
`channel.message.ingest` quando claim e complete já estão ativos. O token é
salvo em arquivo privado separado; em caso de resultado de banco incerto, o
arquivo pendente é preservado para reconciliação. Em 2026-10-18, o script foi
executado com autorização para a integração de teste já existente. Os três
escopos (`claim`, `complete` e `message.ingest`) foram verificados ativos; o
token inbound foi salvo localmente em
`~/.config/atua/channel/message-ingest.env`.
Para uma integração nova, sem nenhuma credencial Channel ativa,
`infra/scripts/seed-channel-credentials.sh` cria os três escopos
(claim, complete e message.ingest) com as mesmas salvaguardas: `--dry-run`,
`--confirm-write`, caminho fixo `~/.config/atua/channel/channel.env` e
arquivo pendente preservado em caso de resultado incerto. Esse script não foi
necessário no ambiente de teste.
Em 2026-10-19, a API foi redeployada na EC2 de teste pelo alvo documentado
`AWS_PROFILE=moldato AWS_REGION=sa-east-1 make redeploy-api`; o serviço ficou
ativo e o MD5 local/remoto do artefato conferiu. A inspeção somente leitura
não encontrou serviço Channel/Baileys instalado na EC2. Conforme ADR-035 e
ADR-038, o Channel de teste deve ser executado localmente, não instalado na
EC2. Ele usa o túnel SSH local em `127.0.0.1:18080`; não se devem enviar
bearer tokens por HTTP público. O procedimento está em
[channel-node-teste-local.md](../guides/channel-node-teste-local.md).

E2E sintético de saída executado em 2026-10-20 com autorização explícita:

- ID `E2E-20261020100245`; túnel SSH respondeu `{"status":"healthy"}` e o
  Channel local estava conectado;
- resultado no banco: OS `assigned`, outbox `Processed`, triagem
  `ContatoIniciado`, comando `Completed`, zero tentativas extras, sem erros e
  nenhuma mensagem inbound registrada;
- o responsável confirmou exatamente uma mensagem no destinatário de teste e
  a mensagem visível na conversa do WhatsApp do Posto;
- após autorização, somente os dados desse ID foram removidos; a consulta final
  retornou zero linhas;
- isso valida o fluxo de mensagem inicial de saída. Não valida ingestão inbound,
  respostas do consumidor, transição para `EM_COLETA`, LLM ou mídias. O E2E
  inbound foi validado em seguida (ver abaixo).

E2E inbound executado em 2026-10-20 com autorização explícita (aprovado):

- ID `E2E-20261020101138`; envio inicial `Completed`, zero tentativas extras,
  sem erros; o responsável confirmou o recebimento no telefone de teste e
  respondeu pelo aparelho;
- primeira tentativa: nenhum evento inbound registrado e zero mensagens no
  banco. O Channel recusava sessão pareada por QR (`creds.registered=false`)
  e não tinha diagnóstico sanitizado de descarte; corrigido em `6f597e3` e
  `a3a6bfb` (sessão com `me.id` aceita, motivos de descarte categorizados,
  remetente aceito somente de JID `@s.whatsapp.net`). Testes do Channel:
  36/36 aprovados;
- segunda tentativa: logs `whatsapp_inbound_upsert_received` →
  `whatsapp_inbound_message_received` → `channel_inbound_message_reported`,
  mas a mensagem foi gravada como `SemTriagem`. Causa: o WhatsApp entrega o
  remetente brasileiro sem o nono dígito (`55` + DDD + 8 dígitos), enquanto o
  comando guarda o celular com 13 dígitos. Corrigido em `bd786e4` e `0cb749f`:
  a associação usa chave que remove somente o nono dígito de celular
  brasileiro; `SenderPhone` continua gravado como recebido. Suíte da API:
  418/418 aprovados;
- API republicada na EC2 de teste com conferência de MD5, serviço ativo e
  health OK pelo túnel;
- resultado final: triagem `EmColeta` e uma mensagem com resultado
  `ColetaIniciada`;
- limpeza autorizada: OS sintética removida (consulta final com zero linhas)
  e cinco mensagens órfãs `SemTriagem` do número de teste, geradas em
  2026-10-20 antes da correção, excluídas; `triagem_channel_messages` ficou
  sem registros;
- risco conhecido mantido: a fila inbound do Channel é em memória e não é
  durável.

Após as migrations, a tabela `triagem_channel_messages` foi verificada no RDS
de teste com 17 colunas, 7 índices e sem registros inbound.
O ADR-044 define retenção no banco primário: mensagem completa por cinco anos
desde `ReceivedAtUtc`, com exclusão física um ano após `ExpiresAtUtc` (até
seis anos no total). O job executa diariamente enquanto a Master API estiver
em execução; desligamentos podem atrasar o purge. O ambiente sob demanda não
é uma garantia operacional para produção.
Pedido de exclusão ou mais de cinco anos de inatividade da conta, conforme
RS-003, pode antecipar a remoção, mas o fluxo geral de exclusão do tenant
ainda não existe e deverá incluir os registros inbound, anexos e transcrições.
A política de anexos foi definida pelo PO em 2026-10-20 conforme o
[ADR-045](../decisions/ADR-045-retencao-anexos-e-transcricao-audios-triagem.md):

- todos os anexos têm o mesmo prazo: cinco anos desde o recebimento da mensagem
  no inbound do posto, com elegibilidade para expurgo físico um ano depois;
- áudios originais devem ser armazenados em S3 e transcritos; o texto segue
  a lógica de persistência e retenção das mensagens inbound, com vínculo ao
  áudio e ao atendimento;
- upload, transcrição ou reprocessamento não reiniciam a retenção;
- trata-se de decisão de produto informada pelo PO como prazo legal, não de
  parecer jurídico; a validação jurídica e a política de backups permanecem
  pendentes;
- ingestão de mídia e job de expurgo estão implementados localmente; o E2E
  com S3 permanece pendente. Transcrição e exclusão geral do tenant não
  estão implementadas;
- o PO definiu que a transcrição pode ocorrer em segundo plano, que Amazon
  Transcribe deve ser avaliado primeiro e que volume de áudios é desconhecido.
  Também definiu que transcrição pendente ou com falha não bloqueia a triagem:
  operador mantém acesso ao áudio original e vê o texto quando estiver pronto;
- a direção arquitetural foi aceita por `software-architect` e `aws-architect`
  em 2026-10-20 e registrada no
  [ADR-046](../decisions/ADR-046-pipeline-midia-transcricao-audio-triagem.md):
  persistir o áudio original no S3 antes da transcrição batch assíncrona; o
  texto segue a lógica das mensagens inbound e herda o recebimento original;
- a direção aceita não valida a escolha final do fornecedor, qualidade ou
  custos. O expurgo deve ser executado por job ativo da aplicação orientado
  pela data do inbound; S3 Lifecycle por idade do objeto é apenas complementar;
- não há estimativa numérica de custo. Antes da implementação da transcrição,
  validar
  qualidade de pt-BR em amostras representativas, formatos e duração máxima,
  prazo máximo esperado, retentativas e volume. A amostra deve ser autorizada
  e desidentificada. A política de backup e a validação jurídica permanecem
  pendentes;

Validação local do incremento de retenção em 2026-10-18:

- 41/41 testes focados aprovados: política de retenção, mapeamento da triagem
  e endpoints do Channel. Incluem ano bissexto, limite exato de elegibilidade,
  coluna obrigatória e indexada, e operações de preenchimento da migration;
- a migration `20261018123636_AddTriagemInboundMessageRetention` preenche
  `ExpiresAtUtc` em UTC antes de tornar a coluna obrigatória;
- `dotnet ef migrations has-pending-model-changes --no-build`: nenhuma
  diferença entre modelo e snapshot;
- SQL idempotente das três migrations inbound gerado localmente, com
  connection string fictícia; nenhum acesso ao RDS;
- naquela validação local, o job de purge e as migrations ainda não haviam sido
  executados contra PostgreSQL. As migrations de mensagens inbound foram
  aplicadas ao RDS de teste em 2026-10-18; a migration de mídia foi aplicada
  em 2026-10-21. O job de purge foi testado localmente, mas ainda não foi
  validado em serviço remoto;
- em 2026-10-20 foram concluídos os E2E sintéticos de saída e inbound
  descritos acima.

## 7. Bloqueios de produção

Não bloqueiam o desenvolvimento local, mas impedem produção:

- parecer jurídico sobre o uso do canal não oficial;
- definição de base legal e revisão jurídica LGPD;
- validação jurídica e operacionalização da política de retenção dos anexos
  definida no ADR-045; definição da política de backups e implementação do
  fluxo geral de exclusão de dados do tenant conforme RS-003;
- aprovação do ADR-046, avaliação de qualidade e confirmação de limites e
  volume/custos da transcrição antes da implementação;
- topologia always-on do Channel;
- persistência de sessão em produção;
- definição de custos e responsável pelo ambiente contínuo;
- fornecedor e contrato da LLM;
- catálogo confiável para sugestão de peças.

## 8. Fora do escopo deste plano

- omnichannel;
- sugestão de peças sem catálogo;
- cancelamento automático da OS;
- agendamento ou conclusão automática;
- classificação automática de SubStatus;
- exposição do ATYNO como marca ao consumidor;
- armazenamento de tokens ou sessão no repositório.

## 9. Bucket S3 isolado para testes de mídia (2026-10-20, aws-architect)

Provisionado sob demanda explícita do usuário (plano AWS FREE, perfil
`moldato`, região `sa-east-1`), exclusivamente para suportar os futuros testes de integração de
mídia da triagem (ADR-045/ADR-046). Criado **fora** do app CDK em `infra/`
(nenhuma stack existente foi alterada ou destruída), via AWS CLI, seguindo o
mesmo padrão de bucket privado já usado em `AtuaDataStack`
(`blockPublicAccess: BLOCK_ALL`, `encryption: S3_MANAGED`).

Configuração:

- bucket: `atua-462991286554-media-triagem-test`, região `sa-east-1`;
- Block Public Access: todas as 4 opções ativas;
- Object Ownership: `BucketOwnerEnforced` (ACLs desabilitadas);
- criptografia padrão: SSE-S3 (AES256);
- bucket policy: nega qualquer `s3:*` sem TLS (`aws:SecureTransport=false`);
- versionamento: não habilitado (permite limpeza simples por chave);
- sem Object Lock/retenção de 6 anos, sem replicação cross-region (dado
  sintético de teste, não dado de produção coberto pelo ADR-045);
- tags: `app=atua`, `environment=test`, `purpose=media-triagem-smoke-test`,
  `owner=aws-architect`, `created-by=ari`;
- nenhuma role/usuário/key IAM foi criada; nenhum acesso programático de
  runtime foi concedido. A implementação local do adaptador S3 está registrada
  na seção 10, mas o acesso real da API ao bucket ainda depende de autorização
  operacional separada e de credencial de serviço com escopo mínimo.

Smoke test executado (arquivo sintético, sem dados reais):

- chave: `smoke-test/smoke-20261020T205104-6bda84.txt`;
- PUT com `--server-side-encryption AES256`: sucesso, `ServerSideEncryption:
  AES256` confirmado no `HeadObject`;
- GET e comparação de hash SHA-256 local vs. baixado: `HASH_MATCH=OK`
  (`63e5d0a9ab50645eb46baf5845138f2bde974b57d04df3501a03b9985a6b56ed`);
- acesso anônimo (sem credenciais, via HTTPS direto): `HTTP 403` (negado,
  conforme esperado com Block Public Access + policy);
- `DeleteObject` da chave exata executado; `HeadObject` subsequente retornou
  `404 Not Found`;
- `ListObjectsV2` e `ListMultipartUploads` no bucket: ambos vazios
  (`Contents` ausente, nenhum upload incompleto) — bucket permanece vazio e
  existente para os próximos testes de integração.

Comando de limpeza para uso futuro (apagar objetos por chave exata
enumerada, nunca recursivo/wildcard):

```bash
aws s3api delete-object --bucket atua-462991286554-media-triagem-test \
  --key "<chave-exata>" --profile moldato --region sa-east-1
```

Custos: sem estimativa numérica verificada. Cobrança qualitativa aplicável —
armazenamento por GB-mês, requisições PUT/COPY/POST/LIST e GET/SELECT, e
transferência de saída — conforme
[aws.amazon.com/s3/pricing](https://aws.amazon.com/s3/pricing/). Não há objetos
armazenados ao final deste teste; as requisições realizadas continuam sujeitas
à cobrança aplicável ao projeto.

Próximos passos operacionais (não autorizados nesta etapa):

- avaliar Server Access Logging ou CloudTrail data events (S3) e métricas
  do S3 (request metrics) antes do uso real;
- configurar IAM de runtime com escopo mínimo e credencial de serviço para
  `channel.media.ingest` somente após autorização operacional;
- aplicar a migration no ambiente de teste e executar um E2E autorizado com
  mídia sintética, removendo ao final apenas as chaves exatas criadas;
- decidir a vida útil deste bucket de teste junto ao `orchestrator`/PO; a
  exclusão do bucket em si não está autorizada.

## 10. Implementação local da ingestão de mídia (2026-10-21)

ADR-047 autoriza o primeiro incremento, sem transcrição e sem operações na
AWS. A implementação local está concluída e foi aprovada pelo `qa-engineer`.

- API: `POST /api/internal/triagens/canal/midias`, multipart, escopo exclusivo
  `channel.media.ingest`; tenant e integração vêm dos claims da credencial.
- Mídias aceitas: áudio, imagem, vídeo e PDF; limite de 16 MiB; assinatura do
  conteúdo e SHA-256 validados pela API.
- Associação por tenant, integração e telefone; sem triagem única, o binário
  não é armazenado. Para uma única triagem, a mídia é aceita em qualquer
  estado; `CONTATO_INICIADO` avança para `EM_COLETA`.
- S3 adapter usa `PutIfAbsent` e chave
  `tenants/{tenantId}/inbound-media/{mediaId}`; testes usam storage fake.
- Retenção: cinco anos desde `receivedAtUtc`, com elegibilidade de expurgo
  após mais um ano. O job de expurgo ainda não foi implementado.
- Channel Node extrai a mídia do evento WhatsApp e a encaminha como evento
  único, com fila e retentativas; mídia não é duplicada como mensagem textual.
- Migration adicionada, mas não aplicada em ambiente real. Nenhuma chamada
  AWS, alteração de IAM/credenciais/RDS ou deploy foi executada.
- Validação: API **433/433** testes; Channel Node **45/45** testes. A suíte
  .NET reporta avisos de auditoria para `System.Security.Cryptography.Pkcs`
  `9.0.0` (NU1902/NU1903), não alterado nesta tarefa.

Na data desta implementação, a migration e o E2E com S3 ainda estavam
pendentes. O status atualizado está na seção 11.

## 11. Expurgo de mídia e preparação do E2E (2026-10-21)

### Job local de expurgo

- Implementado `TriagemInboundMediaPurgeService` e
  `TriagemInboundMediaPurgeJob`.
- O job reaproveita o marco de retenção existente: elegibilidade quando
  `ExpiresAtUtc + 1 ano <= agora`, onde `ExpiresAtUtc` representa cinco anos
  desde `receivedAtUtc`.
- Para cada item com objeto, exclui primeiro o objeto do storage e depois os
  metadados. Falha de storage mantém os metadados para nova tentativa e não
  interrompe os demais itens do lote; objeto ausente é idempotente.
- Job revisado e aprovado pelo `qa-engineer`; os 5 testes focados de expurgo
  passaram. A suíte completa da API passou anteriormente com 438/438 testes.
- O job está habilitado no código, mas ainda não foi implantado em serviço
  remoto. A migration aplicada abaixo cria a tabela que ele consulta.

### Estado da migration do banco compartilhado

- A migration `20261021194516_AddTriagemChannelInboundMedia` foi aplicada ao
  banco `atua` no RDS `atua-postgres-mvp`, inicialmente tratado como banco de
  teste. Em 2026-10-22, comparação somente leitura confirmou que a conexão
  local, a secret `atua/rds-postgres` usada pela API implantada e esse RDS
  apontam para o mesmo banco.
- O Product Owner autorizou a preparação da publicação da API e da migration
  em produção. Como a migration já constava no histórico do banco usado pela
  API, nenhuma instrução DDL foi executada nessa autorização.

### E2E com S3 (2026-10-22): aprovado

- Decisão do Product Owner: no MVP, sem role de teste dedicada. A API foi
  executada localmente com o profile do desenvolvedor (`moldato`), aceitando
  esse risco apenas para o teste. Nenhuma role/policy IAM foi criada ou
  alterada.
- Credencial `channel.media.ingest` criada com autorização para a integração
  de teste existente, via novo
  `infra/scripts/add-channel-media-ingest-credential.sh` (mesmas salvaguardas
  do script de `message.ingest`); token em
  `~/.config/atua/channel/media-ingest.env` (0600).
- API local em `127.0.0.1:5240` com
  `TriagemInboundMedia__BucketName=atua-462991286554-media-triagem-test`,
  conectada ao RDS de teste; Channel local apontado para ela. Durante o
  teste, os jobs da API local também rodaram contra o RDS compartilhado;
  outbox/comandos usam `SKIP LOCKED` + lease.
- OS sintética `E2E-20261022111425`: outbox `Processed`, comando `Completed`,
  triagem `ContatoIniciado`; recebimento confirmado nos dois aparelhos.
- O Channel caiu com `SessionFatalError` e, após reconectar, não recebia
  eventos; foi necessário reparear a sessão (QR). Depois disso, uma foto real
  sem dados pessoais do número de teste gerou
  `whatsapp_inbound_media_downloaded` (212.780 bytes) e
  `channel_inbound_media_reported`.
- Evidências: `triagem_channel_media` com `Outcome=ColetaIniciada`,
  `MediaKind=Image`, `ExpiresAtUtc` = recebimento + 5 anos; triagem
  `ContatoIniciado` → `EmColeta`; objeto S3
  `tenants/{tenantId}/inbound-media/{mediaId}` com 212.780 bytes,
  `image/jpeg`, `SSE=AES256`.
- Limpeza autorizada: objeto exato excluído (bucket com 0 objetos, 0 versões,
  0 delete markers), linha de mídia excluída, `cleanup E2E-20261022111425`
  com status final de 0 linhas e API local desligada.
- Em validação local, os testes focados da API passaram 23/23 após as
  correções de comparação de hash na reentrega e interrupção de lotes de
  expurgo sem progresso. A suíte também inclui buffers sintéticos com
  assinaturas PDF, OGG e MP4 usando storage fake; isso não exercita WhatsApp
  ou S3 para esses formatos.
- Em 2026-10-22, a infraestrutura foi implantada: bucket persistente
  `atua-462991286554-media-triagem` e somente `s3:PutObject`/
  `s3:DeleteObject` para a role da API nos objetos desse bucket.
- A API foi redeployada a quente em 2026-10-22 a partir do commit
  `eae1edc05e2fb609a83b397c06d6ffae9c3c1289`. O alvo validou o mesmo MD5
  local/remoto e `atua-api` ficou `active (running)`; depois, `GET /health`
  retornou 200 e um POST sem credenciais à rota de mídia retornou 401.
- Pendências: publicar o Channel após resolver os riscos de fila/memória e
  isolamento de erros identificados na revisão; reconectar o Channel ao
  túnel (`127.0.0.1:18080`); executar E2E remoto de mídia com dados sintéticos.
  PDF/áudio/vídeo ainda não foram exercitados em E2E remoto. A transcrição
  permanece bloqueada pelos gates do ADR-046.

### E2E remoto do pool (2026-10-23): envio e pool aprovados

- Primeiro E2E com o Channel rodando na EC2 da API (`atua-channel`, ADR-050),
  sem túnel local. OS sintética `E2E-20261023100358`: outbox `Processed`,
  comando `Completed` sem retentativas, triagem `ContatoIniciado`. O PO
  confirmou o recebimento nos dois aparelhos.
- A resposta do número de teste (texto e foto) foi vinculada: 1 mensagem e 1
  mídia com `Outcome=ColetaIniciada`; triagem `ContatoIniciado` → `EmColeta`.
- Sem a LLM, nenhum fluxo automático leva a `LiberadaAoPool`. Com autorização
  do PO, **somente** essa triagem sintética foi avançada via SQL de `EmColeta`
  para `LiberadaAoPool`. O PO viu o item no pool do Office e conseguiu assumir.
- Limpeza autorizada. O `cleanup` original falhou e foi revertido por
  completo (FK de `triagem_channel_media`). O script passou a apagar as linhas
  de mídia e os objetos no S3 depois do `COMMIT`. Nova execução: 1 mídia
  removida do S3 e status final com 0 linhas.
- Observações do PO: (1) depois de assumir, a triagem sai do pool e o
  operador perde o acesso à conversa; é preciso um ambiente de atendimento
  (proposta: `channel.atyno.com.br`, base do omnichannel). (2) A lista do pool
  é atualizada por polling a cada 30 s, não em tempo real.
- Pendências: anexos não abrem no pool (falta `s3:GetObject`); a transcrição
  no detalhe não foi confirmada visualmente pelo PO.
- Correção (2026-10-23): `s3:GetObject` concedido à `apiRole` nos objetos do
  bucket de mídia (`cdk deploy AtuaComputeStack`, só mudança de policy) e
  validado na EC2 (put/get/URL pré-assinada 200/delete). A skill
  `atua-testar-triagem` foi atualizada para o fluxo remoto. Repetir o E2E
  para confirmar lista, transcrição e anexo no detalhe.

### Repetição do E2E do pool (2026-10-23): bloqueada na entrega

- OS sintética `E2E-20261023110726`, injetada uma única vez após a correção
  de leitura dos anexos. Pré-verificação: API saudável, Channel ativo e
  conectado, nenhuma OS sintética existente.
- Resultado técnico: outbox `Processed`, comando `Completed`, triagem
  `ContatoIniciado`, zero retentativas e zero mensagens recebidas.
- Logs às `11:07:49–11:07:50 UTC`: resolução do destinatário com
  `canonicalJidChanged=true`, ACK do servidor e conclusão do comando com
  `Success`. Nenhum erro ou desconexão observado na janela consultada.
  ACK não comprova entrega ao aparelho.
- O PO confirmou duas vezes que a mensagem deste teste não aparece nem no
  número de teste nem no aparelho do Posto. O texto e a foto mencionados
  pelo PO pertencem ao teste anterior, não a esta execução.
- **Estado: BLOCKED na entrega WhatsApp.** Lista do pool, conversa, anexo
  e assunção não foram validados nesta execução. Não houve liberação ao
  pool, reinjeção, reinício de processo ou limpeza. OS preservada para
  diagnóstico; não reenviar sem nova autorização.

### Reinício do E2E do pool (2026-10-23): mensagem indisponível nos aparelhos

- Após autorização explícita do PO para recomeçar e remover os testes
  anteriores, o script executou `cleanup E2E-20261023110726`. A consulta
  global de status retornou zero linhas; esse teste não tinha mídia vinculada.
- Uma única nova injeção criou a OS `E2E-20261023113541`: outbox `Processed`,
  comando `Completed`, triagem `ContatoIniciado`, zero retentativas, nenhum
  código de erro e zero mensagens recebidas na consulta após o envio.
- O PO viu "Aguardando mensagem. Essa ação pode levar alguns instantes" no
  destinatário. Após conferir os celulares principais conectados, confirmou
  que tanto o celular de teste quanto o do posto continuam mostrando
  "Aguardando mensagem". Ainda não enviou texto ou foto nesta execução.
- **Estado: BLOCKED na disponibilização do conteúdo pelo WhatsApp.** O ACK
  não comprova mensagem legível. A presença de WhatsApp Web aberto foi
  relatada, mas não foi demonstrada como causa. Limpar registros do ATUA não
  desfaz ACK nem corrige o estado criptográfico do WhatsApp.
- OS preservada; sem liberação ao pool, novo reenvio, reinício do Channel,
  exclusão de sessão ou novo pareamento. Lista, conversa, anexo e assunção
  continuam pendentes de validação nesta execução.

### Continuação do E2E do pool (2026-10-23): recebimento e pool aprovados

- A OS `E2E-20261023113541` permaneceu preservada após o relato de
  "Aguardando mensagem". O Channel estava ativo; a consulta filtrada não
  encontrou assinaturas de log para os padrões pesquisados de falha de
  descriptografia/prekey. Os eventos inbound observados inicialmente não
  estavam vinculados à OS (status ainda em `ContatoIniciado`, zero mensagens).
- Sem novo envio outbound, o PO enviou diretamente do número de teste ao
  WhatsApp do posto um texto identificável `E2E 113541`. O script confirmou
  uma mensagem vinculada e a triagem passou a `EmColeta`.
- O PO enviou uma foto na mesma conversa. Consulta agregada somente por tipo
  e resultado confirmou `Image | Armazenada | 1`.
- A OS foi avançada com o subcomando restrito `release-to-pool
  E2E-20261023113541`; o script confirmou `LiberadaAoPool`. O PO confirmou no
  Office: item visível na lista, texto no detalhe, foto aberta e ação
  `Assumir` bem-sucedida.
- Limpeza explicitamente autorizada: o script removeu 7 registros e 1 objeto
  de mídia do S3. Consulta final do ID retornou zero linhas.
- **Resultado:** fluxo inbound (texto e mídia), associação com OS, visualização
  no pool e assunção validados. O envio inicial continua com a observação de
  "Aguardando mensagem" nos aparelhos e não foi considerado entrega visual
  aprovada; a causa ainda não foi determinada. Não houve reinjeção nem
  reinício do Channel nesta continuação.

### Investigação outbound e recuperação de retry (2026-10-23)

- O adapter anterior não fornecia `getMessage` ao Baileys 6.7.24. A
  implementação instalada usa o default `async () => undefined`; no handler
  de receipt retry, o SDK só retransmite se recuperar o payload original.
  Lacuna comprovada no código e compatível com "Aguardando mensagem", sem
  afirmar causa definitiva dos episódios já observados.
- O PO escolheu cache somente em memória, com retenção máxima de 1 hora e
  limite de quantidade. Implementado limite de 256 mensagens por sessão,
  expiração ativa, recuperação do payload original e verificação de
  destinatário/aliases PN e LID. Reconexões preservam o cache no mesmo
  processo; reinício e logout o perdem. Nenhum arquivo de conteúdo foi criado.
- Diagnósticos sanitizados do SDK e do cache registram pedidos de retry,
  original disponível/ausente, falha e limite, sem expor mensagens, JIDs,
  telefones ou chaves. Disponibilidade do original não comprova entrega.
- Validação local: Channel `check`, 69/69 testes e `build` aprovados.
  Cobertura inclui callback no adapter de produção, payload original,
  pedido durante envio, expiração exata, limite de 256, reconexão,
  isolamento de sessão/destinatário e logs sem dados privados.
- Publicação e novo teste real de entrega pendentes; nenhum reenvio,
  reinício, novo pareamento ou mudança remota ocorreu nesta investigação.
- O PO autorizou publicar API, Channel e Office após a validação local e
  um único envio sintético depois da publicação. A skill de E2E foi alinhada
  ao Office gerencial (validar pré-pool e ausência de `Assumir`/envio).
  O status do script agora expõe quantidades, tipos e resultados de mídia,
  além do contador de textos; sintaxe e consulta somente leitura aprovadas.

### E2E pós-fix `getMessage` e Office de gestão (2026-10-23)

- OS `E2E-20261023140750`, uma única injeção: comando `Completed`, 0 retentativas.
- Entrega: nos dois aparelhos, a mensagem mostrou "Aguardando mensagem" por
  cerca de 5 s e depois ficou **legível** (1 mensagem). Os logs registram 4
  `whatsapp_retry_requested` e 4 `whatsapp_retry_message_available`. Isso
  confirma a causa: o aparelho pede retry e, sem `getMessage`, o original não
  era retransmitido.
- Atraso residual de alguns segundos no primeiro envio (negociação de sessão
  E2E do aparelho): aceito por ora; ver riscos.
- Office (gestão, somente leitura): a aba "Em triagem" mostrou a OS em
  `EmColeta` com texto e foto; após `release-to-pool`, a aba "Pool" mostrou a
  conversa e a foto. Sem "Assumir" e sem campo de mensagem.
- Cleanup autorizado: linhas e 1 objeto S3 removidos; `status` = 0 linhas.

### E2E simultâneo com 2 números (2026-10-24)

- Trava ampliada para 2 destinatários (commit `67ee937`); API e Channel
  republicados com a sessão preservada.
- `E2E-20261024161117` (…1248): comando `Completed` em 16:11:39 UTC, 0
  retentativas, triagem `ContatoIniciado`.
- `E2E-20261024161150` (…0065): comando `Completed` em 16:11:58 UTC; o
  cliente respondeu e a IA enviou `RespostaLlm` em 16:12:10 UTC; triagem
  `EmColeta`.
- O PO confirmou nos aparelhos: 1 mensagem inicial legível no …1248;
  mensagem inicial e resposta da IA legíveis no …0065.
- Pendente: concluir as duas conduções, validar no Office e limpar com
  autorização.

## Recuperação de retransmissão após reinício do Channel (2026-10-24)

- Incidente: mensagem com ACK do servidor e sem confirmação de entrega; o Channel reiniciou e
  perdeu o original em memória, e os pedidos de retransmissão do aparelho receberam "original
  indisponível" (cliente vendo "Aguardando mensagem").
- Correção: quando o original não está em memória, o Channel consulta
  `POST /api/internal/triagens/canal/retransmissao` (credencial de claim, sem novo segredo).
  A API devolve o texto somente de comando `Completed` da mesma tenant/integração concluído
  na última hora, casando o id WhatsApp derivado da chave de idempotência. O Channel só
  responde ao WhatsApp se o solicitante for o destinatário original (JID do telefone ou seu
  alias LID). Sem novo armazenamento e sem reenvio de backlog; só atende pedido do aparelho.
- Limitação: mensagens fora da janela de 1 hora (como a das 15h36) não são recuperadas.
- Logs: `whatsapp_retry_message_recovered`, `whatsapp_retry_recovery_failed`,
  `whatsapp_retry_recovery_recipient_mismatch`, `whatsapp_disconnected_fatal`,
  `whatsapp_session_unpaired_fatal`.
