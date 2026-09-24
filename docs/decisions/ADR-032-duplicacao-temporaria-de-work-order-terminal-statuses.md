# ADR-032 - Duplicação Temporária de `WorkOrderTerminalStatuses` entre Collector e API

## Status

Accepted

## Contexto

RF-027 (OS abertas por tempo de abertura — aging) exige que a Master API
saiba quais status são "terminais" (`"Payment Approved"`, `"cancelled"`,
`"closed"`) para excluir OS já encerradas do cálculo de aging — tanto no
filtro `minAgeDays` de `GET /api/tenants/{tenantId}/work-orders` quanto no
novo `GET /api/tenants/{tenantId}/work-orders/aging-summary`.

Essa mesma lista de status terminais já existe em
`apps/collector/Atua.Collector/Consumer/WorkOrderTerminalStatuses.cs`, usada
pelo `ProviderInteractionConsumerWorker` (ADR-026/ADR-031) para decidir
quando parar de re-enriquecer uma OS via `queryOneWorkOrder`.

A Master API (`Atua.Api`) e o Collector (`Atua.Collector`) são dois
projetos .NET distintos, sem um pacote/projeto compartilhado (`shared`)
entre eles hoje. Criar essa infraestrutura de compartilhamento apenas para
uma constante de 3 strings é desproporcional ao problema atual.

## Decisão

Duplicar deliberadamente `WorkOrderTerminalStatuses` como uma classe
`internal`/`public static` independente em
`apps/api/Atua.Api/Domain/WorkOrders/WorkOrderTerminalStatuses.cs`, com o
mesmo conteúdo (`HashSet<string>` com `StringComparer.Ordinal`) da versão do
Collector, em vez de introduzir um projeto/pacote compartilhado entre
`Atua.Api` e `Atua.Collector` neste momento.

### Critério objetivo para reavaliar (unificar)

Esta duplicação deve ser eliminada — via extração para um projeto
compartilhado (ex.: `Atua.Shared.Domain` ou similar) — assim que **qualquer**
uma das condições abaixo ocorrer primeiro:

1. Uma **terceira** ocorrência do mesmo conceito (lista de status
   terminais de OS) precisar existir em um terceiro projeto/serviço; ou
2. As duas listas **divergirem** (por exemplo, uma nova integração de
   provedor introduzir um status terminal diferente e apenas um dos dois
   projetos for atualizado); ou
3. Qualquer outra constante/regra de domínio de OS precisar ser
   compartilhada entre `Atua.Api` e `Atua.Collector`, tornando um projeto
   compartilhado proporcional ao número de itens duplicados.

Até lá, cada cópia deve ser mantida sincronizada manualmente quando a lista
de status terminais mudar, e este ADR deve ser referenciado nos comentários
de ambos os arquivos.

## Consequências

* **Positivas**: nenhuma infraestrutura nova (projeto/pacote compartilhado,
  versionamento de pacote interno) é necessária agora; a mudança fica
  isolada a um único arquivo pequeno em cada projeto.
* **Negativas**: risco de divergência silenciosa entre as duas cópias caso
  a lista de status terminais mude e apenas um dos arquivos seja
  atualizado. Mitigado por: (a) os testes de ambos os projetos cobrirem o
  comportamento de exclusão de status terminais; (b) o critério objetivo
  acima, que força a reavaliação assim que o custo da duplicação superar o
  custo de uma abstração compartilhada.
