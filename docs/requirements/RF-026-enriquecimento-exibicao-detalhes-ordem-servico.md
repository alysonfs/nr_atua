# RF-026 - Enriquecimento e Exibição de Detalhes de Ordem de Serviço

Status: `Proposto`

**Data:** 2026-09-22

## Contexto e motivação

Atualmente, existe um mecanismo parcial de busca de detalhes de OS implementado
em produção: o método `EnrichAssignedOrdersAsync` do `IServiceCollectorService`
(Collector) busca detalhes via `queryOneWorkOrder` apenas para OS com status
"assigned" (Atua.Collector/IService/IServiceCollectorService.cs, linha ~1113),
registrando cada interação como `provider_interactions` com `interaction_type="detail_query"`
(RF-022). Esses detalhes são armazenados nos campos já existentes de `work_orders`
(cliente, endereço, produto, sintoma, etc. — via `WorkOrder.UpdateDetails`,
`apps/api/Atua.Api/Domain/WorkOrders/WorkOrder.cs`).

**Lacunas identificadas:**

1. **Escopo limitado:** a busca de detalhes é incondicional apenas para "assigned".
   Outras OSs (em qualquer outro status) não recebem detalhes enriquecidos,
   limitando a visibilidade do usuário sobre o ciclo de vida completo da OS.

2. **Sem critério explícito de quando buscar:** não há regra documentada sobre
   quando reutilizar detalhes já obtidos, quando buscá-los novamente (mudança
   de status?) e quando evitar chamadas desnecessárias ao provedor (OSs terminais).
   Isso resulta em possível redundância de requisições.

3. **Sem exibição no Office:** os detalhes enriquecidos existem no PostgreSQL
   (`work_orders`), mas o Office não oferece visualização dedicada desses
   campos. O usuário não consegue explorar informações normalizadas (cliente,
   endereço, produto) ou entender o histórico de transições de status
   (`work_order_histories`) de forma eficiente.

**Contexto de negócio:**

Um usuário do Office (gestor de OSs, técnico, supervisor) precisa analisar
uma OS específica sem ter que acessar o portal iService diretamente. Ele quer
saber:

- Detalhes completos da OS (cliente, endereço, equipamento, sintoma).
- Cronologia de mudanças de status desde a criação até o estado atual.
- Informações normalizadas e consistentes, já coletadas pelo Collector do ATUA.

**Consequência de não ter isto:**

Usuários continuam dependendo de acesso direto ao iService (portal externo)
para investigar OSs, reduzindo o valor do ATUA como uma ferramenta de
supervisão centralizada.

## Objetivo

Definir as regras de negócio para **quando buscar e enriquecer detalhes de
cada OS** (ampliando o escopo atual limitado a "assigned"), e implementar uma
**página dedicada de detalhes no Office** que exiba essas informações normalizadas
e o histórico de transições de status, permitindo ao usuário analisar completamente
uma OS sem sair da plataforma.

## Usuário / Ator

- **Usuários do Office** (membros do tenant com acesso a `OWNER`/`ADMIN`/`MEMBER` —
  roles definidas em RF-021): consultam OS e navegam até uma página de detalhes
  para investigar situação de uma OS específica.
- **Worker Coletor** (ou processo assíncrono equivalente, conforme ADR futuro):
  produtor de `provider_interactions` com `interaction_type="detail_query"` no
  MongoDB, a partir das regras definidas neste RF.
- **Consumer/SnapshotConsumerWorker** (ADR-023, atualizado por ADR-028): processa
  snapshots/interações e projeta dados em `work_order` e `work_order_history`
  no PostgreSQL.

## Escopo

### O que É RF-026

- **Regras de negócio:** quando buscar detalhes de uma OS (primeira vez,
  mudança de status, exceção para terminais).
- **Aplicação genérica:** generalizando o mecanismo hoje limitado a OSs "assigned".
- **UI no Office:** página dedicada de detalhes com visualização de campos
  normalizados + linha do tempo de histórico de status.
