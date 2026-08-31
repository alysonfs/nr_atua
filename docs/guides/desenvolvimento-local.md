# Desenvolvimento Local

Guia prático para rodar a API Atua localmente.

> **Alternativa mais rápida (sem .NET SDK local):** a API e o Worker Coletor
> também rodam totalmente containerizados via `docker compose up -d --build`
> (serviços `api` e `collector` no `docker-compose.yml` da raiz). Útil quando o
> foco é o frontend e você só precisa de um endereço estável da API/Workers —
> ver comentário "COMO USAR" no topo do `docker-compose.yml`. Os passos 2 e 3
> abaixo (migrations e ServiceCredentials) continuam manuais mesmo nesse modo.
> O restante deste guia cobre o fluxo `dotnet run` local, usado no dia a dia de
> quem desenvolve a API/Coletor.

## Pré-requisitos

- **Docker** (com Compose v2 — `docker compose`, sem hífen)
- **.NET 10 SDK**

---

## 1. Subir os bancos de dados

Na raiz do repositório:

```bash
docker compose up -d
```

Isso inicia:
- **PostgreSQL 16** em `localhost:5432` (banco `atua`, usuário `atua`, senha `atua_dev`)
- **MongoDB 7** em `localhost:27017` (usuário `atua`, senha `atua_dev`), como replica set de nó único (`rs0`) — exigido para Change Streams (ADR-023)

> **Nota:** O MongoDB é consumido diretamente pelo **Worker Coletor** para persistir snapshots e observações de OS (ADR-012), e também para os Change Streams do consumer snapshot→work_order (ADR-023). A API REST não acessa o MongoDB — ela usa apenas o PostgreSQL.

Para verificar se os serviços estão saudáveis:

```bash
docker compose ps
```

---

## Bancos de dados

| Banco       | Host        | Porta  | Banco/DB | Usuário | Senha     |
|-------------|-------------|--------|----------|---------|-----------|
| PostgreSQL  | localhost   | 5432   | atua     | atua    | atua_dev  |
| MongoDB     | localhost   | 27017  | atua     | atua    | atua_dev  |

**Connection string do PostgreSQL** (usada em `appsettings.Development.json` e migrations):
```
Host=localhost;Port=5432;Database=atua;Username=atua;Password=atua_dev
```

**Connection string do MongoDB** (consumida pelo Worker Coletor):
```
mongodb://atua:atua_dev@localhost:27017/atua?authSource=admin
```

> ℹ️ O MongoDB **é consumido ativamente pelo Worker Coletor** para persistir:
> - `work_order_snapshots`: snapshots de estado de cada OS capturado
> - `work_order_observations`: observações e evidências de execução
>
> Consulte ADR-012 para mais contexto sobre a arquitetura de persistência do Coletor (Atlas Free Tier em produção).

---

## 2. Configurar o segredo JWT (obrigatório)

A API exige uma chave de assinatura JWT com **mínimo de 32 caracteres**. Configure via user-secrets — **nunca commite essa chave**.

Dentro de `apps/api/Atua.Api`:

```bash
cd apps/api/Atua.Api
dotnet user-secrets set "Authentication:SigningKey" "$(openssl rand -hex 32)"
```

O valor é armazenado fora do repositório (user-secrets) e nunca vai para o controle de versão.

---

## 3. Aplicar as migrations

As migrations **não são aplicadas automaticamente** no startup. Rode:

```bash
cd apps/api/Atua.Api
dotnet ef database update
```

> **Nota:** O `AtuaDbContextFactory` lê a connection string na ordem de precedência padrão do ASP.NET Core
> (`appsettings.json` → `appsettings.Development.json` → user-secrets → variáveis de ambiente).
> **Não é necessário exportar `ConnectionStrings__Atua` manualmente** antes de rodar `dotnet ef`.

Se o comando `dotnet ef` não estiver disponível:

```bash
dotnet tool install --global dotnet-ef
```

---

## 4. Rodar a API

```bash
cd apps/api/Atua.Api
dotnet run
```

A API ficará disponível em: **http://localhost:5240**

> **Aviso esperado em dev:** ao iniciar, a API exibe `"KMS NÃO ESTÁ ATIVO"`. Isso é **normal em desenvolvimento** — as credenciais são cifradas com chave local (`AlgorithmVersion=1`). Em produção o KMS é obrigatório.

### E-mails de confirmação em Development

Em ambiente Development, **nenhum e-mail é enviado**. O código de confirmação aparece no log do console:

```
[DEV] E-mail de confirmação NÃO enviado. Destinatário: usuario@exemplo.com | Código: 123456
```

Isso é feito pelo `LoggingEmailConfirmationSender`, registrado automaticamente quando `ASPNETCORE_ENVIRONMENT=Development`.
Em outros ambientes, o `SesEmailConfirmationSender` (AWS SES) é usado, e `Email:SenderAddress` deve estar configurado.

---

## 5. Worker Coletor

O Worker Coletor (`apps/collector/Atua.Collector`) se comunica **via HTTP com a API**
para claim/complete de comandos e elegibilidade (ADR-021, variante D9-B) — a
credencial do OS já chega decifrada pela API, então o Coletor **não decifra
credenciais nem acessa a tabela de credenciais**.

