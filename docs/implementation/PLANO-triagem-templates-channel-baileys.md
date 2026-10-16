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
- [ADR-042 — polling com claim/complete](../decisions/ADR-042-polling-claim-complete-para-canal-whatsapp.md).

## 2. Decisões confirmadas

### 2.1. Processo do Channel

- O processo definitivo do Channel será implementado em Node.js/TypeScript.
- O processo Node executará diretamente:
  - polling da Master API;
  - `claim` do comando;
  - envio pelo Baileys;
  - `complete` com resultado estruturado.
- O worker C# atual é uma prova do contrato do ADR-042 e será substituído.
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

- [ ] Gerar tokens distintos para `claim` e `complete`.
- [ ] Persistir somente os hashes no PostgreSQL.
- [ ] Configurar os tokens no ambiente local do Channel.
- [ ] Validar autenticação e isolamento da integração.
- [ ] Manter credenciais do Collector intactas.

Critério de conclusão:

- Channel autentica nos dois endpoints;
- token com escopo de `claim` não executa `complete`, e vice-versa;
- nenhum segredo aparece em logs, commits ou documentação.

### Incremento 4 — Channel Node.js/TypeScript com Baileys

- [ ] Criar worker Node.js/TypeScript.
- [ ] Implementar polling e backoff.
- [ ] Reutilizar os contratos `claim` e `complete` do ADR-042.
- [ ] Integrar o envio do contato inicial pelo Baileys.
- [ ] Carregar sessão a partir do diretório externo configurado.
- [ ] Mapear sucesso, falha transitória e falha definitiva.
- [ ] Preservar chave de idempotência e correlação.
- [ ] Remover o sucesso fake do caminho operacional.
- [ ] Descontinuar o worker C# de prova após equivalência validada.

Critério de conclusão:

- Channel envia uma mensagem real para o número de teste;
- reiniciar o processo não exige novo QR code quando a sessão é válida;
- timeout não é reportado como sucesso;
- `complete` atualiza corretamente comando e triagem.

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

1. evento `MessageReceived` com deduplicação;
2. transição de `CONTATO_INICIADO` para `EM_COLETA`;
3. recebimento de mídias e armazenamento no S3;
4. checklist determinístico e contadores de tentativas;
5. integração da LLM;
6. lembretes de 24 e 36 horas;
7. expiração em 48 horas;
8. pool, notificações e assunção pelo operador;
9. respostas do operador pelo painel.

## 7. Bloqueios de produção

Não bloqueiam o desenvolvimento local, mas impedem produção:

- parecer jurídico sobre o uso do canal não oficial;
- definição de base legal, retenção e exclusão LGPD;
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