- **Links na tabela:** integração da tabela de dashboard existente do Office
  para tornar cada OS um link acessível à página de detalhes.
- **Ambiguidade arquitetural:** este RF define o **O QUE** e o **QUANDO**,
  mas **NÃO decide onde executar a lógica** de "devo buscar detalhe?" —
  essa é uma decisão técnica que requer ADR (ver "Fora de escopo").

### O que NÃO é RF-026

- **Implementação técnica de onde a decisão roda:** a lógica de "primeira vez?
  status mudou? é terminal?" pode ser implementada no Collector (durante
  scraping via Playwright) ou no Consumer (ao processar mudanças de estado
  no Postgres) — ambas as arquiteturas são viáveis. A decisão é técnica
  e será formalizada em ADR, não em RF.
- **Algoritmo de detecção de mudança de status:** a implementação técnica de
  como consultar o histórico anterior para validar mudança fica fora deste
  escopo — RF-026 assume que a comparação é possível.
- **Dados brutos/payloads:** o schema exato de campos do `rawData` do provedor
  que compõem `WorkOrderDetails` é tratado em RF-017 e RF-022. RF-026 assume
  que os campos já são normalizados e armazenados em `work_order.{client*,
  address*, product*, symptom, ...}` conforme hoje.
- **Autenticação/autorização em detalhe:** reutiliza as regras já definidas
  em RF-021 (roles) e RF-013 (isolamento por tenant). Detalhe de que role
  específica vê qual campo de uma OS é pós-MVP.
- **Exibição de dados brutos/rawData:** a página de detalhes mostra apenas
  campos normalizados; acesso ao `rawData` bruto do provedor é trabalho futuro.
- **Relatórios, filtros avançados ou comparação entre OSs:** múltiplas OSs
  lado a lado, dashboards de tendências etc. são pós-MVP.
- **Notificações/alertas ao usuário quando detalhes chegam:** sinalizações
  de "novo detalhe obtido" ou "status mudou" são trabalho futuro (relacionado
  a DP-017.1 resolvida).

## Requisitos funcionais

### RF-026.1 - Buscar detalhes na primeira vez que uma OS é vista

Quando o Collector identificar uma OS **nova** (não existe em `work_orders`
ainda), deve buscar os detalhes dessa OS via `queryOneWorkOrder` (ou equivalente
do provedor futuro, respeitando `IServiceCollectorService.WoDetailUrl`,
linha ~38).

- A interação de detalhes deve ser registrada em `provider_interactions` com
  `interaction_type="detail_query"` (RF-022.1).
- Os detalhes retornados devem ser aplicados aos campos normalizados de
  `work_order` via `WorkOrder.UpdateDetails(details)` (domínio definido em
  `Atua.Api/Domain/WorkOrders/WorkOrder.cs`).
- Esta requisição é **incondicional** para qualquer OS nova, independentemente
  de seu status inicial.

**Justificativa:** qualquer OS desconhecida merece enriquecimento inicial para
que o Office possa exibi-la completamente ao usuário.

### RF-026.2 - Buscar detalhes quando a OS muda de status

Quando o Collector detectar uma OS **existente** que teve seu `status` alterado
em relação ao último estado conhecido em `work_order`:

- Deve buscar os detalhes via `queryOneWorkOrder`.
- Os detalhes retornados devem ser aplicados aos campos normalizados de
  `work_order` via upsert (RF-017.1).
- Um novo registro é criado em `work_order_history` (RF-017.2).
- A interação de detalhes é registrada em `provider_interactions` com
  `interaction_type="detail_query"`.

**Justificativa:** quando uma OS muda de status, é provável que novos detalhes
estejam disponíveis no provedor (ex.: técnico atribuído, observações,
avançamento de etapas). Manter os detalhes sincronizados com cada transição
de status garante que o Office sempre exiba informações atuais.

