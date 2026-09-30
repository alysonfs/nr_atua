# Protocolo de Economia de Tokens

## 1. Objetivo

Definir regras operacionais para reduzir o custo de tokens no uso dos
agentes do framework Batuta, sem perda de qualidade nas decisões que
importam. Este protocolo complementa `docs/protocols/model-and-context.md`,
que trata da seleção de modelo e gestão de contexto.

## 2. Contexto

O custo de tokens do Copilot/Claude é ordens de magnitude maior que o
custo da infraestrutura AWS do MVP (US$ ~59/mês em set/2026 vs. ~US$
248/mês em tokens no meio do mês). Reduzir token é a alavanca de maior
impacto no custo operacional do projeto.

## 3. `reasoning_effort` — a alavanca mais subutilizada

`reasoning_effort` controla quanto o modelo "pensa" antes de responder.
Valores típicos: `low`, `medium`, `high`.

- `low`: resposta direta, custo ~1×.
- `medium`: raciocínio moderado, custo ~2–3×.
- `high`: cadeia longa de raciocínio, custo ~5–10×.

Quando não definido, o default do modelo (geralmente `medium` ou `high`)
é usado. Em tarefas estruturadas, isso é desperdício.

### 3.1 Regra obrigatória

**Toda delegação deve declarar `reasoning_effort` explicitamente**, junto
com `Modelo:`. Se não declarar, o orchestrator deve rejeitar a
delegação e pedir a definição.

### 3.2 Mapa de `reasoning_effort` por tipo de tarefa

| Tipo de tarefa | `reasoning_effort` | Justificativa |
|---|---|---|
| Decisão arquitetural, código crítico, segurança, custo | `high` | Erro se propaga |
| Implementação comum, análise de requisitos | `medium` | Erro visível, mas custoso |
| Documentação, indexação de insumo, parsing de CSV, geração de tabela markdown | `low` | Erro barato de corrigir |
| Consulta a custo AWS, geração de changelog, release notes | `low` | Dados verificáveis |

### 3.3 Mapa cruzado Modelo × `reasoning_effort`

| Categoria | Modelo | `reasoning_effort` |
|---|---|---|
| Crítico | forte | `high` |
| Padrão | forte | `medium` |
| Estruturado | econômico | `low` |
| Consulta | econômico | `low` |

## 4. Regras de contexto

### 4.1 Nunca colar arquivos no chat

Arquivos **nunca** devem ser colados como texto no chat. Devem ser
referenciados por caminho e lidos pelo subagente via `read`/`search`
sob demanda.

Motivo: cada arquivo colado permanece no contexto da sessão e é
reenviado ao modelo a cada turno, multiplicando o custo.

### 4.2 Leitura pesada é do subagente

O orchestrator **não deve** ler diretamente:

- código-fonte com mais de 50 linhas;
- logs de build;
- saídas de teste;
- relatórios extensos;
- planilhas CSV grandes.

Isso deve ser delegado ao subagente apropriado, que retorna **resumo
estruturado** (máximo 10 linhas).

### 4.3 Checkpoint obrigatório

A cada 10 tool calls do orchestrator na mesma sessão, gerar checkpoint
conforme `model-and-context.md` §7.5. Ao atingir, fechar a sessão e
abrir nova com o checkpoint colado.

## 5. Regras de delegação

### 5.1 Orçamento default menor

- Delegação padrão: **até 4 tool calls** (era 6–12).
- Delegação de leitura/parsing: **até 3 tool calls**.
- Delegação exploratória justificada: **até 8 tool calls**, com
  justificativa explícita no prompt.

### 5.2 `Modelo:` e `reasoning_effort:` obrigatórios

Todo `WORK_REQUEST` deve declarar:

```text
Tipo: WORK_REQUEST
Tarefa: <tarefa>
Modelo: <modelo-forte | modelo-econômico>
Reasoning-effort: <high | medium | low>
Objetivo: <objetivo>
```

### 5.3 Resultado sempre resumido
Todo WORK_RESULT deve ter:
- resumo em até 10 linhas;
- sem saída bruta (logs, stack traces completos, código);
- referência a arquivos (caminhos) em vez de conteúdo.

## 6. Regras de sessão
### 6.1 Sessão por tarefa (já adotado)
Uma tarefa = uma sessão. Ao concluir, gerar checkpoint, fechar, abrir
nova para a próxima.

### 6.2 Sessão limpa ao trocar de contexto
Ao mudar de frente (ex.: de backend para marca), fechar sessão e abrir
nova. Não carregar contexto de uma frente em outra.

## 7. Antipadrões proibidos
- Colar arquivos de protocolo/agente no chat.
- Delegar sem Modelo: e Reasoning-effort:.
- Orchestrator lendo arquivo >50 linhas.
- WORK_RESULT com saída bruta.
- Delegar tarefa que não cabe em 4–8 tool calls sem decompor.
- Usar high em tarefa estruturada.
- Reexecutar tarefa já concluída para "confirmar".

## 8. Checklist antes de delegar
□ A tarefa cabe em ≤4 tool calls? (Se não, decompor.)
□ Modelo: declarado?
□ Reasoning-effort: declarado?
□ Leitura pesada vai para o subagente?
□ Resultado esperado é resumo ≤10 linhas?
□ Contexto da sessão está mínimo?

## 9. Checklist antes de encerrar sessão
□ Tarefa concluída (ou BLOCKED explícito)?
□ Checkpoint gerado?
□ Estado persistido em docs/?
□ Próxima sessão tem contexto mínimo?

## 10. Regra final
Token é o custo dominante do projeto. Toda decisão operacional deve
considerar o custo de token. Economia de token não é sobre trabalhar
menos — é sobre trabalhar com contexto mínimo e modelo adequado.