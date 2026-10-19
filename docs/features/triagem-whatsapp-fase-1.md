# Épico: Triagem automatizada de OS via WhatsApp (Fase 1)

Status: `REQUIREMENTS_DEFINED`

**Data:** 2026-09-30

**Atualizado em:** 2026-10-13

## 1. Visão

O Posto Autorizado recebe OS "assigned" do provedor, como a Midea. Cada OS exige uma triagem com o consumidor antes do agendamento. Hoje isso é manual.

O módulo inicia a conversa no WhatsApp e coleta os dados necessários. Uma LLM interpreta a descrição do defeito e sugere peças. A OS chega ao operador já com um checklist preenchido, para que a primeira visita seja resolutiva.

- **Princípio:** o ATYNO é ferramenta interna do posto. O consumidor conversa com o posto, não com o ATYNO.
- **Princípio:** o operador humano é sempre obrigatório. A LLM só coleta, interpreta e sugere, e termina seu papel ao liberar a OS.

## 2. Atores

- **Consumidor final:** interage apenas pelo WhatsApp.
- **Operador do posto:** assume a OS após a liberação, revisa o checklist e segue o atendimento.
- **Gestor do posto:** configura números, pools e grupos. Este papel é hipótese, a confirmar.
- **Provedor (Midea):** origem da OS e dono da regra de cancelamento em 48h.
- **LLM (externa):** coleta, interpreta e sugere. Nunca conclui a OS.
- **Collector:** fonte das OS "assigned".

## 3. Subsistemas (visão funcional, sem arquitetura)

1. **Ingestão e fila:** o Collector detecta OS "assigned" e as enfileira. Um worker consome a fila.
2. **Canal WhatsApp:** envia o template inicial e recebe mensagens e anexos. O vínculo é pelo número cadastrado na OS.
3. **Orquestrador de conversa (LLM):** conduz a triagem, pede o que falta, interpreta e decide quando as informações bastam.
4. **Armazenamento de anexos:** S3, vinculado à OS.
5. **Roteamento e pools:** números por posto associados a pools de grupos de atendimento, ou a uso geral.
6. **Notificações:** notificação in-app em tempo real para operadores online.
7. **Painel de suporte à triagem:** checklist, anexos, peças sugeridas e transcrição.

## 4. Fluxo completo

1. O Collector obtém a OS "assigned" e a coloca na fila.
2. O worker consome a OS e os dados do consumidor, e envia o template inicial.
3. O consumidor responde. A LLM conduz a coleta dos itens do checklist mínimo:
   - descrição detalhada do defeito
   - foto legível da nota fiscal
   - foto legível da etiqueta (modelo e série)
   - pelo menos uma foto OU vídeo do defeito
   - endereço completo
4. Itens opcionais (ponto de referência e localização) podem ser coletados posteriormente pelo operador.
5. A LLM estrutura os dados (tipo de defeito, sintoma, código de erro, categoria). Sugestão de peças depende de catálogo confiável (RF-TRIAGEM-015).
6. A LLM considera o checklist mínimo satisfeito e libera a OS para `LIBERADA_AO_POOL`, notificando o pool. O operador assume a triagem.

## 5. Máquina de estados da triagem

| Estado | Descrição |
|---|---|
| `AGUARDANDO_ENVIO` | OS enfileirada, template ainda não enviado |
| `CONTATO_INICIADO` | Template enviado, aguardando o consumidor |
| `EM_COLETA` | Conversa ativa com a LLM, itens pendentes |
| `INFORMACOES_SUFICIENTES` | A LLM considera o checklist completo |
| `LIBERADA_AO_POOL` | Disponível aos operadores |
| `ASSUMIDA_POR_OPERADOR` | O humano assumiu. Estado terminal da triagem |
| `EXPIRADA` | Prazo de 48h sem checklist mínimo; triagem visível/encaminhada ao pool para decisão humana |
| `FALHA_DE_CONTATO` | Número inválido, sem WhatsApp, envio falhou |
| `AGUARDANDO_INTERVENCAO` | Mídia ilegível após 3 tentativas do mesmo item ou mensagens fora de contexto após 3 desvios consecutivos |

**Total de estados: 9**

**Transições principais**
- `AGUARDANDO_ENVIO` → `CONTATO_INICIADO` → `EM_COLETA` → `INFORMACOES_SUFICIENTES` → `LIBERADA_AO_POOL` → `ASSUMIDA_POR_OPERADOR`.
- `CONTATO_INICIADO` ou `EM_COLETA` → `EXPIRADA` por prazo (48h sem checklist mínimo).
- `AGUARDANDO_ENVIO` → `FALHA_DE_CONTATO` por erro de envio imediato.
- `EM_COLETA` → `AGUARDANDO_INTERVENCAO` por mídia ilegível (3+ tentativas) ou desvios fora de contexto (3 consecutivos).
- Após `ASSUMIDA_POR_OPERADOR`, a LLM não participa mais deste atendimento.