> ⚠️ **Atualização (ADR-023):** desde a implementação do consumer de snapshots
> (RF-016/RF-017), o Coletor **também acessa PostgreSQL diretamente via Npgsql**
> (sem EF Core/DbContext) — `WorkOrderPgRepository` faz upsert em `work_orders`,
> append em `work_order_histories` e persiste o resume token do Change Stream
> em `consumer_states`. A regra "não acessa Postgres" vale apenas para
> credenciais/KMS, não para o pipeline de snapshot→work_order. Ver ADR-023 para
> o desenho completo.

Além disso:
- O Coletor **persiste snapshots brutos e observações no MongoDB** (ADR-012), e
  consome os próprios snapshots via **Change Streams do MongoDB** para
  alimentar o consumer acima — por isso o MongoDB local precisa rodar como
  replica set (ver seção 1 e `docker-compose.yml`; `docker compose up -d` já
  sobe o Mongo como replica set de nó único `rs0`).

### Configuração local do Coletor

O arquivo `apps/collector/Atua.Collector/appsettings.Development.json` contém:
```json
{
  "CollectorWorker": {
    "ApiBaseUrl": "http://localhost:5240",
    "ClaimServiceToken": "firW/GGcVIZuF3iFv2ST8Bgw10znk52fdEqobk+fh0eCQBgvW27otiIjpopxf1me",
    "CompleteServiceToken": "DuXcE2Lrycf/kvaXWJgtmIkyNfEVsxHxLSNYhNqbUCxHAN/sKsbf/sPvHIgYOPKT",
    "EligibilityServiceToken": "<TOKEN_3_ELIGIBILITY>",
    "PollingIntervalSeconds": 10,
    "Headless": false
  },
  "MongoDB": {
    "ConnectionString": "******localhost:27017/atua?authSource=admin",
    "DatabaseName": "atua"
  }
}
```

> ℹ️ **Importante:** O Coletor precisa de **três ServiceCredentials separados** porque cada um aceita um escopo distinto:
> - `ClaimServiceToken`: escopo `collector.command.claim` (para buscar comandos de OS)
> - `CompleteServiceToken`: escopo `collector.command.complete` (para registrar conclusão)
> - `EligibilityServiceToken`: escopo `collector.eligibility.read` (para verificar elegibilidade do tenant — RF-009.2/RN-009.2)
>
> Se você reutilizar o mesmo token para escopos diferentes, o Coletor falhará ao tentar usar o escopo não autorizado.

### Provisioning de ServiceCredentials em desenvolvimento

Como ainda não existe endpoint/CLI para provisionar `ServiceCredentials`, crie-os diretamente no PostgreSQL.

**Pré-requisitos:**
- PostgreSQL deve estar rodando (`docker compose up`)
- Migrations devem estar aplicadas (`dotnet ef database update`)

**Passo a passo:**

#### 1. Conecte ao banco PostgreSQL:
```bash
psql -h localhost -U atua -d atua
```
Senha: `atua_dev`

#### 2. Obtenha o ID da Provider "iService" (já semeada via migration):
```sql
SELECT "Id" FROM "integration_providers" WHERE "Name" = 'iService' LIMIT 1;
```
Esperado: `00000000-0000-0000-0000-0000000000e1`

#### 3. Crie um Tenant e uma Integration de teste (ou use existentes):
```sql
-- Se não existir, crie um Tenant de teste
INSERT INTO tenants ("Id", "Name", "Cnpj", "TimeZoneId")
VALUES ('00000000-0000-0000-0000-000000000001', 'Dev Tenant', '12345678901234', 'America/Sao_Paulo')
ON CONFLICT DO NOTHING;

-- Se não existir, crie uma Integration para teste
INSERT INTO integrations ("Id", "TenantId", "ProviderId", "IsEnabled")
VALUES ('00000000-0000-0000-0000-000000000010', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-0000000000e1', true)
ON CONFLICT DO NOTHING;
```

#### 4. Gere três tokens aleatórios em Base64 (um para cada escopo):

**Token 1 (claim):**
```bash
dotnet -V && printf 'using System; using System.Security.Cryptography; Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));' | dotnet script -
```
ou manualmente (exemplo):
```
firW/GGcVIZuF3iFv2ST8Bgw10znk52fdEqobk+fh0eCQBgvW27otiIjpopxf1me
```

**Token 2 (complete):**
```bash
dotnet -V && printf 'using System; using System.Security.Cryptography; Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));' | dotnet script -
```
ou manualmente (exemplo):
```
DuXcE2Lrycf/kvaXWJgtmIkyNfEVsxHxLSNYhNqbUCxHAN/sKsbf/sPvHIgYOPKT
```

**Token 3 (eligibility):**
```bash
dotnet -V && printf 'using System; using System.Security.Cryptography; Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));' | dotnet script -
```

#### 5. Calcule o SHA256 em hex uppercase de cada token:

