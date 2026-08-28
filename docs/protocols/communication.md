# Protocolo de Comunicação entre Agentes

## 1. Objetivo

Definir como os agentes devem enviar informações, solicitar trabalho,
reportar resultados, comunicar bloqueios e solicitar decisões durante o
workflow.

Este protocolo torna operacional a hierarquia definida em
`docs/protocols/hierarchy.md`.

## 2. Princípio fundamental

O `orchestrator` é o ponto central de comunicação operacional.

Todo agente deve comunicar ao `orchestrator`:

- início ou necessidade de uma tarefa;
- resultado de uma tarefa;
- bloqueio;
- conflito;
- necessidade de decisão fora de seu domínio;
- necessidade de devolução para uma etapa anterior.

O `orchestrator` avalia a informação, define a próxima ação e, quando
necessário, encaminha a solicitação ao agente responsável pelo domínio.

```text
AGENTE
	|
	v
ORCHESTRATOR
	|
	v
AGENTE RESPONSÁVEL
```

## 3. Regras gerais

Toda comunicação deve ser:

- objetiva;
- verificável;
- orientada à execução;
- suficiente para que o `orchestrator` determine a próxima ação.

O agente deve distinguir fatos, decisões existentes, suposições e
recomendações. Informações vagas, como `Terminei.`, não representam uma
entrega válida.

Agentes não devem iniciar comunicação arbitrária entre si para tomar
decisões fora do workflow. A comunicação direta só pode ocorrer quando
o `orchestrator` a tiver determinado e deve ter seu resultado reportado
de volta a ele.

## 4. Envelope obrigatório

Toda mensagem ao `orchestrator` deve conter os campos aplicáveis abaixo:

```text
Tipo:
Status:
Tarefa:
Objetivo:
Contexto:
Estado atual do workflow:
Informações ou evidências relevantes:
Impacto:
Próxima ação recomendada:
Responsável recomendado:
```

Campos sem aplicação devem ser preenchidos com `Não aplicável`, e não
omitidos silenciosamente. Referências a documentos, decisões, arquivos,
testes ou erros devem ser identificadas de forma específica.

## 5. Solicitação de trabalho

Somente o `orchestrator` delega trabalho a outro agente. A delegação deve
informar:

```text
Tipo: WORK_REQUEST
Tarefa:
Objetivo:
Contexto:
Entradas:
Resultado esperado:
Restrições:
Dependências:
Estado de origem:
Estado esperado após a conclusão:
Critério para considerar a tarefa concluída:
```

O agente que receber a solicitação deve verificar se possui as entradas
necessárias. Caso não possua, deve responder com `BLOCKED` em vez de
assumir requisitos, arquitetura ou decisões de outro domínio.

### Exemplo

```text
Tipo: WORK_REQUEST
Tarefa: Implementar autenticação via Google.
Objetivo: Permitir que usuários existentes façam login utilizando Google.
Contexto: O produto já possui autenticação por e-mail e senha.
Entradas: Requisitos em docs/requirements/authentication.md.
Resultado esperado: Endpoint backend e fluxo frontend integrados.
Restrições: Não alterar o mecanismo atual de autenticação sem justificativa.
Dependências: Decisão arquitetural registrada em docs/decisions/.
Estado de origem: ARCHITECTURE_DEFINED
Estado esperado após a conclusão: IMPLEMENTATION
Critério para considerar a tarefa concluída: Testes relevantes executados e resultado reportado.
```

## 6. Entrega de trabalho

Ao concluir uma tarefa, o agente deve reportar o resultado ao
`orchestrator`:

```text
Tipo: WORK_RESULT
Status: COMPLETED
Tarefa:
Alterações realizadas:
Arquivos afetados:
Decisões tomadas:
Testes realizados e resultados:
Problemas encontrados:
Pendências:
Impacto no workflow:
Próximo agente recomendado:
Próxima ação recomendada:
```

`COMPLETED` significa apenas que o trabalho delegado foi concluído. Não
significa aprovação de QA, atualização de documentação, liberação ou
conclusão total da tarefa.

## 7. Comunicação de bloqueio

