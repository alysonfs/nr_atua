# RF-009 - Coleta Inicial

> **⚠️ Atenção — atualização parcial (2026-08-31):**
> Os subrequisitos RF-009.5, RF-009.8 e RF-009.9 e a regra RN-009.9 foram
> substituídos pelo redesenho do modelo de dados de OS (2026-08-31).
> O modelo de persistência do Worker passa a ser definido em **RF-016** e
> **RF-017**. As seções afetadas deste documento foram marcadas com
> `[SUBSTITUÍDO — ver RF-016/RF-017]`. Todo o restante permanece vigente.

Status: `Entregue (lado API) — D7 resolvido; Worker pendente`

**Data de entrega (lado API):** 2026-08-30, commit `44fdbef`.

## Objetivo

Executar a primeira coleta real de ordens de servico de um tenant no iService a
partir do `ImmediateCollectionCommand` criado pela ativacao (RF-008), registrando
o dado bruto de cada OS retornada (RF-016) e iniciando o histórico agnóstico de
provedor (RF-017).

## Escopo

O RF-009 e o gate do Coletor: e nele que passa a existir acesso real ao iService.

O fluxo abrangido e:

```
claim -> autenticacao CAS -> consulta dos 5 status suportados ->
enriquecimento das OS Designadas -> persistencia (snapshot bruto + registro
em work_order / work_order_history — ver RF-016 e RF-017) ->
complete (Succeeded | Failed | Cancelled)
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

### O que NAO e RF-009 (ainda pendente — lado Worker)

- Implementação do loop de polling no Worker (`apps/collector`).
- Acesso real ao iService (scraping via Playwright, autenticação CAS, consulta do iService).
- Paginação, throttling, backoff e tratamento de erro do Worker.
- Persistência efetiva em MongoDB (Worker invocando inserts — ver RF-016 e RF-017).

### O que NAO e RF-009

- Coleta recorrente (RF futuro).
- Atualizacao de estado de OS ja existente (RF futuro).
- Interpretacao de ausencia de OS (RF-012).
- Escrita no iService (RF-013).
- Exibicao de OS na interface do Office (RF futuro).

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

### RF-009.5 - Identidade das OS `[SUBSTITUÍDO — ver RF-016]`

> Este subrequisito foi substituído pelo redesenho do modelo de dados
> (2026-08-31). A identidade das OS agora é definida em RF-016.

~~Cada OS possui:~~
~~- `providerOrderId`: identificador externo proveniente do iService.~~
~~- UUID interno do ATUA (UUIDv7): gerado pelo proprio sistema e mantido separado do identificador externo.~~

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

### RF-009.8 - Referencia temporal `[SUBSTITUÍDO — ver RF-016]`

> Este subrequisito foi substituído pelo redesenho do modelo de dados
> (2026-08-31). A referência temporal agora é tratada via `created_at` das
> entidades definidas em RF-016 e RF-017.

~~O campo `capturedAt` registra o instante exato da coleta no Worker. Esse instante~~
~~e a referencia historica do ATUA para aquela OS. Nao e permitido retroagir datas~~
~~nem fabricar observacoes anteriores ao `capturedAt`.~~

### RF-009.9 - Idempotencia da persistencia `[SUBSTITUÍDO — ver RF-016]`

> Este subrequisito foi substituído pelo redesenho do modelo de dados
> (2026-08-31). As regras de idempotência agora são definidas em RF-016.

~~O mesmo `commandId` ou o mesmo `providerOrderId` nao devem gerar documentos~~
~~duplicados no MongoDB. A persistencia deve ser idempotente.~~

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
| RN-009.5  | **[SUBSTITUÍDO — ver RF-016]** ~~OS possui `providerOrderId` externo e UUIDv7 interno do ATUA, mantidos separados.~~ |
| RN-009.6  | Credenciais e sessao CAS sao proibidas em logs, eventos, MongoDB, payloads e respostas.                           |
| RN-009.7  | OS preservadas enquanto conta ativa; removidas apos 5 anos de inatividade ou solicitacao formal.                  |
| RN-009.8  | **[SUBSTITUÍDO — ver RF-016]** ~~`capturedAt` e o instante de referencia historica; retroatividade e fabricacao de datas sao vedadas.~~ |
| RN-009.9  | **[SUBSTITUÍDO — ver RF-016]** ~~Persistencia idempotente: mesmo `commandId` ou `providerOrderId` nao gera duplicatas.~~ |
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
   entao deve ser gerado um novo snapshot bruto em `work_order_snapshots` e
   atualizado o registro em `work_order` e `work_order_history` conforme RF-016
   e RF-017.

6. Dado que a coleta foi concluida com sucesso, quando o Worker chamar `complete`,
   entao o comando deve transitar para `Succeeded`.

7. Dado que ocorreu falha irrecuperavel durante a coleta, quando o Worker chamar
   `complete`, entao o comando deve transitar para `Failed`.

8. Dado qualquer etapa da coleta, quando credenciais ou dados de sessao CAS
   estiverem presentes, entao eles nao devem aparecer em logs, eventos, MongoDB,
   payloads ou respostas.

9. Dado um usuario autorizado consultando o Office durante a coleta, quando o
   estado do comando mudar, entao o Office deve refletir o estado atualizado em
   tempo real, sem expor segredos.

## Decisoes pendentes

> Todas as decisões (D1–D9) foram resolvidas conforme ADR-021 (2026-08-30). D7 foi resolvido em 2026-08-31 com evidência empírica real.

---

### D7 - Qual identificador externo do iService e a chave de identidade da OS?

**Status:** ✅ **Resolvido (2026-08-31)**

**Decisão:** A chave de identidade estável da OS é `workOrderId` (inteiro interno do iService, ex.: `101538111`), **NÃO** `workOrderNo` (string de documento, ex.: `"BRWO260821681"`).

**Evidência:**
- Dataset: ~100 capturas reais de produção da POC anterior (iService, tenant real, datas 25-26/08/2026).
- Fonte: endpoint `queryWorkOrder` do iService, snapshots armazenados em `rag/iservice/automated-login-ics-amer-robot/robots/ics-amer/output/*.json`.
- Amostra: 23 `workOrderId` distintos rastreados ao longo de dezenas de capturas sucessivas (a cada 15 minutos).
- Resultado: zero instabilidade observada — o par `workOrderId ↔ workOrderNo` **nunca mudou** ao longo da série temporal.

**Motivo:**
- `workOrderId` é a chave primária do sistema legado (iService), garantindo imutabilidade enquanto a OS existir.
- `workOrderNo` é numeração de documento derivada, sujeita a regras administrativas de regeneração em outros ERPs — padrão de risco conhecido (não confirmado neste caso específico, mas evitado por precaução).

**Limitação documentada:**
- O dataset analisado cobre apenas o status `assigned` (não foi testada transição em vivo ou reabertura de OS durante a captura).
- A decisão foi tomada com o nível de confiança disponível; não é 100% validada para o caso de reabertura de OS com mudança de status.
- Validação completa em reabertura fica para ciclo futuro se necessário.

**Implementação:**
- O mapeamento de `workOrderId` para `provider_id` da OS é feito pelo Worker; o isolamento desse mapeamento é definido em RF-016.
- Suíte de testes: 165/165 passando após implementação anterior (nota: código de persistência anterior — `WorkOrderMapper`, `WorkOrderSnapshotDocument`, `WorkOrderObservationDocument`, `WorkOrderRepository` — será substituído pela implementação de RF-016 e RF-017).

---

## Dependencias

- RF-008 (implementado): cria o `ImmediateCollectionCommand` consumido por este
  requisito.
- ADR-020: define a máquina de estados do comando; endpoints `claim` e `complete`
  implementados (commit `44fdbef`).
- ADR-021: resolve todas as decisões arquiteturais e de segurança de RF-009
  (D1–D6, D8, D9). Status: `Aceita` (2026-08-30). D7 resolvido em 2026-08-31.
- ADR-003: define Worker Service e isolamento por tenant.
- ADR-004: define credenciais cifradas e KMS; mecanismo de entrega ao Worker
  implementado (variante B em ADR-021, commit `44fdbef`).
- ADR-017: credencial de serviço interno; limites do contrato de elegibilidade.
- **RF-016** (novo): define o modelo de persistência de snapshots brutos de OS.
- **RF-017** (novo): define o modelo de `work_order` e `work_order_history`
  agnóstico de provedor.

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
- ⏳ Persistência em MongoDB conforme RF-016 e RF-017.
- ⚠️ Código de persistência existente (`WorkOrderMapper`, `WorkOrderSnapshotDocument`,
  `WorkOrderObservationDocument`, `WorkOrderRepository`) será substituído pela
  implementação de RF-016 e RF-017.

## Fora do escopo

- Coleta recorrente (RF futuro).
- Atualização de OS já existentes (RF futuro).
- Interpretação de ausência de OS (RF-012).
- Escrita no iService (RF-013).
- Exibição de OS no Office (RF futuro).
- Mapeamento de campos do rawData em campos estruturados (RF futuro — ver RF-016).

## Gate de implementação

**Lado API:** ✅ Implementado. Sem bloqueadores restantes.

**Lado Worker:** bloqueado por:

1. ⏳ Implementação de RF-016 e RF-017 (modelo de persistência).
2. ⏳ Implementação do loop de polling no Worker com acesso efetivo ao iService.