## 6. Sobreposição com o catálogo de SubStatus

Em princípio não há sobreposição:
- SubStatus (RF-031) é o motivo de pendência do provedor, com um responsável associado.
- A classificação de defeito da triagem (tipo, sintoma, código de erro, categoria) é um conceito distinto.

A fase 1 não classifica SubStatus. O risco é de confusão terminológica, e o RF-028 já corrigiu um erro parecido com `pendingReason` e sintoma. Sugiro nomear os campos da triagem de forma distinta. **A confirmar:** a leitura do CSV de SubStatus.

## 7. Premissas (do PO, sujeitas à revisão de arquitetura)

- Vínculo conversa↔OS pelo número de WhatsApp cadastrado na OS.
- Anexos em S3, vinculados à OS, com retenção a definir.
- Novas mensagens caem em pool compartilhado, com atribuição automática a grupo.
- Um posto pode ter vários números. Na Fase 1, as triagens seguem para um pool compartilhado por posto; associações por grupos ficam para evolução futura.
- Notificação in-app em tempo real e atualização do contador do pool para operadores autorizados.
- Em desenvolvimento, o Baileys roda localmente. A execução na EC2 existente foi considerada inviável na avaliação registrada na seção 13.
- LLM externa aceita na fase 1. O fornecedor fica para decisão arquitetural.
- Operador humano sempre obrigatório.
- A LLM não classifica SubStatus, não agenda e não conclui.

## 8. Bloqueios e dependências

### Bloqueios de desenvolvimento
- **Template do Midea:** conteúdo pendente de revisão pelo PO antes de implementar e validar o envio real. Não impede as demais frentes de desenvolvimento.
- **Catálogo de peças:** sugestão de peças (B5) fica dependente de catálogo confiável. Mantido no épico como dependência explícita.

### Bloqueios de produção
- **Parecer jurídico e LGPD:** Uso de Baileys (API não oficial), envio de mensagens ativas, coleta de dados sensíveis (CPF na nota fiscal), retenção de anexos e conformidade com LGPD. Não bloqueia desenvolvimento, mas bloqueia transição para produção.
- **Fornecedor de LLM:** contrato, base legal, local de processamento de dados e limite de custo. Não bloqueia desenvolvimento.
- **Topologia always-on para produção:** instância dedicada (ADR-035). Não bloqueia desenvolvimento (execução sob demanda). Bloqueia operação contínua em produção.

### Riscos técnicos (não bloqueadores, com mitigações)

| Risco | Descrição | Mitigação |
|---|---|---|
| Alucinação da LLM | Sugestão incorreta de peças ou de "suficiência". | Humano sempre revisa. Exibir a sugestão como sugestão, nunca como decisão. |
| Baileys (instabilidade) | Risco de banimento do número (uso de API não oficial) e instabilidade. | `aws-architect` valida e `software-architect` define topologia. |
| Expiração em 48h | A regra é da Midea e o ATYNO precisa acompanhar o prazo com lembretes. | Sistema de timers, notificações e encaminhamento para `AGUARDANDO_INTERVENCAO` já definidos. |
| Multi-tenant | Isolamento de números, conversas e anexos entre tenants. | Tratar como requisito não funcional na arquitetura. |
| Custo de inferência | Custo variável por OS/conversa, sem responsável definido. | Definir modelo de cobrança e limites por plano (RF-020). |

## 9. Perguntas em aberto

### Resolvidas em 2026-10-13

6. **Expiração em 48h:** Após 48h sem checklist mínimo, a OS passa de `CONTATO_INICIADO` ou `EM_COLETA` para `EXPIRADA`, visível/encaminhada ao pool para decisão humana com histórico completo. Não há cancelamento automático da OS; a decisão sobre cancelamento fica com o pool.

7. **Notificação:** Notificação in-app em tempo real + atualização do contador do pool. Ocorre em: liberação para pool, expiração, falha de contato e nova mensagem pré-assunção.

10. **Critério de "informações suficientes":** Checklist mínimo rígido = descrição detalhada; foto legível da nota fiscal; foto legível da etiqueta (modelo e série); pelo menos uma foto OU vídeo do defeito; endereço completo. Ponto de referência e localização são opcionais e podem ser coletados pelo operador após assunção.

