# RF-009 - Coleta Inicial

Status: `Entregue (lado API) — Worker e D7 pendentes`

**Data de entrega (lado API):** 2026-08-30, commit `44fdbef`.

## Objetivo

Executar a primeira coleta real de ordens de servico de um tenant no iService a
partir do `ImmediateCollectionCommand` criado pela ativacao (RF-008), registrando
o estado atual e exatamente uma observacao inicial de cada OS retornada, e
iniciando o historico observado pelo ATUA (RF-010).

## Escopo

O RF-009 e o gate do Coletor: e nele que passa a existir acesso real ao iService.

O fluxo abrangido e:

```
claim -> autenticacao CAS -> consulta dos 5 status suportados ->
enriquecimento das OS Designadas -> persistencia (estado atual +
observacao inicial por OS) -> complete (Succeeded | Failed | Cancelled)
```

O Office reflete o estado do comando em tempo real durante todo o processo.

### O que e entregue (lado API — 2026-08-30)

- **Endpoints implementados e testados:**
  - `POST /api/internal/collector/commands/claim`: transita comando de `Pending` a `Claimed`, decifra credenciais do iService via `ICredentialCipher` (variante B do D9), retorna credenciais em claro protegidas por TLS, incrementa `AttemptCount`, preenche `ClaimedAtUtc` e `ClaimExpiresAtUtc` (D2: 30 minutos). Cuida atomicamente de comando `Claimed` expirado.
  - `POST /api/internal/collector/commands/{commandId}/complete`: transita comando para `Succeeded`, `Failed` ou `Cancelled`, aciona `ReconcileEligibilityAsync` apenas quando `failureReason = CredentialRejected` (D4), implementa idempotência.

- **BackgroundService `ClaimTimeoutJob`:** executa a cada 5 minutos, transita comandos `Claimed` com `ClaimExpiresAtUtc < now` para `Failed` com razão `ClaimTimeout`.

- **Configuração não hardcoded:**
  - `ImmediateCollectionOptions.ClaimTimeoutMinutes` (default 30, decisão D2).
  - `ImmediateCollectionOptions.HistoryWindowMonths` (default 3, decisões D1/D6).

- **Segurança — Variante B do D9:**
  - A API é o único componente que acessa a chave mestra (via `ICredentialCipher.UnwrapDataKey`).
  - Credenciais são decifradas em memória no `claim` e entregues em claro ao Worker via TLS.
  - Worker **não recebe DEK nem chave mestra**, e **não acessa** tabela `IServiceCredentials`.
  - DEK é zerada da memória da API imediatamente após decifragem.
  - Middleware de logging filtra o body da resposta do `claim`.

- **Decisões D3 (1 tentativa) e D4 (CredentialRejected):** implementadas.
  - D3: sem retentativas automáticas; falha resulta em `Failed`; reativação é manual.
  - D4: apenas `ECommandFailureReason.CredentialRejected` invalida credencial e dispara `ReconcileEligibilityAsync`.

- **Controle de escopo por tenant:** movido de `ServiceCredentialAuthenticationHandler` para policies de authorization. 6 testes de isolamento comprovam que cross-scope é impossível.

- **Tratamento de concorrência:** `DbUpdateConcurrencyException` no `claim` é capturada; o Worker perdedor da corrida recebe `204 No Content`, não `500`.

- **Testes:** 148 testes passando, build sem avisos. QA (`qa-engineer`) aprovou.

### O que NAO e RF-009 (ainda pendente — lado Worker e ambiente iService)

- Implementação do loop de polling no Worker (`apps/collector`).
- Acesso real ao iService (scraping via Playwright, autenticação CAS, consulta do iService).
- Paginação, throttling, backoff e tratamento de erro do Worker.
- Persistência efetiva em MongoDB (Worker invocando inserts).
- D7 (chave de identidade da OS: `workOrderNo` vs `workOrderId`) — segue pendente de descoberta do iService real.

### O que NAO e RF-009

