# Guia de validação manual — Fase 6 (provider_interactions)

**Objetivo:** validar manualmente, sem acionar o agente `qa-engineer`, os
quatro critérios da Fase 6 do
[PLANO-refactor-provider-interactions-collector.md](/Users/alysonfs/workspace/Software/natal-refrigeracao/atua/docs/implementation/PLANO-refactor-provider-interactions-collector.md):

1. Idempotência (reprocessar o mesmo `command_id`/interação não duplica dado).
2. Reuso de sessão do iService (evita login repetido).
3. Busca por data retorna o mesmo conjunto de OS que a busca por status trazia antes.
4. Resume token do Change Stream sobrevive a restart do Worker.

Este documento é um roteiro operacional: rode os passos você mesmo com o
Collector já em execução local (via depurador do VS Code, como no ciclo
anterior), e traga de volta apenas os trechos indicados na seção
**"O que colar aqui para análise"** de cada passo. Não é necessário colar
o log inteiro — apenas os recortes pedidos.

## Pré-requisitos

- `Postgres__ConnectionString` e `MongoDB__ConnectionString` exportados no
  ambiente (mesmas variáveis já usadas nas investigações anteriores).
- `/tmp/dbcheck-venv/bin/python3` disponível com `psycopg2-binary` e
  `pymongo` instalados (venv já criado nas sessões anteriores).
- Collector rodando localmente (depurador do VS Code), com
  `ApiBaseUrl=https://api.atyno.com.br` (confirme no log de startup — é o
  comportamento observado na última sessão).
- Acesso ao terminal para rodar `make collector-trigger-cycle` a partir de
  `infra/` (skill `atua-deploy`), com `AWS_PROFILE` configurado.

## Script auxiliar (reutilizado em vários passos)

Salve como `/tmp/qa_check.py` (ou rode inline) — ele resume o estado atual
sem precisar reescrever a conexão a cada passo:

```python
import os, psycopg2
from pymongo import MongoClient

def pg_connect():
    raw = os.environ["Postgres__ConnectionString"]
    parts = dict(p.split("=", 1) for p in raw.split(";") if p.strip())
    mapping = {"Host": "host", "Port": "port", "Database": "dbname",
               "Username": "user", "Password": "password", "SSL Mode": "sslmode"}
    kwargs = {mapping[k.strip()]: v.strip() for k, v in parts.items() if k.strip() in mapping}
    kwargs["sslmode"] = kwargs["sslmode"].lower()
    return psycopg2.connect(**kwargs)

def mongo_db():
    client = MongoClient(os.environ["MongoDB__ConnectionString"])
    return client["atua"]

if __name__ == "__main__":
    conn = pg_connect()
    cur = conn.cursor()
    cur.execute('SELECT COUNT(*) FROM work_orders')
    print("work_orders:", cur.fetchone()[0])
    cur.execute('SELECT COUNT(*) FROM work_order_histories')
    print("work_order_histories:", cur.fetchone()[0])
    cur.execute('SELECT "ConsumerId", "UpdatedAt" FROM consumer_states')
    for row in cur.fetchall():
        print("consumer_state:", row)

    db = mongo_db()
    print("provider_sessions:", db["provider_sessions"].count_documents({}))
    print("provider_interactions:", db["provider_interactions"].count_documents({}))
```

Rode com: `/tmp/dbcheck-venv/bin/python3 /tmp/qa_check.py`

---

## Passo 0 — Estado inicial (baseline)

1. Rode o script auxiliar acima e guarde a saída.
2. No terminal do Collector (log ao vivo), confirme que está ocioso
   (ciclos de `ClaimAsync` retornando 204).

**O que colar aqui para análise:**
- Saída completa do script auxiliar (baseline "antes").

---

## Passo 1 — Disparar um ciclo novo e observar reuso de sessão

1. A partir de `infra/`: `AWS_PROFILE=<seu profile> make collector-trigger-cycle`.
2. Observe o log do Collector até o ciclo terminar (`CompleteAsync` com
   `Succeeded`). Isso deve levar alguns minutos dependendo do volume.
3. Procure no log, na ordem:
   - Uma linha de `ClaimAsync` retornando um comando (200, não 204).
   - **Reuso de sessão**: procure por algo como "sessão reaproveitada" /
     "storage_state" reutilizado, **sem** uma nova chamada de login ao
     iService logo em seguida. Se em vez disso aparecer um novo login
     completo (usuário/senha enviados, página de login navegada), a
     sessão NÃO foi reaproveitada — isso é uma falha do critério 2.
   - Uma ou mais linhas de `list_query` (busca por data) com contagem de
     OS por página.
   - Linhas de `detail_query`, se houver.
   - Linhas `[CONSUMER] Interação processada` — uma por interação
     (`login` é ignorado pelo consumer; não gera linha de projeção).

**O que colar aqui para análise:**
- As linhas de log da sessão (login novo vs. reaproveitada) — só o
  trecho relevante, não o ciclo inteiro.
- Contagem de páginas/OS reportada pelo `list_query`.
- Todas as linhas `[CONSUMER] Interação processada` deste ciclo.
- Qualquer linha de erro/exception, se houver.

---

## Passo 2 — Conferir projeção completa (sem perda de OS)

