# RF-023 - Persistência e Reuso de Sessão do Provedor

Status: `Especificado`

**Data:** 2026-09-12

## Contexto e motivação

Durante a POC, a sessão CAS com o iService era mantida em memória para
evitar login repetido a cada ciclo de coleta. O **ADR-022** havia
postergado formalmente a persistência dessa sessão no MVP, sob a premissa
de baixo volume de tenants.

O usuário confirmou que essa premissa não se sustenta: com mais tenants
utilizando o ATUA, múltiplos logins simultâneos ou repetidos contra o
mesmo provedor podem gerar rate-limit, bloqueio, ou ruído indesejado
observável pelo próprio provedor (Midea/iService). Persistir e reutilizar
a sessão do provedor é, portanto, requisito real do MVP — não otimização
futura. Esta decisão está formalizada em **ADR-028**.

## Objetivo

Definir como o Worker Coletor persiste o estado de sessão do provedor
(cookies/`storage_state` do Playwright) para reutilização entre ciclos de
coleta, evitando login CAS desnecessário, e como essa sessão é invalidada
e renovada quando deixa de ser válida.

## Usuário / Ator

- **Worker Coletor** (`apps/collector`): único produtor e consumidor da
  sessão persistida.

## Escopo

RF-023 cobre exclusivamente a entidade `provider_sessions` e as regras de
criação, reuso, invalidação e renovação da sessão. Não cobre:

- o registro de tentativas de login como interação auditável (RF-022,
  `interaction_type = "login"`);
- cifra do conteúdo de `storage_state` (fora de escopo do MVP — ver
  ADR-028, item 4: melhoria futura não bloqueante);
- gestão de múltiplas sessões simultâneas para o mesmo `(tenant_id,
  provider_type)` (fora de escopo — um documento por chave).

## Entidade: `provider_sessions`

```
provider_sessions {
  tenant_id:      UUID
  provider_type:  string (ex.: "iservice")
  storage_state:  documento — cookies e origins do Playwright BrowserContext
                  (equivalente ao retorno de `context.storage_state()`)
  valid:          bool — se a sessão é considerada utilizável no momento
  expires_at:     timestamp UTC — expiração estimada (TTL conservador,
                  valor inicial a calibrar por observação real; ver
                  DP-023.1)
  created_at:     timestamp UTC — instante do login que originou esta sessão
  updated_at:     timestamp UTC — instante da última atualização deste documento
}
```

Chave natural: `(tenant_id, provider_type)`. Documento único e mutável —
sobrescrito a cada novo login bem-sucedido ou invalidação, não é
append-only.

## Requisitos funcionais

### RF-023.1 - Reuso de sessão válida antes de login

No início de cada ciclo de coleta, antes de iniciar o fluxo de login CAS,
o Worker deve buscar em `provider_sessions` um documento para
`(tenant_id, provider_type)` com `valid = true` e `expires_at` no futuro.
Se existir, o Worker deve tentar hidratar o `BrowserContext` do Playwright
com o `storage_state` salvo, em vez de executar o login CAS.

### RF-023.2 - Validação barata da sessão reutilizada

Após hidratar o contexto com uma sessão salva, o Worker deve validar que
ela ainda é aceita pelo provedor com uma checagem de baixo custo (ex.:
acessar uma rota autenticada conhecida e confirmar ausência de redirect
para a tela de login), antes de prosseguir com a coleta. Se a checagem
falhar, a sessão é tratada como inválida (RF-023.4) e o Worker segue para
login CAS normal.

### RF-023.3 - Persistência após login bem-sucedido

Sempre que o Worker completar um login CAS com sucesso — seja porque não
havia sessão salva, seja porque a sessão salva falhou na validação —, ele
deve capturar o `storage_state` resultante e fazer upsert em
`provider_sessions` para aquela `(tenant_id, provider_type)`, com
`valid = true`, `expires_at` recalculado e `updated_at` atualizado.