11. **Quando o consumidor manda mensagem fora de contexto ou depois da liberação:**
    - **Fora de contexto durante `EM_COLETA`:** resposta breve redirecionando ao fluxo; após 3 desvios consecutivos, passa para `AGUARDANDO_INTERVENCAO`, pool assume, LLM para neste atendimento.
    - **Após `LIBERADA_AO_POOL` e antes de `ASSUMIDA_POR_OPERADOR`:** nova mensagem/anexo fica no mesmo fluxo de triagem, atualiza checklist se complementa os itens, e alerta o pool com notificação in-app.
    - **Após `ASSUMIDA_POR_OPERADOR`:** somente o operador responde. LLM para definitivamente.

12. **Consumidor sem WhatsApp, número divergente ou mídia ilegível:**
    - **Sem WhatsApp ou número inválido / falha definitiva do envio inicial:** `FALHA_DE_CONTATO`, encaminhamento imediato ao pool, sem reintentatórias.
    - **Mídia ilegível (nota, etiqueta ou foto do defeito):** item pendente, LLM pede novo envio explicando o problema específico; máximo 3 tentativas por item. Após 3ª rejeição: passa para `AGUARDANDO_INTERVENCAO`, encaminhado ao pool, LLM para neste atendimento.

14. **Template:** Primeira entrega usa template fixo da Midea. O texto atual requer revisão pelo PO, pois diz que vídeo é opcional, enquanto a decisão exige pelo menos uma foto OU vídeo. Isso bloqueia a implementação/validação do envio real, embora não impeça outras frentes de desenvolvimento.

15. **Nota fiscal:** Sem nota fiscal legível o checklist não completa; em 48h expira e vai ao pool.

5. **Pools e roteamento (Fase 1):** Pool compartilhado por posto, visível a operadores autorizados. Primeiro operador que assume tem exclusividade. Priorização, grupos e SLA permanecem como pendências futuras.

### Resolvidas em 2026-10-18

16. **Retenção de mensagens inbound:** manter o registro completo por cinco
    anos desde `ReceivedAtUtc` e excluí-lo fisicamente um ano após
    `ExpiresAtUtc`. Solicitação de exclusão ou inatividade da conta por mais
    de cinco anos, conforme RS-003, pode antecipar a remoção. Ver ADR-044.

### Pendentes

1. Formato final dos campos estruturados e tipo de triagem (formulário, checklist dinâmico, roteiro livre ou híbrido).
2. Existe catálogo de peças? É por provedor ou por tipo de produto? Onde está?
3. Existe catálogo de sintomas e defeitos por tipo de produto?
4. Retenção de anexos em S3: prazo, política e LGPD.
8. LGPD: base legal, local da LLM e contrato com o fornecedor.
9. Custo de LLM: modelo de cobrança e quem paga (ATYNO ou posto).
13. Papéis: o gestor configura números e pools? Isso conversa com RF-021.
17. Implementar o fluxo geral de exclusão de dados do tenant conforme RS-003,
    incluindo mensagens inbound; definir também como a retenção de backups
    atende a exclusões antecipadas.

## 10. Decomposição sugerida em user stories

**Bloco A – Ingestão e contato**
- A1: detectar OS "assigned" e enfileirar para triagem.
- A2: enviar template inicial por OS.
- A3: tratar falha de envio (`FALHA_DE_CONTATO`).

**Bloco B – Conversa e coleta**
- B1: receber mensagens e anexos, vinculando à OS pelo número.
- B2: a LLM conduz a coleta dos itens obrigatórios e opcionais.
- B3: armazenar anexos vinculados à OS.
- B4: estruturar a descrição (tipo, sintoma, erro, categoria).
- B5: sugerir peças. Depende do catálogo.
- B6: a LLM decide a suficiência e libera a OS.

**Bloco C – Humano**
- C1: pool de triagem e notificações.
- C2: painel com checklist, anexos e transcrição.
- C3: o operador assume a OS (exclusividade para primeiro operador; os demais veem quem assumiu).
- C4: operador responde ao consumidor (texto e anexos) e edita campos/sugestões da LLM, com auditoria de alterações. Removido de bloqueio de fase 1, incluído no escopo.

**Bloco D – Gestão**
- D1: cadastro de números do posto e associação a pools e grupos.
- D2: expiração em 48h, lembretes automáticos em 24h e 36h (listando todos os itens pendentes naquele momento), e tratamento pós-expiração (encaminhamento ao pool).
- D3: auditoria, retenção e LGPD (consentimento e exclusão).
- D4: acompanhamento de custo de LLM.

