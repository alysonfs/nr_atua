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
- Channel Node — `src/test-safeguard.ts`: qualquer destinatário diferente do
  número de teste é concluído como `PermanentFailure`
  (`TEST_RECIPIENT_NOT_ALLOWED`) sem envio.

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
3. recebimento de mídias e armazenamento no S3 (evento `MediaReceived`);
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
ainda não existe e deverá incluir os registros inbound. Retenção de anexos
em S3 e de cópias em backups permanece pendente.

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
  executados contra PostgreSQL. As migrations foram aplicadas ao RDS de teste
  em 2026-10-18; o job de purge continua sem validação operacional;
- em 2026-10-20 foram concluídos os E2E sintéticos de saída e inbound
  descritos acima.

## 7. Bloqueios de produção

Não bloqueiam o desenvolvimento local, mas impedem produção:

- parecer jurídico sobre o uso do canal não oficial;
- definição de base legal e revisão jurídica LGPD;
- definição da retenção de anexos e backups; implementação do fluxo geral de
  exclusão de dados do tenant conforme RS-003;
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
