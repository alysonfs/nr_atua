# RF-024 - Coleta Manual Sob Demanda (Botão "Coletar")

Status: `Proposto`

## História de Usuário

Como usuário `OWNER` ou `ADMIN` de um tenant no Office,
eu quero acionar manualmente uma nova coleta a partir da tela de
Configurações,
para que os dados das ordens de serviço sejam atualizados sem depender do
próximo ciclo automático do Agente Coletor.

**Fluxo**: o usuário navega até a rota `/settings` do Office. Na seção
identificada por `integration-heading` (mesma seção do painel de integração
iService, `IServiceIntegrationPanel`), há um botão com o rótulo **"Coletar"**.
Ao clicar, o Office dispara o comando que solicita ao Agente Coletor a
atualização imediata dos dados da integração atual.

## Objetivo

Permitir que membros ativos `OWNER` e `ADMIN` solicitem, a partir do Office,
uma nova coleta imediata para a integração atual do tenant, reaproveitando o
contrato de ativação e comando imediato já existente (RF-008/ADR-020), sem
exigir a dança manual de desativar e reativar o Agente.

## Escopo

Este requisito define a experiência do usuário (botão "Coletar" na seção
`integration-heading` de `/settings`) e a orquestração no Office para
solicitar um novo `ImmediateCollectionCommand` `Pending` quando as
pré-condições de RF-008 estiverem satisfeitas. Não define nova execução de
coleta pelo Worker, nem altera o contrato da Master API além do necessário
para expor o disparo de forma explícita ao usuário.

## Requisitos funcionais

### RF-024.1 - Visibilidade do botão

O botão "Coletar" deve ser exibido na seção `integration-heading` da página
`/settings` sempre que houver uma integração ativa (`integrationId`)
associada ao tenant selecionado.

### RF-024.2 - Autorização

Somente usuários com membership ativo `OWNER` ou `ADMIN` no tenant podem
acionar o botão. Para outros papéis, o botão deve ficar oculto ou desabilitado
(RN-008.1/RN-008.2 aplicam-se integralmente).

### RF-024.3 - Pré-condições de disparo

O clique só deve resultar em nova solicitação quando as mesmas pré-condições
de RF-008.3 forem atendidas: elegibilidade vigente (`eligible=true`) e
credencial iService da integração atual com `ValidationStatus` `Succeeded`.
Caso alguma condição não seja atendida, o Office deve informar o motivo sem
expor segredos, e nenhum comando deve ser criado.

### RF-024.4 - Reaproveitamento do Agente já Ativado

Se o Agente já estiver **Ativado** e não houver comando imediato `Pending`
em aberto, o clique deve resultar na criação de exatamente um novo
`ImmediateCollectionCommand` `Pending`, sem desativar e reativar o Agente.

### RF-024.5 - Agente ainda Desativado

Se o Agente estiver **Desativado** e as pré-condições estiverem satisfeitas,
o clique deve ativá-lo e criar o comando imediato `Pending`, conforme já
definido em RF-008.4.

### RF-024.6 - Comando já Pendente

Se já existir um comando imediato `Pending` para a integração, o clique não
deve criar um segundo comando (RN-008.4). O Office deve informar ao usuário
que já existe uma coleta em andamento/pendente.

### RF-024.7 - Feedback ao usuário

Após o clique, o Office deve exibir o resultado da solicitação: sucesso
(comando criado ou já pendente reconhecido), indisponibilidade por
pré-condição não atendida, ou erro de autorização/rede — sem expor
credenciais, tokens ou segredos.

## Regras de negócio

- RN-024.1: A ação do botão "Coletar" é semanticamente uma nova ativação
  idempotente (mesmo contrato de RF-008.4/RN-008.4); não introduz um novo tipo
  de comando nem novo estado de `ImmediateCollectionCommand`.
- RN-024.2: Cada clique válido deve gerar uma `Idempotency-Key` própria, para
  evitar duplicidade em caso de reenvio de rede.
- RN-024.3: O botão herda todas as restrições de isolamento por tenant e
  autorização já definidas em RF-008 (RN-008.1, RN-008.2, RN-008.6).

## Casos de borda

- Usuário sem membership ativo ou com papel diferente de `OWNER`/`ADMIN` →
  botão oculto/desabilitado; qualquer tentativa de disparo direto à API deve
  ser negada.
- Elegibilidade `eligible=false` ou credencial sem `ValidationStatus`
  `Succeeded` → nenhum comando é criado; Office exibe o motivo.
- Já existe comando `Pending` → nenhum novo comando é criado; Office informa
  que a coleta já está em andamento.
- Clique duplicado (duplo clique, reenvio de rede) com a mesma
  `Idempotency-Key` → deve resultar na mesma resposta (replay), sem criar
  comando duplicado.
- Integração ainda não provisionada (`integrationId` nulo) → botão não deve
  ser exibido.

## Critérios de aceite

1. Dado um usuário `OWNER`/`ADMIN` autorizado, com elegibilidade e credencial
   válidas, e Agente já Ativado sem comando Pendente, quando clicar em
   "Coletar", então deve ser criado exatamente um novo comando imediato
   Pendente.
2. Dado um usuário `OWNER`/`ADMIN` autorizado, com Agente Desativado e
   pré-condições válidas, quando clicar em "Coletar", então o Agente deve ser
   Ativado e um comando imediato Pendente deve ser criado.
3. Dado um comando imediato já Pendente para a integração, quando o usuário
   clicar em "Coletar", então nenhum novo comando deve ser criado e o Office
   deve informar que já há uma coleta pendente.
4. Dado qualquer pré-requisito de RF-008.3 não atendido, quando o usuário
   clicar em "Coletar", então nenhum comando deve ser criado e o motivo deve
   ser exibido sem expor segredos.
5. Dado um usuário sem papel `OWNER`/`ADMIN`, quando acessar `/settings`, então
   o botão "Coletar" não deve estar disponível para acionamento.

## Dependências

- RF-008 (Ativação do Agente Coletor) — contrato de ativação e comando
  imediato reaproveitado integralmente.
- ADR-020 (Ativação e comando imediato do Agente Coletor).
- `IServiceIntegrationPanel` (`apps/office/src/app/office/components/Integrations`)
  — componente onde o botão será inserido, na seção `integration-heading` de
  `SettingsPage.tsx`.

## Impactos

- Não requer novo endpoint na Master API: reutiliza
  `PUT /api/tenants/{tenantId}/integrations/{integrationId}/collector-activation`.
- Requer novo elemento de UI (botão + feedback) no Office.
- Não altera o processamento de comandos pelo Worker, que continua fora deste
  escopo.

## Fora do escopo

- Implementação ou execução de coleta pelo Worker.
- Agendamento recorrente.
- Alteração do contrato da Master API além do necessário para o disparo pelo
  Office.
- Aprovação de implementação ou validação de QA.
