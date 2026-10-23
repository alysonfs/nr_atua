# Plano de implementação — LLM + JEV para finalizar a triagem (Fase 1)

Status: `PLANNED`

**Data:** 2026-10-23

**Autor:** `software-architect`

Este documento é um **plano**, não uma arquitetura definitiva isolada nem
código. Ele organiza a entrega incremental da peça que falta na fase 1:
substituir o encerramento manual da triagem (hoje feito por script E2E) por
um loop de conversa conduzido pela LLM, com decisões estruturadas delegadas
a um segundo modelo (JEV), mantendo o código da Master API como única
autoridade de estado.

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
- `apps/api/Atua.Api/Domain/Triagens/Triagem.cs` (máquina de estados vigente)

## 1. Objetivo e escopo

### Objetivo

Fechar o ciclo da triagem: hoje a OS entra, o template é enviado, mensagens
e mídias inbound são persistidas, mas a liberação ao pool (`EM_COLETA` →
`INFORMACOES_SUFICIENTES` → `LIBERADA_AO_POOL`) é feita manualmente por um
script E2E, pois não existe LLM nem decisão automatizada. Este plano cobre
a implementação da condução conversacional pela LLM e das decisões
estruturadas pelo JEV, respeitando as regras do PO.

### Dentro do escopo

- Loop de conversa: LLM interpreta inbound, propõe resposta e campos
  estruturados; API valida e envia via canal existente.
- JEV como motor de decisão estruturada (Choice/Score/Noul) consumido
  **pela Master API**, nunca pelo consumidor diretamente.
- Gates determinísticos no código que leem as saídas do JEV e aplicam (ou
  não) transições de estado.
- Modo sombra (shadow mode) do JEV antes de qualquer gate automático.
- Modelo de dados para auditoria de chamadas de LLM e JEV (tokens, custo,
  probabilidades, versão de prompt/modelo).
- Exibição read-only no Office dos campos sugeridos e das decisões do JEV
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
                        ITriagemLlmClient   ITriagemDecisionClient (JEV)
                          (Bedrock,            (vendor externo,
                           ADR-048)             a definir — ver §2.3)
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
na arquitetura da fase 1, §1 e §2.5). Este plano **não** introduz um novo
processo de orquestração: a condução da conversa roda **dentro da Master
API**, como mais um passo do mesmo fluxo que hoje processa
`MessageReceived`/`MediaReceived`. Não há worker novo ou serviço separado
para a conversa — isso evitaria reintroduzir o problema de memória da EC2
(ADR-035, ADR-050: ~916 MB totais, ~517 MB disponíveis, sem swap). A
chamada à LLM e ao JEV é um I/O de saída (HTTP) feito de dentro do mesmo
processo .NET que já atende requisições e já roda o `BackgroundService` da
outbox (ADR-041), seguindo o mesmo padrão arquitetural já usado: nenhum
componente novo residente em memória, apenas chamadas de saída
request/response com timeout e retentativa.

Dois adaptadores ficam atrás de interfaces que não dependem de fornecedor,
seguindo o padrão já definido (`ITriagemLlmClient`, ADR-048):

- `ITriagemLlmClient` — já previsto em `triagem-llm-conducao.md`; adaptador
  Bedrock `sa-east-1`.
