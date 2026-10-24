# Plano de implementação — LLM + JEV para finalizar a triagem (Fase 1)

Status: `PLANNED`

**Data:** 2026-10-23

**Autor:** `software-architect`

Este documento é um **plano**, não uma arquitetura definitiva isolada nem
código. Ele organiza a entrega incremental da peça que falta na fase 1:
substituir o encerramento manual da triagem (hoje feito por script E2E) por
um loop de conversa conduzido pela LLM, com decisões estruturadas delegadas
a um motor de decisão (JEV ou alternativa), mantendo o código da Master API
como única autoridade de estado.

Referências (fonte de verdade, não repetidas aqui):

- [Épico — triagem-whatsapp-fase-1.md](../features/triagem-whatsapp-fase-1.md)
- [Condução da conversa — triagem-llm-conducao.md](../features/triagem-llm-conducao.md)
- [Identidade e tom — triagem-llm-identidade-tom.md](../features/triagem-llm-identidade-tom.md) (em edição pelo PO; tratar como vigente)
- [Pool do operador — triagem-pool-operador.md](../features/triagem-pool-operador.md)
- [Arquitetura da fase 1](../architecture/triagem-whatsapp-fase-1-arquitetura.md)
- [Channel Node multi-tenant](../architecture/channel-node-multi-tenant.md)
- [ADR-045](../decisions/ADR-045-retencao-anexos-e-transcricao-audios-triagem.md),
  [ADR-046](../decisions/ADR-046-pipeline-midia-transcricao-audio-triagem.md),
  [ADR-047](../decisions/ADR-047-ingestao-inicial-midias-triagem.md),
  [ADR-048](../decisions/ADR-048-llm-bedrock-sa-east-1-cobranca-por-tenant.md)
- [PLANO-triagem-templates-channel-baileys.md](./PLANO-triagem-templates-channel-baileys.md)
  (estado atual, evidências de E2E)