**Sugestão de ordem:** A1 → A2 → B1 → B2 → B3 → C1 → C2 → C3 → C4 → B4 → B6. Depois B5 (dependente do catálogo), D1, D2, D3 e D4.

**Dependências:**
- B5 (sugerir peças) fica bloqueado até catálogo confiável disponível. Não será removido do épico, mantido como dependência explícita.
- C4 (operador responder e editar) está no escopo da fase 1.

## 11. Visão de longo prazo

**Épico 2 – Omnichannel:** Instagram, Facebook, Webchat, e-mail, Reclame Aqui e Google. O ATYNO seria a central de atendimento do posto. É direção futura, sem detalhamento nesta fase.

## 12. Encaminhamentos recomendados

- `aws-architect`: validar Baileys no EC2 em relação ao ADR-034 e ao controle de custo, e persistência de sessão para desenvolvimento.
- Parecer jurídico e LGPD: antes de qualquer lançamento em produção. Não bloqueia desenvolvimento.
- Product owner: catálogo de peças e de sintomas, e a decisão de quem paga a LLM.

**Estado atual:** As 15 decisões do PO foram incorporadas (2026-10-13). Máquina de estados revisada (9 estados). Requisitos macro e critérios de aceite adicionados (seções 16 e 17). Transição de `Draft` para `REQUIREMENTS_DEFINED`.

## 13. Nota técnica: desenho preliminar do Baileys (2026-10-01)

### (a) Decisões do PO

- **Operação sempre ligada adiada:** a proposta de EC2 dedicada e always-on para o Baileys (ADR-035) é adiada para quando o épico for a produção. Desenvolvimento segue **sob demanda**, sem instância dedicada.
- **PO não aceita custo fixo adicional por ora:** a premissa de instância dedicada sempre ligada é rejeitada para a fase de desenvolvimento, conforme ADR-035 (Status Proposed).
- **Risco jurídico/LGPD sem parecer:** a questão de canal não oficial permanece sem parecer jurídico/LGPD. Risco identificado mas **não bloqueia desenvolvimento**. Bloqueia apenas a **transição para produção**.

### (b) Premissa revisada: Baileys no EC2 existente

A premissa original (seção 7) "Baileys rodando no EC2 existente" foi avaliada pelo `aws-architect` como **inviável** na t3.micro colocada.

**Medição do estado atual (2026-10-01, make ec2-exec):**
- RAM total: 916 MB
- RAM usada: 613 MB
- RAM disponível: ~119 MB (~13%)
- Swap: 391/1023 MB (38% em uso)
- atua-api: 45 MB / 20 tasks
- atua-collector: 610 MB / 94 tasks (Chromium/Playwright)
- Baileys estimado: 150–250 MB

**Resultado:** Não há espaço para adicionar Baileys sem risco de OOM-kill. Referência: ADR-034 (stack API + Collector na t3.micro), ADR-035 (análise de topologia).

### (c) Desenho preliminar do software-architect (alto nível)

**Premissa para desenvolvimento:** Baileys roda **apenas durante o ciclo da stack** (`make up/make down`), com número de teste, sem instância dedicada.

**Componentes e responsabilidades:**

1. **Canal WhatsApp (Baileys):** apenas transporta mensagens. Não mantém estado da triagem.
2. **Master API:** concentra todo o estado da triagem (máquina de estados, validações, regras de negócio).
3. **Worker de envio e fila:** consome a fila de OS "assigned" e gerencia o envio de templates e mensagens. Reutiliza o padrão **MongoDB Change Streams** definido em ADR-023 (a formalizar em decisão específica da fila do épico).
   - Registro por OS garante envio único do template (idempotência).
   - Fila sem SQS (conforme ADR-023).
4. **LLM:** consultada pela Master API, não pelo Baileys. Apenas sugestões e interpretação.
5. **Painel de suporte à triagem:** acesso apenas via Master API, sem acesso direto ao Baileys.

**Contrato Canal ↔ API (eventos):**

- Eventos do canal para API:
  - `MessageReceived`: mensagem de texto recebida
  - `MediaReceived`: mídia recebida (foto, vídeo, etc.)
  - `DeliveryStatusUpdate`: status de entrega do template enviado
  - `ChannelFailure`: erro do canal

- Comandos da API para canal:
  - `SendMessage`: enviar mensagem de texto
  - `SendTemplate`: enviar template estruturado

**Sessão do Baileys:** opção a validar por spike, sem decisão ainda. Alternativas:
- Volume EBS (localizado na instância efêmera)
- MongoDB Atlas (conforme ADR-012, reutilizável)
- AWS Secrets Manager

