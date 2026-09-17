# RF-025 - Coleta Recorrente Configurável

Status: `Proposto`

## História de Usuário

Como usuário `OWNER` ou `ADMIN` de um tenant no Office,
eu quero configurar o intervalo de frequência (em minutos) com o qual o Agente
Coletor coletará dados automaticamente do iService,
para que eu possa ajustar o ciclo de coleta conforme a necessidade operacional
do meu negócio sem depender de mudanças técnicas ou redeploy.

**Fluxo**: o usuário navega até a rota `/settings` do Office. Na seção
de integração do provedor (mesma seção do painel `CollectorActivationPanel`
e do botão "Coletar" de RF-024), há um controle chamado **"Frequência da
coleta"** (ou "Intervalo de coleta recorrente") exibindo o valor atual em
minutos. Ao editar, o usuário informa um novo intervalo (ex.: 15, 30, 60
minutos). O Office valida o intervalo contra limites mínimo/máximo, envia a
configuração para a API e persiste a alteração na integração. Quando o
Agente Coletor estiver Ativado, a coleta recorrente passa a respeitar esse
novo intervalo a partir da próxima execução.

## Objetivo

Permitir que clientes (`OWNER`/`ADMIN`) configurem a frequência da coleta
recorrente do Agente Coletor diretamente no Office, sem dependência de
configuração técnica do backend. A frequência deve ser persistida na
integração (tenant+provider) e aplicada automaticamente quando o Agente
estiver Ativado.

## Escopo

Este requisito define:

1. Um novo campo de configuração (`RecurrentCollectionIntervalMinutes`) na
   entidade `Integration` (tenant+provider —
   `apps/api/Atua.Api/Domain/Integrations/Integration.cs`).
2. Um controle de UI no Office (seção de integração em `/settings`) para
   visualizar e editar esse intervalo.
3. Validação de mínimo/máximo do intervalo, aplicada tanto no frontend quanto
   na API (defesa em profundidade).
4. Extensão de um endpoint existente (ou criação de um novo) na Master API
   para o Office ler e alterar o intervalo.
5. Definição clara do comportamento esperado quando o intervalo é alterado
   (recálculo do próximo agendamento) e quando o Agente está desativado
   (intervalo persiste, mas não há coleta).
6. Suporte a i18n (pt-BR, en-US, es-AR) no rótulo e texto de ajuda do
   controle.

Este requisito **não define** a implementação técnica do agendador recorrente
(BackgroundService interno, EventBridge Scheduler externo, cron em EC2, etc.)
— essa é uma decisão de arquitetura a ser registrada em ADR próprio. RF-025
assume que o agendador será implementado em fase posterior, respeitando o
intervalo aqui configurado.

## Requisitos funcionais

### RF-025.1 - Novo campo de configuração na integração

A entidade `Integration` deve possuir um novo campo
`RecurrentCollectionIntervalMinutes` do tipo `int`, com valor padrão de `15`
minutos (conforme padrão citado em ADR-003 e no requisito de onboarding).

### RF-025.2 - Visibilidade do controle na UI

O controle "Frequência da coleta" deve ser exibido na seção de integração
(mesma seção de `CollectorActivationPanel` e do botão "Coletar" de RF-024) da
página `/settings` do Office, sempre que houver uma integração provisionada
(`integrationId` não nulo) associada ao tenant selecionado.

### RF-025.3 - Autorização

Somente usuários com membership ativo `OWNER` ou `ADMIN` no tenant podem
visualizar e editar o intervalo de coleta recorrente. Para outros papéis, o
controle deve ficar oculto ou desabilitado. A mesma regra de autorização de
RF-008 (ativação do Agente) aplica-se integralmente.

### RF-025.4 - Validação de intervalo

O intervalo de coleta recorrente deve estar dentro de limites definidos:

- **Mínimo**: 5 minutos (evitar sobrecarga do iService/provedor).
- **Máximo**: 1440 minutos (24 horas).
- **Padrão**: 15 minutos (valor histórico citado em ADR-003 e no requisito de
  onboarding).

Se o usuário tentar salvar um intervalo fora desses limites, o Office deve
exibir uma mensagem de validação clara, sem enviar a requisição para a API.
A API deve validar novamente (defesa em profundidade) e retornar um erro de
validação se receber um intervalo inválido.

### RF-025.5 - Persistência da configuração

Ao enviar o novo intervalo, a Master API deve atualizá-lo de forma durável na
`Integration` e devolver a configuração atualizada ao Office. A alteração deve
ficar imediatamente visível no Office em consultas subsequentes.

### RF-025.6 - Independência em relação ao estado de ativação

O intervalo é uma configuração de integração independente do estado de
ativação do Agente (`CollectorActivation`, RF-008). Ou seja:

- Se o Agente estiver **Desativado**, a alteração do intervalo é persistida,
  mas nenhuma coleta recorrente ocorre.
- Se o Agente estiver **Ativado**, a coleta recorrente passa a respeitar o
  novo intervalo a partir da próxima execução agendada.
- Alterar o intervalo enquanto o Agente está Ativado **não** ativa,
  desativa, nem cancela comandos pendentes.

### RF-025.7 - Recálculo de agendamento ao alterar intervalo com Agente ativo

Quando o intervalo é alterado com o Agente Ativado, o agendador recorrente
(a ser implementado em ADR futuro, ver "Fora do escopo") deve recalcular a
próxima execução com base no novo intervalo, sem interromper o Agente nem
cancelar comandos em andamento.

### RF-025.8 - Sem criação automática de comando apenas por alterar o intervalo

O sistema não deve criar nem agendar nenhum `ImmediateCollectionCommand`
automaticamente apenas por alterar o intervalo. Comandos recorrentes só devem
ser criados quando o Agente estiver Ativado (RF-008/ADR-020), respeitando o
intervalo configurado a partir de então.

### RF-025.9 - Exibição e unidade

O controle deve exibir o intervalo em **minutos** (ex.: "15", "30", "1440").
Rótulo e texto de ajuda devem ser localizados via i18n (chaves como
`integration.recurrentCollectionInterval.label` e
`integration.recurrentCollectionInterval.help`), seguindo o padrão já usado
para as chaves de `collector.*`.

## Regras de negócio

| Número   | Regra                                                                                                                                |
|----------|---------------------------------------------------------------------------------------------------------------------------------------|
| RN-025.1 | O intervalo de coleta recorrente é uma propriedade durável da integração; persiste mesmo se o Agente for desativado.                   |
| RN-025.2 | Valores permitidos: mínimo 5 minutos, máximo 1440 minutos (24 horas); padrão: 15 minutos.                                              |
| RN-025.3 | Validação ocorre tanto no cliente (frontend, feedback imediato) quanto no servidor (defesa em profundidade).                            |
| RN-025.4 | Somente `OWNER`/`ADMIN` podem alterar o intervalo; autorização é por tenant, igual à regra de RF-008.                                  |
| RN-025.5 | Alterar o intervalo não ativa, desativa nem cancela o Agente ou comandos em andamento.                                                 |
| RN-025.6 | O agendador recorrente (implementação futura) deve criar coletas respeitando o intervalo configurado, apenas com o Agente Ativado.      |
| RN-025.7 | Se o intervalo for alterado enquanto o Agente está Ativado, o próximo agendamento deve usar o novo intervalo.                          |
| RN-025.8 | O intervalo configurável é uma frequência de **negócio**; o polling técnico do Worker (`PollingIntervalSeconds`, default 60s em produção) é um mecanismo interno separado de long-polling para obter comandos pendentes, não deve ser confundido com este requisito. |

## Casos de borda

