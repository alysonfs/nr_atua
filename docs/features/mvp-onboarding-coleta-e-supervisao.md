# MVP - Fluxo de onboarding, coleta e supervisao

## Fluxo principal

```text
Landing
  |
  v
Cadastro por e-mail e senha
  |
  v
Confirmacao de e-mail e Trial de 7 dias
  |
  v
Office: configuracao iService
  |
  v
Validacao de sessao iService
  |
  v
Ativacao do Agente Coletor
  |
  v
Coleta imediata e atualizacoes recorrentes de OS
```

O cadastro Trial nao solicita CNPJ ou outros dados empresariais. Esses dados
serao solicitados somente quando o cliente contratar um plano mensal.

## Comportamento do Agente Coletor

O Agente Coletor inicia desativado e so pode ser ativado apos a validacao de
credenciais iService. Ele atua somente em leitura, busca as OS nos status
suportados, registra o estado atual e mantem o historico de observacoes pelo
ATUA.

Quando uma OS mudar de status entre duas coletas, o ATUA deve preservar o
estado atual mais recente e incluir a nova observacao no historico da mesma OS.
Ao expandir a OS, o cliente deve visualizar suas observacoes em ordem
cronologica. Uma OS ausente em coleta posterior nao recebe automaticamente um
novo status.

O ATUA trata Designado, Em Processamento, Pendente, Concluido e Cancelado como
estados de OS. Em Processamento corresponde a `accepted` no iService. A sessao
ativa indica apenas que o Agente Coletor esta autenticado no provedor.

## Visao de superadministracao

O Manager permite ao superadministrador visualizar clientes, Trial, resultado
e data da ultima validacao do iService e estado do Agente Coletor. Credenciais,
cookies e tokens nunca sao exibidos. O Manager tambem informa que a escrita no
iService esta bloqueada; esse estado e somente para visualizacao. O usuario
`ROOT` pode estender, renovar ou converter planos, preservando tenant e
historico do cliente.

## Referencias

- Requisitos: `docs/requirements/mvp-onboarding-coleta-e-supervisao.md`.
- Arquitetura: `docs/decisions/ADR-003-backend-master-api-e-agente-coletor.md`.
- Frontends: `docs/decisions/ADR-002-kit-e-aplicativos-frontend.md`.
