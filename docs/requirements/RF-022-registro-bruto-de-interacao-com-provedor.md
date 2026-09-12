# RF-022 - Registro Bruto de Interação com o Provedor

Status: `Especificado`

**Data:** 2026-09-12

## Contexto e motivação

Este requisito substitui **RF-016** (Persistência de Snapshots Brutos de
OS), por decisão de **ADR-028**.

RF-016 preservava o dado bruto por **OS individual**
(`work_order_snapshots`, 1 documento por OS por coleta). O usuário
identificou uma lacuna real: isso não preserva a **interação** do Worker
Coletor com o provedor como unidade auditável — não há registro de login,
nem de "esta chamada de listagem retornou estas N OS, com sucesso ou
falha, neste instante, com estes parâmetros". Ele quer poder estudar a
situação das OS do tenant através do ATUA sem depender de acesso direto
ao portal do provedor, e precisa entender exatamente o que cada requisição
de coleta retorna para poder explorar e ajustar a integração.

A granularidade também muda por necessidade prática: a nova estratégia de
busca (ver ADR-028, item 7) substitui N chamadas por status por uma única
chamada paginada por data, que retorna OS de qualquer status num único
payload — não faz mais sentido gravar 1 documento por OS quando a unidade
real de interação com o provedor já é "um request, uma resposta com N
OS".

## Objetivo

Definir como o Worker Coletor persiste o registro bruto de cada interação
(request + response) com o provedor — login, busca de OS, enriquecimento
de OS — sem interpretar ou mapear o conteúdo retornado, preservando a
interação como unidade auditável completa.

## Usuário / Ator

- **Worker Coletor** (`apps/collector`): único produtor de documentos de
  interação.
- **SnapshotConsumerWorker** (ADR-023, atualizado por ADR-028): único
  consumidor via Change Stream, para projeção em `work_order`/
  `work_order_history` (RF-017).
- **Tenant / Office / Manager** (consumidor futuro, fora de escopo deste RF): estudo
  do histórico de interações sem acesso direto ao provedor.

## Escopo

RF-022 cobre exclusivamente a persistência do registro bruto de interação
na coleção `provider_interactions`. Não cobre:

- projeção/mapeamento em `work_order`/`work_order_history` (RF-017);
- persistência ou reuso de sessão do provedor (RF-023);
- exibição de interações no Office (RF futuro);
- a nova estratégia de busca por data em si, além do que é necessário para
  descrever o formato de `request` (ADR-028, item 7, é a referência
  arquitetural completa).

## Entidade: `provider_interactions`

```
provider_interactions {
  id:               UUID (UUIDv7, gerado pelo Worker, chave primária interna)
  tenant_id:        UUID
  provider_type:    string (ex.: "iservice")
  command_id:       UUID (comando de coleta que gerou esta interação)
  interaction_type: string ("login" | "list_query" | "detail_query")
  request:          documento — metadados da chamada feita ao provedor
                     (ex.: página, filtros de data, provider_order_id no
                     caso de detail_query). NUNCA contém credenciais nem
                     cookies de sessão (ver RF-022.7).
  orders:           array de documentos — payload bruto de OS retornado
                     nesta interação, exatamente como recebido do
                     provedor. Vazio ou ausente para interações do tipo
                     "login". Tratado como estrutura provisória: o
                     formato exato de cada item não é fixado por este RF,
                     apenas que é preservado sem transformação.
  success:          bool — se a interação foi concluída sem erro
  error_message:     string | null — mensagem de erro, se success = false
  created_at:       timestamp UTC de inserção
}
```

## Requisitos funcionais

### RF-022.1 - Um documento por interação real com o provedor

Cada chamada HTTP feita pelo Worker ao provedor (login, listagem,
enriquecimento) deve gerar exatamente um documento em
`provider_interactions`, independentemente do número de OS retornadas
naquela chamada.

### RF-022.2 - Preservação integral do payload de OS em `orders`

Quando a interação retornar OS (`list_query`/`detail_query`), o campo
`orders` deve conter o payload bruto retornado pelo provedor, sem
omissão de campos, transformação de valores ou interpretação de
semântica — mesma garantia que RF-016.2 dava por OS individual, agora
aplicada ao array completo por interação.

### RF-022.3 - Identificadores obrigatórios

