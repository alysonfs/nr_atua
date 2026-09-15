# Guia de validação manual — Fase 6 (provider_interactions)

**Objetivo:** validar manualmente, sem acionar o agente `qa-engineer`, os
quatro critérios da Fase 6 do
[PLANO-refactor-provider-interactions-collector.md](/Users/alysonfs/workspace/Software/natal-refrigeracao/atua/docs/implementation/PLANO-refactor-provider-interactions-collector.md):

1. Idempotência (reprocessar o mesmo `command_id`/interação não duplica dado).
2. Reuso de sessão do iService (evita login repetido).
3. Busca por data retorna o mesmo conjunto de OS que a busca por status trazia antes.
4. Resume token do Change Stream sobrevive a restart do Worker.

Este documento é um roteiro operacional: rode os passos você mesmo com o
Collector já em execução local (via depurador do VS Code), usando
**DBeaver** (Postgres RDS) e **Compass** (MongoDB Atlas) — nenhum script
Python é necessário. Traga de volta apenas os resultados indicados em
**"O que colar aqui para análise"** de cada passo.

## Pré-requisitos

- Conexão do DBeaver já configurada para o Postgres RDS do ATUA.
- Conexão do Compass já configurada para o MongoDB Atlas do ATUA,
  banco `atua`.
- Collector rodando localmente (depurador do VS Code), com
  `ApiBaseUrl=https://api.atyno.com.br` (confirme no log de startup).
- Acesso ao terminal para rodar `make collector-trigger-cycle` a partir de
  `infra/` (skill `atua-deploy`), com `AWS_PROFILE` configurado.

---

## Passo 0 — Estado inicial (baseline)

### No DBeaver (Postgres)

```sql
SELECT COUNT(*) AS total FROM work_orders;

SELECT COUNT(*) AS total FROM work_order_histories;

SELECT "ConsumerId", "ResumeToken", "UpdatedAt"
FROM consumer_states
ORDER BY "UpdatedAt" DESC;
```

### No Compass (MongoDB)

- Coleção `provider_sessions` → aba **Documents**, sem filtro, ver o
  contador de documentos no topo da lista (ou rode em "Aggregations"
  uma pipeline `[{ "$count": "total" }]`).
- Coleção `provider_interactions` → mesma checagem de contador/`$count`.

No Collector (log ao vivo): confirme que está ocioso (ciclos de
`ClaimAsync` retornando 204).

**O que colar aqui para análise:**
- Resultado das 3 queries SQL acima.
- Contagem de `provider_sessions` e `provider_interactions`.

---

## Passo 1 — Disparar um ciclo novo e observar reuso de sessão

1. A partir de `infra/`: `AWS_PROFILE=<seu profile> make collector-trigger-cycle`.
2. Observe o log do Collector até o ciclo terminar (`CompleteAsync` com
   `Succeeded`).
3. Procure no log, na ordem:
   - Uma linha de `ClaimAsync` retornando um comando (200, não 204) —
     anote o `CommandId`, ele é usado nos passos seguintes.
   - **Reuso de sessão**: procure por "sessão reaproveitada"/
     `storage_state` reutilizado, **sem** um novo login completo
     (usuário/senha enviados, página de login navegada) logo em seguida.
     Se aparecer um login novo, o critério 2 falhou neste ciclo.
   - Uma ou mais linhas de `list_query` (busca por data) com contagem de
     OS por página.
   - Linhas de `detail_query`, se houver.
   - Linhas `[CONSUMER] Interação processada` — uma por interação
     (`login` é ignorado pelo consumer, não gera linha de projeção).

**O que colar aqui para análise:**
- `CommandId` do ciclo disparado.
- As linhas de log da sessão (login novo vs. reaproveitada).
- Contagem de páginas/OS reportada pelo `list_query`.
- Todas as linhas `[CONSUMER] Interação processada` deste ciclo.
- Qualquer linha de erro/exception, se houver.

---

## Passo 2 — Conferir projeção completa (sem perda de OS)

### No Compass — total de OS únicas do ciclo

Coleção `provider_interactions` → aba **Aggregations**, monte esta
pipeline (substitua `SEU_COMMAND_ID` pelo valor anotado no Passo 1):

```json
[
  { "$match": {
      "command_id": "SEU_COMMAND_ID",
      "interaction_type": { "$in": ["list_query", "detail_query"] }
  }},
  { "$unwind": "$orders" },
  { "$group": {
      "_id": {
        "$toString": {
          "$ifNull": ["$orders.workOrderId", "$orders.id"]
        }
      }
  }},
  { "$count": "os_unicas" }
]
```

Isso dá o total de OS únicas (`workOrderId`, com fallback `id`) que o
ciclo retornou.

### No DBeaver — crescimento em Postgres

```sql
SELECT COUNT(*) AS total FROM work_orders;

SELECT COUNT(*) AS total FROM work_order_histories;
```

