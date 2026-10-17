# ADR-038 - Persistência da Sessão Baileys em Desenvolvimento e Produção

## Status

**Accepted** — aprovado pelo Product Owner em 2026-10-13.

## Contexto

O Baileys atualiza credenciais e chaves da sessão com frequência. O spike
registrado no épico confirmou que o mecanismo oficial
`useMultiFileAuthState` usa arquivos locais e que duas instâncias não devem
operar simultaneamente com a mesma sessão.

O desenvolvimento segue sob demanda, conforme ADR-036, e o PO não aprovou
custo fixo adicional. A topologia always-on de produção continua proposta no
ADR-035.

## Decisão proposta

Adotar estratégias diferentes por ambiente:

- **Desenvolvimento:** arquivos locais na máquina do desenvolvedor, fora do
  ciclo `make up`/`make down`.
- **Produção:** volume EBS persistente anexado a uma única instância do canal,
  quando a topologia always-on do ADR-035 for aprovada.

A sessão não será armazenada primariamente no Secrets Manager nem no S3.
MongoDB Atlas não será adotado nesta fase porque não há store oficial do
Baileys validado pelo projeto.

Deve existir somente uma instância ativa por sessão. Backup e restauração do
volume serão definidos antes da produção.

## Motivos

- Arquivos locais seguem o mecanismo oficial e não geram custo adicional no
  desenvolvimento.
- EBS oferece semântica de filesystem adequada às escritas frequentes.
- A decisão evita depender de bibliotecas comunitárias de sessão MongoDB sem
  avaliação.
- Secrets Manager e S3 são inadequados como store primário para atualizações
  por mensagem.

## Alternativas consideradas

### MongoDB Atlas

Reaproveitaria infraestrutura existente, mas depende de implementação de
terceiros ainda não avaliada.

### AWS Secrets Manager

Adequado a segredos com baixa frequência de atualização, não ao conjunto de
chaves mutáveis da sessão.

### Amazon S3

Útil como backup frio, mas não como filesystem primário consistente para as
escritas frequentes.

### Sessão efêmera em produção

Foi rejeitada porque exigiria novo pareamento após reinício ou recriação.

## Consequências

### Positivas

- Desenvolvimento sem infraestrutura adicional.
- Caminho de produção compatível com a topologia proposta no ADR-035.
- Menor risco de incompatibilidade com o comportamento oficial do Baileys.

### Negativas

- A sessão de desenvolvimento depende da máquina local.
- Produção exige backup, criptografia, controle de acesso e recuperação do
  volume.
- Não há alta disponibilidade ativa/ativa para uma mesma sessão.
- A parte de produção não pode ser implementada antes da aprovação do
  ADR-035.

## Agentes envolvidos

- `orchestrator`
- `software-architect`
- `backend-engineer` (spike)
- `aws-architect` (análise anterior da topologia)

## Data

2026-10-13

## Relações

- Complementa o ADR-035.
- Respeita a operação de desenvolvimento do ADR-036.
- Formaliza a recomendação da seção 15 do épico.
