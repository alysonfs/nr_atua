# RF-031 - SubStatus da OS (`pendingReason`) e Responsável

Status: `Proposto`

**Data:** 2026-09-26

## História de Usuário

Como usuário do Office,
eu quero ver o motivo detalhado (substatus) da pendência de uma OS e quem
é o responsável por resolvê-la,
para que eu saiba se a próxima ação é do Posto Autorizado ou de alguma
área da Midea (Callcenter, Planejamento, Jurídico, Correios, Suporte
Técnico), sem precisar consultar o iService diretamente.

## Contexto e motivação

O iService expõe um campo de motivo de pendência — a cliente identifica
como `pendingReason` — que hoje **já é lido pelo Collector**, mas de forma
incorreta: `IServiceProviderInteractionOrderAdapter` inclui `pendingReason`
na lista de campos candidatos a **sintoma**
(`SymptomCandidateFields`), quando na verdade é o substatus da OS, um
conceito totalmente diferente. RF-028 já remove `pendingReason` dessa
lista; este RF define o tratamento correto dele como substatus dedicado.

A cliente forneceu um catálogo de 36 substatus conhecidos. Esses 36
valores mapeiam para apenas **7 responsáveis distintos** (não 36 nem 35
responsáveis — o número de valores de substatus é maior que o número de
responsáveis, já que vários substatus compartilham o mesmo responsável):
Posto Autorizado, Midea - Callcenter, Midea - Planejamento, Midea -
Correios, Midea - Jira, Midea - Jurídico, Midea - Suporte Técnico.

## Objetivo

Capturar o `pendingReason` do provedor como um campo próprio (`SubStatus`)
em `WorkOrder`, e mapear cada valor conhecido a um responsável, para uso
em indicadores (RF-032) e na página de detalhes da OS.

## Escopo

### O que É RF-031

- Novo campo `WorkOrder.SubStatus`, populado a partir de `pendingReason`
  do provedor.
- Catálogo de mapeamento substatus → responsável, com os 35 valores já
  fornecidos pela cliente.
- Regra explícita para substatus que não estejam no catálogo conhecido
  (não podem ser descartados/ocultados silenciosamente).

### O que NÃO é RF-031

- Alteração de fluxo de trabalho ou automação de ação por responsável
  (ex.: notificar automaticamente "Midea - Jurídico") — apenas exibição/
  indicador nesta fase.
- Tradução/i18n do texto de cada substatus (são valores em português,
  específicos deste provedor/cliente — DP já resolvida em RF-017 de que
  status é string livre por provedor).

## Requisitos funcionais

### RF-031.1 - Captura do campo `SubStatus`

O Collector deve extrair `pendingReason` do payload do provedor e
persisti-lo em `WorkOrder.SubStatus` (nullable — nem toda OS tem
pendência, ex.: uma recém-criada ou já concluída).

### RF-031.2 - Catálogo de substatus e responsáveis

Deve existir um mapeamento (tabela/dicionário, não hardcoded em múltiplos
lugares) entre `SubStatus` e responsável, com os seguintes 36 valores de
substatus conhecidos, agrupados em apenas 7 responsáveis distintos:

| substatus | responsável |
|---|---|
| Acordo de ressarcimento - pendente de coleta | Posto Autorizado |
| Acordo de ressarcimento - processo em andamento | Midea - Callcenter |
| Acordo de troca - pendente de execução de troca | Posto Autorizado |
| Acordo de troca - pendente de remessa do novo produto | Midea - Planejamento |
| Acordo de troca - processo em andamento | Midea - Callcenter |
| Acordo de troca - produto em trânsito | Midea - Planejamento |
| Call Center - aguardando resposta de Aprovação Especial | Midea - Callcenter |
| Call Center - Aprovação Especial respondida | Midea - Callcenter |
| Call Center - negociação de acordo em andamento | Midea - Callcenter |
| Consumidor - aguardando confirmação de agendamento | Posto Autorizado |
| Consumidor - contato em andamento | Posto Autorizado |
| E-Ticket - aguardando entrega do produto pelo Correios | Midea - Correios |
| Jira - aguardando resposta de suporte | Midea - Jira |
| Jira - suporte concluído aguardando atuação do Posto Autorizado | Posto Autorizado |
| Jurídico - aguardando resposta de suporte | Midea - Juridico |
| Jurídico - suporte concluído aguardando atuação do Posto Autorizado | Posto Autorizado |
| Orçamento - aguardando aprovação do consumidor | Posto Autorizado |
| Orçamento - aprovação de orçamento para pagamento | Posto Autorizado |
| Orçamento - rejeição de orçamento | Posto Autorizado |
| Peça - material em designação ao técnico | Posto Autorizado |
| Peça - material entregue | Posto Autorizado |
| Peça - pendência de entrega | Midea - Planejamento |
| Suporte Técnico - aguardando resposta de suporte | Midea - Suporte tecnico |
| Suporte Técnico - suporte concluído aguardando atuação do Posto Autorizado | Posto Autorizado |
| Técnico - reparo/visita em andamento | Posto Autorizado |
| Técnico - visita futura confirmada | Posto Autorizado |
| Triagem - execução em andamento | Posto Autorizado |
| Balcão - Aguardando retirada do cliente | Posto Autorizado |
| Balcão - Reparo concluído | Posto Autorizado |
| Domicílio - Atendimento realizado | Posto Autorizado |
| Domicílio - Consumidor ausente | Posto Autorizado |
| Domicílio - Dificuldade de acesso ao consumidor | Posto Autorizado |
| Domicílio - Orientação ao consumidor | Posto Autorizado |
| Domicílio - Recusa de visita pelo consumidor | Posto Autorizado |
| E-Ticket - Aguardando retirada do Correios para devolução | Midea - Correios |
| E-Ticket - Reparo concluído | Posto Autorizado |