Nenhuma validada. Impacto: se a sessão for perdida a cada `make down`, requer novo pareamento por QR code. **Questão em aberto para `software-architect`:** avaliar persistência da sessão em armazenamento externo de modo que sobreviva a ciclos `make down/up`.

### (d) ADRs pendentes em ordem de decisão

1. **ADR-035 (Topologia do Baileys):** EC2 dedicada always-on **adiada para produção**. Desenvolvimento segue sob demanda.
2. **Persistência da sessão do Baileys:** armazenamento externo vs. efêmero. Decisão e spike necessários.
3. **Operação always-on vs. sob demanda:** confirmação para produção (ADR-035 relacionada).
4. **Fila do épico:** escolha entre MongoDB Change Streams, SQS ou outra. Relacionada a ADR-023 (fila geral do projeto).
5. **Contrato canal ↔ API:** definição formal de eventos e comandos (esboçado acima).
6. **Idempotência do envio do template:** garantia de "exatamente uma vez" por OS.

### (e) Em aberto

- **Desenvolvimento sem custo fixo:** como rodar o Baileys na fase de desenvolvimento sem EC2 dedicada, mantendo a sessão de teste entre ciclos (se necessário). A validar em spike.
- **Quem paga o custo de produção (pergunta 9 da seção 9):** ainda sem responsável definido. Relacionada a decisão de billing e RF-020.

## 14. Nota operacional: Baileys na fase de desenvolvimento (2026-10-01)

### (a) Decisão do PO

**Baileys rodando localmente na máquina do desenvolvedor** com número de teste, apontando para a API na stack ligada por `make up`. Referência: ADR-036 (a registrar).

### (b) Custos e esforço

- **Custo:** zero (sem infraestrutura adicional).
- **Esforço:** baixo (máquina local).

### (c) Operação

1. Desenvolvedor executa `make up` para ligar a stack (API + Collector + dependências).
2. Desenvolvedor executa Baileys localmente (Node.js) apontado para o endpoint da API.
3. **Reconfiguração obrigatória:** o endpoint da API deve ser reconfigurado a cada `make up` porque o IP pode mudar (docker network efêmera).
4. **Segurança:** liberar temporariamente o IP do desenvolvedor no Security Group do RDS (regra restrita `/32`, removida ao final do teste).

### (d) Limitações conhecidas

- **Sessão/QR:** a sessão do Baileys fica ligada ao processo local e cai se a máquina dormir ou o processo for interrompido. Requer novo pareamento via QR code.

### (e) Alternativas avaliadas e não recomendadas agora

Pelo `aws-architect`:

1. **t3.small temporária durante `make up`:** custo estimado ~US$ 0,0208/h (preço us-east-1, **não confirmado para sa-east-1**). Adiciona complexidade de provisionamento e descomissionamento, sem benefício na fase de desenvolvimento.
2. **Baileys na t3.micro colocada:** risco de OOM-kill (apenas ~119 MB de RAM livre disponível, vide seção 13.b). Exigiria parar o Collector para liberar espaço. Não recomendado.

### (f) Registro e escopo

Esta nota é registrada **como nota operacional, sem ADR novo** neste momento. ADR será necessário quando a operação always-on for decidida para produção (relacionada a ADR-035 e ADR-036).

### (g) Pendências

1. **Spike de persistência de sessão** (`backend-engineer`): avaliar armazenamento externo (MongoDB Atlas, EBS volume ou AWS Secrets Manager) de modo que a sessão sobreviva a ciclos `make down/up` e dormir da máquina. Impacta a praticidade do desenvolvimento.
2. **Parecer jurídico/LGPD** (antes da produção): validar o uso de Baileys como canal não oficial, coleta de dados sensíveis (CPF, endereço, mídia) e conformidade com LGPD. Não bloqueia desenvolvimento, mas bloqueia transição para produção.

## 15. Spike: persistência da sessão do Baileys (2026-10-01)

**Realizado por:** `backend-engineer`

**Fonte:** README oficial do Baileys (WhiskeySockets/Baileys). Nenhum arquivo do repositório foi alterado.

### (a) Confirmado no spike

- Método padrão (`useMultiFileAuthState`) grava credenciais e chaves em arquivos num diretório local.
- As chaves mudam a cada mensagem enviada ou recebida (evento `creds.update`), resultando em **escritas frequentes**.
- O próprio Baileys recomenda banco SQL/NoSQL em produção.
- Não há store MongoDB oficial no core; apenas implementações comunitárias estão disponíveis.

### (b) Opções avaliadas

1. **Arquivos em EBS persistente**
   - Maturidade: alta
   - Esforço: mínimo
   - Restrição: exige backup do volume
   - Status: viável

