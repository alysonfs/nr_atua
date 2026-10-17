# ADR-036 - Operação sob Demanda do Ambiente AWS (Make Up/Make Down) para Controle de Créditos

## Status

**Accepted** — Decisão do product owner em 01/10/2026.

## Contexto

A conta AWS é financiada com créditos (US$ 160 emitidos; saldo estimado US$ 83,48 em 01/10/2026; expiram 28/08/2027). O crédito AWS Free Tier de US$ 100 consumiu aproximadamente US$ 76,52 (ESTIMATIVA). Existem três créditos "Explore AWS" de US$ 20 ainda intactos; produtos aplicáveis não foram verificados.

Consumo bruto medido em setembro (ESTIMATIVA): ~US$ 2,40/dia (~US$ 71,86 no mês).
Distribuição:
- RDS: ~US$ 1,06/dia (~44%) — db.t3.micro ~0,91 + storage gp2 ~0,15
- EC2 compute: ~US$ 0,81/dia
- IPv4 público: ~US$ 0,24-0,36/dia
- EBS: ~US$ 0,20/dia
- Secrets Manager: ~US$ 0,05/dia
- KMS: ~US$ 0,03/dia

**Runway estimado: ~35 dias** (esgota ~5-6 de novembro/2026) no ritmo de setembro, quando a stack ficou ligada continuamente. A topologia colocada (ADR-034, 30/09/2026) ainda não dispõe de dados completos de consumo.

**Nota**: O "custo fixo ~US$ 2,20" citado em ADR-023 está desatualizado. Este ADR não altera o ADR-023.

O processo `make down` já realiza snapshot final e destrói RDS e stack. Custos remanescentes após `make down`: Secrets Manager (~US$ 1,20/mês) e snapshots (centavos).

## Decisão

Adotar o **modelo 2 — operação sob demanda**: o ambiente fica desligado (`make down`) por padrão e é ligado (`make up`) apenas para testes, demonstrações ou desenvolvimento, sendo desligado ao fim do uso. O ambiente **não é uma instância de produção contínua** nesta fase.

**Consequências operacionais:**
- Coleta recorrente (RF-025) e Collector não executam com a stack desligada
- Endereço IP muda a cada `make up`
- Restauração do RDS é feita via snapshot

## Alternativas Consideradas

1. **Operação em horário comercial**: stack ligada ~45% das horas; economia estimada e ciclo de subida não foram medidos.
2. **Sempre ligado com ajustes de otimização**: redimensionamento para db.t4g.micro, EBS para gp3; economia estimada (ESTIMATIVA) US$ 4-8/mês.

## Impacto

**Economia**:
- Maior economia possível dos créditos na fase atual.

**Limitações**:
- Não resolve sozinho o Baileys (ADR-035, status Proposed, operação always-on, custo fixo estimado ~US$ 13-15/mês), que fica em tensão com este modelo sob demanda e deve ser reavaliado.
- O épico de triagem WhatsApp (`docs/features/triagem-whatsapp-fase-1.md`) exige canal contínuo, logo será necessário revisar este modelo antes da produção.

## Pendências

- Medir custo/tempo de um ciclo `make up` + `restore`
- Avaliar agendamento automático de `make down` para evitar esquecimento
- Reconsultar o ritmo de custo em ~7 dias
- Verificar produtos aplicáveis dos créditos "Explore AWS"
- Otimizações de RDS (t4g.micro, gp3) permanecem como melhorias futuras

## Nota de Escopo (2026-10-01)

Este modelo sob demanda (make up/make down com stack desligada por padrão) é **válido para a fase de desenvolvimento e validação** do projeto.

**NÃO é aplicável à operação em produção da triagem WhatsApp**, que exige:
- Canal contínuo
- API sempre disponível
- Banco de dados sempre acessível
- Fila processando (ADR-023)
- Prazo de 48h do template Midea

Entrar em produção com a triagem exigirá **ADR próprio de operação sempre ligada**, com decisão de responsabilidade de custos (ATYNO ou posto; questão em aberto no épico `docs/features/triagem-whatsapp-fase-1.md`, questão 9).

**Custo estimado para operação sempre ligada:**
- Infraestrutura AWS contínua: ~US$ 72/mês (medição setembro)
- Baileys dedicado (ADR-035): ~US$ 13–15/mês (ESTIMATIVA)
- **Total estimado: ~US$ 85–87/mês**

Ambas as estimativas são preliminares e dependem de confirmação de preços reais e medições em produção.