- `ITriagemDecisionClient` (novo, nome provisório) — encapsula as três
  operações do JEV (Choice, Score, Noul), combináveis em uma única
  chamada por turno quando fizer sentido (ex.: Noul de "checklist
  suficiente" + Choice de categoria de defeito no mesmo payload).

Nenhum dos dois clientes decide estado. Eles **propõem**; o domínio
`Triagem` (código já existente) **decide**, como já estabelecido para a
LLM em `triagem-llm-conducao.md` §1 ("a LLM só propõe, a API valida e
decide") — este plano estende o mesmo princípio ao JEV.

### 2.2 Divisão de responsabilidade LLM × JEV

| Papel | Componente | Responsabilidade |
|---|---|---|
| Conversar | LLM (Bedrock) | Entender a mensagem do consumidor, redigir a próxima pergunta/resposta no tom definido (`triagem-llm-identidade-tom.md`), extrair campos estruturados candidatos, sugerir peças quando houver catálogo (futuro). |
| Decidir | JEV | Choice: categoria de defeito dentre as opções do ATUA. Score: prioridade operacional (rubrica 1–5, a definir pelo PO — ver §10). Noul: probabilidade de proposições binárias como "checklist tem evidência suficiente para liberar ao pool", "esta triagem precisa de intervenção humana agora", "este anexo é nota fiscal/etiqueta legível". |
| Aplicar | Código determinístico (domínio `Triagem`) | Lê a saída do JEV, compara com o limiar vigente (ver §5) e só então chama os métodos existentes do agregado (ex.: avançar para `InformacoesSuficientes`, mover para `AguardandoIntervencao`). |

JEV não conversa com o consumidor, não escreve orientação e não executa
ação externa — ele só retorna resultado estruturado com probabilidade,
conforme a definição do PO. Isso mantém válidos, sem alteração, todos os
limites já documentados em `triagem-llm-conducao.md` §1 e
`triagem-whatsapp-fase-1-arquitetura.md` §1.3: nenhum modelo cancela,
agenda, classifica `SubStatus` ou conclui a OS — essas ações continuam
fora do alcance de ambos os modelos por construção (os adaptadores não
expõem nenhum método que produza esses efeitos).

### 2.3 Decisões que exigem ADR (abertas, bloqueantes para produção)

1. **ADR-051 (proposto) — Adoção do JEV (TypeSafe AI / jev-llm.com) como
   motor de decisão estruturada.** Pontos obrigatórios do ADR:
   - Fornecedor fora da conta AWS: diferente do Bedrock (ADR-048), o JEV é
     um terceiro externo. Isso reabre a mesma pergunta que o ADR-048
     resolveu para a LLM, mas agora para um fornecedor fora da AWS.
   - **Residência de dados / LGPD — BLOQUEANTE.** Enviar qualquer dado do
     consumidor ao JEV exige, no mínimo: (a) DPA (Data Processing
     Agreement) com a TypeSafe AI; (b) minimização — enviar só os campos
     estruturados já extraídos pela LLM (ex.: "defeito: não gela, split
     inverter"), nunca texto livre com nome, CPF, telefone ou endereço;
     (c) avaliar anonimização/pseudonimização adicional antes do envio;
     (d) confirmar região de processamento e se há retenção/treinamento
     pelo fornecedor.
   - Disponibilidade e SLA do serviço (não há ainda avaliação de uptime,
     rate limit, nem plano de contingência de fornecedor único).
   - API: formato de chamada, autenticação, limites de payload, suporte a
     múltiplas operações (Choice+Score+Noul) numa única chamada.
   - Preço: modelo de cobrança (por chamada, por token, por operação) e
     como isso se combina com a cobrança por tenant do ADR-048.
   - Alternativa a avaliar e registrar no ADR: usar só a LLM (Bedrock) com
     saída estruturada (JSON Schema / structured output) em vez de um
     segundo fornecedor, caso o JEV não passe nos critérios acima. Essa
     alternativa reduz superfície de exposição de dados a terceiros, ao
     custo de abrir mão das garantias específicas de calibração/formato
     bounded do JEV.
   - **Este plano trata a decisão de adotar o JEV como aberta.** M0 (§9)
     entrega o ADR; as fases seguintes (M3+) dependem da sua aprovação.
2. **ADR complementar (pode ser seção do ADR-051 ou ADR separado) —
   Modelo de confiança e limiares dos gates do JEV.** Ver §5.

Enquanto o ADR-051 não for aceito, a arquitetura permite (e recomenda)
avançar M0–M2 (LLM pura, sem JEV) para não bloquear todo o incremento —
ver §9.

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
   conta como tentativa inválida (regra já definida); nenhuma mensagem é
   enviada ao consumidor nesse caso.
5. **Novo passo (quando o ADR-051 estiver aceito)**: com os campos
   estruturados em mãos, a API chama `ITriagemDecisionClient` (JEV) para
   os gates aplicáveis ao turno atual (ex.: "mídia é nota fiscal legível?"
   logo após receber uma imagem; "checklist suficiente?" a cada turno que
   altera `itensAtendidos`). Antes do ADR-051, este passo é pulado e o
   código usa apenas a checagem determinística já definida em
   `triagem-llm-conducao.md` §3 (todos os itens obrigatórios atendidos).
6. O domínio `Triagem` aplica as transições permitidas pelo seu código
   atual (ex.: `MarcarItemAtendido`, transição para
   `InformacoesSuficientes`), usando as saídas validadas da LLM/JEV como
   **entrada**, nunca como comando direto de transição.
7. A API persiste o comando `SendMessage` com a resposta validada, na
   mesma fila/outbox de saída já usada para `SendTemplate` (ADR-041,
   ADR-042) — sem novo canal de transporte.
8. O Channel reivindica por polling e envia, reportando `complete` como já
   ocorre hoje.

### Idempotência, rate limit e expiração de 48h

- Idempotência: reaproveita a chave `(tenantId, channelIdempotencyKey)` já
  usada para mensagens inbound e a chave de idempotência dos comandos de
  saída (ADR-039). A chamada à LLM/JEV não é reexecutada para o mesmo
  evento inbound já processado — o resultado fica associado ao evento, não
  ao turno, para suportar reentrega `at-least-once` sem duplicar efeitos
  observáveis (mesmo princípio da arquitetura, §1.4).
- Rate limit: por turno, no máximo uma chamada à LLM e uma (combinada) ao
  JEV. Orçamento por tenant e por triagem (chamadas e tokens) conforme já
  previsto em `triagem-llm-conducao.md` §5; ao exceder, a triagem vai para
  `AguardandoIntervencao` (mesma regra já definida, reaproveitada, não
  redefinida aqui).
- Expiração de 48h: nenhuma mudança — o timer e a transição para
  `Expirada` já são responsabilidade do código existente e independem de
  LLM/JEV estarem disponíveis ou não.

## 4. Fallback de indisponibilidade

Reaproveita a política já definida em `triagem-llm-conducao.md` §4
(retentativa com backoff; falha persistente → `AguardandoIntervencao`).
Este plano estende a mesma política ao JEV:

- JEV indisponível ou timeout: retentativa com backoff limitado; se
  persistir, a API **não aplica o gate automático** e segue a regra
  determinística equivalente já existente (ex.: checklist determinístico
  de `triagem-llm-conducao.md` §3) ou encaminha a triagem para
  `AguardandoIntervencao` quando não houver regra determinística
  equivalente disponível para aquele gate.
- Em nenhum caso o consumidor fica sem resposta por tempo indefinido —
  mesma garantia já estabelecida para a LLM.
- Falha de ambos (LLM e JEV) no mesmo turno: mesma consequência de falha
  da LLM isolada (`AguardandoIntervencao` após as tentativas definidas).

## 5. Gates de decisão com JEV e política de limiares

### 5.1 Princípio: shadow mode antes de gate automático

```
Fase A — Shadow mode
  JEV roda em paralelo ao fluxo determinístico existente.
  Decisão do JEV é registrada (probabilidade + resultado), mas
  NÃO altera estado. Humano decide como hoje (regra determinística
  ou operador no pool).
        │
        ▼
Fase B — Calibração
  Compara decisão do JEV com a decisão real tomada (humana ou
  determinística) em um conjunto de casos reais.
  Define limiar de confiança por gate (ex.: Noul >= 0.X).
        │
        ▼
Fase C — Gate automático calibrado
  Código passa a usar a saída do JEV para a transição, somente
  acima do limiar aprovado. Abaixo do limiar: sempre cai para
  AguardandoIntervencao (nunca decide "no escuro").
```

Nenhum gate entra em produção pulando a fase A. O critério de avanço de
fase é decisão de produto (`product-analyst` + PO), não técnica.

### 5.2 Gates candidatos (sujeitos a confirmação do PO — ver §10)

| Gate | Operação JEV | Efeito quando acima do limiar | Efeito quando abaixo |
|---|---|---|---|
| Checklist suficiente | Noul | Autoriza o código a mover para `InformacoesSuficientes` (hoje: regra determinística do checklist, mantida como dupla checagem mesmo após M4) | Mantém `EmColeta` |
| Necessita intervenção humana | Noul | Move para `AguardandoIntervencao` | Mantém fluxo normal |
| Anexo é nota fiscal/etiqueta legível | Noul | Marca item do checklist como atendido | Pede reenvio (contador de tentativa já existente) |
| Categoria de defeito | Choice | Preenche campo estruturado exibido no Office como sugestão | — |
| Prioridade operacional | Score | Preenche campo estruturado exibido no Office como sugestão | — |

**Importante**: mesmo após calibração, o gate "checklist suficiente"
nunca substitui a checagem determinística dos itens obrigatórios definida
em `triagem-llm-conducao.md` §3 — ela é dupla validação, não substituição,
porque a suficiência do checklist é regra de produto explícita e auditável
(lista fixa de itens), e o Noul do JEV é um sinal probabilístico adicional
(ex.: detectar que a descrição textual, mesmo com todos os itens técnicos
presentes, está incoerente ou insuficiente em conteúdo).

## 6. Modelo de dados (alto nível)

Não define esquema físico; aponta as adições necessárias para o
`backend-engineer` detalhar:

- **Log de decisão de IA** (uma tabela ou reaproveitamento de uma tabela
  de auditoria existente, a avaliar pelo `backend-engineer`), por
  triagem e por turno:
  - tipo de chamada (`Llm` | `JevChoice` | `JevScore` | `JevNoul`);
  - fornecedor e modelo/versão (ex.: `bedrock:anthropic.claude-...`,
    `jev:v1.2`);
  - versão do prompt/roteiro (já previsto para a LLM em
    `triagem-llm-conducao.md` §5; estender ao JEV);
  - tokens de entrada/saída (quando aplicável) e latência;
  - para JEV: operação, opções candidatas (Choice), rubrica (Score) ou
    proposição (Noul), probabilidade retornada, limiar vigente no
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
  JEV, a critério do `backend-engineer`.

## 7. Impacto no Office

- Detalhe da triagem (visão já existente, pré-pool) passa a exibir,
  **somente leitura**:
  - campos estruturados sugeridos pela LLM, marcados "sugerido pela IA"
    (já previsto em `triagem-llm-conducao.md` §5/§6);
  - decisões do JEV com a probabilidade retornada (ex.: "Categoria:
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

- Métricas por tenant: chamadas de LLM e de JEV por triagem, taxa de
  saída inválida, taxa de acionamento de cada gate, distribuição de
  probabilidades por gate (para calibração), tempo médio de triagem
  concluída via IA vs. via intervenção humana.
- Custo: reaproveita o registro de tokens por tenant do ADR-048; para o
  JEV, registrar custo por chamada/operação conforme o modelo de
  cobrança do fornecedor (a definir no ADR-051).
- Alertas operacionais: taxa de indisponibilidade do JEV/LLM acima de um
  limiar, taxa de `AguardandoIntervencao` por falha de IA acima do normal
  (sinal de regressão de prompt ou de modelo).

### Segurança / LGPD

- Minimização já é princípio estabelecido para a LLM
  (`triagem-llm-conducao.md` §2): nenhum CPF, telefone, endereço completo
  ou nome, salvo indispensável. Este plano estende o mesmo princípio ao
  JEV, com reforço adicional porque o JEV é fornecedor fora da AWS: só
  recebe campos já estruturados e, sempre que possível, sem nenhum texto
  livre do consumidor.
- Logs de decisão (§6) não armazenam conteúdo de mídia nem texto livre
  do consumidor — apenas metadados e campos estruturados, mesmo padrão já
  aplicado aos logs de mídia (ADR-047: "resultado técnico deve ser
  registrado sem conteúdo ou dados pessoais nos logs").
- Pendência explícita: validação jurídica de contrato/DPA com a TypeSafe
  AI é bloqueio de produção, mesmo padrão já usado para o fornecedor de
  LLM e para Transcribe (ADR-046, ADR-047). Não bloqueia desenvolvimento
  em ambiente de teste com dados sintéticos/anonimizados.

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
  regressão para a extração de campos estruturados e para os gates do
  JEV antes de qualquer promoção de fase (§5.1).
- **E2E**: estende a skill/script `atua-testar-triagem` já usado nos
  incrementos anteriores (evidências em
  [PLANO-triagem-templates-channel-baileys.md](./PLANO-triagem-templates-channel-baileys.md)
  §5, §11) para cobrir: resposta de texto conduzida pela LLM, pelo menos
  um turno com mídia e marcação de legibilidade, e liberação ao pool sem
  intervenção do script manual (critério de "fechamento do ciclo").
- Nenhum teste de carga ou de custo real do JEV entra antes do ADR-051
  ser aceito.

## 10. Entrega faseada

| Fase | Objetivo | Agentes responsáveis | Critério de aceite |
|---|---|---|---|
| **M0** | Spikes e ADRs: avaliar JEV (API, disponibilidade, preço, região, retenção); redigir e levar ADR-051 a aprovação; confirmar com o PO as questões abertas do §10 (rubrica de prioridade, lista de categorias, definição de "suficiente" além do checklist, fonte do catálogo de peças). | `software-architect` (ADR, spike técnico), `product-analyst` (requisitos abertos, rubrica, categorias), `aws-architect` (se envolver avaliação de alternativa via Bedrock structured output) | ADR-051 com status definido (Accepted/Rejected); respostas do PO registradas; sem isso, M3+ não inicia. |
| **M1** | Extração da LLM offline sobre transcrições já existentes (dataset golden), sem loop de conversa em produção. Validar qualidade de extração de campos estruturados antes de automatizar qualquer resposta ao consumidor. | `backend-engineer` (adaptador `ITriagemLlmClient` + fake), `qa-engineer` (avaliação de qualidade do dataset) | Extração roda sobre o dataset golden com taxa de acerto revisada manualmente pelo PO/product-analyst; nenhuma chamada em produção ainda. |
| **M2** | Loop conversacional real, atrás de feature flag, restrito ao número de teste (E2E), sem gates automáticos do JEV (checklist determinístico apenas, como hoje). | `backend-engineer` (loop na Master API, idempotência, fallback), `qa-engineer` (E2E estendido) | E2E completo: consumidor de teste conversa com a LLM do início ao fim do checklist mínimo e a triagem chega a `InformacoesSuficientes`/`LiberadaAoPool` sem o script manual. |
| **M3** | JEV em modo sombra: roda em paralelo, registra decisão e probabilidade, não altera estado. Requer ADR-051 aceito. | `backend-engineer` (adaptador `ITriagemDecisionClient`, log de decisão), `aws-architect` (se exigir infraestrutura de rede/egress para chamar o fornecedor externo a partir da EC2) | Decisões do JEV aparecem no log/Office como somente leitura para 100% dos turnos elegíveis, sem nenhuma transição de estado decidida por ele. |
| **M4** | Calibração: comparar decisões do JEV com os desfechos reais (humanos ou determinísticos) de um volume mínimo de casos (a definir com o PO); aprovar limiares por gate. | `product-analyst` (critério de aprovação do limiar), `qa-engineer` (relatório de calibração), `software-architect` (revisão do ADR de limiares) | Limiar por gate aprovado pelo PO, registrado em ADR/documento de decisão; sem isso, gate não liga. |
| **M5** | Habilitação para o tenant atual: gates automáticos ativos acima do limiar aprovado; abaixo do limiar, sempre cai para intervenção humana. | `backend-engineer`, `frontend-engineer` (exibição no Office), `qa-engineer` (regressão completa), `documentation` (atualizar arquitetura e features) | Ciclo completo roda em produção para o tenant real sem o script E2E manual; métricas de custo/observabilidade (§8) ativas; documentação de arquitetura atualizada. |

Cada fase é incremental e reversível pela feature flag; nenhuma fase
pressupõe que a seguinte será aprovada.

## 11. Riscos

| Risco | Descrição | Mitigação |
|---|---|---|
| Dependência de fornecedor externo (JEV) sem avaliação de disponibilidade/custo | Pode não ser viável tecnicamente ou comercialmente | M0 avalia antes de qualquer integração real; alternativa de Bedrock com saída estruturada registrada no ADR-051 |
| Exposição de PII a terceiro fora da AWS | Viola minimização/LGPD se mal implementado | Minimização obrigatória, DPA, revisão antes de M3; log sem conteúdo livre |
| Alucinação ou falha de calibração do JEV | Decisão errada liberada automaticamente | Shadow mode (M3) + calibração com casos reais (M4) antes de qualquer gate automático (M5) |
| Processo único na EC2 com memória limitada | Chamadas síncronas de I/O a dois fornecedores externos no mesmo processo da API podem aumentar latência sob carga | Timeout e backoff definidos; sem novo processo residente; `aws-architect` acompanha métricas de memória/latência ao habilitar M5 |
| Ambiguidade entre checklist determinístico e gate "checklist suficiente" do JEV | Risco de dois critérios divergentes confundirem o operador | Checklist determinístico continua autoritativo mesmo após M4 (ver §5.2); JEV é sinal adicional, não substituto |
| Falta de catálogo de peças e de categorias de defeito | Choice do JEV depende de lista fechada ainda não definida pelo PO | Bloqueia M0 até confirmação (ver §10); sem lista fechada, Choice não entra em produção |

## 12. Perguntas em aberto para o PO

1. **Catálogo de peças**: existe hoje? É por provedor ou por tipo de
   produto? Onde está hospedado? (Já registrada como pendência em
   `triagem-whatsapp-fase-1.md` §9, item 2; reafirmada aqui porque bloqueia
   a extensão futura de sugestão de peças pela LLM.)
2. **Lista fechada de categorias de defeito** para o Choice do JEV: o
   plano assume o exemplo do PO (refrigeração, elétrica, instalação, uso
   incorreto, administrativo) — essa é a lista final ou há mais
   categorias/subcategorias?
3. **Rubrica de prioridade operacional (Score 1–5)**: quais critérios
   definem cada nível? Quem define a rubrica (PO, operação do posto)?
4. **Definição de "suficiente" além do checklist determinístico**: o que
   exatamente o Noul "checklist suficiente" deveria capturar que a lista
   fixa de itens obrigatórios (já determinística) não capturaria sozinha?
   Sem uma resposta clara, este plano recomenda manter apenas a checagem
   determinística e não ativar esse gate específico (ver §5.2).
5. **Limiares de confiança por gate**: quem aprova (PO sozinho, ou com
   `product-analyst` e QA) e qual volume mínimo de casos reais é exigido
   antes de aprovar (M4)?
6. **Volume e orçamento de chamadas** ao JEV por triagem/tenant: existe
   um teto, análogo ao já prudente para a LLM (ADR-048), antes de cair em
   `AguardandoIntervencao` por orçamento excedido?

## 13. BLOQUEANTE — resumo para o orchestrator

```text
Status: ARCHITECTURE_CHANGE_REQUIRED / BLOCKED (parcial)

Bloqueante: adoção do JEV como fornecedor externo de decisão.
Motivo: fornecedor fora da conta AWS; envio de dados do consumidor requer
DPA, minimização e confirmação de região/retenção, ainda não avaliados.
Informação necessária: disponibilidade, API, preço, região, retenção de
dados do JEV (TypeSafe AI / jev-llm.com); respostas do PO às perguntas do
§12.
Impacto: M3–M5 deste plano não iniciam sem o ADR-051 aceito; M0–M2 podem
prosseguir (LLM pura, sem JEV, com checklist determinístico mantido).
Responsável recomendado: `software-architect` conduz o ADR-051 com apoio
de `aws-architect` (se a alternativa via Bedrock for escolhida) e decisão
final do Product Owner.
```