2. **MongoDB Atlas** (conforme ADR-012)
   - Reaproveitamento: infra de dados já em uso
   - Limitação: apenas bibliotecas de terceiros, não avaliadas
   - Esforço: médio
   - Risco: possível overhead de escritas no free tier
   - Status: viável, mas com ressalvas

3. **AWS Secrets Manager**
   - Status: **descartado** — inadequado para dado que muda a cada mensagem

4. **Amazon S3**
   - Status: **descartado** — janela de inconsistência, apenas adequado para backup frio, não como armazenamento principal

### (c) Risco comum

Duas instâncias com a mesma sessão não são suportadas. O WhatsApp invalida a sessão duplicada.

### (d) Recomendação provisória

- **Desenvolvimento:** arquivos locais em disco (seção 14.d). Em desenvolvimento local a sessão fica na máquina.
- **Produção:** volume EBS persistente. Revisitar MongoDB Atlas apenas quando o processo contínuo de produção for desenhado e arquitetura de estado for definida (ligado a ADR-035).
- **Consequência:** exigirá ADR específico ligado a ADR-035.

**Nota:** Esta recomendação **substitui** a ideia de usar MongoDB Atlas para a sessão levantada na nota do ADR-035 como necessária para sobreviver a `make down/up`.

### (e) Não verificado (deixar para próximas fases)

1. Qualidade e manutenção das bibliotecas MongoDB de terceiros
2. Preço atual do Secrets Manager por chamada
3. Consequência para a conta (banimento) de conexões simultâneas

## 16. Requisitos macro (decisões do PO, 2026-10-13)

### RF-TRIAGEM-001: Checklist mínimo rígido
A OS não é liberada para o pool sem os seguintes itens obrigatórios:
1. Descrição detalhada do defeito (texto)
2. Foto legível da nota fiscal
3. Foto legível da etiqueta (modelo e série)
4. Pelo menos uma foto OU vídeo do defeito
5. Endereço completo do consumidor

Ponto de referência e localização são opcionais; o operador pode coletar após assunção.

### RF-TRIAGEM-002: Transição para LIBERADA_AO_POOL
Após o checklist mínimo ser satisfeito, a OS passa para `LIBERADA_AO_POOL` e notifica o pool (notificação in-app + atualização de contador).

### RF-TRIAGEM-003: Mensagens pós-liberação (antes de assunção)
Consumidor envia nova mensagem ou anexo após `LIBERADA_AO_POOL` e antes de `ASSUMIDA_POR_OPERADOR`:
- A mensagem/anexo fica na mesma triagem
- Atualiza o checklist se complementa itens obrigatórios ou opcionais
- Pool recebe notificação in-app de nova atividade + atualização de contador

### RF-TRIAGEM-004: Exclusividade do operador assumido
O primeiro operador a assumir a OS obtém exclusividade para aquele atendimento. Os demais operadores veem quem assumiu. Notificação in-app em tempo real.

### RF-TRIAGEM-005: Contexto após ASSUMIDA_POR_OPERADOR
Após o operador assumir, somente o operador responde ao consumidor. A LLM para definitivamente neste atendimento. Operador usa painel ATYNO para enviar/receber mensagens e anexos.

### RF-TRIAGEM-006: Edição e auditoria pelo operador
O operador edita todos os campos produzidos pela LLM (tipo de defeito, etc.). Quando sugestões de peças existirem (após habilitação do catálogo), o operador também poderá editá-las. Todas as alterações são auditadas com identificação do operador, timestamp e valor anterior.

### RF-TRIAGEM-007: Expiração em 48h
Sem checklist mínimo após 48h, a OS passa de `CONTATO_INICIADO` ou `EM_COLETA` para `EXPIRADA`, visível/encaminhada ao pool para decisão humana com histórico completo. Não há cancelamento automático da OS.

### RF-TRIAGEM-008: Lembretes automáticos
Lembretes em 24h e 36h sem checklist mínimo, listando todos os itens ainda pendentes naquele momento. Textos dos lembretes pendentes de revisão do PO.

### RF-TRIAGEM-009: Falha de contato inicial
Número inválido, sem WhatsApp ou falha definitiva do envio inicial do template:
- Estado `FALHA_DE_CONTATO`
- Encaminhamento imediato ao pool
- Sem reintentatórias automáticas
- Notificação in-app + contador

### RF-TRIAGEM-010: Pool compartilhado por posto
O pool de triagem é compartilhado por posto, visível a todos os operadores autorizados do posto. Isolamento por posto (multi-tenancy).

