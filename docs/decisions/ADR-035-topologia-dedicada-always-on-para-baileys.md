# ADR-035 - Topologia Dedicada e Always-On para o Processo Baileys

## Status

**Proposed**

**Data:** 2026-10-01

## Contexto

O épico `docs/features/triagem-whatsapp-fase-1.md` (Draft) tem como premissa do PO rodar o Baileys (processo contínuo Node.js, canal WhatsApp) no EC2 existente.

O ADR-034 colocou API + Collector na mesma t3.micro (1 GiB) com swap 1 GiB, serviços systemd, apiRole compartilhada e ciclo efêmero por make up/make down.

**Medição de 2026-10-01** (`make ec2-exec`):
- RAM total: 916 MB
- Usada: 613 MB
- Disponível: 119 MB (~13%)
- Swap: 391/1023 MB (38%)
- atua-api: 45 MB / 20 tasks
- atua-collector: 610 MB / 94 tasks (Chromium/Playwright)

**Baileys** é estimado em 150–250 MB e exige:
- Processo contínuo (not ephemeral)
- Sessão persistente (credenciais / QR code)

**Referência:** ADR-023 define fila por MongoDB Change Streams (sem SQS). A decisão sobre a fila do épico não é objeto deste ADR.

**Escopo:** Apenas a topologia do Baileys. A escolha de fornecedor (Baileys vs WhatsApp Cloud API / Twilio / 360dialog) está fora do escopo e será decidida posteriormente.

## Decisão Proposta

A premissa "Baileys na mesma t3.micro colocada e sob make up/down" **não é viável**. Recomenda-se:

**Instância EC2 dedicada e always-on para o Baileys**, fora do escopo do `make up/make down` do ADR-034, com:
- IAM role própria (não reutilizar `apiRole`)
- Volume persistente para a sessão
- Processo de backup/restore da sessão
- Alvo próprio no Makefile com ciclo de vida independente

### Análise de Viabilidade nos 4 Eixos

#### Memória
**Inviável:** 119 MB livres + swap em 38%.
- Risco: OOM-kill do Collector ou thrashing com a adição de 150–250 MB do Baileys

#### Operação
**Inviável:** `make down/up` destruiria instância/volume, exigindo novo pareamento por QR code e derrubando o canal

#### Segurança
**Inviável:** Credenciais de sessão do WhatsApp sob a `apiRole` compartilhada ampliam o blast radius

#### Disponibilidade
**Inviável:** Queda da instância derrubaria API + Collector + WhatsApp juntos

## Alternativas Consideradas

**Custo mensal ESTIMATIVA** (sa-east-1, sem cotação oficial):

| Alternativa | Estimativa | Análise |
|---|---|---|
| (a) Upgrade EC2 t3.small/medium | ~US$ 15–17/mês vs ~US$ 7–8 atual | Resolve só memória; mantém conflito com `make down`; isolamento nulo |
| (b) **EC2 dedicada sempre ligada** | ~US$ 7–15/mês | **Recomendada** |
| (c) ECS Fargate dedicado | ~US$ 9–12/task + EFS | Exige armazenamento persistente e IaC novo |
| (d) Spot / menor | Economia limitada | Interrupção perde a sessão; inadequado |
| (e) WhatsApp Cloud API oficial | Variável por conversa | Apenas comparativo; decisão de fornecedor fora do escopo |

### Nota: Avaliação de "Tudo em Containers"

Registra-se a análise de containerizar API, Collector e Baileys:
- Containers não criam RAM; na t3.micro acrescentam ~30–50 MB de overhead apenas
- Docker Compose numa t3.small (~US$ 15–17, estimativa) resolveria memória, mas mantém conflito com `make down`
- ECS/Fargate por serviço (~US$ 30–40+, estimativa): exige IaC, registry de imagens e CI novos; desproporcional ao estágio MVP
- Baileys exigiria volume persistente de qualquer forma
- **Conclusão:** Não altera a recomendação de EC2 dedicada

## Impacto

- **Custo:** Custo fixo permanente adicional (quebra o modelo "liga só quando necessário")
- **Ciclo de vida:** `make up/down` não toca na instância do Baileys
- **Operação:** Novo processo de backup/restore da sessão
- **Identidade:** Nova role IAM

## Riscos

- Custo recorrente
- Canal não oficial (risco de banimento; questão contratual — ver épico)
- Perda de sessão exige novo QR code
- Operação adicional

## Pendências / Questões em Aberto

1. Confirmar dimensionamento real do Baileys (estimativa 150–250 MB)
2. Confirmar preços AWS reais (`aws-cost-monitor`)
3. Validar com `software-architect` o impacto na fila (ADR-023) e no contrato entre canal e API
4. Medição `free -m` posterior ao ciclo
5. Decisão de fornecedor do canal

## Nota de Escopo (2026-10-01)

Esta proposta (Status Proposed) **aplica-se à fase de produção da triagem WhatsApp**, não ao desenvolvimento.

Na fase de desenvolvimento, o Baileys executa **apenas durante o ciclo de operação da stack** (ADR-036, make up/make down), utilizando número de teste e sem EC2 dedicada.

**Questão em aberto para o `software-architect`:**

Avaliar a viabilidade de guardar a sessão do Baileys em armazenamento persistente **externo à stack** (ex.: MongoDB Atlas, conforme ADR-012) de modo que a sessão sobreviva a ciclos `make down/up` sem necessidade de novo pareamento por QR code.

**Status:** Viabilidade com a biblioteca Baileys não foi verificada. Requer análise técnica da API de sessão/autenticação do Baileys e integração com armazenamento externo.

### Atualização sobre persistência da sessão (2026-10-13)

O spike registrado na seção 15 do épico superou a hipótese de que a sessão
precisaria ser mantida no MongoDB Atlas. O ADR-038 formaliza a proposta atual:

- arquivos locais na máquina do desenvolvedor durante o desenvolvimento;
- volume EBS persistente para a futura topologia de produção;
- Secrets Manager e S3 não são stores primários;
- MongoDB Atlas só poderá ser reconsiderado após validação de uma
  implementação compatível e mantida.

Esta atualização não aprova a topologia always-on nem altera o status
`Proposed` deste ADR.

## Decisão do PO (2026-10-01)

O Product Owner **não aceita custo fixo adicional por ora**. A proposta de EC2 dedicada e always-on permanece com Status **Proposed** e é **adiada para a fase de produção** da triagem WhatsApp.

**Implicações:**
- A proposta **não deve ser implementada agora**.
- Desenvolvimento segue sob demanda, reutilizando a t3.micro existente do ADR-034, durante o ciclo `make up/make down`.
- A decisão e a implementação da topologia dedicada serão revisadas quando o épico transicionar para produção.
- Permanecem abertos: validação de custo real, persistência da sessão e operação contínua em produção.
