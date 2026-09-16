# Protocolo de Modelo de IA e Gestão de Contexto

## 1. Objetivo

Definir como o `orchestrator` deve selecionar modelos de IA ao acionar subagentes e como gerenciar compactação de contexto em sessões longas.

## 2. Princípio fundamental

O campo `model:` no frontmatter de `.github/agents/*.agent.md` declara apenas a INTENÇÃO do agente.

O modelo efetivamente utilizado é determinado pelo parâmetro `model` passado pelo `orchestrator` NA CHAMADA do agente.

Se o `orchestrator` omitir esse parâmetro, o runtime utiliza um modelo padrão potencialmente inadequado ou caro.

Portanto: **o `orchestrator` DEVE passar explicitamente o parâmetro `model` ao acionar qualquer subagente.**

## 3. Categorias de modelos

Existem duas categorias de modelos com custo-benefício distintos:

### 3.1. Modelo forte

Modelo indicado para tarefas onde o custo do ERRO é alto.

Características:
- melhor raciocínio;
- menor taxa de alucinação;
- compreensão mais profunda de contexto complexo;
- custo maior por uso.

Use modelo forte para tarefas em que o defeito se propaga, afeta arquitetura, código crítico, decisões, infraestrutura ou custos.

### 3.2. Modelo econômico

Modelo indicado para tarefas estruturadas, textuais ou de leitura onde o defeito é visível e barato de corrigir.

Características:
- modelo adequado para tarefa estruturada;
- custo menor por uso;
- saída verificável;
- resultado revisável.

Use modelo econômico para tarefas em que o defeito é detectado facilmente na revisão.

## 4. Seleção de modelo por agente

Recomendação de custo-benefício por agente (válido em 30/08/2026; pode variar conforme modelos evoluem):

### 4.1. Agentes com modelo forte

Estes agentes tomam decisões ou definem código onde o erro se propaga:

| Agente | Responsabilidade |
| --- | --- |
| `orchestrator` | Decisões de delegação, gate de workflow, resolução de conflitos. |
| `software-architect` | Arquitetura de aplicação, decisões técnicas de alto nível. |
| `aws-architect` | Infraestrutura AWS, decisões de custo, segurança, escalabilidade. |
| `backend-engineer` | Código backend, lógica crítica, APIs, persistência. |
| `frontend-engineer` | Componentes de interface, integração com backend. |
| `qa-engineer` | Aprovação de qualidade, decisão sobre regressões e bugs. |

### 4.2. Agentes com modelo econômico

Estes agentes trabalham com texto, documentação, análise ou consultas onde o resultado é facilmente verificável:

| Agente | Responsabilidade |
| --- | --- |
| `product-analyst` | Análise de requisitos, esclarecimento de ambiguidades. |
| `documentation` | Documentação técnica e funcional. |
| `release-versioning` | Preparação de release, versionamento. |
| `aws-cost-monitor` | Consulta de custos AWS, relatórios somente leitura. |
| `brand-strategist` | Estratégia de marca, posicionamento. |
| `brand-identity` | Identidade visual, diretrizes. |
| `brand-copywriter` | Textos institucionais, slogans, mensagens. |
| `brand-content` | Planejamento de conteúdo, narrativas. |
| `brand-guardian` | Revisão de consistência de marca. |

## 5. Critério de escolha

O critério não é o tamanho da tarefa, mas o impacto do ERRO:

```text
Erro se propaga?        → Modelo forte
Erro é visível/revisável?  → Modelo econômico
```

Exemplos:

- Documentar um fluxo de 2 páginas: modelo econômico (texto é revisável).
- Definir arquitetura de integração AWS: modelo forte (erro afeta toda infraestrutura).
- Escrever um relatório de custo: modelo econômico (números são verificáveis).
- Corrigir bug em autenticação: modelo forte (defeito de segurança é crítico).

## 6. Passar modelo ao acionar agente

Ao delegar, o `orchestrator` deve sempre indicar explicitamente o modelo:

```text
Tipo: WORK_REQUEST
Tarefa: <tarefa>
Modelo: <modelo-forte | modelo-econômico>
Objetivo: <objetivo>
...
```

Exemplo:

```text
Tipo: WORK_REQUEST
Tarefa: Implementar autenticação OAuth.
Modelo: modelo-forte
Objetivo: Permitir login via Google.
...
```

Outro exemplo:

```text
Tipo: WORK_REQUEST
Tarefa: Documentar API de autenticação.
Modelo: modelo-econômico
Objetivo: Descrever endpoints, payloads e respostas.
...
```

## 7. Gestão de contexto em sessões longas

### 7.1. Problema

Em sessões longas, o `orchestrator` consome contexto para:

- ler código extenso;
- ler testes ou build logs;
- analisar decisões históricas;
- coordenar múltiplos agentes.

Quando o contexto se aproxima do limite, o `orchestrator` perde capacidade de:

- coordenar novas tarefas;
- manter o usuário informado;
- decidir próximas ações.

### 7.2. Regra 1: Leitura extensa é responsabilidade do subagente

O `orchestrator` NÃO deve executar leitura extensa de:

- código-fonte;
- testes;
- logs de build;
- saídas de comando;
- relatórios grandes.

Esse trabalho vai para o subagente responsável, que:

- realiza a leitura;
- analisa;
- retorna apenas RESUMO ao `orchestrator`.

Exemplo:

Ruim:
```text
Orchestrator lê 500 linhas de testes, analisa, conclui.
```