1. Rode o script auxiliar de novo.
2. Compare `work_orders`/`work_order_histories` com o baseline do Passo 0
   — o crescimento deve ser compatível com o total de OS únicas
   reportadas pelo `list_query` no Passo 1 (descontando OS que já
   existiam de ciclos anteriores — é upsert, não insert puro, então o
   total pode crescer menos que o total bruto de OS do ciclo).
3. Rode a checagem de cruzamento Mongo × Postgres abaixo para confirmar
   que **todas** as OS do ciclo mais recente estão em `work_orders`
   (substitua `COMMAND_ID` pelo valor do comando disparado no Passo 1 —
   aparece no log como `CommandId=...`):

```python
import os
from pymongo import MongoClient
import psycopg2

# reaproveite pg_connect()/mongo_db() do script auxiliar, ou cole aqui de novo
command_id = "COLE_O_COMMAND_ID_AQUI"

db = mongo_db()
docs = list(db["provider_interactions"].find(
    {"command_id": command_id, "interaction_type": {"$in": ["list_query", "detail_query"]}}))
mongo_ids = set()
for d in docs:
    for o in d.get("orders", []):
        wid = o.get("workOrderId") or o.get("id")
        mongo_ids.add(str(int(wid)) if isinstance(wid, float) and wid.is_integer() else str(wid))
print("Mongo OS únicas neste ciclo:", len(mongo_ids))

conn = pg_connect()
cur = conn.cursor()
cur.execute('SELECT "ProviderId" FROM work_orders')
pg_ids = set(r[0] for r in cur.fetchall())
missing = mongo_ids - pg_ids
print("Faltando em work_orders:", len(missing))
if missing:
    print("IDs faltando:", sorted(missing)[:20])
```

**O que colar aqui para análise:**
- Saída do script (`Mongo OS únicas neste ciclo`, `Faltando em work_orders`).
- Se houver IDs faltando, a lista impressa.

---

## Passo 3 — Teste de idempotência (reprocessar sem duplicar)

Este é o teste mais delicado — precisa forçar o consumer a reler uma
interação já processada.

1. Com o Collector **parado** (Ctrl+C, deixe finalizar limpo), anote o
   `ResumeToken` atual salvo em `consumer_states` (via script auxiliar,
   campo `consumer_state`).
2. Limpe manualmente o token do consumer `provider-interaction-to-work-order`
   no Postgres, para forçar o Change Stream a reabrir sem `ResumeAfter` e
   reprocessar o histórico recente (⚠️ isto NÃO apaga nenhum dado de
   negócio, apenas o ponteiro de resume — está alinhado com o
   comportamento já implementado de "token expirado" no
   `ProviderInteractionConsumerWorker`):

   ```sql
   -- Rode manualmente via script python com psycopg2, NÃO apague outras linhas
   UPDATE consumer_states SET "ResumeToken" = NULL
   WHERE "ConsumerId" = 'provider-interaction-to-work-order';
   ```

3. Rode o script auxiliar (contagens) **antes** de reiniciar o Collector —
   guarde como baseline deste passo.
4. Reinicie o Collector local (novo `dotnet run`/relançar debug).
5. Observe o log: o Change Stream deve reabrir do início (ou de um ponto
   anterior) e reprocessar interações já vistas. Aguarde estabilizar
   (parar de gerar novas linhas `[CONSUMER] Interação processada`).
6. Rode o script auxiliar de novo.

**Critério de sucesso:** `work_orders` e `work_order_histories` **não
crescem** além do que já tinham (upsert idempotente) — ou crescem apenas
pela diferença de estado real (`woStatus` mudou desde a última leitura,
gerando 1 novo registro de histórico por OS que mudou, o que é esperado,
não é duplicação).

**O que colar aqui para análise:**
- Contagens antes/depois deste passo (`work_orders`, `work_order_histories`).
- Se `work_order_histories` cresceu, confirme se é por causa de status
  realmente diferente (rode a query abaixo) e cole o resultado:

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
2. Rode o script auxiliar e anote o `ResumeToken`/`UpdatedAt` de
   `provider-interaction-to-work-order`.
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
- Contagens finais depois do restart, para confirmar que o ciclo
  completou (comparar com o total esperado de OS do `list_query`).

---

## Passo 5 — Busca por data vs. busca por status antigo

Não temos mais o código antigo rodando em paralelo para comparar
diretamente. Validação indireta:

1. No log do Passo 1, confirme os parâmetros usados na nova busca:
   `creationDateFrom`, `creationDateTo`, `woStatus=""`,
   `woStatusCond="me"` (devem aparecer no log de request ao iService, se
   `EnableLocalDebugLog=true`).
2. Confirme que o total de OS retornado (soma de todas as páginas do
   `list_query`) é plausível para o período coberto por
   `HistoryWindowMonths` (1 mês, desde o commit `2cf8e05`) — compare com
   o número de OS que o usuário sabe (por conhecimento do negócio/portal
   do iService) que existem nesse período, se houver como conferir
   visualmente no portal do iService.
3. Se possível, rode uma consulta manual no portal do iService (fora do
   Collector) filtrando por período e todos os status, e compare a
   contagem manualmente.

**O que colar aqui para análise:**
- Total de OS retornado pelo `list_query` (soma das páginas).
- Se foi possível conferir no portal do iService, o número visto lá.
- Se NÃO for possível conferir (ex.: não há acesso ao portal agora),
  registre isso explicitamente — este critério fica como "não
  totalmente verificável neste ambiente", não como falha.

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