> **Confirmado pela cliente (2026-09-26):** o item original "Suporte
> Técnico - suporte concluído aguardando atuação do Posto Autorizado
> Técnico - reparo/visita em andamento" é de fato dois substatus
> distintos colados na mesma linha do CSV fonte (`docs/_inputs/vendor-
> midea-carrier/Status e Responsabilidades.csv`, linha 25), confirmado
> pela descrição composta de duas frases ("Caso respondido pelo Suporte
> Técnico após escalada." + "Caso com execução de visita no dia atual ou
> reparo sendo realizado em tempo real.") — mantida a separação em dois
> itens já feita no catálogo acima ("Suporte Técnico - suporte
> concluído..." e "Técnico - reparo/visita em andamento").

### RF-031.3 - Substatus desconhecido

Um valor de `pendingReason` que não esteja no catálogo (RF-031.2) deve
ainda assim ser persistido em `WorkOrder.SubStatus` (não descartado), mas
tratado como "Responsável desconhecido"/"Não mapeado" nos indicadores que
dependem do responsável (RF-032), com registro (log) para acompanhamento
e futura atualização do catálogo.

### RF-031.4 - Extensão do gatilho de `work_order_histories` para incluir mudança de substatus e de próximo atendimento

Hoje `WorkOrderPgRepository.ProcessInteractionAsync` só insere uma nova
linha em `work_order_histories` quando `Status` muda (`isNew ||
previousStatus != order.Status`). Com a introdução de `SubStatus`
(RF-031) e `NextAppointmentAt` (RF-030), a cliente confirmou que um novo
`visitStartTime` pode ser informado mesmo quando o `Status` não muda,
acompanhando uma mudança de `SubStatus` (`pendingReason`). O gatilho
deve ser estendido para disparar quando **qualquer um** dos três campos
mudar — `Status`, `SubStatus` ou `NextAppointmentAt` —, não apenas
`Status`, sob pena de perder o histórico do agendamento e do substatus
anteriores.

## Critérios de aceite

1. Uma OS cujo payload traga `pendingReason` = "Peça - pendência de
   entrega" tem `SubStatus` = esse valor e responsável resolvido como
   "Midea - Planejamento".
2. Uma OS sem `pendingReason` no payload tem `SubStatus` nulo.
3. Uma OS com `pendingReason` fora do catálogo tem o valor persistido em
   `SubStatus`, aparece como "não mapeado" em indicadores por
   responsável, e gera um registro para revisão do catálogo.
4. Uma OS cujo `Status` permanece igual, mas cujo `SubStatus` muda entre
   duas interações, gera uma nova linha em `work_order_histories`
   (RF-031.4).
5. Uma OS cujo `Status` e `SubStatus` permanecem iguais, mas cujo
   `NextAppointmentAt` muda entre duas interações, também gera uma nova
   linha em `work_order_histories` (RF-031.4).

## Decisões (cliente, 2026-09-26)

- Confirmado o split da linha colada "Suporte Técnico - suporte
  concluído..." / "Técnico - reparo/visita em andamento" em dois itens
  distintos do catálogo (RF-031.2).
- Confirmado que "Midea - Jira" e "Midea - Jurídico" são responsáveis
  **distintos** (não é erro de digitação) — o catálogo (RF-031.2) e a
  contagem de 7 responsáveis já refletiam essa distinção corretamente;
  nenhuma alteração necessária no catálogo ou no total.