Quando não puder continuar, o agente deve comunicar imediatamente:

```text
Tipo: BLOCKER
Status: BLOCKED
Tarefa:
Motivo:
Informação, decisão ou dependência necessária:
Tentativas realizadas:
Impacto sobre a tarefa e o workflow:
Responsável recomendado:
Próxima ação necessária:
```

Nenhum agente deve permanecer bloqueado silenciosamente, criar solução
provisória incompatível ou avançar para fora do seu domínio.

## 8. Comunicação de conflito

Quando uma decisão, requisito ou implementação conflitar com outra
informação válida, o agente deve interromper a alteração conflitante e
reportar:

```text
Tipo: CONFLICT
Status: CONFLICT
Tarefa:
Decisão ou informação atual:
Fonte da informação atual:
Nova proposta ou informação:
Conflito:
Impactos:
Agente responsável pelo domínio:
Recomendação:
Próxima ação necessária:
```

O `orchestrator` encaminha o conflito ao responsável pelo domínio e
registra a decisão de encaminhamento no workflow. Nenhum agente deve
sobrescrever silenciosamente uma decisão existente.

## 9. Solicitação de decisão

Quando a continuidade depender de decisão fora da autoridade do agente,
ele deve utilizar:

```text
Tipo: DECISION_REQUEST
Status: DECISION_REQUIRED
Tarefa:
Decisão necessária:
Motivo:
Opções identificadas:
Impacto de cada opção:
Recomendação:
Agente responsável pelo domínio:
Prazo ou impacto de atraso:
```

O `orchestrator` não substitui o especialista responsável pela decisão.
Ele encaminha a solicitação, controla a dependência e comunica a decisão
resultante aos agentes afetados.

Alterações arquiteturais relevantes devem indicar
`Agente responsável pelo domínio: software-architect`. Alterações de
infraestrutura AWS devem indicar `aws-architect`.

## 10. Resposta do Orchestrator

Após receber uma comunicação, o `orchestrator` deve responder com uma
direção operacional clara:

```text
Status da tarefa:
Decisão ou encaminhamento:
Agente responsável pela próxima ação:
Entradas fornecidas ou pendentes:
Dependências controladas:
Próxima ação:
Condição para avançar no workflow:
```

Quando a informação for insuficiente, o `orchestrator` deve solicitar o
complemento necessário. Quando houver bloqueio, conflito ou decisão
pendente, deve impedir o avanço da tarefa até a resolução apropriada.

## 11. Integração com o workflow

As comunicações devem refletir o estado definido em
`docs/protocols/workflow.md`.

| Situação | Comunicação obrigatória | Ação esperada do Orchestrator |
| --- | --- | --- |
| Tarefa recebida | `WORK_REQUEST` | Classificar e delegar a análise necessária. |
| Requisito ambíguo | `DECISION_REQUEST` ou `BLOCKER` | Encaminhar ao `product-analyst`. |
| Decisão técnica necessária | `DECISION_REQUEST` | Encaminhar ao `software-architect` ou `aws-architect`. |
| Implementação concluída | `WORK_RESULT` | Encaminhar para validação. |
| QA reprovou | `WORK_RESULT` com `Status: REJECTED` | Devolver à implementação com os defeitos reportados. |
| QA aprovou | `WORK_RESULT` com `Status: APPROVED` | Encaminhar para documentação ou release, conforme aplicável. |
| Dependência pendente | `BLOCKER` | Manter a tarefa em `BLOCKED` e encaminhar a resolução. |

## 12. Fonte de verdade e registro

Quando houver divergência entre memória da conversa e documentação do
projeto, a documentação versionada é a fonte de verdade, salvo quando o
usuário determinar explicitamente uma nova decisão.

Informações temporárias podem permanecer na comunicação entre agentes.
Informações que representam decisões permanentes devem ser registradas
nos documentos apropriados:

- requisitos em `docs/requirements/`;
- funcionalidades em `docs/features/`;
- arquitetura em `docs/architecture/`;
- decisões em `docs/decisions/`;
- releases em `docs/releases/`.

Uma comunicação não substitui o registro documental exigido pelo
workflow.