### RF-023.4 - Invalidação em falha de requisição autenticada

Se qualquer requisição autenticada durante o ciclo de coleta falhar por
motivo de sessão (ex.: HTTP 401, ou redirect inesperado para a tela de
login), o Worker deve marcar o documento correspondente em
`provider_sessions` como `valid = false` e executar login CAS uma única
vez dentro do mesmo ciclo, antes de desistir e reportar falha do comando.

### RF-023.5 - Um documento por par tenant/provedor

`provider_sessions` mantém no máximo um documento por
`(tenant_id, provider_type)`. Um novo login sempre sobrescreve
(`upsert`) o documento existente para essa chave — não gera histórico.

### RF-023.6 - Vedação de exposição da sessão fora do Worker

O conteúdo de `storage_state` não deve ser incluído em nenhum log, em
`provider_interactions` (RF-022.7), nem no log de diagnóstico local
`.txt` (Development-only) descrito em requisito de diagnóstico do
Collector. É acessível apenas ao processo do Worker que a gravou/lê.

## Regras de negócio

| Número   | Regra                                                                                                                       |
|----------|--------------------------------------------------------------------------------------------------------------------------|
| RN-023.1 | O Worker tenta reusar sessão salva válida antes de executar login CAS, para todo `(tenant_id, provider_type)`.            |
| RN-023.2 | Uma sessão reutilizada é validada com checagem barata antes do uso; falha na checagem invalida a sessão.                  |
| RN-023.3 | Todo login CAS bem-sucedido resulta em upsert de `provider_sessions` para a chave correspondente.                         |
| RN-023.4 | Falha de requisição autenticada por motivo de sessão invalida o documento e aciona login CAS único dentro do mesmo ciclo. |
| RN-023.5 | Existe no máximo um documento de sessão por `(tenant_id, provider_type)`.                                                  |
| RN-023.6 | `storage_state` nunca aparece em log de diagnóstico local nem em `provider_interactions`.                                 |

## Critérios de aceite

1. Dado que existe uma sessão válida e não expirada para
   `(tenant_id, provider_type)`, quando o Worker iniciar um ciclo de
   coleta, então ele deve hidratar o contexto com a sessão salva e não
   deve gerar um novo documento `provider_interactions` do tipo `login`
   nesse ciclo.

2. Dado que a sessão salva falha na checagem de validação (ex.: redirect
   para tela de login), quando o Worker detectar isso, então deve marcar
   a sessão como inválida, executar login CAS, e persistir a nova sessão
   resultante.

3. Dado que não existe sessão salva para `(tenant_id, provider_type)`,
   quando o Worker executar login CAS com sucesso, então deve ser criado
   um novo documento em `provider_sessions` com `valid = true`.

4. Dado que uma requisição autenticada retorna 401 no meio do ciclo,
   quando o Worker tratar essa falha, então a sessão correspondente deve
   ser marcada como `valid = false` e um novo login CAS deve ser tentado
   uma única vez antes de reportar falha do comando.

5. Dado um documento em `provider_sessions`, quando qualquer log de
   diagnóstico ou documento de `provider_interactions` for inspecionado,
   então nenhum deles deve conter o conteúdo de `storage_state`.

## Decisões pendentes

### DP-023.1 - Valor inicial de TTL (`expires_at`)

Não há dado empírico da duração real de uma sessão CAS aceita pela Midea
no momento deste requisito. Um valor conservador deve ser escolhido para
a primeira implementação (Fase 2 do plano de implementação) e ajustado
por observação real em produção — não é uma decisão arquitetural (não
requer nova ADR), apenas parametrização a validar operacionalmente.

### DP-023.2 - Cifra de `storage_state`

Registrada em ADR-028 (item 4) como melhoria futura não bloqueante,
sujeita aos critérios de revisão já definidos em ADR-022 (volume de
tenants, sinal de rate-limit, expansão do modelo de ameaça).