Bom:
```text
Orchestrator delega ao qa-engineer:
"Execute os testes de autenticação. Retorne resumo: passou/falhou, 
quantidade de testes, bugs encontrados se houver."

QA retorna:
"12 testes executados. 10 passaram. 2 falharam: 
- teste_refresh_token_expirado
- teste_logout_global
Resumo: erro em refresh token expirado."

Orchestrator tem contexto da conclusão sem ler 500 linhas.
```

### 7.3. Regra 2: Solicitar resumo, não saída bruta

Ao delegar, o `orchestrator` deve solicitar:

- RESUMO, não saída completa;
- conclusão, não detalhes excessivos;
- dados estruturados, não logs brutos.

Exemplo:

```text
Tipo: WORK_REQUEST
Tarefa: Executar testes de backend.
Modelo: modelo-forte
Objetivo: Validar critérios de aceite.
...
Resultado esperado: 
- Quantidade total de testes
- Quantidade de testes que passaram
- Lista de testes que falharam (nome apenas)
- Impacto: testes bloqueiam release? Sim/Não
- Próximo passo: corrigir bugs encontrados
```

### 7.4. Regra 3: Manter estado em armazenamento externo

O `orchestrator` NÃO deve manter o estado da sessão apenas na memória da conversa.

Estado relevante deve estar registrado em:

- `docs/decisions/` — decisões tomadas;
- `docs/requirements/` — requisitos aprovados;
- `docs/architecture/` — arquitetura definida;
- tabelas de tarefas/restrições — estado de cada frente de trabalho;
- bloqueios conhecidos — o que aguarda resolução.

Assim, após compactação, o `orchestrator` pode reler a documentação e reconstruir o estado sem perder informação crítica.

### 7.5. Regra 4: Checkpoint antes de compactação

Quando o contexto se aproximar do limite, o `orchestrator` deve produzir um CHECKPOINT explícito:

```text
Status: CHECKPOINT

Estado de cada frente de trabalho:
- Backend: <estado>
- Frontend: <estado>
- Testes: <estado>
- Documentação: <estado>

Decisões tomadas:
- [ADR-XXX] <decisão>

Bloqueios conhecidos:
- <bloqueio 1>
- <bloqueio 2>

Aguardando:
- <o que aguarda resposta do usuário>

Próximo passo concreto:
- <próxima ação>

Contexto consumido:
- <estimativa>
```

Assim, o usuário e os agentes temos referência clara do estado antes da compactação.

### 7.6. Regra 5: Reler protocolos e documentação após compactação

Após qualquer compactação, o `orchestrator` deve:

1. Reler `docs/protocols/hierarchy.md` — autoridades e responsabilidades não mudam;
2. Reler `docs/protocols/communication.md` — formato obrigatório de comunicação;
3. Reler `docs/protocols/workflow.md` — estados do workflow;
4. Reler `docs/protocols/model-and-context.md` — este protocolo;
5. Consultar documentação viva relevante:
   - `docs/decisions/` — decisões já tomadas;
   - `docs/requirements/` — requisitos aprovados;
   - `docs/architecture/` — arquitetura definida.

A documentação versionada é a fonte de verdade (conforme definido em `.github/copilot-instructions.md`).

Não assuma mudanças baseado em memória de conversa antes da compactação.

### 7.7. Regra 6: Identificação explícita do agente

O `orchestrator` deve se identificar nas respostas ao usuário.

Ao retomar após compactação, mensagem inicial:

```text
**[Otto/orchestrator]** Retomando sessão após compactação de contexto.

Leitura de protocolos: OK
Leitura de documentação viva: OK

Estado anterior (checkpoint):
[resumo do estado]

Pronto para continuar. Qual o próximo passo?
```

Assim fica claro qual agente está comunicando e que releu os documentos necessários.

## 8. Impacto na comunicação entre agentes

Quando um subagente reportar resultado ao `orchestrator`, ele deve:

1. Indicar qual modelo utilizou (confirmação);
2. Indicar quanto de contexto consumiu (estimativa);
3. Entregar resumo estruturado, não saída bruta.

Formato:

```text
Tipo: WORK_RESULT
Status: COMPLETED
Tarefa: <tarefa>
Modelo utilizado: <modelo>
Contexto consumido: <estimativa>
Resultado (resumido):
<informação estruturada>
Próximo agente recomendado: <agente>
```

## 9. Economia de contexto — checklist

Antes de delegar, o `orchestrator` deve considerar:

- [ ] O subagente consegue fazer a leitura e resumir? Sim → delegar.
- [ ] Preciso ler 100+ linhas de código/logs? Não → delegar.
- [ ] O resultado esperado cabe em 5 linhas? Sim → solicitar resumo.
- [ ] Essa informação está em documentação viva? Sim → citar, não reler.
- [ ] Essa decisão já foi tomada? Sim → reler a decisão, não rediscutir.
- [ ] Preciso de informação do usuário? Sim → perguntar antes de delegar.

## 10. Regra final

O `orchestrator` é o coordenador e porteiro.

Sua responsabilidade é:

- coordenar fluxo de trabalho;
- manter comunicação com o usuário;
- garantir que tarefas sejam atribuídas aos agentes corretos;
- controlar dependências;
- manter contexto viável.

Para isso:

- delegue leitura pesada;
- solicite resumos;
- mantenha estado documentado;
- faça checkpoints;
- releia os protocolos após compactação;
- identifique-se nas respostas.

Contexto desperdiçado é capacidade de coordenação perdida.
