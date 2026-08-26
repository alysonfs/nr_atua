# Protocolo de Decisões

## 1. Objetivo

Definir como decisões relevantes são identificadas, analisadas, aprovadas, registradas e posteriormente alteradas.

## 2. O que é uma decisão relevante

Uma decisão deve ser registrada quando puder afetar significativamente:

- arquitetura;
- infraestrutura;
- segurança;
- custos;
- persistência;
- integrações;
- contratos de API;
- tecnologias;
- padrões de desenvolvimento;
- escalabilidade;
- manutenção futura;
- comportamento importante do produto.

## 3. Decisões que não precisam ser registradas

Não é necessário criar uma decisão formal para:

- pequenas correções de código;
- alterações puramente cosméticas;
- refatorações locais sem impacto arquitetural;
- decisões óbvias já estabelecidas pelos padrões do projeto;
- alterações que seguem uma decisão existente sem modificá-la.

## 4. Autoridade

### Produto

`product-analyst`

Decide questões relacionadas a:

- comportamento do produto;
- regras de negócio;
- requisitos;
- critérios de aceite.

---

### Arquitetura

`software-architect`

Decide questões relacionadas à arquitetura de software.

---

### Infraestrutura

`aws-architect`

Decide questões relacionadas à arquitetura AWS e infraestrutura.

---

### Implementação

`backend-engineer` e `frontend-engineer`

Podem tomar decisões locais de implementação desde que não contrariem:

- requisitos;
- arquitetura;
- decisões existentes;
- padrões definidos.

---

## 5. Conflito com decisão existente

Nenhum agente deve simplesmente substituir uma decisão existente.

Quando uma nova necessidade contradizer uma decisão anterior:

```text
DECISÃO EXISTENTE
       |
       v
NOVO REQUISITO
       |
       v
CONFLITO
       |
       v
ANÁLISE
       |
       v
NOVA DECISÃO
```

A decisão anterior deve permanecer registrada para preservar o histórico.

A nova decisão deve indicar que substitui ou altera a decisão anterior.

## 6. Registro

As decisões devem ser armazenadas em:

```text
docs/decisions/
```
Formato recomendado:

```text
ADR-001-titulo-da-decisao.md
ADR-002-titulo-da-decisao.md
ADR-003-titulo-da-decisao.md
```
ADR significa:

`Architecture Decision Record`

## 7. Estrutura recomendada de uma decisão
```markdown
# ADR-XXX - Título

## Status

Proposed | Accepted | Rejected | Superseded

## Contexto

Qual problema ou necessidade originou a decisão?

## Decisão

O que foi decidido?

## Motivos

Por que essa solução foi escolhida?

## Alternativas consideradas

Quais alternativas foram analisadas?

## Consequências

Quais são os impactos positivos e negativos?

## Agentes envolvidos

Quais agentes participaram da decisão?

## Data

YYYY-MM-DD

## Substitui

ADR-XXX, se aplicável.
```

## 8. Status das decisões
### Proposed

Decisão proposta, ainda não aprovada.

### Accepted

Decisão aprovada e válida.

### Rejected

Decisão analisada e rejeitada.

### Superseded

Decisão substituída por uma decisão posterior.

## 9. Princípio de rastreabilidade

Uma decisão importante deve ser rastreável.

Sempre que possível:
```text
Requisito
    |
    v
Decisão
    |
    v
Arquitetura
    |
    v
Implementação
    |
    v
Teste
```
Isso permite entender posteriormente por que determinada solução foi implementada.

## 10. Regra de preservação histórica

Decisões antigas não devem ser apagadas apenas porque deixaram de ser válidas.

Quando uma decisão for substituída:

- manter a decisão antiga;
- alterar seu status para `Superseded`;
- criar uma nova decisão;
- referenciar a decisão anterior;
- explicar o motivo da mudança.

O histórico das decisões faz parte da memória técnica do projeto.