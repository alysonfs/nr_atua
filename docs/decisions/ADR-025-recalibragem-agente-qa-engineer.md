# ADR-025 - Recalibragem do agente qa-engineer (Queiroz)

## Status

Accepted

## Contexto

O agente `qa-engineer` (apelido "Queiroz") apresentou, em validações
recentes de escopo pequeno e bem delimitado (ex.: validar 4 cards de UI
mockados), um padrão de custo/tempo desproporcional ao risco da mudança:

- execuções que ficaram rodando por longos períodos sem produzir turnos
  observáveis, exigindo cancelamento;
- criação de scripts descartáveis fora do framework de testes (ex.:
  `node -e '...'` no terminal) para "double-checar" hipóteses matemáticas,
  em vez de expressar essas checagens como testes automatizados
  reaproveitáveis;
- tendência a tratar o checklist extenso do seu próprio agent definition
  (`.github/agents/qa-engineer.agent.md`, seções de API, frontend,
  backend, segurança, regressão) como um roteiro obrigatório a esgotar em
  toda validação, independentemente do tamanho real do escopo.

O usuário (dono do produto) relatou que esse agente "é o que mais dá
trabalho" no time Batuta e pediu revisão: remover o agente ou melhorar sua
interação. Optou-se por reformar em vez de remover.

## Decisao

Recalibrar o `qa-engineer` em vez de removê-lo do time Batuta:

1. Adicionar ao `qa-engineer.agent.md` uma seção `0. Regra de agilidade e
   escopo (prioridade máxima)`, posicionada antes de qualquer outra regra,
   estabelecendo:
   - validar apenas o que foi pedido no escopo da tarefa; as seções de
     API/frontend/backend/segurança/regressão são catálogo de referência,
     não checklist obrigatório em toda execução;
   - proibição de descoberta de ambiente por conta própria quando o
     caminho absoluto já foi informado (nada de `find /` ou varreduras de
     disco);
   - proibição de scripts descartáveis fora do framework de testes; toda
     checagem que valer a pena deve virar teste automatizado permanente,
     ou ser descartada/reportada como sugestão não-bloqueante;
   - preferência por uma única passada objetiva (ler diff, rodar
     testes/lint/build, reportar) em vez de ciclos repetidos de
     investigação;
   - timebox explícito: fechar como aprovado quando os comandos de
     validação passarem e não houver bug bloqueante, sem procurar
     problemas hipotéticos indefinidamente.
2. Trocar o modelo padrão do `qa-engineer` de "Claude Sonnet 5" para
   "Claude Haiku 4.5", alinhando custo/velocidade ao perfil de validações
   de rotina (mesmo padrão já usado por `product-analyst`,
   `documentation`, `release-versioning` e os agentes de marca).

## Motivos

- Preservar o papel de QA no fluxo Batuta (é autoridade de aprovação
  antes de conclusão de tarefa), mas com custo/tempo compatível com o
  risco real de cada mudança.
- Scripts descartáveis não deixam rastro nem protegem contra regressão
  futura; testes permanentes no repositório são reaproveitáveis e rodam
  no CI.
- Um checklist de 22 seções aplicado integralmente a toda tarefa,
  independente do tamanho, gera trabalho desproporcional e frustração do
  usuário.

## Alternativas consideradas

- **Remover o `qa-engineer` do time Batuta**: rejeitada. O papel de
  validação independente antes de commit/conclusão continua valioso;
  o problema identificado era de calibragem de instruções e modelo, não
  de necessidade do papel.
- **Reduzir uso sem alterar o agente**: rejeitada como solução única,
  pois não resolve a causa raiz (instruções empurrando para auditoria
  exaustiva); poderia ser combinada no futuro se a recalibragem não for
  suficiente.

## Consequências

- Validações futuras do `qa-engineer` devem ser mais rápidas e baratas
  para escopos pequenos, com relatórios objetivos.
- Caso o padrão de "ruminação"/custo desproporcional se repita mesmo após
  esta recalibragem, a alternativa de reduzir/remover o uso do agente
  volta a ser considerada.