Compare com o baseline do Passo 0. O crescimento não precisa bater
exatamente com o total de OS do ciclo (é upsert — OS que já existiam de
ciclos anteriores não geram nova linha em `work_orders`, só atualizam),
mas **não pode ser zero ou muito menor** que o esperado se o `list_query`
reportou centenas de OS.

### No DBeaver — conferência por janela de tempo (proxy de completude)

Como não dá para cruzar IDs diretamente entre Mongo e Postgres via SQL
puro, use o horário do ciclo como proxy: confirme que `work_orders` foi
atualizado numa janela de tempo compatível com a duração observada do
ciclo no log (não deve haver um "corte" abrupto muito antes do fim do
ciclo, o que indicaria processamento parcial):

```sql
SELECT date_trunc('minute', "UpdatedAt") AS minuto, COUNT(*) AS registros
FROM work_orders
WHERE "UpdatedAt" >= now() - interval '30 minutes'
GROUP BY 1
ORDER BY 1;
```

**O que colar aqui para análise:**
- Resultado da pipeline do Compass (`os_unicas`).
- As duas contagens do DBeaver (`work_orders`, `work_order_histories`)
  comparadas com o baseline do Passo 0.
- Resultado da query de janela de tempo (`minuto`/`registros`) — sinalize
  se percebeu um corte abrupto antes do fim esperado do ciclo.

> **Se sobrar dúvida sobre completude** (por exemplo, a janela de tempo
> parece cortada), há uma checagem opcional mais rigorosa ao final deste
> documento, na seção **"Checagem opcional: diff de IDs Mongo × Postgres"**.

---

## Passo 3 — Teste de idempotência (reprocessar sem duplicar)

Este é o teste mais delicado — precisa forçar o consumer a reler uma
interação já processada.

1. Com o Collector **parado** (Ctrl+C, deixe finalizar limpo), no
   DBeaver, confirme o token atual salvo:

   ```sql
   SELECT "ConsumerId", "ResumeToken", "UpdatedAt"
   FROM consumer_states
   WHERE "ConsumerId" = 'provider-interaction-to-work-order';
   ```