### RF-026.3 - Não buscar detalhes se a OS já estiver em status terminal

Quando o Collector encontrar uma OS com status que seja **um dos terminais
confirmados** (`"Payment Approved"`, `"cancelled"` ou `"closed"` — strings
exatas, case-sensitive, conforme uso em produção com RDS Postgres):

- **E** essa OS já existe em `work_order` com detalhes já obtidos anteriormente
  (campo `DetailsFetchedAt` ou equivalente — ver nota técnica abaixo):
- **ENTÃO** não deve buscar detalhes novamente.

- Se a OS estiver em status terminal **pela primeira vez** (transição de não-terminal
  para terminal), deve buscar detalhes uma última vez (regra RF-026.2 aplica-se).
- Se a OS já estava em terminal e permanece em terminal, nenhuma busca de detalhe
  é necessária.

**Justificativa:** evitar requisições desnecessárias ao provedor. Uma OS em status
terminal não muda mais; detalhes já capturados são imutáveis. A poupança de
requisições cresce com o tamanho do histórico — critical para não sobrecarregar
o iService.

**Nota técnica (ambiguidade de implementação):**

RF-026 menciona "já existe em `work_order` com detalhes já obtidos", mas não
define tecnicamente como saber se uma busca de detalhes já foi feita para uma
OS. Opções:

- Adicionar coluna `DetailsFetchedAt` a `work_order` (marca quando detalhe foi
  obtido pela última vez).
- Usar heurística: "se `work_order` tem valores em campos de detalhes (ex.:
  `CustomerName` não nulo), então já foi enriquecida".
- Consultar `provider_interactions` com `interaction_type="detail_query"`
  para verificar se já há um documento para aquela OS.

A escolha é um detalhe de arquitetura; RF-026 apenas garante que a decisão
"já busquei detalhe desta OS?" é possível de determinar. Uma ADR técnica
definirá o mecanismo exato.

### RF-026.4 - Aplicação genérica a TODAS as OSs coletadas

As regras acima (RF-026.1, RF-026.2, RF-026.3) aplicam-se a **todas as OS
coletadas**, independentemente de seu status.

- **Hoje:** apenas OS com status `"assigned"` recebem `detail_query`
  (condicionado em `EnrichAssignedOrdersAsync`).
- **Após RF-026:** qualquer OS segue as 3 regras acima.

Esta é uma **generalização/ampliação** do mecanismo hoje limitado a um subconjunto.

### RF-026.5 - Página de detalhes da OS no Office com campos normalizados

O Office deve exibir uma rota dedicada (ex.: `/dashboard/work-orders/:providerId/details`
ou `/office/work-orders/:id/details`) que mostre, para uma OS específica:

#### Seção 1: Informações Gerais

- **ID da OS:** `provider_id` (identificador externo do provedor).
- **Status atual:** `status` (string crua do provedor).
- **Datas:** `created_at` (ATUA), `updated_at` (ATUA), `ProviderCreatedAt`/
  `ProviderUpdatedAt` (originários do provedor, se disponíveis).

#### Seção 2: Dados do Cliente

- **Tipo de cliente:** `CustomerType` (ex.: "Pessoa Jurídica" — valor bruto do
  provedor normalizado via `WorkOrderDetails`).
- **Nome do cliente:** `CustomerName`.
- **CPF/CNPJ:** `CustomerCpf` (prenome exato, pode ser CPF ou CNPJ conforme tipo).
- **E-mail de contato:** `ContactEmail`.
- **Telefone de contato:** `ContactPhone`.
- **Nome de contato:** `ContactName`.

#### Seção 3: Dados de Localização

- **Endereço completo:** `Address`.
- **CEP:** `ZipCode`.
- **País:** `CountryName`.
- **Estado:** `StateName`.
- **Cidade:** `CityName`.

#### Seção 4: Dados do Produto/Equipamento

