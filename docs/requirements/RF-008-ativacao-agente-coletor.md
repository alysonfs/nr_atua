# RF-008 - Ativação do Agente Coletor

Status: `Requisitos definidos`

## Objetivo

Permitir que membros ativos `OWNER` e `ADMIN` ativem ou desativem o Agente
Coletor de seu tenant no Office, condicionando a ativação à elegibilidade
vigente e à validação bem-sucedida da credencial iService da integração atual.

## Escopo

Este requisito define o estado de ativação, a autorização, as pré-condições,
a criação e o cancelamento de solicitações de coleta imediata e as informações
operacionais exibidas no Office. A coleta propriamente dita não faz parte deste
portão.

## Requisitos funcionais

### RF-008.1 - Estado inicial

O Agente Coletor deve iniciar no estado **Desativado** para cada tenant.

### RF-008.2 - Autorização para alteração de estado

Somente usuários com membership ativo `OWNER` ou `ADMIN` no tenant podem
ativar ou desativar o Agente Coletor. A autorização deve ser validada para o
tenant selecionado; um usuário não pode alterar o estado de outro tenant.

### RF-008.3 - Pré-condições de ativação

A ativação somente é permitida quando, simultaneamente:

- a API interna de elegibilidade retorna `eligible=true` para o contexto
  autorizado; e
- a credencial iService da integração atual está configurada e seu
  `ValidationStatus` é `Succeeded`.

Caso alguma pré-condição não seja atendida, o Agente deve permanecer
Desativado e o Office deve informar sua indisponibilidade e o motivo aplicável,
sem expor segredos.

### RF-008.4 - Ativação e solicitação imediata

Quando uma ativação válida for concluída, o Agente deve passar para o estado
**Ativado** e deve ser criado **exatamente um** comando de coleta imediata no
estado **Pendente**.

No escopo deste requisito, o comando é somente uma solicitação rastreável. Ele
não é executado, não cria agendamento recorrente e não realiza interação com a
integração.

### RF-008.5 - Desativação manual

Um `OWNER` ou `ADMIN` autorizado pode desativar manualmente o Agente. A
desativação deve impedir novas execuções e cancelar o comando de coleta
imediata que estiver Pendente.

### RF-008.6 - Desativação por perda de condição

O Agente deve ser desativado e seu comando Pendente deve ser cancelado quando:

- a elegibilidade de Trial deixar de ser atendida; ou
- a credencial da integração atual perder ou alterar seu estado de validação
  `Succeeded`.

### RF-008.7 - Novo ciclo de ativação

Um comando cancelado não pode ser reativado automaticamente. Após uma nova
ativação válida, deve ser criada uma nova solicitação de coleta imediata,
distinta e rastreável.

### RF-008.8 - Visibilidade no Office

O Office deve expor, para o tenant autorizado:

- o estado atual do Agente;
- a disponibilidade de ativação e seu motivo quando indisponível;
- o estado de validação da credencial da integração atual; e
- a existência e o estado do comando de coleta imediata Pendente.

Essas informações não podem incluir credenciais, tokens, cookies ou outros
segredos.

## Regras de negócio

- RN-008.1: O estado do Agente, a integração atual, a credencial, a
  elegibilidade e os comandos pertencem a um único tenant e devem permanecer
  isolados dos demais tenants.
- RN-008.2: A resposta positiva da elegibilidade deve ser obtida pelo contrato
  interno autorizado da Master API; o Agente não consulta diretamente dados de
  Trial ou Tenant.
- RN-008.3: `eligible=false`, credencial ausente, credencial sem validação
  `Succeeded` ou perda posterior de qualquer dessas condições impedem a
  ativação.
- RN-008.4: Cada ativação válida cria exatamente um novo comando Pendente;
  comandos cancelados não satisfazem essa obrigação nem voltam a Pendente.
- RN-008.5: O cancelamento do comando Pendente é obrigatório tanto na
  desativação manual quanto na desativação por perda de condição.
- RN-008.6: O Office apresenta apenas estado e motivos operacionais
  necessários, sem dados sensíveis da integração.

## Casos de borda

- Um `OWNER` ou `ADMIN` tenta ativar com `eligible=false` → a ativação é
  recusada, o Agente permanece Desativado e nenhum comando é criado.
- Um `OWNER` ou `ADMIN` tenta ativar sem credencial configurada ou com
  `ValidationStatus` diferente de `Succeeded` → a ativação é recusada e nenhum
  comando é criado.
- A elegibilidade ou a validação deixa de ser válida enquanto há comando
  Pendente → o Agente é desativado e esse comando é cancelado.
- Um usuário sem membership ativo, ou com membership em outro tenant, tenta
  alterar o estado ou visualizar o comando → a operação deve ser negada.
- Após cancelamento, as condições voltam a ser válidas sem nova ação do usuário
  → o comando cancelado permanece cancelado; somente uma nova ativação válida
  cria nova solicitação.

## Critérios de aceite

1. Dado um tenant com Agente ainda não ativado, quando sua configuração for
   criada, então o estado inicial do Agente deve ser Desativado.
2. Dado um `OWNER` ou `ADMIN` com membership ativo, elegibilidade
   `eligible=true` e credencial atual configurada com `ValidationStatus`
   `Succeeded`, quando ativar o Agente, então ele deve ficar Ativado e deve ser
   criado exatamente um comando imediato Pendente.
3. Dado qualquer pré-requisito de ativação não atendido, quando o usuário
   autorizado tentar ativar, então o Agente deve permanecer Desativado e nenhum
   comando imediato deve ser criado.
4. Dado um Agente Ativado com comando Pendente, quando um `OWNER` ou `ADMIN`
   autorizado o desativar, então novas execuções devem ser impedidas e o comando
   deve ser cancelado.
5. Dado um Agente Ativado com comando Pendente, quando a elegibilidade de Trial
   for perdida ou a validação deixar de ser `Succeeded`, então o Agente deve ser
   desativado e o comando deve ser cancelado.
6. Dado um comando cancelado, quando as pré-condições se tornarem válidas sem
   nova ativação, então ele não deve ser reativado; quando houver nova ativação
   válida, então deve existir uma nova solicitação rastreável.
7. Dado um usuário autorizado no Office, quando consultar o estado do Agente,
   então deve visualizar estado, disponibilidade ou motivo, estado de validação
   e comando Pendente, sem qualquer segredo.
8. Dado um usuário associado a outro tenant ou sem membership ativo, quando
   tentar consultar ou alterar o estado do Agente, então não deve receber dados
   nem alterar recursos do tenant.

## Dependências

- ADR-003 (Master API, Agente Coletor e isolamento por tenant).
- ADR-015 (elegibilidade de Trial e contrato interno de elegibilidade).
- ADR-017 (credencial de serviço interno e limites de resposta do contrato de
  elegibilidade).
- RF-007 (configuração e `ValidationStatus` da credencial iService).

## Impactos

- Requer estado rastreável do Agente e de cada solicitação imediata por tenant.
- Requer que o Office reflita a elegibilidade e a validação vigentes sem expor
  segredos.
- A implementação e a execução de coleta permanecem gateadas e exigem aprovação
  explícita do usuário em portão posterior.

## Fora do escopo

- Implementação ou execução de coleta.
- Agendamento recorrente.
- Processamento de comandos Pendentes.
- Aprovação de implementação ou validação de QA.
