---
name: atua-seed-collector-credentials
description: Use quando o Collector der 401 Unauthorized ao chamar a API (/claim, /complete, /eligibility) por falta de credenciais em service_credentials, ou quando precisar listar/verificar quais credenciais de serviço já existem para um tenant/integration. Conecta diretamente no Postgres real (RDS) via Postgres__ConnectionString já exportado no ambiente do usuário — não usa Docker nem túnel SSH.
---

# Seed de Collector Service Credentials

## Por que este skill existe

O Collector autentica suas chamadas HTTP para a API (`/claim`, `/complete`,
`/eligibility`) com um Bearer token cujo hash SHA256 precisa existir, não
revogado, na tabela `service_credentials`. **Nenhum seeder/migration do
projeto popula essa tabela** — ela é criada vazia pela migration inicial e
precisa ser preenchida manualmente em cada ambiente de desenvolvimento.

Sintoma típico sem essa seed:

```
fail: Atua.Collector.Worker[0]
      [WORKER] Exceção não tratada no ciclo principal. Aguardando antes de reiniciar.
      System.Net.Http.HttpRequestException: Response status code does not indicate success: 401 (Unauthorized).
         at Atua.Collector.Api.CollectorApiClient.ClaimAsync(...)
```

Importante: pode haver linhas em `service_credentials` sem que ninguém tenha o
token em texto plano (ele nunca é armazenado, só o hash). Nesse caso o 401
persiste mesmo com credenciais "ativas" no banco — a única saída é gerar
credenciais novas.

## Pré-requisitos

- `psql` instalado (`brew install libpq`; o binário não entra no PATH por
  padrão no macOS, o script resolve isso sozinho em
  `/usr/local/opt/libpq/bin/psql` ou `/opt/homebrew/opt/libpq/bin/psql`).
- Variável de ambiente `Postgres__ConnectionString` já exportada no shell
  (formato Npgsql: `Host=...;Port=...;Database=...;Username=...;Password=...;SSL Mode=Require`).
  Isso normalmente já está no `~/.zshrc` do usuário — não é preciso Docker
  nem túnel SSH para este banco.
- `openssl` e `shasum` (padrão no macOS).

## Comandos disponíveis

### Listar credenciais existentes (somente leitura)

```bash
.github/skills/atua-seed-collector-credentials/scripts/list-collector-credentials.sh [--tenant-id <uuid>]
```

Mostra tenant, provider, scope, prévia do hash (8 primeiros chars) e status
(ativa/revogada). Nunca mostra o token — ele é irreversível a partir do hash.

### Gerar e inserir novas credenciais (GRAVA no banco)

```bash
.github/skills/atua-seed-collector-credentials/scripts/seed-collector-credentials.sh \
  [--tenant-id <uuid>] [--integration-id <uuid>] [--provider-id <uuid>] [--revoke-existing]
```

- Se `--tenant-id`/`--integration-id` forem omitidos e existir exatamente um
  tenant/uma integration no banco, o script resolve automaticamente (cenário
  comum no MVP com um único tenant).
- Gera 3 tokens (um por escopo: `collector.command.claim`,
  `collector.command.complete`, `collector.eligibility.read`), calcula o hash
  exatamente como `TokenHashService.cs` (Base64(48 bytes aleatórios) → SHA256
  hex maiúsculo) e insere 3 linhas em `service_credentials`.
- `--revoke-existing`: antes de inserir, marca `RevokedAt = now()` em todas as
  credenciais ativas do mesmo Tenant/Integration. Use quando quiser
  "aposentar" credenciais órfãs (sem token conhecido) em vez de deixá-las
  acumulando na tabela.
- Ao final, imprime linhas `export CollectorWorker__<Scope>Token="..."`
  prontas para colar no `~/.zshrc` do usuário.

**Requer aprovação explícita do usuário antes de rodar** — este script grava
no banco de produção/dev real (RDS), não em um container descartável.

## O que validar depois de rodar

1. Colar as linhas `export CollectorWorker__*Token=...` no `~/.zshrc` e rodar
   `source ~/.zshrc` (ou abrir um novo terminal).
2. Reiniciar o Collector.
3. Confirmar no log que o ciclo `[WORKER]` não repete mais o 401.
4. Rodar `list-collector-credentials.sh` de novo para confirmar que a
   credencial nova aparece como "ativa" e (se usado `--revoke-existing`) as
   antigas aparecem como "revogada em ...".

## Quando NÃO usar

- Para o 401 de `iservice_credentials` (login do Collector no portal externo
  iService via Playwright) — isso é gerenciado por `IServiceCredentialService`
  e é um assunto completamente diferente, não relacionado a este skill.
- Para inspecionar dados de negócio (work orders, tenants, etc.) sem
  necessidade de credenciais de Collector — use o skill `atua-pg-inspect`.
- Em produção real com múltiplos tenants sem confirmar antes qual
  tenant/integration deve receber as novas credenciais (sempre informe
  `--tenant-id`/`--integration-id` explicitamente nesse caso).

## Diagnóstico de falhas comuns

| Sintoma | Causa provável |
|---|---|
| `Postgres__ConnectionString não definida` | Faltou `export` no shell atual; confira `~/.zshrc` e abra um terminal novo. |
| `psql não encontrado` | Rode `brew install libpq`. |
| `existe(m) 0 tenant(s)` ou `2 tenant(s)` | Ambiente com zero ou mais de um tenant; informe `--tenant-id` manualmente. |
| 401 continua após seed | Confirme que o `export` foi realmente carregado no processo do Collector (reiniciar o terminal/processo), e que o `Scope` da credencial bate com o endpoint chamado. |
| Erro de FK ao inserir | `--provider-id`/`--integration-id` incorretos; rode sem esses parâmetros para deixar o script resolver via tenant único, ou confira `integrations`/`integration_providers` manualmente. |