- **Marca:** `ProductBrand`.
- **Código do produto (SKU):** `PdCode`.
- **Categoria:** `ProductCategoryCode` + `ProductCategoryId` (conforme originário
  do provedor, normalizados via adapter).
- **Modelo:** `ProductModel`.
- **Código interno da OS:** `ProductCode`.
- **Status do equipamento:** `ProductStatus`.

#### Seção 5: Dados do Atendimento

- **Sintoma/Reclamação relatada:** `Symptom` (descrição bruta da falha).
- **ID da Solicitação de Serviço:** `ServiceRequestId` (link ou referência para
  o documento de origem no provedor, se disponível).
- **Valor/Orçamento:** `Amount` (valor em moeda do provedor).

**Critério de exibição:** campos sem valor (null/vazio) podem ser ocultados ou
exibidos com placeholder "Não informado". O Office define a estética; RF-026
apenas requere que os campos existentes sejam exibidos quando populados.

### RF-026.6 - Linha do tempo de histórico de status na página de detalhes

A mesma página de detalhes deve incluir uma **seção de linha do tempo** que
liste todas as transições de status da OS em ordem cronológica. Para cada
entrada do histórico (linha em `work_order_history`):

- **Status anterior** (opcional, se disponível no histórico anterior — pode
  ser inferido): mostrar qual era o status antes desta transição.
- **Status novo:** qual é o status nesta entrada.
- **Data/hora da transição:** `work_order_history.created_at`.
- **Ordenação:** mais recente no topo ou no fim, conforme padrão de UX do
  Office (a ser definido por `frontend-engineer`).

**Exemplo visual:**

```
[2026-09-22 14:30:00] Status mudou para "Designado"
[2026-09-21 10:15:00] Status mudou para "Pendente"
[2026-09-20 09:00:00] OS criada com status "Novo"
```

**Fonte dos dados:** `work_order_history` (PostgreSQL), consultada via query
`WHERE tenant_id = ? AND provider_id = ? ORDER BY created_at`.

### RF-026.7 - Tabela de dashboard do Office com links para página de detalhes

A tabela de OSs exibida no dashboard do Office (ex.: `/dashboard` ou
`/office/work-orders`) deve transformar cada linha de OS (cada `provider_id`)
em um **link navegável** para a página de detalhes de RF-026.5/RF-026.6.

- O Office define qual coluna ou elemento da linha é clicável (ex.: coluna
  "ID da OS", ou um ícone "ver detalhes").
- Ao clicar, o usuário é navegado para `/work-orders/:providerId/details`
  (ou rota equivalente).
- A mesma rota deve requerir que o usuário tenha membership ativo no tenant
  e isolamento de tenant deve ser garantido (RF-013, já implementado no Office).

## Regras de negócio

| Número   | Regra                                                                                                                                    |
|----------|-----------------------------------------------------------------------------------------------------------------------------------------|
| RN-026.1 | Qualquer OS nova deve receber enriquecimento de detalhes antes de ser armazenada em `work_order` (RF-026.1).                            |
| RN-026.2 | Mudança de status sempre dispara nova busca de detalhes (RF-026.2), garantindo sincronização.                                           |
| RN-026.3 | Status terminais confirmados são: `"Payment Approved"`, `"cancelled"`, `"closed"` (strings exatas, case-sensitive).                    |
| RN-026.4 | Uma OS em status terminal que foi enriquecida não deve ter detalhes buscados novamente (RN-026.4, RF-026.3).                            |
| RN-026.5 | Uma OS em transição **para** status terminal pela primeira vez deve receber enriquecimento nessa transição (RF-026.2 aplica-se).        |
| RN-026.6 | Enriquecimento genérico aplica-se a **todas as OS**, não apenas "assigned" (RF-026.4).                                                |
| RN-026.7 | Cada interação de enriquecimento (detail_query) é registrada em `provider_interactions` (RF-022, não duplicado por este RF).             |
| RN-026.8 | Dados normalizados são armazenados nos campos existentes de `work_order`, não em tabela separada (RF-017, não cria nova entidade).     |
| RN-026.9 | Página de detalhes exibe apenas campos normalizados; acesso a `rawData` bruto é trabalho futuro.                                      |
| RN-026.10| Isolamento por tenant é garantido: um usuário vê detalhes apenas de OSs do seu tenant (RF-021, ADR-005).                               |