### RF-TRIAGEM-011: Notificações em tempo real
Notificação in-app em tempo real + atualização de contador do pool para os eventos:
- Liberação para pool (`LIBERADA_AO_POOL`)
- Expiração (`EXPIRADA`)
- Falha de contato (`FALHA_DE_CONTATO`)
- Nova mensagem pré-assunção (antes de `ASSUMIDA_POR_OPERADOR`)

### RF-TRIAGEM-012: Mídia ilegível e limite de tentativas
Nota fiscal, etiqueta ou foto do defeito ilegível:
- Item pendente, LLM pede novo envio explicando o problema específico
- Máximo 3 tentativas por item
- Após rejeição 3ª: passa para `AGUARDANDO_INTERVENCAO`, encaminhado ao pool, LLM para

### RF-TRIAGEM-013: Mensagens fora de contexto
Durante `EM_COLETA`, mensagem fora do fluxo de triagem:
- Resposta breve redirecionando ao fluxo
- Após 3 desvios consecutivos: `AGUARDANDO_INTERVENCAO`, pool assume, LLM para

### RF-TRIAGEM-014: Estado AGUARDANDO_INTERVENCAO
Estado intermediário para mídia ilegível após 3 tentativas do mesmo item ou mensagens fora de contexto após 3 desvios consecutivos. É encaminhado ao pool com histórico completo.

### RF-TRIAGEM-015: Sugestão de peças bloqueada
Sugestão de peças não será implementada sem catálogo confiável. Mantida como dependência explícita do épico (item B5). A LLM estrutura a informação, mas não sugere peças até catálogo disponível.

### RF-TRIAGEM-016: Retenção de mensagens inbound
Mensagens inbound completas são mantidas por cinco anos a partir de
`ReceivedAtUtc`, com `ExpiresAtUtc` calculado para esse marco. O registro é
excluído fisicamente um ano após a expiração. Solicitação de exclusão ou
inatividade da conta por mais de cinco anos, conforme RS-003, pode antecipar
a remoção. O fluxo geral de exclusão do tenant deve incluir esses registros.
Anexos e backups têm políticas separadas ainda pendentes. Ver ADR-044.

## 17. Critérios de aceite verificáveis

### CA-001: Checklist mínimo e liberação (RF-TRIAGEM-001, RF-TRIAGEM-002)
**Cenário:** OS com todos os 5 itens obrigatórios
- Dado uma OS com descrição detalhada, foto legível da nota fiscal, foto legível da etiqueta, foto ou vídeo do defeito e endereço completo
- Quando o checklist é validado pela LLM
- Então a OS passa para `LIBERADA_AO_POOL` e o pool recebe notificação in-app + atualização de contador

**Cenário:** OS faltando item obrigatório
- Dado uma OS sem a foto da etiqueta
- Quando o checklist é validado
- Então permanece em `EM_COLETA` e LLM pede a foto da etiqueta

### CA-002: Mídia ilegível e limite de 3 tentativas (RF-TRIAGEM-012)
**Cenário:** Primeira rejeição de mídia
- Dado uma foto da nota fiscal ilegível
- Quando a LLM valida
- Então marca como pendente, pede novo envio explicando por que foi rejeitada e mantém contador em 1/3

**Cenário:** Terceira rejeição (limite atingido)
- Dado 3 rejeições da mesma mídia (nota fiscal) após 3 envios
- Quando o terceiro envio é rejeitado
- Então passa para `AGUARDANDO_INTERVENCAO`, encaminha ao pool, LLM para neste atendimento

### CA-003: Mensagens fora de contexto e limite de 3 desvios (RF-TRIAGEM-013)
**Cenário:** Primeiro desvio
- Dado uma mensagem fora do contexto de triagem (ex.: "Qual é o valor do conserto?")
- Quando a LLM identifica
- Então envia resposta breve redirecionando ao fluxo e incrementa desvio_count para 1/3

**Cenário:** Terceiro desvio consecutivo
- Dado 3 desvios consecutivos sem retorno ao contexto
- Quando ocorre o terceiro desvio
- Então passa para `AGUARDANDO_INTERVENCAO`, encaminha ao pool, LLM para

### CA-004: Nova mensagem pré-assunção (RF-TRIAGEM-003)
**Cenário:** Novo anexo complementando checklist
- Dado uma OS em `LIBERADA_AO_POOL` ainda não assumida
- Quando o consumidor envia nova foto do defeito (item obrigatório já satisfeito)
- Então atualiza o anexo na triagem, alerta pool com notificação in-app e atualiza contador

