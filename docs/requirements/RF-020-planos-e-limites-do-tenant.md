# RF-020 - Planos e limites do tenant

Status: `Proposto`

## Objetivo

Substituir o modelo atual de "Trial" como entidade própria ligada ao usuário
por um catálogo de **Planos** (`Plan`), do qual o Trial passa a ser apenas o
plano gratuito de 7 dias. O plano vigente de um tenant define seus limites de
uso (quantidade de integrações e de usuários) e sua relação comercial com o
ATUA.

## Escopo

Catálogo de planos, atribuição de um plano ao tenant no momento da criação
(onboarding) do tenant, aplicação dos limites de plano nas ações de convite de usuário
e ativação de integração, e consulta do plano ativo pelo Office. Não inclui
cobrança, checkout de pagamento nem upgrade/downgrade self-service (fora do
MVP; hoje só existem os planos `Trial` e `Essencial`, ambos com os mesmos
limites).

## Requisitos funcionais

### RF-020.1 - Catálogo de planos

O sistema deve manter um catálogo de planos (`Plan`) com, no mínimo:
código, nome, se é gratuito, valor, duração em dias (nulo = sem expiração
automática), limite máximo de integrações ativas e limite máximo de
usuários (memberships) por tenant. No MVP existem dois planos:

- `trial`: gratuito, expira em 7 dias, até 2 integrações, até 5 usuários.
- `essencial` (nome provisório): pago, sem expiração automática, até 2
  integrações, até 5 usuários.

### RF-020.2 - Atribuição do plano ao tenant

Ao criar um tenant (RF-006), o sistema deve atribuir automaticamente o plano
`trial` como plano ativo, sem exigir nenhuma ação do usuário. A contagem do
prazo do Trial é partir do instante de confirmação de e-mail do
usuário (`EmailConfirmedAt`), preservando a regra já decidida em ADR-015 de
não reiniciar o prazo quando o tenant é criado depois da confirmação.

### RF-020.3 - Plano ativo único por tenant

Um tenant deve ter, a qualquer momento, no máximo um plano com status ativo.
Mudança de plano (ex.: upgrade futuro) deve encerrar o vínculo anterior e
criar um novo vínculo ativo, preservando o histórico de planos anteriores.

### RF-020.4 - Aplicação dos limites de integrações

Antes de habilitar uma nova integração (RF-006) para um tenant, o sistema
deve validar se a quantidade de integrações já ativas atingiu o limite do
plano vigente. Se atingido, a ativação deve ser recusada com mensagem
orientando o motivo (limite do plano).

### RF-020.5 - Aplicação dos limites de usuários

Antes de conceder acesso a um novo usuário no tenant (convite/membership),
o sistema deve validar se a quantidade de memberships ativos já atingiu o
limite de usuários do plano vigente. Se atingido, a operação deve ser
recusada com mensagem orientando o motivo.

### RF-020.6 - Consulta do plano ativo pelo Office

O Office deve poder consultar o plano ativo do tenant corrente (nome,
gratuito ou não, dias restantes quando aplicável, limites e quantidade
utilizada de integrações/usuários), para exibir a situação do plano ao
usuário.

### RF-020.7 - Bloqueio de coleta por plano expirado

Um plano expirado (ex.: Trial vencido sem conversão) não bloqueia login nem
acesso ao Office, apenas a ativação do Agente Coletor e a execução de coleta
(mesma regra de RF-005.4/RF-008, agora avaliada em função do plano ativo em
vez da antiga entidade de Trial).

## Regras de negócio

- RN-020.1: os limites de um plano (`MaxIntegrations`, `MaxUsers`) são dados
  de configuração do plano, não valores fixos em código, para permitir criar
  novos planos com limites diferentes sem alteração de schema.
- RN-020.2: o histórico de planos do tenant deve ser preservado (nunca
  apagado), mesmo após a troca de plano ativo.
- RN-020.3: o cálculo de expiração do plano gratuito permanece em UTC,
  conforme ADR-015.
- RN-020.4: somente o membership com papel `Owner` (responsável legal pelo
  tenant) pode contratar, trocar ou cancelar o plano do tenant. Demais papéis
  (`Admin`, `Default`, `Technical`) podem apenas visualizar o plano ativo
  (RF-020.6).
- RN-020.5: um tenant nunca fica sem `TenantPlan`. A expiração de um plano
  não remove nem apaga o vínculo — apenas transiciona seu `Status` (ex.:
  `Active` → `Expired`). Enquanto não houver um novo plano ativo, o tenant
  permanece "congelado": nada é excluído, apenas novas ações de consumo
  (convite de usuário, ativação de integração, coleta) ficam bloqueadas.

## Casos de borda

- Tenant tenta ativar uma 3ª integração estando no limite de 2 → deve ser
  recusado, orientando a necessidade de upgrade de plano (mensagem, sem
  fluxo de upgrade automatizado no MVP).
- Tenant tenta convidar um 6º usuário estando no limite de 5 → deve ser
  recusado pelo mesmo motivo.
- Plano gratuito expira com integrações e usuários já configurados → dados e
  configurações permanecem acessíveis; apenas ativação/coleta são bloqueadas
  (estado "congelado", RN-020.5).
- Membership com papel `Admin`, `Default` ou `Technical` tenta trocar o plano
  do tenant → deve ser recusado por falta de autoridade (RN-020.4).

## Perguntas em aberto

- Nome definitivo do plano pago (mantido `essencial` como provisório).
- Fluxo de upgrade/downgrade de plano (fora do MVP; considerar em requisito
  futuro).