## Casos de borda

- **OS nova, primeira coleta:** regra RF-026.1 aplica-se; detalhe é buscado
  incondicionalmente.
- **OS conhecida, mesmo status:** apenas `work_order.updated_at` é atualizado;
  nenhuma busca de detalhe (não há mudança de status, não é "primeira vez").
- **OS conhecida, status mudou:** regra RF-026.2 aplica-se; detalhe é buscado.
- **OS em terminal (ex.: "closed"), detalhes já obtidos:** regra RF-026.3
  aplica-se; nenhuma busca de detalhe.
- **OS em terminal, primeira e única vez visto (status inicial é terminal):**
  regra RF-026.1 aplica-se (é "primeira vez"); detalhe é buscado.
- **Usuário sem membership no tenant:** acesso à página de detalhes é negado
  (401/403, conforme ADR-017).
- **Usuário com membership, mas OS do outro tenant:** tentativa de acesso é
  negada (404 ou 403, isolamento de tenant garantido).
- **Campo de detalhe é null/vazio:** Office exibe placeholder ou oculta; a
  decisão visual é de `frontend-engineer`.
- **Página de detalhes carregada, histórico ainda não sincronizado:** página
  exibe detalhes atuais e histórico vazio ou parcial; refresh/polling futuro
  pode melhorar, mas não é bloqueante para RF-026.
- **Provedor retorna payload parcial em detail_query (alguns campos faltam):**
  campos obtidos são aplicados via `UpdateDetails` (merge, não replace);
  campos ausentes retêm valores anteriores (comportamento hoje em
  `WorkOrder.UpdateDetails`).

## Critérios de aceite

### Cenários de busca de detalhe

1. **Primeira vez, qualquer status**
   - Dado uma OS nunca vista em `work_orders` (nova),
   - Quando o Collector processar essa OS com qualquer status (ex.: "Novo",
     "Designado", "closed", etc.),
   - Então `queryOneWorkOrder` deve ser chamado para enriquecer detalhes,
   - E um documento em `provider_interactions` com `interaction_type="detail_query"`
     deve ser criado,
   - E os campos normalizados devem ser aplicados em `work_order` via
     `UpdateDetails`.

2. **Mudança de status, qualquer transição**
   - Dado uma OS conhecida com `status = "Novo"`,
   - Quando o Collector ver a mesma OS com `status = "Designado"`,
   - Então `queryOneWorkOrder` deve ser chamado,
   - E `work_order.status` e campos normalizados devem ser atualizados,
   - E um novo registro em `work_order_history` deve ser criado com o novo
     status.

3. **Status terminal, já enriquecida, sem nova busca**
   - Dado uma OS com status `"closed"` que já foi enriquecida (tem detalhes
     em `work_order`),
   - Quando essa mesma OS for vista novamente com status `"closed"`,
   - Então `queryOneWorkOrder` **não** deve ser chamado,
   - E nenhum novo documento em `provider_interactions` deve ser criado,
   - E apenas `work_order.updated_at` deve ser atualizado (RF-017.1).

4. **Transição para terminal pela primeira vez**
   - Dado uma OS com status `"Designado"` (enriquecida anteriormente),
   - Quando o Collector ver a mesma OS com status `"Payment Approved"`
     (terminal),
   - Então `queryOneWorkOrder` deve ser chamado uma última vez,
   - E um novo `work_order_history` deve ser criado,
   - E a partir desse ponto, nenhuma nova busca de detalhe deve ocorrer
     enquanto permanecer em terminal.