- Coleta recorrente (RF-011).
- Atualizacao de estado de OS ja existente (RF-011).
- Interpretacao de ausencia de OS (RF-012).
- Escrita no iService (RF-013).
- Exibicao de OS na interface do Office (RF futuro).

### Fronteira critica com RF-010

As datas que o iService retorna sobre a OS (por exemplo, data de abertura) sao
metadados da OS no provedor, **nao observacoes historicas do ATUA**. O historico
registrado pelo ATUA comeca no `capturedAt` da coleta inicial. Essa distincao nao
estava articulada na documentacao existente e esta sendo definida neste requisito.

## Requisitos funcionais

### RF-009.1 - Acionamento via claim

A coleta inicial somente pode ser iniciada pelo Worker via `claim` do
`ImmediateCollectionCommand` criado na ativacao (RF-008). Nao existe atalho
direto pelo Office.

### RF-009.2 - Verificacao de elegibilidade antes do acesso ao iService

Imediatamente antes de qualquer acesso ao iService, o Worker deve verificar a
elegibilidade vigente do tenant. Se a elegibilidade for negada, o comando deve
ser concluido como `Cancelled` sem que nenhuma coleta seja realizada.

### RF-009.3 - Status suportados

Somente os cinco status suportados sao coletados. OS do iService em outros status
sao ignoradas sem geracao de erro.

Os cinco status suportados sao:

1. Designado
2. Em Processamento
3. Pendente
4. Concluido
5. Cancelado

Para OS no status **Designado**, o Worker deve realizar enriquecimento adicional
via `queryOneWorkOrder` para obter dados complementares.

### RF-009.4 - Modo somente leitura

O Worker opera exclusivamente no modo de leitura. Nenhuma escrita, nenhuma
atualizacao e nenhuma interacao que altere dados no iService e permitida. Esse
bloqueio nao e configuravel.

### RF-009.5 - Identidade das OS

Cada OS possui:

- `providerOrderId`: identificador externo proveniente do iService.
- UUID interno do ATUA (UUIDv7): gerado pelo proprio sistema e mantido separado
  do identificador externo.

Os dois identificadores nao devem ser misturados nem usados como substitutos um
do outro.

### RF-009.6 - Protecao de credenciais

Credenciais do iService e dados de sessao CAS sao **proibidos** em:

- logs;
- eventos de dominio;
- documentos do MongoDB;
- payloads de comunicacao interna;
- respostas de API.

### RF-009.7 - Retencao de dados

OS sao preservadas enquanto a conta do tenant estiver ativa. Apos cinco anos de
inatividade da conta ou mediante solicitacao formal, os dados devem ser removidos.

### RF-009.8 - Referencia temporal

O campo `capturedAt` registra o instante exato da coleta no Worker. Esse instante
e a referencia historica do ATUA para aquela OS. Nao e permitido retroagir datas
nem fabricar observacoes anteriores ao `capturedAt`.

### RF-009.9 - Idempotencia da persistencia

O mesmo `commandId` ou o mesmo `providerOrderId` nao devem gerar documentos
duplicados no MongoDB. A persistencia deve ser idempotente.

### RF-009.10 - Exclusividade do claim

Um comando no estado `Claimed` pertence exclusivamente ao Worker que o
reivindicou. Nenhum outro Worker ou processo pode processar ou reivindicar o
mesmo comando enquanto ele estiver nesse estado.

## Regras de negocio

| Numero    | Regra                                                                                                              |
|-----------|--------------------------------------------------------------------------------------------------------------------|
| RN-009.1  | Coleta so comeca via `claim` de um `ImmediateCollectionCommand`; sem atalho pelo Office.                          |
| RN-009.2  | Elegibilidade verificada imediatamente antes do acesso ao iService; negativa resulta em `Cancelled` sem coleta.   |
| RN-009.3  | Somente os 5 status suportados sao coletados; outros status do iService sao ignorados.                            |
| RN-009.4  | Modo estritamente somente leitura; bloqueio nao e configuravel.                                                    |
| RN-009.5  | OS possui `providerOrderId` externo e UUIDv7 interno do ATUA, mantidos separados.                                 |
| RN-009.6  | Credenciais e sessao CAS sao proibidas em logs, eventos, MongoDB, payloads e respostas.                           |
| RN-009.7  | OS preservadas enquanto conta ativa; removidas apos 5 anos de inatividade ou solicitacao formal.                  |
| RN-009.8  | `capturedAt` e o instante de referencia historica; retroatividade e fabricacao de datas sao vedadas.             |
| RN-009.9  | Persistencia idempotente: mesmo `commandId` ou `providerOrderId` nao gera duplicatas.                             |
| RN-009.10 | Comando `Claimed` pertence exclusivamente ao Worker que o reivindicou.                                            |

