``# RF-027 - OS Abertas por Tempo de Abertura (Aging)

Status: `Implementado`

**Data:** 2026-09-24

## História de Usuário

Como usuário do Office (gestor/supervisor de OSs de um tenant),
eu quero visualizar quantas OS estão abertas há mais de 5, 10 e 30 dias,
independentemente do status atual (peça pendente, triagem, contato em
andamento, proposta de troca, etc.),
para que eu possa identificar rapidamente as OSs "ofensoras" — paradas há
muito tempo, não importa em qual etapa travaram — e priorizar ações para
finalização rápida.

**Fluxo**: o usuário acessa o Dashboard do Office e visualiza três novos
cards (ex.: "Abertas há +5 dias", "Abertas há +10 dias", "Abertas há +30
dias"), cada um com a contagem de OS do tenant que atendem ao critério,
somando todos os status não finalizados. Ao clicar em um card, o usuário
navega para uma lista filtrada dessas OS (mesmo padrão de navegação já
usado nos demais cards/listas do dashboard).

## Contexto e motivação

A cliente mantém hoje um painel próprio (fora do ATUA, ex. planilha/BI
interno — "Painel de OS Imperial Tec") com cards de contagem por
criticidade (ex. "Atendimento vencido", "Críticas — mais de 5 dias sem
atualização"). Esse painel usa como critério **tempo desde a última
atualização** — uma métrica diferente da solicitada aqui.

O pedido desta RF é um recorte complementar: medir o **tempo desde a
abertura da OS** (criação), sem olhar para status intermediários. Isso
existe porque uma OS pode ter várias mudanças de status recentes (o que
"esconderia" o problema em uma métrica de "dias sem atualização") mas
ainda assim estar aberta há muito tempo desde que foi criada — esse é o
sinal que a cliente quer capturar para achar OSs verdadeiramente
"ofensoras".

**Consequência de não ter isto:** a cliente continua sem visibilidade
automática sobre OSs antigas ainda abertas, dependendo de controle manual
externo (planilha) para identificar esses casos.

## Viabilidade (análise prévia)

Investigação já realizada no domínio existente confirma que **este
requisito é atendível apenas com dados já coletados hoje, sem mudança no
Agente Coletor**:

- `WorkOrder.ProviderCreatedAt` (`apps/api/Atua.Api/Domain/WorkOrders/WorkOrder.cs`)
  já armazena a data de criação real informada pelo provedor
  (`creationDate` no iService), coletada para toda OS.
- `WorkOrder.CreatedAt` serve de fallback (data em que o ATUA viu a OS
  pela primeira vez), caso `ProviderCreatedAt` venha nulo.
- Já existe uma lista de status terminais (fechamento real) em
  `WorkOrderTerminalStatuses`
  (`apps/collector/Atua.Collector/Consumer/WorkOrderTerminalStatuses.cs`):
  `"Payment Approved"`, `"cancelled"`, `"closed"`. Uma OS é considerada
  "aberta" quando seu `Status` não está nesse conjunto.

Ponto de atenção técnico (não de negócio): essa lista de status terminais
hoje só existe no projeto do Collector; a API precisa de sua própria cópia
(ou de um projeto compartilhado) para aplicar o mesmo critério de "aberta"
— ver observação equivalente já registrada em
`WorkOrderMonthlyTrendQueryHandler.cs`. **Resolvido**: duplicação
deliberada e temporária, ver [ADR-032](../decisions/ADR-032-duplicacao-temporaria-de-work-order-terminal-statuses.md).

## Objetivo

Fornecer, via API, a contagem de OS do tenant que estão **abertas** (status
fora do conjunto terminal) há mais de 5, 10 e 30 dias contados a partir da
data de abertura, e exibir esse recorte como novos cards no Dashboard do
Office, com navegação para a lista de OS correspondente.

## Escopo

### O que É RF-027

- Definição de negócio de "OS aberta" (status não terminal).
- Definição do cálculo de idade da OS: `agora - data de abertura`, usando
  `ProviderCreatedAt` com fallback para `CreatedAt`.
- Três faixas fixas de idade: mais de 5 dias, mais de 10 dias, mais de 30
  dias (não mutuamente exclusivas — uma OS aberta há 40 dias conta nas três
  faixas, mesmo comportamento cumulativo dos demais cards do dashboard).
- Endpoint de contagem por faixa na Master API.
- Três novos cards no Dashboard do Office, com link para lista filtrada de
  OS daquela faixa.
- i18n dos rótulos dos cards (pt-BR, en-US, es-AR).

### O que NÃO é RF-027

- Reprodução do painel externo da cliente (cards "Atendimento vencido",
  "Vence hoje", "Em triagem" etc.) — esses usam campos que não existem
  hoje no domínio ATUA (ex. data de próximo atendimento agendado) e ficam
  fora deste requisito.
- Alteração no Agente Coletor — nenhum novo dado precisa ser coletado.
- Notificações/alertas proativos (e-mail, push) quando uma OS entra em uma
  faixa — trabalho futuro.
- Configuração dos limites de dias (5/10/30) pelo usuário — nesta fase os
  três limites são fixos; parametrização é pós-MVP.

## Requisitos funcionais

### RF-027.1 - Definição de "OS aberta"

Uma OS é considerada **aberta** quando seu `Status` atual **não** pertence
ao conjunto de status terminais conhecidos (`"Payment Approved"`,
`"cancelled"`, `"closed"`). OS terminais nunca entram na contagem de
nenhuma das três faixas, independentemente de há quanto tempo foram
criadas.

### RF-027.2 - Cálculo da data de abertura

A data de abertura de uma OS, para fins desta métrica, é
`WorkOrder.ProviderCreatedAt` quando presente; caso contrário,
`WorkOrder.CreatedAt`. A idade da OS é `agora (UTC) - data de abertura`.

### RF-027.3 - Contagem por faixa

A API deve expor a contagem de OS abertas (RF-027.1) do tenant cuja idade
(RF-027.2) seja maior que 5, 10 e 30 dias, respectivamente. As faixas são
cumulativas (uma OS com mais de 30 dias também é contada na faixa de mais
de 5 e mais de 10 dias).

### RF-027.4 - Exibição no Dashboard do Office

O Dashboard do Office deve exibir três novos cards com a contagem de cada
faixa (RF-027.3), seguindo o padrão visual dos demais cards de contagem já
existentes. Cada card deve ser clicável e navegar para uma lista das OS
que compõem aquela contagem (reaproveitando o componente de lista/tabela
de OS já existente no dashboard).

### RF-027.5 - Isolamento por tenant

A contagem e a lista respeitam o isolamento por tenant já estabelecido
(RF-013/RF-021) — um usuário só vê a contagem e as OS do(s) tenant(s) aos
quais pertence.

### RF-027.6 - i18n

Os rótulos dos três cards devem ser traduzidos para pt-BR, en-US e es-AR,
seguindo o padrão de i18n já usado no restante do Office.

## Critérios de aceite

1. Uma OS com status terminal (`"Payment Approved"`, `"cancelled"` ou
   `"closed"`) nunca é contada em nenhuma das três faixas, mesmo que tenha
   sido criada há mais de 30 dias.
2. Uma OS não terminal criada há mais de 5 dias (e menos de 10) é contada
   apenas na faixa "+5 dias".
3. Uma OS não terminal criada há mais de 30 dias é contada nas três
   faixas ("+5", "+10" e "+30 dias").
4. Quando `ProviderCreatedAt` é nulo, o cálculo usa `CreatedAt` como
   fallback sem erro.
5. O Dashboard do Office exibe os três cards com a contagem correta para o
   tenant autenticado, e clicar em um card leva à lista de OS
   correspondente.
6. Usuário sem vínculo com o tenant não consegue obter a contagem nem a
   lista de outro tenant (isolamento validado).
7. Rótulos dos três cards aparecem corretamente traduzidos nos três
   idiomas suportados.

## Fora de escopo / Trabalho futuro

- Reprodução completa do painel externo da cliente (campos de agendamento
  de próximo atendimento, motivo de pendência, responsabilidade do posto).
- Parametrização dos limites de dias pelo usuário.
- Notificações proativas por faixa.