5. **Terminal como status inicial**
   - Dado uma OS nova com status inicial `"closed"`,
   - Quando o Collector processar essa OS,
   - Então `queryOneWorkOrder` deve ser chamado (é primeira vez, RF-026.1),
   - E os detalhes devem ser armazenados em `work_order`.

### Cenários de UI: Página de detalhes

6. **Navegação para página de detalhes**
   - Dado um usuário `MEMBER` do tenant T com uma OS disponível no dashboard,
   - Quando clicar no link "Ver detalhes" ou no ID da OS,
   - Então a aplicação deve navegar para `/work-orders/:providerId/details`,
   - E a página deve carregar os dados de `work_order` para esse `provider_id`,
   - E exibir as seções definidas em RF-026.5 (Gerais, Cliente, Localização,
     Produto, Atendimento).

7. **Exibição de campos normalizados**
   - Dado uma OS enriquecida com dados de cliente, endereço e produto,
   - Quando o usuário acessar a página de detalhes,
   - Então os campos devem ser visíveis:
     - `CustomerName`, `CustomerType`, `CustomerCpf`
     - `ContactName`, `ContactEmail`, `ContactPhone`
     - `Address`, `ZipCode`, `StateName`, `CityName`, `CountryName`
     - `ProductBrand`, `ProductModel`, `PdCode`, `ProductCategoryCode`, `Symptom`
     - `Amount`, `ServiceRequestId`
   - E campos vazios devem ser tratados conforme padrão de UI do Office.

8. **Linha do tempo de histórico**
   - Dado uma OS com múltiplas transições de status armazenadas em
     `work_order_history`,
   - Quando o usuário acessar a página de detalhes,
   - Então uma seção de "Histórico de Status" ou "Linha do Tempo" deve ser
     exibida,
   - E deve listar todas as entradas de `work_order_history` em ordem
     cronológica (crescente ou decrescente, conforme UX),
     cronológica (crescente ou decrescente, conforme UX),
   - E cada entrada deve exibir o status e a data/hora da transição.

9. **Isolamento de tenant**
   - Dado um usuário do tenant T1 tentando acessar uma OS do tenant T2,
   - Quando tentar navegar para `/work-orders/provider-id-de-T2/details`,
   - Então o acesso deve ser negado (404 ou 403).

10. **Sem membership, acesso negado**
    - Dado um usuário sem membership ativo no tenant,
    - Quando tentar acessar a rota `/work-orders/.../details`,
    - Então deve ser redirecionado para login ou acesso negado (conforme
      ADR-017).

### Cenários de integração: Links no dashboard

11. **Tabela do dashboard exibe links**
    - Dado o dashboard do Office exibindo a tabela de OSs,
    - Quando a página for carregada,
    - Então cada linha da tabela deve ter um elemento clicável (ex.: coluna
      "ID" ou ícone "detalhes") que leve à página de detalhes daquela OS.

## Dependências

### Requisitos relacionados

- **RF-017** (Modelo agnóstico work_order): define as entidades `work_order`
  e `work_order_history` onde os dados normalizados são armazenados. RF-026
  consome diretamente esses dados.
- **RF-022** (Registro bruto de interação com o provedor): define
  `provider_interactions` onde as interações `detail_query` são registradas.
  RF-026 cria `provider_interactions` com `interaction_type="detail_query"`.
- **RF-013** (Modo somente leitura): garante que o Collector não realiza
  escrita no iService; enriquecimento via `queryOneWorkOrder` é uma leitura
  (seguro).
- **RF-021** (Papéis de usuário do tenant): define quem tem acesso ao Office
  (OWNER/ADMIN/MEMBER); isolamento de acesso à página de detalhes reutiliza
  essas regras.
- **RF-024** (Coleta manual sob demanda): UI irmã no Office; ambas estão na
  seção de integração/dashboard.

### Arquiteturas / Decisões

