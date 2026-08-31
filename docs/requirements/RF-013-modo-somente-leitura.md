# RF-013 - Modo Somente Leitura

Status: `Implementado (guard ativo) — validação formal pendente`

## Objetivo

Garantir que o Agente Coletor não execute nenhuma operação de escrita no
iService — nem aceite OS, nem reatribua técnicos, nem realize qualquer outra
ação que altere dados no portal.

## Escopo

RF-013 formaliza o requisito de isolamento do Coletor em relação ao iService.
O mecanismo técnico (guard de rede via Playwright) já está implementado em
`IServiceCollectorService.SetupReadOnlyGuardAsync`. Este documento registra
o contrato de negócio e os critérios verificáveis que o guard precisa
satisfazer.

### Estado atual da implementação

O método `SetupReadOnlyGuardAsync` em `IServiceCollectorService` instala um
interceptador de rotas via `page.RouteAsync("**/*", ...)` que avalia cada
requisição antes de enviá-la. A lógica de bloqueio (`IsWriteRequestOnIService`)
bloqueia qualquer requisição `POST` para `ics-amer.midea.com` cujo endpoint
em `/web/iservice-wom/workOrder/` não comece com `query`, `get` ou `select`.
Requisições bloqueadas recebem `blockedbyclient` e são logadas como `[READONLY]`.

**Interface `IIServiceCollector`:** expõe apenas `CollectAsync` — nenhum
método de escrita está presente na interface nem na implementação.

**Conclusão da leitura do código:** o guard está ativo e cobre os endpoints
`/web/iservice-wom/workOrder/`. RF-013 pode ser considerado
**substancialmente implementado** pelo guard existente, com as ressalvas
registradas em DP-013.1 e DP-013.2 abaixo.

### O que é RF-013

- Regra absoluta: nenhuma operação de escrita no iService é permitida pelo
  Coletor, sob nenhuma condição, inclusive quando o usuário solicitar.
- Bloqueio não configurável: o guard não pode ser desativado por configuração,
  flag de feature ou qualquer parâmetro em tempo de execução.
- Cobertura total: o bloqueio deve cobrir todos os endpoints e métodos HTTP
  de escrita do iService acessíveis pelo Coletor.

### O que NÃO é RF-013

- Controle de escrita na base de dados interna do ATUA (MongoDB) — isso é
  escopo dos requisitos de persistência (RF-009, RF-010, RF-011).
- Controle de acesso de outros componentes ao iService (apenas o Coletor
  é endereçado aqui).

## Requisitos funcionais

### RF-013.1 - Proibição absoluta de escrita no iService

O Agente Coletor não deve enviar nenhuma requisição que altere dados no
iService. Isso inclui, sem limitação: aceitar OS, reatribuir técnicos,
alterar status, inserir comentários ou qualquer outra operação de escrita.

### RF-013.2 - Guard de rede não configurável

O bloqueio de requisições de escrita deve ser instalado antes de qualquer
interação com o iService e não pode ser desativado por configuração, variável
de ambiente, flag de feature ou parâmetro de execução.

### RF-013.3 - Log de tentativas bloqueadas

Qualquer requisição de escrita interceptada pelo guard deve ser registrada
em log com nível de aviso (`Warning`), incluindo método HTTP e URL, sem
incluir credenciais ou dados de sessão.

### RF-013.4 - Cobertura de endpoints

O guard deve cobrir todos os endpoints do host `ics-amer.midea.com` que
possam realizar escrita. A heurística atual (bloquear `POST` em
`/web/iservice-wom/workOrder/` cujo endpoint não comece com `query`,
`get` ou `select`) deve ser revisada se novos endpoints de leitura ou
escrita forem descobertos.

## Regras de negócio

| Número   | Regra                                                                                                                    |
|----------|--------------------------------------------------------------------------------------------------------------------------|
| RN-013.1 | O Coletor opera exclusivamente em modo de leitura no iService; escrita é vedada sem exceção.                             |
| RN-013.2 | O bloqueio de escrita não é configurável — não pode ser desativado por nenhum parâmetro ou operador.                    |
| RN-013.3 | Toda tentativa de escrita bloqueada deve ser registrada em log com método e URL, sem credenciais.                        |
| RN-013.4 | A cobertura do guard deve ser revisada se novos endpoints do iService forem descobertos.                                  |

## Critérios de aceite

1. Dado que o guard está instalado,
   quando o Coletor executar uma coleta completa,
   então nenhuma requisição `POST` que não seja de leitura deve ser enviada
   ao host `ics-amer.midea.com`.

2. Dado que o guard está instalado,
   quando qualquer código interno tentar disparar uma requisição `POST` de
   escrita ao iService (ex.: aceitar OS),
   então a requisição deve ser bloqueada com `blockedbyclient`,
   e uma entrada de log `Warning` com método e URL deve ser registrada,
   sem incluir credenciais ou dados de sessão.

3. Dado que o Coletor é iniciado com qualquer combinação de configurações,
   quando o método `CollectAsync` for invocado,
   então o guard de somente leitura deve estar instalado antes da primeira
   requisição ao iService.

4. Dado que `IIServiceCollector` é a interface pública do Coletor,
   quando a interface for inspecionada,
   então não deve existir nenhum método de escrita exposto (aceitar, reatribuir,
   alterar status, etc.).

## Dependências

- RF-009.4: declara o modo somente leitura como requisito desde a coleta
  inicial; RF-013 é a especificação detalhada desse requisito.
- `IServiceCollectorService.SetupReadOnlyGuardAsync`: implementação atual do
  guard — instala interceptador de rotas Playwright antes do login CAS.
- `IServiceCollectorService.IsWriteRequestOnIService`: heurística de
  classificação de requisições de escrita.
- `IIServiceCollector`: interface que não expõe métodos de escrita.

## Impactos

- Qualquer novo endpoint do iService descoberto via Playwright deve ser
  avaliado quanto à necessidade de atualização da heurística de
  `IsWriteRequestOnIService`.

## Decisões pendentes

### DP-013.1 — Cobertura da heurística para endpoints fora de `/workOrder/`

**Situação:** O guard atual bloqueia `POST` apenas em
`/web/iservice-wom/workOrder/`. Endpoints de escrita em outros paths do
iService (ex.: módulos de peças, agendamento, comunicação com cliente) não
estão cobertos pela heurística atual.

**Impacto se não decidido:** se o Playwright, por algum fluxo de navegação,
acionar um endpoint de escrita fora de `/workOrder/`, o guard não o bloqueará.

**Aguarda:** mapeamento dos endpoints acessíveis durante a navegação do
Coletor (tarefa de descoberta via Playwright/engenharia).

### DP-013.2 — Teste automatizado do guard em ambiente de integração

**Situação:** Não está claro se existe um teste automatizado que valide o
comportamento do guard tentando disparar uma requisição de escrita e
verificando que ela é bloqueada. Os testes atuais (148 passando, RF-009)
cobrem o lado API.

**Impacto se não decidido:** a garantia do guard depende apenas de revisão
de código, não de teste verificável.

**Aguarda:** decisão do `qa-engineer` sobre cobertura de teste do guard.

## Fora do escopo

- Controle de escrita na base de dados interna do ATUA (MongoDB).
- Restrições de acesso de outros componentes ao iService.
- Mecanismo de autenticação e autorização CAS (RF-009).