- Usuário sem membership ativo ou com papel diferente de `OWNER`/`ADMIN` →
  controle oculto/desabilitado; qualquer tentativa direta à API deve ser
  negada (403, mesma semântica de RF-008).
- Usuário tenta enviar intervalo menor que 5 minutos ou maior que 1440
  minutos → validação do frontend rejeita antes de enviar; se a requisição
  chegar à API, ela retorna `400 Bad Request` com código de erro de
  validação.
- Intervalo é alterado enquanto um `ImmediateCollectionCommand` está
  `Pending`/`Claimed` → o comando em andamento não é afetado; o novo
  intervalo aplica-se apenas ao próximo agendamento após a conclusão do
  comando atual.
- Intervalo alterado de 15 para 60 minutos com Agente Ativado → o próximo
  agendamento respeita 60 minutos.
- Agente Desativado, intervalo alterado, Agente reativado → a coleta
  recorrente passa a respeitar o novo intervalo assim que reativada.
- Integração ainda não provisionada (`integrationId` nulo) → controle não
  deve ser exibido.
- Leitura do intervalo para a UI quando não há valor persistido → retorna o
  valor padrão (15 minutos).

## Critérios de aceite

1. Dado um `OWNER`/`ADMIN` autorizado com integração provisionada, quando
   acessar a seção de integração em `/settings`, então o controle
   "Frequência da coleta" deve estar visível exibindo o valor atual em
   minutos.

2. Dado um usuário autorizado visualizando o intervalo de 15 minutos, quando
   alterar para 30 minutos e salvar, então a API deve persistir o novo valor
   e o Office deve refletir o novo valor após a resposta.

3. Dado um usuário tentando salvar um intervalo de 2 minutos (abaixo do
   mínimo), quando enviar a alteração, então a UI deve rejeitar localmente e
   exibir mensagem de validação sem chamar a API.

4. Dado um usuário tentando salvar um intervalo de 2000 minutos (acima do
   máximo), quando enviar a alteração, então a UI deve rejeitar localmente e
   exibir mensagem de validação sem chamar a API.

5. Dado uma integração sem valor explícito de intervalo persistido, quando o
   Office consultar a configuração, então deve exibir o valor padrão de 15
   minutos.

6. Dado um Agente Ativado com intervalo de 15 minutos, quando o usuário
   alterar para 60 minutos, então o Agente deve permanecer Ativado, nenhum
   comando deve ser cancelado, e o novo intervalo deve ser aplicado no
   próximo agendamento (comportamento do agendador definido em ADR futuro).

7. Dado um Agente Desativado com intervalo de 15 minutos, quando o usuário
   alterar para 30 minutos e depois reativar o Agente, então a coleta
   recorrente deve respeitar o intervalo de 30 minutos a partir da
   reativação.

8. Dado um usuário sem papel `OWNER`/`ADMIN`, quando acessar `/settings`,
   então o controle "Frequência da coleta" não deve estar disponível para
   edição.

9. Dado que a alteração de intervalo é enviada por requisição autenticada,
   quando processada com sucesso, então a resposta deve conter o campo
   `recurrentCollectionIntervalMinutes` atualizado.

10. Dado o Office configurado em pt-BR, en-US ou es-AR, quando exibir o
    controle, então o rótulo e o texto de ajuda devem estar no idioma
    correspondente.

## Fora do escopo

- **Implementação técnica do agendador recorrente**: o mecanismo que cria
  periodicamente novos `ImmediateCollectionCommand`s respeitando o intervalo
  (BackgroundService interno, EventBridge Scheduler externo, cron em EC2,
  etc.) é uma decisão de arquitetura a ser registrada em ADR próprio quando
  implementada. Este requisito apenas garante que o intervalo é configurável
  e persistido.
- **Ajuste automático/adaptativo de intervalo**: nenhuma lógica de ajuste de
  frequência com base em carga, taxa de sucesso ou disponibilidade do
  provedor.