- **ADR-023** (Fila e Consumer: Snapshot → work_order): descreve o mecanismo
  atual de `SnapshotConsumerWorker` e `provider_interactions` Change Stream.
  RF-026 consome esse mecanismo (não altera sua essência, mas amplia o escopo
  de quando `detail_query` é feita).
- **ADR-028** (Registro bruto de interação com o provedor): define
  `provider_interactions` como a única fonte bruta no Mongo. RF-026 utiliza
  essa coleção para registrar `detail_query`.
- **ADR-017** (Contrato de autenticação browser e serviço interno): isolamento
  de tenant e autenticação do Office. RF-026 assume esse mecanismo já
  implementado.
- **ADR-005** (Tenancy, memberships e integrações): regras de isolamento por
  tenant e papéis. RF-026 reutiliza.

### Ambiguidade arquitetural — ADR necessária

#### DP-026.1: Onde executar a lógica de decisão "devo buscar detalhe?"

**Problema não resolvido por este RF:**

A decisão de "esta OS é nova?", "seu status mudou?", "é terminal?" pode ser
implementada em dois locais arquiteturalmente viáveis:

**Opção A: No Collector (durante scraping)**
- Vantagem: lógica centralizada no código de scraping (today: `IServiceCollectorService`).
- Desvantagem: o Collector roda via Playwright, não tem acesso direto/síncrono
  a PostgreSQL em tempo real para consultar `work_order` e `work_order_history`.
  Requereria uma chamada adicional à Master API para verificar estado anterior,
  ou necessitaria passar informações de contexto via `CycleContext` (hoje já
  sobrecarregado). Aumento de latência e complexidade.

**Opção B: No Consumer (durante projeção em PostgreSQL)**
- Vantagem: Consumer roda no mesmo processo de banco de dados, tem acesso
  síncrono a `work_order` para comparação de status. Lógica fica em
  `SnapshotConsumerWorker` ou novo `ProviderInteractionConsumerWorker`.
  Reduz acoplamento Collector-API.
- Desvantagem: lógica de negócio "quando buscar?" fica longe do código de
  scraping (separação pode confundir futuros maintainers). Consumer não pode
  reverter decisões do Collector (é assíncrono). Se a lógica detectar "não
  busquei, não deveria buscar", é tarde: Collector já executou.

