# RF-013 - Modo Somente Leitura

Status: `Implementado e testado — cobertura default-deny; descoberta completa de endpoints ainda pendente`

**Data:** 2026-08-31 (guard original) · **2026-09-02** (guard endurecido para
default-deny + testes automatizados, DP-013.2 resolvida)

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

### Estado atual da implementação (revisado em 2026-09-02, corrigido após
regressão em produção — ver "Incidente" abaixo)

O método `SetupReadOnlyGuardAsync` em `IServiceCollectorService` instala um
interceptador de rotas via `page.RouteAsync("**/*", ...)` que avalia cada
requisição antes de enviá-la. A lógica de bloqueio
(`IsWriteRequestOnIService`, agora `public static` para permitir teste
unitário isolado — DP-013.2) segue uma postura **default-deny escopada ao
módulo de negócio de OS** (`/web/iservice-wom/`) no host
`ics-amer.midea.com`:

1. Fora de `/web/iservice-wom/` (ex.: `/web/auth-server/...` — sessão CAS;
   `/web/iservice-admin/...` — telemetria de erro do cliente): **nunca
   bloqueado**. São endpoints de infraestrutura do portal, não de escrita
   de dados de OS/cliente.
2. Dentro de `/web/iservice-wom/`: métodos seguros (`GET`/`HEAD`/`OPTIONS`)
   nunca são bloqueados; `PUT`/`PATCH`/`DELETE` são sempre bloqueados
   (nunca há motivo legítimo para o Coletor usá-los); `POST` só passa se o
   último segmento do caminho começar com `query`, `get` ou `select`
   (heurística original, agora aplicada a todo o prefixo, não apenas a
   `/workOrder/`).

Requisições bloqueadas recebem `blockedbyclient` e são logadas como
`[READONLY]`.

**Mudança em relação à versão original (2026-08-31):** a heurística
cobria apenas `/web/iservice-wom/workOrder/`; qualquer `POST` de escrita em
outro submódulo de negócio do mesmo host (peças, agendamento, comunicação
com cliente etc.) não seria bloqueado. A versão atual amplia o prefixo
protegido para todo `/web/iservice-wom/` e passa a bloquear
incondicionalmente `PUT`/`PATCH`/`DELETE` **dentro desse prefixo**,
mitigando o risco descrito em DP-013.1 sem depender de mapeamento completo
dos endpoints do iService.

### Incidente em produção (2026-09-02) e correção

Uma primeira versão desta mudança aplicou o default-deny ao **host inteiro**
(não apenas a `/web/iservice-wom/`), incluindo `PUT`/`PATCH`/`DELETE` de
qualquer caminho e `POST` fora do prefixo de negócio. Ao ser deployada e
validada ao vivo (`make redeploy-collector` + `make collector-trigger-cycle`),
o guard bloqueou `POST /web/auth-server/login/option`,
`POST /web/auth-server/user/getSetProfile` e
`POST /web/iservice-admin/htmlAppErrorLog/insertLog` — endpoints de sessão
CAS e telemetria de erro do cliente, necessários ao fluxo normal de login,
não relacionados a escrita de dados de OS. O bloqueio quebrou o login do
Coletor (`PlaywrightException: Cannot read properties of null (reading
'innerText')`, comando concluído com `outcome=Failed`).

**Correção:** o escopo do default-deny foi restrito a `/web/iservice-wom/`
(o módulo de negócio de OS, onde reside o risco real descrito em
DP-013.1). Fora desse prefixo, o comportamento permanece "não bloqueado"
(igual à versão original). Os três endpoints reais descobertos no
incidente foram adicionados como casos de teste de regressão em
`IServiceCollectorServiceReadOnlyGuardTests.cs`. Redeploy + novo ciclo de
teste confirmaram login e coleta funcionando normalmente com o guard
corrigido (ver seção "Implementação e validação").

**Interface `IIServiceCollector`:** expõe apenas `CollectAsync` — nenhum
método de escrita está presente na interface nem na implementação.

**Conclusão da leitura do código:** o guard está ativo, cobre todo o
prefixo `/web/iservice-wom/` (não apenas `/workOrder/`) e bloqueia
incondicionalmente métodos de escrita HTTP dentro desse prefixo, sem
afetar endpoints de infraestrutura do portal fora dele. RF-013 é
considerado **implementado e testado** — ver seção "Implementação e
validação" abaixo.

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
possam realizar escrita. A heurística atual bloqueia por padrão
(default-deny) qualquer método diferente de `GET`/`HEAD`/`OPTIONS` fora do
prefixo `/web/iservice-wom/`, bloqueia incondicionalmente `PUT`/`PATCH`/
`DELETE` dentro desse prefixo, e bloqueia `POST` dentro dele cujo endpoint
não comece com `query`, `get` ou `select`. Deve ser revisada se novos
padrões de endpoint de leitura legítimos forem descobertos e bloqueados
incorretamente (falso positivo) — o risco de falso negativo (endpoint de
escrita não coberto) é mitigado pela postura default-deny.

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

## Implementação e validação (2026-09-02)