Para **Token 1**:
```bash
printf 'firW/GGcVIZuF3iFv2ST8Bgw10znk52fdEqobk+fh0eCQBgvW27otiIjpopxf1me' | openssl dgst -sha256 | tr -d ' ' | tr '[:lower:]' '[:upper:]' | sed 's/(stdin)=//'
```
Resultado esperado: `A0E5D4FC619C5486EE17782FBFA494F0183F0DB9E797A3DFD4AA87E4FE7876`

Para **Token 2**:
```bash
printf 'DuXcE2Lrycf/kvaXWJgtmIkyNfEVsxHxLSNYhNqbUCxHAN/sKsbf/sPvHIgYOPKT' | openssl dgst -sha256 | tr -d ' ' | tr '[:lower:]' '[:upper:]' | sed 's/(stdin)=//'
```

Para **Token 3**:
```bash
printf '<SEU_TOKEN_3>' | openssl dgst -sha256 | tr -d ' ' | tr '[:lower:]' '[:upper:]' | sed 's/(stdin)=//'
```

#### 6. Insira as três credenciais na tabela `service_credentials`:

```sql
-- ServiceCredential para claim
INSERT INTO service_credentials (
  "Id",
  "TenantId",
  "IntegrationId",
  "ProviderId",
  "Scope",
  "TokenHash",
  "RevokedAt"
) VALUES (
  '00000000-0000-0000-0000-000000000100',
  '00000000-0000-0000-0000-000000000001',
  '00000000-0000-0000-0000-000000000010',
  '00000000-0000-0000-0000-0000000000e1',
  'collector.command.claim',
  'A0E5D4FC619C5486EE17782FBFA494F0183F0DB9E797A3DFD4AA87E4FE7876',
  NULL
);

-- ServiceCredential para complete
INSERT INTO service_credentials (
  "Id",
  "TenantId",
  "IntegrationId",
  "ProviderId",
  "Scope",
  "TokenHash",
  "RevokedAt"
) VALUES (
  '00000000-0000-0000-0000-000000000101',
  '00000000-0000-0000-0000-000000000001',
  '00000000-0000-0000-0000-000000000010',
  '00000000-0000-0000-0000-0000000000e1',
  'collector.command.complete',
  '<SHA256_DO_TOKEN_2>',
  NULL
);

-- ServiceCredential para eligibility (RF-009.2/RN-009.2)
INSERT INTO service_credentials (
  "Id",
  "TenantId",
  "IntegrationId",
  "ProviderId",
  "Scope",
  "TokenHash",
  "RevokedAt"
) VALUES (
  '00000000-0000-0000-0000-000000000102',
  '00000000-0000-0000-0000-000000000001',
  '00000000-0000-0000-0000-000000000010',
  '00000000-0000-0000-0000-0000000000e1',
  'collector.eligibility.read',
  '<SHA256_DO_TOKEN_3>',
  NULL
);
```

#### 7. Confirme que as credenciais foram criadas:
```sql
SELECT "Id", "Scope", "TokenHash" FROM service_credentials
WHERE "IntegrationId" = '00000000-0000-0000-0000-000000000010';
```

#### 8. Atualize os tokens em claro no `appsettings.Development.json`:
```json
{
  "CollectorWorker": {
    "ClaimServiceToken": "firW/GGcVIZuF3iFv2ST8Bgw10znk52fdEqobk+fh0eCQBgvW27otiIjpopxf1me",
    "CompleteServiceToken": "DuXcE2Lrycf/kvaXWJgtmIkyNfEVsxHxLSNYhNqbUCxHAN/sKsbf/sPvHIgYOPKT",
    "EligibilityServiceToken": "<SEU_TOKEN_3_EM_CLARO>"
  }
}
```

### Instalação do Playwright (Chromium)

O Coletor utiliza **Playwright** (navegador Chromium) para capturar screenshots e dados de OSs web.

Antes de rodar o Coletor:

#### 1. Compile o projeto (isso gera o script de instalação):
```bash
cd apps/collector/Atua.Collector
dotnet build -c Release
```

#### 2. Localize e execute o script de instalação:

**Linux / macOS:**
```bash
./bin/Release/net10.0/playwright.sh install chromium
```

**Windows (PowerShell):**
```powershell
.\bin\Release\net10.0\playwright.ps1 install chromium
```

#### 3. Confirme a instalação:
```bash
# Deve listar o Chromium instalado
./bin/Release/net10.0/playwright.sh install-deps chromium
```

> ⚠️ A instalação do Chromium é obrigatória antes de rodar o Coletor com um comando real.
> Sem o Playwright instalado, o Coletor falhará ao tentar capturar screenshots.

## 6. Parar os bancos

```bash
# Para os containers (preserva os dados)
docker compose down

# Para os containers e apaga todos os dados (volumes)
docker compose down -v
```

---

## Troubleshooting

### `Authentication:SigningKey deve ter ao menos 32 caracteres.`

A chave JWT não foi configurada ou é curta demais.

**Solução:** Configure via user-secrets dentro de `apps/api/Atua.Api`:

```bash
dotnet user-secrets set "Authentication:SigningKey" "$(openssl rand -hex 32)"
```

Confirme que o segredo foi salvo:

```bash
dotnet user-secrets list
```

