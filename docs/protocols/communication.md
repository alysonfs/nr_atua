# Protocolo de Comunicação entre Agentes

## 1. Objetivo

Definir como os agentes devem transferir informações, solicitar trabalho,
reportar resultados, comunicar bloqueios e solicitar decisões.

A comunicação deve ser objetiva, verificável e orientada à execução.

---

## 2. Princípios

Toda comunicação entre agentes deve possuir:

- contexto;
- objetivo;
- informações relevantes;
- resultado esperado;
- dependências, quando existirem.

Agentes não devem transmitir informações vagas quando puderem fornecer
dados concretos.

---

## 3. Solicitação de trabalho

Quando um agente solicitar trabalho a outro agente, deve informar:

```text
Tarefa:
Objetivo:
Contexto:
Entradas:
Resultado esperado:
Restrições:
Dependências:
```

### Exemplo: 
Tarefa:
Implementar autenticação via Google.

Objetivo:
Permitir que usuários existentes façam login utilizando Google.

Contexto:
O produto já possui autenticação por e-mail e senha.

Entradas:
Requisitos definidos em docs/requirements/authentication.md.

Resultado esperado:
Endpoint backend e fluxo frontend integrados.

Restrições:
Não alterar o mecanismo atual de autenticação sem justificativa.

Dependências:
Decisão arquitetural registrada em docs/decisions/.

## 4. Entrega de trabalho

Ao concluir uma tarefa, o agente deve informar:

```text
Status:
Alterações realizadas:
Arquivos afetados:
Decisões tomadas:
Testes realizados:
Problemas encontrados:
Pendências:
Próximo agente recomendado:
```

## 5. Comunicação de bloqueio

Quando não puder continuar:

```text
Status: BLOCKED

Motivo:
<explicação objetiva>

Informação necessária:
<informação necessária>

Impacto:
<impacto sobre a tarefa>

Responsável recomendado:
<agente>

Próxima ação:
<ação necessária>
```

Nenhum agente deve permanecer bloqueado silenciosamente.

## 6. Comunicação de conflito

Quando uma decisão entrar em conflito com outra:

```text
Status: CONFLICT

Decisão atual:
<decisão existente>

Nova proposta:
<nova proposta>

Conflito:
<explicação>

Impactos:
<impactos>

Agente responsável pela decisão:
<agente>

Recomendação:
<recomendação técnica>
```

O agente não deve sobrescrever silenciosamente uma decisão existente.

## 7. Comunicação de mudança arquitetural

Quando uma implementação exigir alteração arquitetural:

```text
Status: ARCHITECTURE_CHANGE_REQUIRED

Motivo:
<motivo>

Arquitetura atual:
<descrição>

Alteração proposta:
<descrição>

Impacto:
<descrição>

Agente responsável:
software-architect
```
A implementação da mudança deve aguardar a decisão arquitetural quando a alteração for relevante.

## 8. Comunicação com o Orchestrator

O orchestrator deve receber informações suficientes para determinar
a próxima ação.

O agente não deve simplesmente responder:
```text
"Terminei."
```
Deve informar o resultado de forma verificável.

## 9. Fonte de verdade

Quando houver divergência entre memória da conversa e documentação
do projeto, a documentação versionada do projeto deve ser considerada
a fonte de verdade, salvo quando o usuário determinar explicitamente
uma nova decisão.

## 10. Registro

Informações temporárias podem permanecer na comunicação entre agentes.

Informações que representam decisões permanentes devem ser registradas
nos documentos apropriados.

Especialmente:

- requisitos em docs/requirements/;
- funcionalidades em docs/features/;
- arquitetura em docs/architecture/;
- decisões em docs/decisions/;
- releases em docs/releases/.