# RF-021 - Papéis de usuário do tenant

Status: `Proposto`

## Objetivo

Ampliar o modelo de papéis de membership do tenant, hoje restrito a
`Owner`/`Admin`, para refletir os perfis reais de uso do Office: **Owner**,
**Admin**, **Default** e **Technical**. O limite de usuários do plano
(RF-020.5) conta qualquer membership ativo, independentemente do papel.

## Escopo

Definição dos papéis de membership e das permissões associadas dentro de um
tenant. Não inclui gestão de papéis entre tenants diferentes nem hierarquia
de organizações (fora do escopo do MVP).

## Requisitos funcionais

### RF-021.1 - Catálogo de papéis

Cada membership de um usuário em um tenant deve ter um papel entre:

- **Owner**: responsável legal pelo tenant perante o ATUA. Único papel com
  autoridade para contratar, trocar ou cancelar o plano (RN-020.4), e para
  promover/rebaixar o papel `Admin`. Herda todas as permissões de Admin.
- **Admin**: usuário do departamento de administração, de confiança do
  Owner, responsável por monitorar e gerenciar os demais usuários do tenant
  (convidar, remover, alterar papel de `Default`/`Technical`), gestão de
  integrações e credenciais, e consulta de plano. Não pode alterar o plano
  do tenant nem o papel de outro `Admin`/`Owner`.
- **Default**: uso operacional do dia a dia — consulta a work orders,
  histórico e status das integrações. Sem gestão de usuários, credenciais
  ou plano.
- **Technical**: pendente de especificação, não crítico ou bloqueante;


### RF-021.2 - Usuário criador do tenant

O usuário que cria o tenant (fluxo de onboarding, RF-006) recebe
automaticamente o papel **Owner**. Um tenant deve ter exatamente um `Owner`
a qualquer momento; transferência de titularidade (mudar quem é o Owner) é
um fluxo sensível fora do escopo deste requisito.

### RF-021.3 - Alteração de papel

O `Owner` pode alterar o papel de qualquer membership do tenant, inclusive
promover/rebaixar `Admin`. Um `Admin` pode alterar apenas memberships com
papel `Default` ou `Technical`, nunca o `Owner` nem outro `Admin`. Não é
permitido remover, rebaixar ou transferir o papel `Owner` por meio desta
operação, para isso pode inferir um "peso" por papel. `Owner` tem peso 100,
`Admin` peso 50, `Default` ou `Technical` tem peso 30 e assim consegue
medir quem pode alterar quem.

### RF-021.4 - Limite de usuários por plano

A soma de memberships ativos de todos os papéis não pode exceder
`Plan.MaxUsers` do plano vigente do tenant (RF-020.5).

## Regras de negócio

- RN-021.1: todo tenant deve ter exatamente um membership com papel `Owner`
  em todos os momentos.
- RN-021.2: a migração dos dados existentes mantém o papel `Owner` atual
  como `Owner`; memberships com papel `Admin` atual permanecem `Admin`.
- RN-021.3: cada papel terá um peso para definir autoridade sobre outro papel;

## Casos de borda

- Owner tenta remover a si mesmo ou rebaixar seu próprio papel → deve ser
  recusado (RN-021.1).
- Admin tenta alterar o papel do Owner ou de outro Admin → deve ser
  recusado por falta de autoridade.
- Usuário com papel Default tenta cadastrar credencial de integração → deve
  ser recusado por falta de permissão.
- Usuário com papel Technical tenta convidar um novo usuário → deve ser
  recusado por falta de permissão.