- [RISCOS-triagem-midia-pendentes.md](./RISCOS-triagem-midia-pendentes.md)
- [Artigo sobre Strands Decider 2B](https://thenewstack.io/aws-strands-decider-model/)
  (referência exploratória; alegações de desempenho e capacidades devem ser
  verificadas contra a documentação e os artefatos oficiais antes de decisão)
- `apps/api/Atua.Api/Domain/Triagens/Triagem.cs` (máquina de estados vigente)

## 1. Objetivo e escopo

### Objetivo

Fechar o ciclo da triagem: hoje a OS entra, o template é enviado, mensagens
e mídias inbound são persistidas, mas a liberação ao pool (`EM_COLETA` →
`INFORMACOES_SUFICIENTES` → `LIBERADA_AO_POOL`) é feita manualmente por um
script E2E, pois não existe LLM nem decisão automatizada. Este plano cobre
a implementação da condução conversacional pela LLM e das decisões
estruturadas pelo motor selecionado, respeitando as regras do PO.

### Dentro do escopo

- Loop de conversa: LLM interpreta inbound, propõe resposta e campos
  estruturados; API valida e envia via canal existente.
- Motor de decisão estruturada (JEV ou alternativa selecionada no ADR-051)
  consumido **pela Master API**, nunca pelo consumidor diretamente.
- Gates determinísticos no código que leem as saídas do motor e aplicam (ou
  não) transições de estado.
- Modo sombra (shadow mode) antes de qualquer gate automático.
- Modelo de dados para auditoria de chamadas de LLM e motor de decisão (tokens, custo,
  probabilidades, versão de prompt/modelo).
- Exibição read-only no Office dos campos sugeridos e das decisões do motor
  no detalhe da triagem.
- Observabilidade e custo por tenant (base ADR-048).
- Estratégia de teste (fakes de contrato, golden conversations, E2E).

### Fora do escopo

- UI de atendimento do operador (fica para o projeto futuro "Channel
  multi-tenant" / painel do operador completo); este plano cobre apenas a
  exibição read-only das sugestões no detalhe da triagem já existente no
  Office.
- Sugestão de peças (B5) continua desabilitada até existir catálogo
  confiável (RF-TRIAGEM-015), sem mudança aqui.
- Transcrição de áudio (ADR-046) — áudio continua tratado conforme
  `triagem-llm-identidade-tom.md` §4 (pede-se o essencial em texto).
- Classificação de `SubStatus`, agendamento, cancelamento ou conclusão de
  OS — vedado por regra do PO e reforçado em cada gate deste plano.
- Operação always-on em produção, política de backups, parecer jurídico —
  bloqueios de produção já registrados nos ADRs anteriores, não resolvidos
  por este plano.
- Catálogo de peças e de sintomas/defeitos — dependências explícitas, não
  resolvidas aqui.

## 2. Arquitetura de alto nível

### 2.1 Componentes

```
Consumidor (WhatsApp)
        │
        ▼
  Channel (polling claim/complete) ──► Master API
        │                                  │
   MessageReceived/MediaReceived            │
        └─────────────────────────────────►│
                                            ▼
                               Master API (orquestrador da triagem)
                                  │              │
                        ITriagemLlmClient   ITriagemDecisionClient
                          (Bedrock,            (implementação a decidir:
                           ADR-048)             Bedrock/JEV/Decider — §2.3)
                                  │              │
                                  ▼              ▼
                        resposta + campos    Choice/Score/Noul
                        estruturados          (com probabilidades)
                                  │              │
                                  └──────┬───────┘
                                         ▼
                        Código determinístico (domínio Triagem)
                        aplica transições de estado e gates
                                         │
                                         ▼
                        Comando SendMessage (fila existente)
                                         │
                                         ▼
                                     Channel → consumidor
```

A Master API continua sendo a única autoridade (princípio já estabelecido
na arquitetura da fase 1, §1 e §2.5). O fluxo de conversa é orquestrado
pela Master API ao processar `MessageReceived`/`MediaReceived`. Bedrock e
eventuais endpoints remotos são I/O de saída. A alternativa Strands
Decider exigiria inferência própria e pode demandar um runtime separado;
não se presume que caiba no processo ou na EC2 existente. Local de
execução, sizing, disponibilidade e custo são decisões de M0/ADR-051,
considerando a limitação registrada da EC2 (ADR-035, ADR-050: ~916 MB
totais, ~517 MB disponíveis, sem swap). Timeouts, limites e retentativas
devem ser definidos para cada integração.

Dois adaptadores ficam atrás de interfaces que não dependem de fornecedor,
seguindo o padrão já definido (`ITriagemLlmClient`, ADR-048):

- `ITriagemLlmClient` — já previsto em `triagem-llm-conducao.md`; adaptador
  Bedrock `sa-east-1`.
- `ITriagemDecisionClient` (novo, nome provisório) — contrato neutro para
  decisões estruturadas. A implementação pode ser Bedrock com saída
  estruturada, JEV ou Strands Decider; Choice, Score e decisões binárias
  equivalentes podem ser combinadas quando o provedor suportar.

Nenhum dos dois clientes decide estado. Eles **propõem**; o domínio
`Triagem` (código já existente) **decide**, como já estabelecido para a
LLM em `triagem-llm-conducao.md` §1 ("a LLM só propõe, a API valida e
decide") — este plano estende o mesmo princípio ao motor selecionado.

### 2.2 Divisão de responsabilidade LLM × motor de decisão

| Papel | Componente | Responsabilidade |
|---|---|---|
| Conversar | LLM (Bedrock) | Entender a mensagem do consumidor, redigir a próxima pergunta/resposta no tom definido (`triagem-llm-identidade-tom.md`), extrair campos estruturados candidatos, sugerir peças quando houver catálogo (futuro). |
| Decidir | Motor de decisão selecionado no ADR-051 | Choice: categoria de defeito dentre as opções do ATUA. Score: prioridade operacional (rubrica 1–5, a definir pelo PO — ver §12). Decisões binárias equivalentes a Noul: probabilidade de proposições como "checklist tem evidência suficiente para liberar ao pool", "esta triagem precisa de intervenção humana agora". Nota fiscal e etiqueta recebem classificação e avaliação de legibilidade pela visão do Bedrock no fluxo M2, com aplicação determinística ao checklist e conferência humana posterior. Vídeo do defeito é opcional e não é interpretado nesta entrega; a evolução de gates probabilísticos continua em M3–M4. |
| Aplicar | Código determinístico (domínio `Triagem`) | Lê a saída do motor, compara com o limiar vigente (ver §5) e só então chama os métodos existentes do agregado (ex.: avançar para `InformacoesSuficientes`, mover para `AguardandoIntervencao`). |

O motor de decisão não conversa com o consumidor, não escreve orientação
e não executa ação externa — ele só retorna resultado estruturado com
probabilidade, conforme a definição do PO. Isso mantém válidos, sem alteração, todos os
limites já documentados em `triagem-llm-conducao.md` §1 e
`triagem-whatsapp-fase-1-arquitetura.md` §1.3: nenhum modelo cancela,
agenda, classifica `SubStatus` ou conclui a OS — essas ações continuam
fora do alcance de ambos os modelos por construção (os adaptadores não
expõem nenhum método que produza esses efeitos).

### 2.3 Decisões que exigem ADR (abertas, bloqueantes para produção)

1. **ADR-051 (proposto) — Seleção do motor de decisão estruturada.**
   Comparar, sem assumir previamente um fornecedor:
   - **Bedrock com saída estruturada:** mantém o processamento dentro do
     provedor já escolhido no ADR-048, sujeito às capacidades efetivas do
     modelo escolhido, custo, formato e calibração. Saída conforme schema
     não implica probabilidade calibrada: M0 deve confirmar quais sinais
     de confiança existem para o modelo/API escolhido, avaliar calibração
     empiricamente e não comparar diretamente escalas de confiança de
     provedores diferentes.
   - **JEV (TypeSafe AI / jev-llm.com):** avaliar API, autenticação,
     disponibilidade/SLA, preço, região de processamento, retenção e uso
     de dados para treinamento. Sendo serviço externo à AWS, qualquer
     envio de dados pessoais exige avaliação jurídica, DPA, minimização e
     controles de privacidade antes de uso com dados reais. Não presumir
     que os campos estruturados são anônimos: podem continuar
     identificáveis ou conter dados pessoais.
   - **Strands Decider 2B:** o artigo descreve modelo aberto baseado em
     Qwen3.5-2B, com artefatos de modelo/treino disponíveis para execução
     própria e respostas limitadas às opções fornecidas, com confiança.
     Isso pode manter inferência sob controle do ATUA, mas não equivale a
     serviço hospedado no Bedrock. Verificar licença, artefatos, requisitos
     de hardware, processo de inferência, qualidade e suporte antes de
     considerá-lo para produção. O artigo não reporta uma versão hospedada;
     a hipótese de produção considerada aqui é auto-hospedagem, sujeita a
     validação técnica.
   - Comparar as alternativas com o mesmo conjunto de casos do ATUA:
     qualidade por gate, calibração, latência, custo total (incluindo
     infraestrutura e operação), privacidade, disponibilidade, facilidade
     de atualização e reversão. Resultados de benchmark público e
     declarações de fornecedor são evidência inicial, não critério de
     aceite do ATUA.
   - **Residência de dados / LGPD — bloqueante por alternativa.** Para
     qualquer opção, definir quais dados são enviados, onde processam,
     quanto retêm e quem pode acessá-los. Se dados pessoais forem enviados
     ao JEV, exigir parecer/contrato apropriado e minimização aprovada;
     para execução própria do Decider, documentar o fluxo e a retenção
     operacional.
   - Registrar preço e unidade de cobrança e como o custo de decisão se
     combina com a cobrança por tenant do ADR-048.
   - A adoção do Strands Decider ou do JEV não está aprovada por este
     plano. A decisão permanece aberta até o ADR-051 selecionar e aprovar
     uma alternativa. Se todas forem rejeitadas, M3–M5 permanecem
     bloqueados.
2. **ADR complementar (pode ser seção do ADR-051 ou ADR separado) —
   modelo de confiança e limiares dos gates.** Ver §5.

M0 deve comparar as opções em ambiente controlado. M1–M2 podem avançar
com a LLM Bedrock e regras determinísticas, sem depender de decisão
automatizada; M3+ depende de ADR-051 aprovado.

## 3. Sequência do loop de conversa

Reaproveita integralmente o contrato Canal ↔ API já existente (ADR-039,
ADR-042, ADR-043, ADR-047); não há novo endpoint de transporte.

1. Consumidor envia mensagem/mídia → Channel → `MessageReceived` /
   `MediaReceived` (rotas e escopos já existentes).
2. Master API deduplica pelo par `(tenantId, channelIdempotencyKey)` (já
   implementado) e associa a uma única triagem.
3. **Novo passo**: se a triagem está em `ContatoIniciado` ou `EmColeta`, a
   API monta a entrada mínima (checklist atendido/pendente, janela de
   histórico em texto, dados estritamente necessários da OS — conforme
   `triagem-llm-conducao.md` §2) e chama `ITriagemLlmClient` de forma
   síncrona ao processamento do evento (mesmo request/response HTTP já
   usado para I/O externo na API; sem fila adicional).
4. A saída da LLM é validada contra o esquema (`respostaAoConsumidor`,
   `camposEstruturados`, `itensAtendidos`, `sinalDesvio`). Saída inválida
   conta como tentativa inválida (regra já definida); a resposta gerada
   não é enviada.
5. **Gate de segurança antes do envio:** antes de persistir uma resposta
   no outbox, validar de forma determinística que ela respeita o contrato,
   não contém promessa de agendamento/cancelamento/conclusão e não
   ultrapassa o escopo de triagem. Um motor de decisão pode também avaliar
   se a resposta proposta está fundamentada na conversa e se faltam dados
   para responder. Em shadow mode, essa avaliação é registrada, mas não
   bloqueia por si só uma resposta que passou nas validações
   determinísticas. Com o gate ativo e calibrado, ausência, divergência ou
   baixa confiança bloqueia a resposta gerada: o código pode enviar uma
   pergunta de continuação previamente aprovada, após aplicar a ela as
   mesmas validações determinísticas, ou encaminhar para
   `AguardandoIntervencao`. Se a resposta for inválida e não houver
   fallback aprovado, nenhuma mensagem é enviada e a triagem vai para
   intervenção humana.
6. **Novo passo (quando o ADR-051 estiver aceito)**: com os campos
   estruturados em mãos, a API chama `ITriagemDecisionClient` para
   os gates aplicáveis ao turno atual (ex.: "mídia é nota fiscal legível?"
   logo após receber uma imagem; "checklist suficiente?" a cada turno que
   altera `itensAtendidos`). Antes do ADR-051, este passo é pulado e o
   código usa apenas a checagem determinística já definida em
   `triagem-llm-conducao.md` §3 (todos os itens obrigatórios atendidos).
7. O domínio `Triagem` aplica as transições permitidas pelo seu código
   atual (ex.: `MarcarItemAtendido`, transição para
   `InformacoesSuficientes`), usando as saídas validadas da LLM/motor como
   **entrada**, nunca como comando direto de transição.
8. A API persiste o comando `SendMessage` com a resposta validada, na
   mesma fila/outbox de saída já usada para `SendTemplate` (ADR-041,
   ADR-042) — sem novo canal de transporte.
9. O Channel reivindica por polling e envia, reportando `complete` como já
   ocorre hoje.

### Idempotência, rate limit e expiração de 48h

- Idempotência: reaproveita a chave `(tenantId, channelIdempotencyKey)` já
  usada para mensagens inbound e a chave de idempotência dos comandos de
  saída (ADR-039). Chamadas à LLM e ao motor não são reexecutadas para o
  mesmo evento inbound já processado — resultados ficam
  associados ao evento, não
  ao turno, para suportar reentrega `at-least-once` sem duplicar efeitos
  observáveis (mesmo princípio da arquitetura, §1.4).
- Rate limit: por turno, no máximo uma chamada à LLM e uma (combinada) ao
  motor. Orçamento por tenant e por triagem (chamadas e tokens) conforme já
  previsto em `triagem-llm-conducao.md` §5; ao exceder, a triagem vai para
  `AguardandoIntervencao` (mesma regra já definida, reaproveitada, não
  redefinida aqui).
- Expiração de 48h: nenhuma mudança — o timer e a transição para
  `Expirada` já são responsabilidade do código existente e independem de
  LLM/motor estarem disponíveis ou não.

## 4. Fallback de indisponibilidade

Reaproveita a política já definida em `triagem-llm-conducao.md` §4
(até 2 novas tentativas com backoff exponencial, 3 chamadas no total; falha
persistente → `AguardandoIntervencao`). Este plano estende a mesma política
ao motor selecionado:

- Motor indisponível ou timeout: até 2 novas tentativas com backoff
  exponencial (3 chamadas no total); se persistir, a API **não aplica o
  gate automático** e segue a regra determinística equivalente já existente
  (ex.: checklist determinístico de `triagem-llm-conducao.md` §3) ou
  encaminha a triagem para `AguardandoIntervencao` quando não houver regra
  determinística equivalente disponível para aquele gate.
- Em nenhum caso o consumidor fica sem resposta por tempo indefinido —
  mesma garantia já estabelecida para a LLM.
- Falha de ambos (LLM e motor) no mesmo turno: mesma consequência de falha
  da LLM isolada (`AguardandoIntervencao` após as tentativas definidas).

## 5. Gates do motor de decisão e política de limiares

### 5.1 Princípio: shadow mode antes de gate automático

```
Fase A — Shadow mode
  O motor selecionado roda em paralelo ao fluxo determinístico existente.
  Decisão e probabilidade são registradas, mas
  NÃO altera estado. Humano decide como hoje (regra determinística
  ou operador no pool).
        │
        ▼
Fase B — Calibração
  Compara a decisão do motor com a decisão real tomada (humana ou
  determinística) em um conjunto de casos reais.
  Define limiar de confiança por gate (ex.: Noul >= 0.X).
        │
        ▼
Fase C — Gate automático calibrado
  Código passa a usar a saída do motor para a transição, somente
  acima do limiar aprovado. Abaixo do limiar: sempre cai para
  fallback conservador específico do gate (ver §5.2); nenhuma ação é
  autorizada "no escuro".
```

Nenhum gate entra em produção pulando a fase A. O critério de avanço de
fase é decisão de produto (`product-analyst` + PO), não técnica.

### 5.2 Gates candidatos (sujeitos a confirmação do PO — ver §12)

| Gate | Operação do motor | Efeito quando acima do limiar | Efeito quando abaixo |
|---|---|---|---|
| Checklist suficiente | Noul | Autoriza o código a mover para `InformacoesSuficientes` (hoje: regra determinística do checklist, mantida como dupla checagem mesmo após M4) | Mantém `EmColeta` |
| Necessita intervenção humana | Noul | Move para `AguardandoIntervencao` | Mantém fluxo normal |
| Anexo é nota fiscal/etiqueta legível | M2: visão Bedrock + validação determinística; Noul calibrado permanece evolução futura | Marca nota fiscal legível; etiqueta exige também modelo e série preenchidos | Item permanece pendente; informa resultado à conversa para pedir nova foto; operador confere posteriormente |
| Categoria de defeito | Choice ou equivalente | Preenche campo estruturado exibido no Office como sugestão | — |
| Prioridade operacional | Score ou equivalente | Preenche campo estruturado exibido no Office como sugestão | — |

**Importante**: mesmo após calibração, o gate "checklist suficiente"
nunca substitui a checagem determinística dos itens obrigatórios definida
em `triagem-llm-conducao.md` §3 — ela é dupla validação, não substituição,
porque a suficiência do checklist é regra de produto explícita e auditável
(lista fixa de itens), e a probabilidade do motor é um sinal adicional
(ex.: detectar que a descrição textual, mesmo com todos os itens técnicos
presentes, está incoerente ou insuficiente em conteúdo).

### 5.3 Verificação da resposta antes do envio

Antes de gravar a resposta da LLM na outbox, o código aplica validações
determinísticas do contrato e dos limites de produto: formato válido,
conteúdo não vazio, ausência de promessas de agendamento/cancelamento/
conclusão e respeito ao papel de triagem. Essas verificações são
obrigatórias independentemente do fornecedor.

Como avaliação adicional, o motor pode julgar se a resposta proposta está
fundamentada no histórico disponível e se há informação suficiente para
enviá-la sem inventar fatos. Essa avaliação começa em shadow mode. Mesmo
quando habilitada, confiança abaixo do limiar, resposta inválida ou
desacordo entre verificações bloqueia o envio daquela resposta; o código
seleciona uma pergunta segura de continuação ou move a triagem para
`AguardandoIntervencao`. Nenhuma pontuação isolada libera conteúdo
irrestrito ao consumidor.

## 6. Modelo de dados (alto nível)

Não define esquema físico; aponta as adições necessárias para o
`backend-engineer` detalhar:

- **Log de decisão de IA** (uma tabela ou reaproveitamento de uma tabela
  de auditoria existente, a avaliar pelo `backend-engineer`), por
  triagem e por turno:
  - tipo de chamada (`Llm` | operação estruturada de decisão);
  - fornecedor e modelo/versão;
  - versão do prompt/roteiro (já previsto para a LLM em
    `triagem-llm-conducao.md` §5; estender ao JEV);
  - tokens de entrada/saída (quando aplicável) e latência;
  - para o motor: operação, opções candidatas (Choice), rubrica (Score)
    ou proposição binária, sinal de confiança/probabilidade retornado e
    indicação se foi calibrado para aquele gate, limiar vigente no
    momento da chamada e decisão final aplicada pelo código;
  - resultado da validação de esquema (válido/inválido) e se gerou
    tentativa inválida;
  - **sem** conteúdo de mídia e **sem** PII em texto livre — apenas
    campos estruturados e metadados (ver §8).
- **Modo do gate por tenant/versão** (flag: shadow | calibrando | ativo),
  para suportar a progressão da §5.1 sem exigir deploy a cada mudança de
  fase.
- Reaproveita o registro de uso por tenant já exigido pelo ADR-048
  (tenant, triagem, modelo, tokens) como base do custo; a tabela de log de
  decisão pode ser a mesma tabela estendida com os campos específicos do
  motor selecionado, a critério do `backend-engineer`.

## 7. Impacto no Office

- Detalhe da triagem (visão já existente, pré-pool) passa a exibir,
  **somente leitura**:
  - campos estruturados sugeridos pela LLM, marcados "sugerido pela IA"
    (já previsto em `triagem-llm-conducao.md` §5/§6);
  - decisões do motor com a probabilidade retornada (ex.: "Categoria:
    Refrigeração — 0.82"; "Checklist suficiente: 0.91 (ainda não aplicado
    — modo sombra)");
  - indicação clara de qual fase do gate está ativa (sombra/calibrando/
    ativo) para que o operador entenda se a decisão exibida é apenas
    informativa.
- Edição desses campos pelo operador já é um requisito existente
  (auditoria, arquitetura §2.6); este plano não adiciona novo
  requisito de edição, apenas novos campos a exibir.
- UI de atendimento ativo (responder ao consumidor pelo painel) continua
  fora de escopo — fica para o projeto "Channel multi-tenant" futuro,
  como já informado.

## 8. Observabilidade, custo e segurança/LGPD

### Observabilidade e custo

- Métricas por tenant: chamadas de LLM e do motor por triagem, taxa de
  saída inválida, taxa de acionamento de cada gate, distribuição de
  probabilidades por gate (para calibração), tempo médio de triagem
  concluída via IA vs. via intervenção humana.
- Custo: reaproveita o registro de tokens por tenant do ADR-048; registrar
  custo por chamada/operação conforme a alternativa selecionada (a definir
  no ADR-051), incluindo infraestrutura própria se houver.
- Alertas operacionais: taxa de indisponibilidade do motor/LLM acima de um
  limiar, taxa de `AguardandoIntervencao` por falha de IA acima do normal
  (sinal de regressão de prompt ou de modelo).

### Segurança / LGPD

- Minimização já é princípio estabelecido para a LLM
  (`triagem-llm-conducao.md` §2): nenhum CPF, telefone, endereço completo
  ou nome, salvo indispensável. Cada alternativa do motor deve ter seu
  fluxo de dados e retenção documentados. Para serviços de terceiros,
  avaliar contrato, residência e retenção antes de enviar dados reais;
  campos estruturados também podem conter dados pessoais.
- Logs de decisão (§6) não armazenam conteúdo de mídia nem texto livre
  do consumidor — apenas metadados e campos estruturados, mesmo padrão já
  aplicado aos logs de mídia (ADR-047: "resultado técnico deve ser
  registrado sem conteúdo ou dados pessoais nos logs").
- Pendência explícita: validação jurídica/contratual é bloqueio de
  produção para qualquer serviço externo selecionado. No caso do JEV,
  verificar especificamente contrato/DPA com a TypeSafe AI. Não bloqueia
  desenvolvimento em ambiente de teste com dados sintéticos/anonimizados.

## 9. Estratégia de teste

- **Unitário**: domínio `Triagem` ganha testes para os novos gates
  (transições só ocorrem acima do limiar configurado; abaixo do limiar,
  cai para o caminho de intervenção humana ou mantém estado).
- **Contrato/fake**: `FakeTriagemLlmClient` e `FakeTriagemDecisionClient`
  (JEV), análogos ao `FakeTriagemChannel` já usado para o canal,
  permitindo testes determinísticos sem chamar fornecedor real.
- **Golden conversations**: conjunto de transcrições sintéticas
  representativas (cobrindo os cenários já descritos em
  `triagem-llm-identidade-tom.md`: termos populares, ambiguidade, mídia
  ilegível, desvio de assunto, pedido de humano) usado como dataset de
  regressão para a extração de campos estruturados, a fundamentação das
  respostas propostas e os gates do motor antes de qualquer promoção de
  fase (§5.1).
- **E2E**: estende a skill/script `atua-testar-triagem` já usado nos
  incrementos anteriores (evidências em
  [PLANO-triagem-templates-channel-baileys.md](./PLANO-triagem-templates-channel-baileys.md)
  §5, §11) para cobrir: resposta de texto conduzida pela LLM, pelo menos
  um turno com mídia e marcação de legibilidade, e liberação ao pool sem
  intervenção do script manual (critério de "fechamento do ciclo").
- Nenhum teste de carga ou custo real de fornecedor/modelo entra antes
  do ADR-051 ser aceito.

## 10. Entrega faseada

| Fase | Objetivo | Agentes responsáveis | Critério de aceite |
|---|---|---|---|
| **M0** | Spike comparativo com Bedrock structured output, JEV e Strands Decider 2B: confirmar API/artefatos/licença, disponibilidade, região, retenção, preço/custo de infraestrutura, hardware, latência, qualidade e calibração em golden set; redigir ADR-051; confirmar com o PO as questões abertas do §12. Não usar dados pessoais reais no spike. | `software-architect` (ADR e avaliação), `product-analyst` (requisitos abertos, rubrica, categorias), `aws-architect` (Bedrock e infraestrutura de inferência própria), `qa-engineer` (protocolo de comparação) | Comparação reproduzível, critérios e resultados documentados; ADR-051 aceito com uma alternativa selecionada; respostas do PO registradas; se todas as alternativas forem rejeitadas, M3+ não inicia. |
| **M1** | Extração da LLM offline sobre transcrições já existentes (dataset golden), sem loop de conversa em produção. Validar qualidade de extração de campos estruturados antes de automatizar qualquer resposta ao consumidor. | `backend-engineer` (adaptador `ITriagemLlmClient` + fake), `qa-engineer` (avaliação de qualidade do dataset) | ✓ **Implementado** (commits 7f3cd00, 7cd3ac7). Extração roda sobre o dataset golden com taxa de acerto revisada manualmente pelo PO/product-analyst; nenhuma chamada em produção ainda. |
| **M2** | Loop conversacional real, atrás de feature flag `TriagemLlmConversationEnabled` com allowlist de tenant e telefone, restrito ao número de teste (E2E), sem gates automáticos do motor (checklist determinístico e validações de resposta obrigatórias). | `backend-engineer` (loop na Master API, idempotência, fallback), `qa-engineer` (E2E estendido) | ✓ **Implementado** (commits e35109b, 664529b). E2E completo: consumidor de teste conversa com a LLM do início ao fim do checklist mínimo; respostas inseguras/inválidas não são enviadas; a triagem chega a `InformacoesSuficientes`/`LiberadaAoPool` sem o script manual. |
| **M3** | Motor selecionado em modo sombra: registra decisão e probabilidade, não altera estado nem libera respostas com base na avaliação probabilística. Requer ADR-051 aceito com alternativa selecionada. | `backend-engineer` (adaptador `ITriagemDecisionClient`, log de decisão), `frontend-engineer` (exibição read-only no Office), `aws-architect` (infra própria ou conectividade do fornecedor, conforme ADR) | Decisões aparecem em log e Office como somente leitura para todos os turnos elegíveis; nenhuma transição nem envio é autorizado apenas pelo motor. |
| **M4** | Calibração: comparar decisões e verificações de resposta com desfechos reais (humanos ou determinísticos) de volume mínimo a definir com o PO; aprovar limiares por gate. | `product-analyst` (critérios), `qa-engineer` (relatório), `software-architect` (revisão do ADR de limiares) | Limiares por gate aprovados pelo PO e registrados; sem isso, nenhum gate probabilístico liga. |
| **M5** | Habilitação gradual para o tenant atual: gates automáticos ativos acima do limiar aprovado; abaixo do limiar, sempre cai para intervenção humana. | `backend-engineer`, `frontend-engineer` (exibição no Office), `qa-engineer` (regressão completa), `documentation` (atualizar arquitetura e features) | Canary controlado no tenant com feature flag, monitoramento e critério de rollback definidos; ciclo completo roda sem o script E2E manual; métricas de custo/observabilidade (§8) ativas; documentação de arquitetura atualizada. |

Cada fase é incremental e reversível pela feature flag; nenhuma fase
pressupõe que a seguinte será aprovada.

## 11. Riscos

| Risco | Descrição | Mitigação |
|---|---|---|
| Dependência de fornecedor externo (JEV) sem avaliação de disponibilidade/custo | Pode não ser viável tecnicamente ou comercialmente | M0 compara as alternativas antes de integração real; decisão registrada no ADR-051 |
| Exposição de PII a terceiro fora da AWS | Viola minimização/LGPD se mal implementado | Avaliação jurídica/contratual e minimização por alternativa antes de M3; log sem conteúdo livre |
| Alucinação ou falha de calibração do motor | Decisão errada liberada automaticamente | Shadow mode (M3) + calibração com casos reais (M4) antes de qualquer gate automático (M5) |
| Inferência própria do Strands Decider exige mais recursos que a EC2 atual | Modelo aberto pode demandar vários GB, além de CPU/GPU e operação contínua; a EC2 da API tem cerca de 916 MB | M0 mede requisitos e custo total; não instalar modelo na EC2 de produção no spike; comparar execução separada e Bedrock; exigir aprovação de arquitetura/custo antes de provisionar compute |
| Resposta da LLM não fundamentada ou fora do escopo | Pode inventar fatos, prometer ação proibida ou induzir o consumidor | Validação determinística sempre ativa; avaliação de fundamentação em shadow; fallback para pergunta segura ou intervenção humana; golden set de regressão |
| Orquestração síncrona aumenta latência ou pressão de recursos | Chamadas externas podem aumentar latência da API; inferência própria pode exigir processo e infraestrutura separados | Timeouts e limites por integração; runtime próprio não roda na EC2 atual sem sizing aprovado; `aws-architect` acompanha métricas e custo antes de M5 |
| Ambiguidade entre checklist determinístico e gate "checklist suficiente" do motor | Risco de dois critérios divergentes confundirem o operador | Checklist determinístico continua autoritativo mesmo após M4 (ver §5.2); o motor é sinal adicional, não substituto |
| Falta de catálogo de peças e de categorias de defeito | Choice depende de lista fechada ainda não definida pelo PO | Bloqueia a definição final desse gate até confirmação (ver §12); sem lista fechada, Choice não entra em produção |

## 12. Perguntas em aberto para o PO

1. **Catálogo de peças**: existe hoje? É por provedor ou por tipo de
   produto? Onde está hospedado? (Já registrada como pendência em
   `triagem-whatsapp-fase-1.md` §9, item 2; reafirmada aqui porque bloqueia
   a extensão futura de sugestão de peças pela LLM.)
2. **Lista fechada de categorias de defeito** para Choice: o
   plano assume o exemplo do PO (refrigeração, elétrica, instalação, uso
   incorreto, administrativo) — essa é a lista final ou há mais
   categorias/subcategorias?
3. **Rubrica de prioridade operacional (Score 1–5)**: quais critérios
   definem cada nível? Quem define a rubrica (PO, operação do posto)?
4. **Definição de "suficiente" além do checklist determinístico**: o que
   exatamente a decisão binária "checklist suficiente" deveria capturar que a lista
   fixa de itens obrigatórios (já determinística) não capturaria sozinha?
   Sem uma resposta clara, este plano recomenda manter apenas a checagem
   determinística e não ativar esse gate específico (ver §5.2).
5. **Limiares de confiança por gate**: quem aprova (PO sozinho, ou com
   `product-analyst` e QA) e qual volume mínimo de casos reais é exigido
   antes de aprovar (M4)?
6. **Volume e orçamento de chamadas** ao motor por triagem/tenant: existe
   um teto, análogo ao já prudente para a LLM (ADR-048), antes de cair em
   `AguardandoIntervencao` por orçamento excedido?
7. **Gate de resposta ao consumidor**: quais critérios o PO exige para
   considerar uma resposta fundamentada e segura? O fallback preferido é
   uma pergunta neutra de esclarecimento ou intervenção humana?

## 13. BLOQUEANTE — resumo para o orchestrator

```text
Status: ARCHITECTURE_CHANGE_REQUIRED / BLOCKED (parcial)

Bloqueante: seleção e aprovação do motor de decisão para M3+.
Motivo: opções (Bedrock structured output, JEV e Strands Decider) têm
trade-offs ainda não comparados quanto a qualidade/calibração, custo,
residência de dados e operação. Serviços externos exigem avaliação
jurídica/contratual antes de dados reais; inferência própria exige
dimensionamento e aprovação de infraestrutura.
Informação necessária: comparação M0, respostas do PO às perguntas do
§12 e ADR-051 aprovado.
Impacto: M3–M5 não iniciam sem ADR-051; M0–M2 podem prosseguir com Bedrock
para conversa, checklist determinístico e validações determinísticas de
resposta.
Responsável recomendado: `software-architect` conduz o ADR-051 com apoio
de `aws-architect`, `product-analyst` e `qa-engineer`, para decisão final
do Product Owner.
```