**Status:** ambas as opções são arquiteturalmente viáveis. A decisão requer
uma **ADR dedicada** (sugerido: ADR-031 "Localização de lógica de decisão:
enriquecimento de detalhes no Collector vs. no Consumer"). Essa ADR deve
avaliar:

- Custo de latência (Collector consultar API vs. Consumer esperar Change Stream).
- Acoplamento de responsabilidades.
- Testabilidade.
- Clareza de manutenção futura.

**RF-026 não decide** qual opção é correta. Apenas formaliza o requisito
funcional "a decisão existe e segue estas 3 regras".

---

## Impactos

### No Collector

- `IServiceCollectorService.cs` (se Opção A): deve estender `EnrichAssignedOrdersAsync`
  para se tornar `EnrichAllOrdersAsync` (ou equivalente), aplicando a lógica de
  RF-026.1/2/3 a todas as OSs, não apenas "assigned".
- Ou: nova lógica no `ProviderInteractionConsumerWorker` (se Opção B).

### Na Master API

- Possível novo endpoint ou extensão de existente para o Office consultar
  detalhes de uma OS específica (`GET /tenants/{tenantId}/work-orders/{providerId}`
  ou similar).
- Nenhuma alteração forçada em contrato de ativação/comando (RF-008/ADR-020
  permanece válido).

### No Office

- **Nova rota:** `/work-orders/:providerId/details` (ou equivalente).
- **Novo componente:** página de detalhes com 5-6 seções de dados +
  componente de linha do tempo.
- **Modificação existente:** tabela de dashboard deve ter links/navegação
  para a página de detalhes de cada OS.

### No banco de dados

- **Postgres (`work_order`):** nenhuma mudança de schema — campos já existem
  (conforme RF-017). Possível: nova coluna `DetailsFetchedAt` se escolher
  Opção B e usar essa marca para evitar retentar (detalhe de implementação,
  fora de escopo deste RF).
- **Mongo (`provider_interactions`):** nenhuma mudança de schema — `interaction_type="detail_query"`
  já é documentado em RF-022.

## Fora de escopo

- **Implementação técnica da decisão "devo buscar?":** decisão de localização
  (Collector vs. Consumer) e mecanismo de persistência da marca "já busquei?"
  fica em ADR futuro (DP-026.1).
- **Integração de dados brutos em detalhes:** se o provedor retornar campos
  adicionais no payload de `queryOneWorkOrder`, mapeamento desses campos
  novos é trabalho futuro (quando houver conhecimento de novos campos a
  normalizar).
- **Histórico antes de RF-026:** OSs coletadas antes da implementação de
  RF-026 podem ter `work_order` sem detalhes se enriquecidas sob a regra
  antiga (apenas "assigned"). Migração retroativa é opcional (não bloqueante).
- **UI/UX detalhada da página de detalhes:** estética, ordem de campos,
  responsividade, temas (dark mode) etc. ficam a cargo do `frontend-engineer`.
- **Testes de cobertura de toda lógica de enriquecimento:** estratégia de
  teste unitário/integração do Collector e Consumer é fora de escopo de
  requisito (cabe a `qa-engineer` quando implementação ocorrer).
- **Exibição de `rawData` bruto:** acesso ao payload completo do provedor é
  trabalho futuro.
- **Sincronização em tempo real:** página de detalhes não terá polling/WebSocket
  por enquanto (refresh manual ou segunda requisição explícita do usuário).
- **Notificações/alertas:** quando um novo detalhe é obtido ou status muda,
  nenhuma notificação/badge é mandatória neste RF (trabalho futuro).

## Decisões pendentes

### DP-026.1 — Localização da lógica de decisão: Collector vs. Consumer

**Status:** aberto, requer ADR-031.

Ver seção "Ambiguidade arquitetural — ADR necessária" acima. RF-026 formaliza
o **O QUE** (as 3 regras); ADR-031 decidirá o **ONDE** (qual componente executa).

## Rastreabilidade e histórico

- **Origem:** consolidação de lacuna identificada em análise de produto
  (2026-09-22). Mecanismo parcial já existe (RF-022 + `EnrichAssignedOrdersAsync`);
  RF-026 generaliza e formaliza o requisito completo.
- **Usuário:** Natal Refrigeração, supervisão de OSs via ATUA sem dependência
  de iService.

## Critérios de aceite — Sumário Executivo

| # | Cenário | Status Esperado |
|---|---------|-----------------|
| 1 | OS nova, qualquer status | Detalhe buscado |
| 2 | OS conhecida, status mudou | Detalhe buscado |
| 3 | OS em terminal, já enriquecida | Detalhe NÃO buscado |
| 4 | OS transição → terminal pela 1ª vez | Detalhe buscado uma última vez |
| 5 | Página de detalhes carrega dados normalizados | ✅ Exibidos nas 5 seções |
| 6 | Histórico de status visível | ✅ Linha do tempo exibida |
| 7 | Links na tabela do dashboard | ✅ Cada OS é navegável |
| 8 | Isolamento de tenant | ✅ Usuário vê apenas seu tenant |

---

**Observações finais:**

- RF-026 formaliza um requisito de negócio focado em **visibilidade e análise de OSs no Office**.
- A implementação técnica (onde a lógica de decisão roda) é uma ambiguidade arquitetural que requer ADR.
- O contrato de dados (campos em `work_order`, estrutura de `work_order_history`, formato de `provider_interactions`) já está definido em RFs anteriores e não é alterado por este requisito.
- Próximo passo: após aprovação de RF-026, acionar `software-architect` para formalizar ADR-031 e definir a localização da lógica de decisão.
