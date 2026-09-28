# Insumos — Midea Carrier

Documentos recebidos da Midea Carrier que servem de referência para
regras de negócio do ATUA/ATYNO. **Não são requisitos formais ainda** —
virarão requisitos quando o `product-analyst` os processar.

## Arquivos

| Arquivo | Origem | Data | Status | Notas |
|---|---|---|---|---|
| CA_2025_0013-Pagamento-Servicos-Rev5.pdf | Evelyn Marins | 12/08/2026 | bruto | Página 2 não extraível por texto (fonte sem ToUnicode CMap); conteúdo recuperado via imagem |
| Status-e-Responsabilidades.csv | — | — | bruto | 36 SubStatus; item 24 malformado; 3 colunas (Status de Pendente, Motivo de Uso, Responsabilidade) |

## Resumo do conteúdo

### CA_2025_0013 — Pagamentos de serviços em garantia

Comunicado administrativo da Midea Carrier aplicável a Springer Carrier e
Climazon Industrial. Define janelas de pagamento (dias 10 e 25), emissão
de pedidos/extratos (dias 05 e 15), composição de lotes por período de
finalização/aprovação em auditoria, regra de vencimento conforme data de
emissão da NF, e prazo-limite de 90 dias para inclusão de NF.

### Status e Responsabilidades — catálogo de 36 SubStatus

Catálogo de SubStatus de OS pendentes do iService, com descrição de uso
e responsável pela pendência. Responsáveis: Posto Autorizado,
Midea-Callcenter, Midea-Planejamento, Midea-Correios, Midea-Jira,
Midea-Juridico, Midea-Suporte técnico.

O detalhe está no próprio CSV (`Status-e-Responsabilidades.csv`).

## Pendências conhecidas

1. **Inconsistência de datas no CA_2025_0013**: emissão 12/08/2026,
   vigência 01/01/2026, calendário "Programação de Pagamentos para 2026".
   Provável erro de digitação (2027?). Confirmar com Evelyn Marins.
2. **Página 2 do CA_2025_0013 não é extraível por texto** (fonte sem
   ToUnicode CMap). Conteúdo foi recuperado via imagem fornecida pelo
   usuário. O PDF permanece como está.
3. **Item 24 do catálogo de SubStatus malformado** — parece junção de
   duas linhas (`Suporte Técnico - suporte concluído aguardando atuação
   do Posto Autorizado` + `Técnico - reparo/visita em andamento`).
   Preservado fiel ao original.
4. **Calendário visual de 2026** (página 2) não transcrito em detalhe —
   as 12 mini-tabelas mensais têm células coloridas por categoria, sem
   legenda textual detalhada. As regras gerais (i)–(iv) são a fonte
   primária; o calendário é provavelmente derivado.
5. **Conflito de nomenclatura de pasta**: `docs/marketing/`
   (context-ia-externa.md) vs `docs/brand/` (protocols/communication.md
   §12). Decisão pendente do `orchestrator`.

## Escopo

Estes insumos **não viram requisito formal** até o `product-analyst`
processá-los. Documentos curados irão para `docs/features/` somente
quando isso acontecer.