## Criterios de aceite

1. Dado um `ImmediateCollectionCommand` no estado `Pending`, quando o Worker
   realizar o `claim`, entao o comando deve transitar para `Claimed` e nenhum
   outro Worker deve poder reivindicar o mesmo comando.

2. Dado um comando `Claimed`, quando a elegibilidade do tenant for negada antes
   do acesso ao iService, entao o comando deve ser concluido como `Cancelled` sem
   que qualquer OS seja consultada ou persistida.

3. Dado um comando `Claimed` com elegibilidade valida, quando o Worker consultar
   o iService, entao somente OS nos cinco status suportados devem ser processadas;
   OS em outros status devem ser ignoradas sem erro.

4. Dado um comando `Claimed` com elegibilidade valida, quando o Worker processar
   OS no status Designado, entao deve realizar enriquecimento adicional via
   `queryOneWorkOrder` antes de persistir.

5. Dado que o Worker persistira uma OS, quando a persistencia for executada,
   entao deve ser gravado exatamente um documento de estado atual e exatamente uma
   observacao inicial com `capturedAt`, sem retroagir datas.

6. Dado que o mesmo `providerOrderId` ja existe no MongoDB para o tenant, quando
   o Worker tentar persistir novamente, entao nenhuma duplicata deve ser criada.

7. Dado que a coleta foi concluida com sucesso, quando o Worker chamar `complete`,
   entao o comando deve transitar para `Succeeded`.

8. Dado que ocorreu falha irrecuperavel durante a coleta, quando o Worker chamar
   `complete`, entao o comando deve transitar para `Failed`.

9. Dado qualquer etapa da coleta, quando credenciais ou dados de sessao CAS
   estiverem presentes, entao eles nao devem aparecer em logs, eventos, MongoDB,
   payloads ou respostas.

10. Dado um usuario autorizado consultando o Office durante a coleta, quando o
    estado do comando mudar, entao o Office deve refletir o estado atualizado em
    tempo real, sem expor segredos.

## Decisoes pendentes

> Apenas **D7** segue pendente de descoberta. As demais decisões (D1–D6, D8, D9) foram resolvidas e implementadas conforme ADR-021 (2026-08-30).

---

### D7 - Qual identificador externo do iService e a chave de identidade da OS?

**Situacao:** A documentacao menciona `workOrderNo` e `workOrderId` (ADR-004,
mvp-onboarding pendencias) e registra incerteza sobre a estabilidade desses
identificadores em reaberturas, reatribuicoes ou alteracoes no iService.

**Impacto se nao decidido:** se o identificador escolhido nao for estavel, a
coleta pode criar duplicatas ou perder o historico de uma OS reaberta.

**Recomendacao da analista:** confirmar com o usuario qual dos dois (`workOrderNo`
ou `workOrderId`) e a chave primaria de identidade da OS no iService para o MVP,
e qual e o comportamento esperado em caso de reabertura. A descoberta via
Playwright (ADR-004) deve validar isso antes da implementacao.

**Aguarda:** decisao do usuario.

---

## Decisoes resolvidas (referência histórica)

**Situacao:** A documentacao indica "estado atual de todas as OS retornadas nos
status suportados", o que implica ausencia de filtro de data. Para um cliente com
anos de historico no iService, isso pode retornar dezenas de milhares de OS na
primeira coleta.

