# ADR-005 - Tenancy, memberships e integracoes

## Status

Accepted

## Contexto

O ATUA precisa permitir que uma empresa possua varios usuarios internos e que
um mesmo usuario atue em mais de uma empresa. A modelagem anterior vinculava o
usuario diretamente a um tenant e limitava o MVP a um unico Owner por usuario.

O cadastro inicial coleta apenas e-mail e senha, enquanto nome e CNPJ sao
necessarios para criar uma empresa e configurar sua primeira integracao.

## Decisao

`Tenant` e a propria empresa cliente do ATUA. Nao existira uma entidade
`Company` separada no MVP. O Tenant possui UUIDv7, nome, CNPJ normalizado e
unico, fuso horario, plano e validade.

`User` e uma identidade global, com UUIDv7, nome, e-mail unico e credenciais.
Ele nao possui `tenant_id` nem papel local.

`TenantMembership` associa usuarios e tenants. O papel `OWNER` ou `ADMIN`
pertence ao membership, e nao ao usuario. Cada tenant deve ter exatamente um
membership ativo `OWNER`; um usuario pode possuir memberships em varios tenants.
`ROOT` permanece uma permissao global para o Manager.

O cadastro e a confirmacao de e-mail criam um usuario e iniciam seu Trial. Ao
configurar a primeira integracao, o usuario informa nome e CNPJ, cria o Tenant,
recebe um membership `OWNER` e associa o Trial existente ao Tenant, sem reiniciar
o prazo de sete dias em UTC.

`IntegrationProvider` e um catalogo global de sistemas externos, com UUIDv7,
nome, fabricante, URL base e estado ativo. `Integration` representa a
configuracao de um Tenant para um Provider, com UUIDv7, estado de ativacao e
referencia a credenciais cifradas. Credenciais iService nao podem ser guardadas
em texto claro na entidade, na API ou no frontend.

```text
User
  └─< TenantMembership >─ Tenant
                               └─< Integration >─ IntegrationProvider
```

## Motivos

- Um usuario pode atender varias empresas sem duplicar conta ou e-mail.
- Tenant concentra a identidade da empresa e elimina uma relacao 1:1 redundante.
- Memberships tornam ownership e futuras transferencias rastreaveis.
- Integrations isolam credenciais e configuracoes por empresa e provedor.
- O Trial pode iniciar apos confirmacao de e-mail sem exigir dados empresariais
  no cadastro.

## Alternativas consideradas

### Company separada de Tenant

Rejeitada no MVP porque a relacao 1:1 adicionaria uma entidade sem nova fronteira
de isolamento ou comportamento.

### Tenant com user_owner_id

Rejeitada porque duplicaria a fonte de verdade do Owner em relacao ao
`TenantMembership` e tornaria transferencias inconsistentes.

### Papel OWNER diretamente em User

Rejeitada porque impede que o mesmo usuario tenha papeis diferentes em empresas
distintas.

## Consequencias

- A autorizacao deve resolver o tenant ativo e validar membership no servidor.
- JWTs nao devem tratar um unico `tenant_id` como verdade permanente de acesso.
- CNPJ deve ser validado e ter unicidade global antes de criar Tenant.
- Credenciais e sessoes de integracao continuam sob as regras de cifra da
  ADR-004.
- Transferencia de Owner, convites e regras detalhadas de ativacao de membros
  serao definidos quando a funcionalidade for implementada.

## Agentes envolvidos

- Usuario: definicao de tenancy, memberships e integracoes.
- software-architect: arquitetura e limites de autorizacao.
- backend-engineer: modelagem e implementacao.

## Data

2026-08-29

## Substitui

Substitui as regras de tenancy e papel local da ADR-004. Mantem suas decisoes
sobre sessao, senha, criptografia e segredos.