**Cenário:** Item opcional pós-liberação
- Dado uma OS em `LIBERADA_AO_POOL`
- Quando o consumidor envia apenas ponto de referência (opcional)
- Então registra no checklist sem bloquear liberação

### CA-005: Expiração em 48h e encaminhamento (RF-TRIAGEM-007)
**Cenário:** Timeout de 48h sem checklist mínimo
- Dado uma OS em `CONTATO_INICIADO` ou `EM_COLETA` há 48h
- Quando o timeout é acionado
- Então passa para `EXPIRADA`, visível/encaminhada ao pool para decisão humana com histórico, notifica pool in-app, e **não cancela a OS**

### CA-006: Lembretes em 24h e 36h (RF-TRIAGEM-008)
**Cenário:** Lembrete em 24h
- Dado uma OS sem checklist mínimo há 24h
- Quando o timer é acionado
- Então envia lembrete listando todos os itens ainda pendentes naquele momento (exemplo: "Faltam: foto da etiqueta, foto ou vídeo do defeito, endereço")

**Cenário:** Lembrete em 36h
- Dado uma OS sem checklist mínimo há 36h
- Quando o timer é acionado
- Então envia segundo lembrete listando todos os itens ainda pendentes naquele momento

### CA-007: Falha de contato inicial (RF-TRIAGEM-009)
**Cenário:** Número inválido
- Dado um número de WhatsApp inválido ou inexistente no template inicial
- Quando o envio é tentado
- Então passa imediatamente para `FALHA_DE_CONTATO`, notifica pool in-app + contador, sem reintentatórias

### CA-008: Exclusividade do operador (RF-TRIAGEM-004, RF-TRIAGEM-011)
**Cenário:** Primeiro operador assume
- Dado uma OS em `LIBERADA_AO_POOL`
- Quando o operador A clica em "Assumir"
- Então passa para `ASSUMIDA_POR_OPERADOR`, operador A fica vinculado, e todos os operadores veem que foi assumida

**Cenário:** Segundo operador vê exclusividade
- Dado uma OS já assumida por operador A
- Quando operador B tenta visualizar a triagem
- Então vê claramente que foi assumida e por quem, sem opção de assumir novamente

### CA-009: Operador responde e edita com auditoria (RF-TRIAGEM-005, RF-TRIAGEM-006)
**Cenário:** Operador envia mensagem
- Dado uma OS em `ASSUMIDA_POR_OPERADOR`
- Quando o operador envia mensagem via painel ATYNO
- Então a mensagem é enviada ao consumidor, LLM não participa, e operador pode receber respostas

**Cenário:** Operador edita campo do checklist
- Dado uma sugestão de peça ou campo preenchido pela LLM
- Quando o operador edita o valor
- Então registra a alteração com nome do operador, timestamp e valor anterior na auditoria

### CA-010: Sugestão de peças bloqueada (RF-TRIAGEM-015)
**Cenário:** LLM identifica peça sem catálogo
- Dado uma OS sem catálogo confiável disponível
- Quando a LLM processa o defeito
- Então estrutura o tipo de defeito e sintoma, mas não sugere peças; mantém campo vazio ou "não disponível"

**Cenário:** Catálogo disponível (futuro)
- Dado um catálogo confiável implementado
- Quando LLM processa novo defeito
- Então pode sugerir peças conforme catálogo validado

### CA-011: Template pendente de revisão (RF-TRIAGEM-001, nota sobre template)
**Cenário:** Template atual com contradição de mídia
- Dado o template fixo da Midea usado na primeira entrega
- Quando a implementação estiver pronta para envio real
- Então o template deve ser revisado pelo PO antes de implementar e validar o envio real, sem reescrever agora o conteúdo do template

### CA-012: Expiração e purge de mensagens inbound (RF-TRIAGEM-016)
**Cenário:** Mensagem ainda dentro do prazo de retenção
- Dado um registro inbound recebido em `ReceivedAtUtc`
- Quando ainda não se passaram cinco anos desde o recebimento
- Então o registro permanece completo e `ExpiresAtUtc` corresponde a
  `ReceivedAtUtc + 5 anos`

**Cenário:** Mensagem além do prazo de retenção pós-expiração
- Dado um registro com `ExpiresAtUtc` definido
- Quando o instante atual alcançar um ano após `ExpiresAtUtc`
- Então o job de purge pode excluir fisicamente o registro do banco primário

**Cenário:** Exclusão antecipada de dados do tenant
- Dado um pedido de exclusão ou uma conta inativa por mais de cinco anos
- Quando o fluxo geral de exclusão de dados do tenant for executado
- Então ele também deve remover os registros inbound, sem aguardar os seis
  anos desde o recebimento