**Testes automatizados** (resolve DP-013.2):
`tests/Atua.Collector.Tests/IServiceCollectorServiceReadOnlyGuardTests.cs`
cobre `IsWriteRequestOnIService` (tornado `public static` para teste
isolado, sem depender de navegador real) com 26 casos: métodos seguros
(`GET`/`HEAD`/`OPTIONS`) nunca bloqueados; `POST` para os dois endpoints
de consulta reais usados pelo Coletor (`queryWorkOrder`,
`queryOneWorkOrder`) e variações de nome (`getStatusCount`,
`selectAssignedTechnicians`) não bloqueado; `POST` de escrita conhecida
(`acceptWorkOrder`, `reassignTechnician`, `updateStatus`) bloqueado;
`POST` para outros submódulos de negócio dentro de `/web/iservice-wom/`
(peças, agendamento, comunicação com cliente) também bloqueado — prova
automatizada da mitigação de DP-013.1; `POST` para os três endpoints de
infraestrutura descobertos no incidente em produção
(`/web/auth-server/login/option`, `/web/auth-server/user/getSetProfile`,
`/web/iservice-admin/htmlAppErrorLog/insertLog`) **não** bloqueado —
teste de regressão que trava a correção do incidente; `PUT`/`PATCH`/
`DELETE` dentro de `/web/iservice-wom/` sempre bloqueados, mesmo em
caminhos com nome de "query"; requisições fora do host do iService (CAS,
CDN) nunca bloqueadas; URL inválida/vazia não bloqueada (mesma tolerância
defensiva de antes). 33/33 testes do Collector passando (7 pré-existentes
+ 26 novos).

**Validação ao vivo em produção**: `make redeploy-collector` +
`make collector-trigger-cycle` (duas rodadas — uma com a versão que
causou o incidente, outra após a correção). Na segunda rodada, os logs
confirmaram `[READONLY] Modo somente leitura ativo`, nenhum bloqueio
inesperado, login CAS bem-sucedido e coleta concluída normalmente,
confirmando que o guard corrigido não introduz regressão no fluxo real.

**Mitigação de DP-013.1**: em vez de aguardar o mapeamento completo dos
endpoints do iService (tarefa de descoberta ainda não realizada), o guard
foi redesenhado para **default-deny escopado a `/web/iservice-wom/`**:
bloqueia por padrão qualquer método de escrita (`POST`/`PUT`/`PATCH`/
`DELETE`) fora do conjunto restrito de padrões de leitura conhecidos,
dentro do módulo de negócio de OS — em vez de bloquear apenas uma lista
específica de padrões de escrita conhecidos dentro de `/workOrder/`.
Fecha o risco descrito em DP-013.1 para todo o módulo de negócio, sem
exigir mapeamento completo dos seus submódulos — mas, como o incidente em
produção mostrou, **não pode ser aplicado ao host inteiro** sem quebrar
endpoints de infraestrutura do portal (sessão CAS, telemetria). O
mapeamento completo de endpoints continua sendo valioso para reduzir o
risco de falso positivo dentro do próprio módulo de negócio (um endpoint
de leitura legítimo em um submódulo ainda não descoberto, ser bloqueado
incorretamente e gerar `IServiceUnavailableEx`).

## Decisões pendentes

### DP-013.1 — ~~Cobertura da heurística para endpoints fora de `/workOrder/`~~ ✅ Mitigada (2026-09-02)

**Situação original:** O guard bloqueava `POST` apenas em
`/web/iservice-wom/workOrder/`. Endpoints de escrita em outros paths do
iService (ex.: módulos de peças, agendamento, comunicação com cliente) não
estavam cobertos pela heurística original.

**Mitigação aplicada:** o guard passou a bloquear por padrão
(default-deny) qualquer `POST`/`PUT`/`PATCH`/`DELETE` fora do conjunto
restrito de padrões de leitura conhecidos em todo `/web/iservice-wom/`, e
bloqueia incondicionalmente `PUT`/`PATCH`/`DELETE` mesmo dentro desse
prefixo. Não depende mais de conhecer os endpoints de escrita
especificamente — qualquer coisa não explicitamente reconhecida como
leitura é tratada como escrita e bloqueada.

**Ainda em aberto (risco residual, menor):** se o Coletor precisar
futuramente navegar por um endpoint de leitura legítimo fora do padrão
`query`/`get`/`select`, o guard o bloqueará incorretamente (falso
positivo) — isso quebra a coleta (`IServiceUnavailableEx`) em vez de
comprometer a garantia de somente leitura, o que é o comportamento seguro
por padrão. Mapeamento completo de endpoints do iService continua sendo
valor futuro para reduzir esse risco de falso positivo, mas não é mais um
bloqueador para a garantia de segurança de RF-013.

### DP-013.2 — ~~Teste automatizado do guard em ambiente de integração~~ ✅ Resolvida (2026-09-02)

**Situação original:** Não estava claro se existia um teste automatizado
que validasse o comportamento do guard tentando disparar uma requisição
de escrita e verificando que ela é bloqueada.

**Resolução:** `IsWriteRequestOnIService` foi tornado `public static`
(sem dependência de Playwright) e coberto por 24 testes unitários em
`IServiceCollectorServiceReadOnlyGuardTests.cs` (ver seção "Implementação
e validação" acima). Não é um teste de integração com navegador real
(não há Playwright real disparando a requisição contra o interceptador de
rotas), mas cobre exaustivamente a lógica de classificação que decide o
que é bloqueado — a parte do guard com maior risco de regressão silenciosa
em uma refatoração futura.

**Residual:** um teste de integração completo (Playwright real,
verificando que `route.AbortAsync` é de fato chamado e a requisição não
chega à rede) não foi criado — não há infraestrutura de teste com browser
no projeto hoje. Se desejado no futuro, seria um teste adicional, não uma
substituição do que já existe.

## Fora do escopo

- Controle de escrita na base de dados interna do ATUA (MongoDB).
- Restrições de acesso de outros componentes ao iService.
- Mecanismo de autenticação e autorização CAS (RF-009).