**Impacto se nao decidido:** o Worker nao sabe se deve aplicar filtro de data.

**Recomendacao da analista:** confirmar que a coleta inicial busca **todas as OS
retornadas pelo iService nos 5 status, sem filtro de data**, e que a paginacao e
tratada pelo Worker.

**Aguarda:** decisao do usuario.

---

### D2 - Timeout de re-claim: o que acontece se o Worker morrer com o comando em `Claimed`?

**Situacao:** ADR-020 define a maquina de estados mas nao especifica timeout. Um
comando `Claimed` orfao bloqueia indefinidamente a integracao, pois o indice
parcial impede novo `Pending`.

**Impacto se nao decidido:** o tenant fica travado e o Agente Coletor nunca
conclui a coleta inicial.

**Recomendacao da analista:** definir um **timeout de `Claimed`** (sugestao: 30
minutos). Apos o timeout, a Master API ou um job de reconciliacao transita o
comando para `Failed`, permitindo nova ativacao. A escolha entre polling e job e
decisao de arquitetura.

**Aguarda:** decisao do software-architect.

---

### D3 - Quantas tentativas sao permitidas para o Worker (`AttemptCount`)?

**Situacao:** A entidade `ImmediateCollectionCommand` ja possui `AttemptCount`
(ADR-020/RF-008), mas o numero maximo de tentativas nao foi definido.

**Impacto se nao decidido:** sem limite, o Worker pode entrar em loop; com limite
muito baixo, uma falha transitoria cancela definitivamente a coleta inicial.

**Recomendacao da analista:** **1 tentativa** para a coleta inicial (e um comando
unico, nao recorrente). Falha resulta em `Failed`; o cliente pode desativar e
reativar para tentar novamente. O numero exato e decisao de produto.

**Aguarda:** decisao do usuario.

---

### D4 - Ao concluir com `Failed`, a Master API deve desativar automaticamente o Agente?

**Situacao:** Se a credencial foi rejeitada definitivamente pelo iService durante
a coleta, o `ValidationStatus` deveria ser alterado para `Failed`, o que
acionaria `ReconcileEligibility` e desativaria o Agente. Porem, `ReconcileEligibility`
nao esta implementado (registrado como prioridade posterior em ADR-020/RF-008).

**Impacto se nao decidido:** o Agente pode permanecer `Active` com coleta em
estado `Failed`, criando inconsistencia operacional visivel no Office.

**Recomendacao da analista:** uma conclusao com `Failed` por credencial rejeitada
**deve** invalidar o `ValidationStatus` e acionar desativacao por
`CredentialNotValidated`. Isso exige que `ReconcileEligibility` seja implementado
como parte de RF-009, nao como prioridade posterior.

**Aguarda:** decisao do software-architect.

---

### D5 - Dados pessoais de tecnicos e clientes finais nas OS: ha LGPD aplicavel?

**Situacao:** OS do iService podem conter nome do tecnico, nome do cliente final,
endereco e telefone. O produto nao definiu se esses dados podem ser persistidos
no MongoDB ou se devem ser omitidos ou pseudonimizados.

**Impacto se nao decidido:** se persistidos sem tratamento, ha risco de
conformidade com a LGPD. Se omitidos, o ATUA perde informacoes operacionais
relevantes.

**Recomendacao da analista:** definir explicitamente quais campos das OS **podem**
ser persistidos no MVP. Sugestao minima: identificadores externos, status e
`capturedAt`, sem PII de tecnico ou cliente final ate que a base legal seja
definida.

**Aguarda:** decisao do usuario.

---

### D6 - Ha limite de OS por coleta inicial?

**Situacao:** A documentacao `mvp-onboarding` registra como pendencia o
comportamento acima de 10.000 OS por status. O produto nao definiu se existe um
teto.

**Impacto se nao decidido:** sem limite, uma coleta inicial de um cliente grande
pode durar horas, consumir memoria excessiva e potencialmente sobrecarregar o
iService do cliente.