- **Histórico/auditoria de alterações do intervalo**: registrar quem alterou
  e quando não é requisito deste RF.
- **Notificações de falha recorrente**: alertas sobre falhas consecutivas de
  coleta recorrente ficam fora de escopo (possível requisito futuro).
- **Dashboard de execuções recorrentes**: visualização de histórico/estatísticas
  de cada execução recorrente é coberta parcialmente por RF-010/RF-011/RF-016,
  mas um painel dedicado de execuções agendadas é pós-MVP.
- **Múltiplos provedores com intervalos independentes por integração**: o
  MVP assume uma integração por tenant+provider; cada integração tem seu
  próprio intervalo, mas suporte a cenários mais complexos de priorização
  entre integrações é pós-MVP.

## Rastreabilidade e requisitos relacionados

### Requisitos

- **RF-008** (Ativação do Agente Coletor): define o estado de ativação e a
  autorização de `OWNER`/`ADMIN`; RF-025 estende a integração com frequência
  configurável.
- **RF-009** (Coleta Inicial): define o ciclo `claim`/`complete` de
  `ImmediateCollectionCommand`; o agendador recorrente (futuro) criará novos
  comandos respeitando este intervalo.
- **RF-011** (Atualização de estado e transições): pendência de "RF futuro de
  coleta recorrente" citada explicitamente; RF-025 formaliza a configuração
  de frequência (o comportamento de transição de estado por coleta
  recorrente permanece regido por RF-011).
- **RF-016** (Persistência de snapshots brutos): cita "coleta recorrente e
  seu scheduling" como fora de escopo até um RF futuro; RF-025 é esse
  requisito para a parte de configuração de frequência.
- **RF-017** (Modelo agnóstico work_order): mesma pendência de "scheduling de
  coleta recorrente (RF futuro)"; RF-025 formaliza a configurabilidade.
- **RF-024** (Coleta Manual Sob Demanda): UI irmã na mesma seção de
  integração do Office.

### Decisões

- **ADR-003** (Backend, Master API e Agente Coletor): menciona "execução
  recorrente a cada 15 minutos" como padrão original de produto; RF-025
  formaliza essa configurabilidade em vez de um valor fixo.
- **ADR-007** (AWS Infrastructure MVP): discute opções de orquestração do
  agendador recorrente (EventBridge Scheduler, SQS+Lambda, cron em EC2);
  decisão de mecanismo permanece em aberto, a ser resolvida em ADR próprio
  quando implementado.
- **ADR-010** (AWS MVP Free Tier Optimization): avalia custo de EventBridge
  Scheduler vs. cron em EC2 para a coleta recorrente; mesma pendência de
  decisão técnica.
- **ADR-020** (Ativação e comando imediato do Agente Coletor): contrato de
  `CollectorActivation`/`ImmediateCollectionCommand` reaproveitado por
  RF-025 sem alteração de contrato.
- **ADR-021** (Coleta inicial, credenciais, timeout e falha de credencial):
  mecanismo de `claim`/`complete` e `PollingIntervalSeconds` do Worker,
  citado para diferenciar polling técnico de frequência de negócio
  (RN-025.8).

### Decisão futura necessária

- **ADR futuro (scheduler de coleta recorrente)**: deve definir o mecanismo
  técnico que lê `RecurrentCollectionIntervalMinutes` de cada `Integration`
  Ativada e cria `ImmediateCollectionCommand`s no intervalo configurado.
  Opções já discutidas em ADR-007/ADR-010: BackgroundService interno à
  Master API, EventBridge Scheduler, cron em EC2. Esta decisão consome
  RF-025, mas não é definida por ele.

## Histórico de alterações

- **2026-09-17** (proposto): requisito criado com escopo completo — campo de
  configuração na integração, validação de limites, independência do estado
  de ativação e rastreabilidade com ADRs/RFs pendentes desde ADR-003.
