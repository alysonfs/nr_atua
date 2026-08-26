# Protocolo de Workflow

## 1. Objetivo

Definir o ciclo de vida padrão de uma tarefa, funcionalidade, correção
ou alteração no projeto.

## 2. Estados

Uma tarefa pode assumir os seguintes estados:

```text
NEW
ANALYSIS
REQUIREMENTS_DEFINED
ARCHITECTURE_DEFINED
IMPLEMENTATION
VALIDATION
DOCUMENTATION
RELEASE_READY
DONE
BLOCKED
CANCELLED
```

## 3. Fluxo principal
```text
NEW
 |
 v
ANALYSIS
 |
 v
REQUIREMENTS_DEFINED
 |
 v
ARCHITECTURE_DEFINED
 |
 v
IMPLEMENTATION
 |
 v
VALIDATION
 |
 v
DOCUMENTATION
 |
 v
RELEASE_READY
 |
 v
DONE
```
Nem toda tarefa precisa passar por todos os estados.

O `orchestrator` deve determinar o fluxo adequado de acordo com o tipo e o impacto da tarefa.

## 4. NEW

Estado inicial.

A solicitação foi recebida, mas ainda não foi analisada.

Responsável:

`orchestrator`

Ações:

- identificar objetivo;
- classificar tarefa;
- determinar agentes necessários;
- identificar possíveis dependências.

## 5. ANALYSIS

A tarefa está sendo analisada.

Dependendo do tipo da solicitação, o `orchestrator` pode solicitar a participação do `product-analyst`.

Objetivo:

- compreender o problema;
- identificar ambiguidades;
- determinar escopo;
- identificar critérios de aceite.

## 6. REQUIREMENTS_DEFINED

Os requisitos necessários estão definidos.

Uma tarefa não deve avançar para implementação quando existir ambiguidade relevante sobre o comportamento esperado.

## 7. ARCHITECTURE_DEFINED

A solução técnica foi definida quando necessário.

Este estado pode envolver:

- `software-architect`;
- `aws-architect`;
- decisões registradas em `docs/decisions/`;
- documentação arquitetural.

Correções pequenas e isoladas podem não exigir uma nova decisão
arquitetural.

## 8. IMPLEMENTATION

A implementação está sendo realizada.

Responsáveis típicos:

- `backend-engineer`;
- `frontend-engineer`.

Os agentes de implementação devem respeitar:

- requisitos;
- arquitetura;
- decisões existentes;
- padrões do projeto.

## 9. VALIDATION

A implementação está sendo validada.

Responsável:

`qa-engineer`

A validação deve considerar:

- critérios de aceite;
- comportamento esperado;
- regressões;
- casos de erro;
- integração entre componentes.

## 10. DOCUMENTATION

Documentação relevante é atualizada.

Responsável:

`documentation`

Podem ser atualizados:

- requisitos;
- arquitetura;
- decisões;
- funcionalidades;
- documentação técnica.

## 11. RELEASE_READY

A tarefa foi validada e está apta para entrar em uma release.

Responsável:

`release-versioning`

Antes de uma release, devem ser verificadas:

- validação concluída;
- documentação necessária;
- versionamento;
- changelog;
- alterações relevantes.

## 12. DONE

A tarefa está concluída.

Uma tarefa só deve ser considerada DONE quando:

- implementação concluída;
- validação concluída;
- bloqueios resolvidos;
- documentação necessária atualizada;
- requisitos atendidos.

## 13. BLOCKED

A tarefa não pode continuar.

Causas possíveis:

- requisito ausente;
- decisão arquitetural pendente;
- dependência externa;
- erro técnico não resolvido;
- conflito entre decisões;
- informação insuficiente.

Ao entrar em BLOCKED, o agente deve informar:

- motivo;
- impacto;
- dependência;
- responsável pela resolução.

## 14. CANCELLED

A tarefa foi cancelada.

O cancelamento deve ocorrer somente quando:

- o usuário solicitar;
- o requisito deixar de existir;
- a funcionalidade for substituída;
- o orchestrator determinar que a tarefa não deve mais prosseguir com base em uma decisão válida.

## 15. Regressão

Se o QA encontrar uma falha:

```text
VALIDATION
    |
    v
FAILED
    |
    v
IMPLEMENTATION
    |
    v
VALIDATION
```

A tarefa não deve avançar para `DOCUMENTATION` enquanto os problemas
que impedem a aprovação não forem resolvidos.

## 16. Regra de dependências

Uma tarefa não deve iniciar quando depender de uma decisão ou informação
que ainda não esteja disponível.

Exemplo:
```text
BACKEND
   |
   | depende de
   v
DECISÃO ARQUITETURAL
   |
   | pendente
   v
BLOCKED
```
O agente deve aguardar a resolução em vez de implementar uma solução
provisória que possa entrar em conflito com a arquitetura.