Cada documento deve conter `tenant_id`, `provider_type`, `command_id` e
`interaction_type`, nos mesmos moldes de RF-016.3 (rastreabilidade por
tenant, provedor de origem e ciclo de coleta).

### RF-022.4 - Registro de falhas de interação

Se a chamada ao provedor falhar (timeout, erro HTTP, sessão inválida
etc.), o Worker deve registrar o documento com `success = false` e
`error_message` preenchido, mesmo sem `orders`. Interações falhas não são
descartadas — são o próprio objeto de auditoria que motivou este
requisito.

### RF-022.5 - Rastreabilidade temporal via `created_at`

Mesma regra de RF-016.4: `created_at` reflete o instante real de
inserção pelo Worker; não pode ser retroagido nem originado de campo do
provedor.

### RF-022.6 - Descarte de OS inválidas migra para o consumer

Diferente de RF-016.5 (que descartava no momento da escrita), este RF
**não** filtra OS sem `provider_id` extraível no momento da gravação — o
Worker grava o array completo tal como recebido. A decisão de descartar
uma OS sem identificador válido é responsabilidade do
`SnapshotConsumerWorker`/`ISnapshotAdapter` no momento da projeção (ver
RF-017, nota de atualização).

### RF-022.7 - Credenciais e dados de sessão proibidos em `provider_interactions`

Mesma vedação de RF-016.7: nem `request` nem `orders` podem conter
credenciais do provedor ou dados de sessão (cookies, tokens CAS). Dados
de sessão têm coleção própria — ver RF-023.

## Regras de negócio

| Número   | Regra                                                                                                                              |
|----------|-------------------------------------------------------------------------------------------------------------------------------------|
| RN-022.1 | Cada chamada HTTP ao provedor gera exatamente um documento em `provider_interactions`; não há upsert.                              |
| RN-022.2 | `orders` preserva o payload bruto do provedor integralmente, sem mapeamento, transformação ou filtragem por OS individual.         |
| RN-022.3 | Todo documento contém `tenant_id`, `provider_type`, `command_id` e `interaction_type` preenchidos.                                 |
| RN-022.4 | Falhas de interação são registradas com `success = false` e `error_message`, não descartadas.                                      |
| RN-022.5 | `created_at` reflete o instante real de inserção; datas do provedor não podem ser usadas como `created_at`.                        |
| RN-022.6 | Filtragem/descarte de OS por identificador inválido é responsabilidade do consumer, não do Worker produtor.                        |
| RN-022.7 | Credenciais e dados de sessão são vedados em `request`/`orders`; dados de sessão vivem em `provider_sessions` (RF-023).             |

## Critérios de aceite

1. Dado que o Worker executa uma busca paginada por data que retorna 12
   OS, quando a interação for concluída, então deve ser inserido
   exatamente um documento em `provider_interactions` com
   `interaction_type = "list_query"` e `orders` contendo as 12 OS com o
   payload bruto exato retornado.

2. Dado que o Worker executa um enriquecimento de detalhe para uma OS
   específica, quando a interação for concluída, então deve ser inserido
   um documento com `interaction_type = "detail_query"` e `request`
   contendo o identificador da OS consultada.

3. Dado que uma chamada de listagem falha por timeout, quando o Worker
   tratar a falha, então deve ser inserido um documento com
   `success = false`, `error_message` preenchido e `orders` vazio ou
   ausente — a interação não é descartada.

4. Dado que o Worker realiza login CAS com sucesso, quando o login for
   concluído, então deve ser inserido um documento com
   `interaction_type = "login"`, `success = true` e sem campo `orders`
   populado.

5. Dado um documento de `provider_interactions` com `orders` contendo uma
   OS sem identificador extraível, quando o consumer processar esse
   documento, então essa OS específica é descartada pelo consumer (não
   pelo Worker) — ver RF-017.

## Decisões pendentes

### DP-022.1 - Formato definitivo de cada item de `orders`

O formato de cada item do array `orders` ainda não é fixado como
contrato — permanece sujeito a ajuste conforme mais variação de payload
real do iService (e de futuros provedores) for observada em produção,
seguindo o mesmo princípio de RF-016 ("guardar o dado bruto primeiro,
mapear depois"). Revisar após a Fase 1 do plano de implementação
(`docs/implementation/PLANO-refactor-provider-interactions-collector.md`)
rodar contra dados reais.

## Substitui

RF-016 (integralmente).