2. No DBeaver, limpe manualmente o token do consumer, para forçar o
   Change Stream a reabrir sem `ResumeAfter` e reprocessar o histórico
   recente (⚠️ isto NÃO apaga nenhum dado de negócio, apenas o ponteiro
   de resume — é o mesmo comportamento já implementado para "token
   expirado" no `ProviderInteractionConsumerWorker`):

   ```sql
   UPDATE consumer_states
   SET "ResumeToken" = NULL
   WHERE "ConsumerId" = 'provider-interaction-to-work-order';
   ```

3. No DBeaver, rode e guarde como baseline deste passo (antes de
   reiniciar o Collector):

   ```sql
   SELECT COUNT(*) AS total FROM work_orders;
   SELECT COUNT(*) AS total FROM work_order_histories;
   ```

4. Reinicie o Collector local (novo `dotnet run`/relançar debug).
5. Observe o log: o Change Stream deve reabrir do início (ou de um ponto
   anterior) e reprocessar interações já vistas. Aguarde estabilizar
   (parar de gerar novas linhas `[CONSUMER] Interação processada`).
6. No DBeaver, rode as mesmas duas queries de novo.

**Critério de sucesso:** `work_orders` e `work_order_histories` **não
crescem** além do que já tinham (upsert idempotente) — ou crescem apenas
pela diferença de estado real (`woStatus` mudou desde a última leitura,
gerando 1 novo registro de histórico por OS que mudou, o que é esperado,
não é duplicação).

**O que colar aqui para análise:**
- Contagens antes/depois deste passo (`work_orders`, `work_order_histories`).
- Se `work_order_histories` cresceu, confirme se é por causa de status
  realmente diferente e cole o resultado desta query:

  ```sql
  SELECT "ProviderId", COUNT(*) AS registros
  FROM work_order_histories
  GROUP BY "ProviderId"
  HAVING COUNT(*) > 1
  ORDER BY 2 DESC
  LIMIT 20;
  ```

---

## Passo 4 — Resume token sobrevive a restart

1. Durante um ciclo de coleta com várias páginas em andamento (ou logo
   após o Passo 1/3), pare o Collector no meio do processamento
   (Ctrl+C) — o ideal é interromper **depois** de pelo menos uma linha
   `[CONSUMER] Interação processada`, mas **antes** do fim do ciclo.
2. No DBeaver, anote o token/horário atual:

   ```sql
   SELECT "ConsumerId", "ResumeToken", "UpdatedAt"
   FROM consumer_states
   WHERE "ConsumerId" = 'provider-interaction-to-work-order';
   ```

3. Reinicie o Collector.
4. Observe o log: deve haver uma linha indicando abertura do Change
   Stream com `ResumeToken` existente (não `ResumeAfter=None`/reabertura
   "do zero", a menos que o Passo 3 tenha limpado o token de propósito).
5. Confirme que o processamento continua das interações que faltavam,
   sem reprocessar tudo do zero (a menos que o critério de idempotência
   do Passo 3 já tenha sido testado separadamente).

**O que colar aqui para análise:**
- Linha de log de reabertura do Change Stream (mostrando se usou o token
  existente ou reabriu do zero).
- Contagens finais depois do restart (mesma query SQL do Passo 2), para
  confirmar que o ciclo completou.

---

## Passo 5 — Busca por data vs. busca por status antigo

Não temos mais o código antigo rodando em paralelo para comparar
diretamente. Validação indireta:

1. No log do Passo 1, confirme os parâmetros usados na nova busca:
   `creationDateFrom`, `creationDateTo`, `woStatus=""`,
   `woStatusCond="me"` (devem aparecer no log de request ao iService, se
   `EnableLocalDebugLog=true`).
2. Confirme que o total de OS retornado (soma de todas as páginas do
   `list_query`, ou o valor `os_unicas` da pipeline do Passo 2) é
   plausível para o período coberto por `HistoryWindowMonths` (1 mês,
   desde o commit `2cf8e05`).
3. No Compass, confira a distribuição de status retornada, comparando
   com o que se espera ver no portal do iService para o mesmo período:

   ```json
   [
     { "$match": {
         "command_id": "SEU_COMMAND_ID",
         "interaction_type": "list_query"
     }},
     { "$unwind": "$orders" },
     { "$group": {
         "_id": "$orders.woStatus",
         "total": { "$sum": 1 }
     }},
     { "$sort": { "total": -1 } }
   ]
   ```

4. Se possível, compare esse resultado com uma consulta manual no portal
   do iService filtrando por período e todos os status.

**O que colar aqui para análise:**
- Resultado da pipeline de distribuição por status (Compass).
- Se foi possível conferir no portal do iService, o número visto lá.
- Se NÃO for possível conferir (ex.: não há acesso ao portal agora),
  registre isso explicitamente — este critério fica como "não
  totalmente verificável neste ambiente", não como falha.

---

## Checagem opcional: diff de IDs Mongo × Postgres

Só é necessária se o Passo 2 deixar dúvida sobre completude (por exemplo,
corte abrupto na janela de tempo). É mais trabalhosa porque exige
comparar listas entre duas ferramentas diferentes — faça apenas se
necessário.

### No Compass — lista ordenada de IDs do ciclo

```json
[
  { "$match": {
      "command_id": "SEU_COMMAND_ID",
      "interaction_type": { "$in": ["list_query", "detail_query"] }
  }},
  { "$unwind": "$orders" },
  { "$group": {
      "_id": null,
      "ids": {
        "$addToSet": {
          "$toString": {
            "$ifNull": ["$orders.workOrderId", "$orders.id"]
          }
        }
      }
  }},
  { "$unwind": "$ids" },
  { "$sort": { "ids": 1 } },
  { "$replaceRoot": { "newRoot": { "ProviderId": "$ids" } } }
]
```

Exporte o resultado (Compass → botão **Export Collection**/**Export
Query Results** na tela de agregação, formato CSV) — chame de
`mongo_ids.csv`.

### No DBeaver — lista ordenada de IDs em Postgres

```sql
SELECT "ProviderId"
FROM work_orders
ORDER BY "ProviderId";
```

Exporte o resultado do grid (botão de exportar do DBeaver, formato CSV)
— chame de `pg_ids.csv`.

### Comparando os dois arquivos

Sem escrever script: abra os dois CSVs num editor de planilhas (Excel/
Google Sheets/Numbers) lado a lado e use uma fórmula de comparação
(`=CONT.SE`/`COUNTIF` de uma coluna contra a outra) para achar quais IDs
do Mongo não aparecem no Postgres. Ou, se preferir linha de comando sem
"script" (apenas comandos utilitários prontos do sistema, não código
customizado):

```bash
comm -23 <(sort mongo_ids.csv) <(sort pg_ids.csv)
```

Isso lista os IDs que estão em `mongo_ids.csv` mas não em `pg_ids.csv` —
ou seja, o que ficou faltando projetar.

**O que colar aqui para análise (se rodar esta checagem opcional):**
- Quantidade de IDs em cada lista.
- Quantidade de IDs faltando (saída do `comm`/da fórmula da planilha).
- Os primeiros IDs faltando, se houver algum.

---

## Consolidação final

Depois de rodar os passos acima, cole neste chat:

1. As saídas indicadas em cada "O que colar aqui para análise".
2. Uma frase indicando se, na sua avaliação, cada um dos 4 critérios da
   Fase 6 passou, falhou, ou não pôde ser verificado.

Com isso eu atualizo o plano
([PLANO-refactor-provider-interactions-collector.md](/Users/alysonfs/workspace/Software/natal-refrigeracao/atua/docs/implementation/PLANO-refactor-provider-interactions-collector.md))
marcando a Fase 6 como concluída (ou documentando o que ficou pendente),
sem precisar rodar um agente QA completo.