**Recomendacao da analista:** definir um **limite de OS por coleta no MVP**
(sugestao: 1.000 OS por status, 5.000 no total). Se excedido, a coleta deve ser
concluida com as OS obtidas ate o limite, registrando uma nota de truncamento, sem
gerar erro. O limite exato e decisao de produto.

**Aguarda:** decisao do usuario.

---

### D7 - Qual identificador externo do iService e a chave de identidade da OS?

**Situacao:** A documentacao menciona `workOrderNo` e `workOrderId` (ADR-004,
mvp-onboarding pendencias) e registra incerteza sobre a estabilidade desses
identificadores em reaberturas, reatribuicoes ou alteracoes no iService.

**Impacto se nao decidido:** se o identificador escolhido nao for estavel, a
coleta pode criar duplicatas ou perder o historico de uma OS reaberta.

**Recomendacao da analista:** confirmar com o usuario qual dos dois (`workOrderNo`
ou `workOrderId`) e a chave primaria de identidade da OS no iService para o MVP,
e qual e o comportamento esperado em caso de reabertura. A descoberta via
Playwright (ADR-004) deve validar isso antes da implementacao.

**Aguarda:** decisao do usuario.

---

### D8 — Mesma OS em múltiplos status

**Resolvido:** desempate por prioridade de status (Designado > Em Processamento > Pendente > Concluído > Cancelado). Uma única observação inicial por OS por comando. Ver ADR-021, seção D8.

### D9 — Entrega de credenciais ao Worker

**Resolvido:** variante B (API decifra, entrega credenciais em claro via TLS, Worker não recebe DEK nem chave mestra, não acessa tabela `IServiceCredentials`). Ver ADR-021, seção D9.

---

## Dependencias

- RF-008 (implementado): cria o `ImmediateCollectionCommand` consumido por este
  requisito.
- ADR-020: define a máquina de estados do comando; endpoints `claim` e `complete`
  implementados (commit `44fdbef`).
- ADR-021: resolve todas as decisões arquiteturais e de segurança de RF-009
  (D1–D6, D8, D9). Status: `Aceita` (2026-08-30). D7 pendente de descoberta.
- ADR-003: define Worker Service e isolamento por tenant.
- ADR-004: define credenciais cifradas e KMS; mecanismo de entrega ao Worker
  implementado (variante B em ADR-021, commit `44fdbef`).
- ADR-017: credencial de serviço interno; limites do contrato de elegibilidade.
- RF-010: histórico de observações; a fronteira entre metadados do iService e
  observações do ATUA está definida na seção de escopo deste documento.

## Impactos

### Lado API — ✅ Implementado (commit `44fdbef`)

- ✅ Endpoints `claim` e `complete` implementados na Master API.
- ✅ Decifragem de credenciais no `claim` via variante B (API decifra, Worker não).
- ✅ BackgroundService de timeout de claim a cada 5 minutos.
- ✅ Acesso ao `ReconcileEligibilityAsync` quando credencial é rejeitada.
- ✅ Controle de escopo por tenant via policies de authorization.
- ✅ Tratamento de concorrência na transição `Pending → Claimed`.

### Lado Worker — ⏳ Pendente

- ⏳ Implementação do loop de polling no Worker (`apps/collector`).
- ⏳ Acesso real ao iService (scraping via Playwright, autenticação CAS).
- ⏳ Paginação, throttling, backoff e tratamento de erro.
- ⏳ Persistência em MongoDB.

### Dependência pendente

- ⏳ D7 (identificador externo da OS: `workOrderNo` vs `workOrderId`) — descoberta do iService real.

## Fora do escopo

- Coleta recorrente (RF-011).
- Atualização de OS já existentes (RF-011).
- Interpretação de ausência de OS (RF-012).
- Escrita no iService (RF-013).
- Exibição de OS no Office (RF futuro).

## Gate de implementação

**Lado API:** ✅ Implementado. Sem bloqueadores restantes.

**Lado Worker:** bloqueado por:

1. ⏳ D7 (chave de identidade da OS) — descoberta do iService real.
2. ⏳ Implementação do loop de polling no Worker com acesso efetivo ao iService.
