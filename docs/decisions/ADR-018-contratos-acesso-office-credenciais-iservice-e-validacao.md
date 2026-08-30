# ADR-018 - Contratos de acesso ao Office, credenciais iService e validação

## Status

Proposed

## Contexto

Paula (`product-analyst`) publicou RF-005 (acesso ao Office), RF-006
(configuração de credenciais iService) e RF-007 (validação de credenciais
antes da ativação do Agente Coletor). As ADR-004, ADR-005, ADR-015 e ADR-017
já definem identidade, sessão, tenancy, ciclo de vida do Trial e o contrato de
autenticação do browser/serviço interno. O código existente em
`Application/Identity` (`AuthService`, `IRefreshTokenStore`,
`RefreshTokenStore`) e `Domain/Tenants` (`Tenant`, `TenantMembership`) já
implementa login, refresh, logout e o modelo de tenancy, mas:

- não existe endpoint de resolução/seleção de tenant ativo (RF-005.2);
- não existe entidade de credenciais iService com os campos de
  configuração (usuário, senha, URL/tenant do provedor) nem endpoints de
  criação/leitura/atualização (RF-006);
- não existe abstração nem endpoint para o teste de validação de
  credenciais junto ao iService, nem registro de resultado (RF-007).

O Agente Coletor está fora de escopo (pausado por decisão do usuário). Este
ADR não desenha nenhuma lógica de scraping, Playwright ou automação de
coleta. O "teste de validação" (RF-007) é modelado apenas como uma tentativa
de autenticação/obtenção de sessão junto ao iService, sem executar nenhuma
ação de leitura de dados de negócio.

Uma lacuna técnica real existe: **não há, neste momento, especificação
técnica do protocolo de autenticação do iService** (se é CAS clássico, se
expõe endpoint REST de login, quais parâmetros exatos são exigidos além de
usuário/senha, formato de resposta, cookies envolvidos). ADR-004 menciona
"sessão CAS" como termo de trabalho, mas não documenta o protocolo real.
Esta ADR trata essa lacuna como uma decisão pendente e propõe uma abstração
que isola essa incerteza do restante do sistema.

## Decisão

### RF-005 - Resolução de tenant ativo

Não é criado nenhum novo mecanismo de sessão. O JWT continua sem
`tenant_id` (ADR-017); o tenant ativo é resolvido a cada requisição
dependente de tenant, a partir do membership do usuário autenticado.

**Novo endpoint:**

```
GET /api/users/me/tenants
Autorização: BrowserSession (JWT de acesso)
```

Resposta `200 OK`:

```json
{
  "tenants": [
    { "tenantId": "uuid", "name": "string", "role": "OWNER" }
  ],
  "defaultTenantId": "uuid | null"
}
```

- `defaultTenantId` é preenchido quando o usuário possui exatamente um
  membership ativo; é `null` quando possui mais de um (exige seleção
  explícita) ou nenhum (deve ser conduzido ao fluxo de criação de tenant,
  RF-006).
- Lista vazia é uma resposta válida (usuário ainda sem tenant); não é erro.

Este endpoint não introduz um novo conceito de "tenant ativo persistido no
servidor" para o MVP. Rotas dependentes de tenant (ex.:
`/api/tenants/{tenantId}/...`) já resolvem e validam o membership a partir do
`tenantId` da própria rota, seguindo o padrão já usado em
`SetTenantTimeZone`. O frontend (Office) é responsável por manter o tenant
selecionado localmente (ex.: estado de aplicação) e enviá-lo como parâmetro
de rota; o servidor sempre valida o membership independentemente do que o
cliente enviar (RN já vigente, sem mudança de comportamento).

Não é necessário introduzir claim, cookie ou tabela de "tenant ativo": o
próprio conjunto de rotas com `{tenantId}` no path, validado contra
`TenantMemberships`, já satisfaz RF-005.2 e os critérios de aceite 4 e 5.

`AuthService` não é alterado. Nenhuma nova lógica de sessão é introduzida.

### RF-006 - Credenciais iService e criação de tenant

#### Criação de tenant (primeira integração)

Reaproveita a regra já modelada em ADR-005 e `TrialSubscription`. Endpoint
novo, orquestrando entidades já existentes (`Tenant`, `TenantMembership`,
`TrialSubscription.AssociateWithTenant`):

```
POST /api/tenants
Autorização: BrowserSession
Body: { "name": "string", "cnpj": "string (14 dígitos)" }
```

Resposta `201 Created`: `{ "tenantId": "uuid" }`.
Resposta `409 Conflict`: `{ "error": "cnpj_already_registered" }`.
Resposta `400 Bad Request`: `{ "error": "invalid_cnpj" }`.
Resposta `409 Conflict` (`{ "error": "user_already_has_tenant" }`) se o
usuário já possuir membership `OWNER` em algum tenant — o MVP assume um
tenant por usuário Owner (decorrência do modelo atual, não uma nova regra).

Regra de execução (`TenantOnboardingService`, novo, em
`Application/Tenants/`):

1. valida formato do CNPJ (normalização + dígitos verificadores);
2. valida unicidade global (`Tenants.Cnpj`);
3. cria `Tenant`;
4. cria `TenantMembership(tenantId, userId, OWNER)`;
5. localiza a `TrialSubscription` do usuário (`UserId` único, já indexado) e
   chama `AssociateWithTenant(tenantId)`;
6. tudo em uma transação única.

Não é necessária nova tabela; reaproveita exatamente o modelo da ADR-005.

#### Nova entidade: `ServiceCredentialProfile` (credenciais de configuração)

**Atenção de nomenclatura:** já existe `Domain/Identity/ServiceCredential`,
mas essa entidade representa uma **credencial de serviço interno** (escopo
`collector.eligibility.read`, `TokenHash`, `Scope` — usada pelo contrato
interno da ADR-017), não a credencial de configuração do iService inserida
pelo cliente. Reutilizar esse nome causaria confusão entre dois conceitos
distintos. Propõe-se uma entidade nova, com nome distinto:

```csharp
namespace Atua.Api.Domain.Integrations;

public sealed class IServiceCredential
{
    private IServiceCredential() { }

    public IServiceCredential(Guid id, Guid tenantId, Guid integrationId,
        string usernameCiphertext, string passwordCiphertext,
        string? baseUrlCiphertext, byte[] nonce, byte[] tag,
        string dataKeyCiphertext, Guid kmsKeyId, int algorithmVersion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        UsernameCiphertext = usernameCiphertext;
        PasswordCiphertext = passwordCiphertext;
        BaseUrlCiphertext = baseUrlCiphertext;
        Nonce = nonce;
        Tag = tag;
        DataKeyCiphertext = dataKeyCiphertext;
        KmsKeyId = kmsKeyId;
        AlgorithmVersion = algorithmVersion;
        CreatedAtUtc = createdAtUtc;
        ValidationStatus = EIServiceValidationStatus.NotValidated;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid IntegrationId { get; private set; }

    // Campos cifrados (AES-256-GCM, ADR-004). Nunca expostos em claro.
    public string UsernameCiphertext { get; private set; } = null!;
    public string PasswordCiphertext { get; private set; } = null!;
    public string? BaseUrlCiphertext { get; private set; }
    public byte[] Nonce { get; private set; } = null!;
    public byte[] Tag { get; private set; } = null!;
    public string DataKeyCiphertext { get; private set; } = null!; // chave de dados cifrada por KMS
    public Guid KmsKeyId { get; private set; }
    public int AlgorithmVersion { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public EIServiceValidationStatus ValidationStatus { get; private set; }
    public DateTimeOffset? LastValidatedAtUtc { get; private set; }

    public void ReplaceSecret(string usernameCiphertext, string passwordCiphertext,
        string? baseUrlCiphertext, byte[] nonce, byte[] tag, string dataKeyCiphertext,
        Guid kmsKeyId, int algorithmVersion, DateTimeOffset now)
    {
        UsernameCiphertext = usernameCiphertext;
        PasswordCiphertext = passwordCiphertext;
        BaseUrlCiphertext = baseUrlCiphertext;
        Nonce = nonce;
        Tag = tag;
        DataKeyCiphertext = dataKeyCiphertext;
        KmsKeyId = kmsKeyId;
        AlgorithmVersion = algorithmVersion;
        UpdatedAtUtc = now;
        ValidationStatus = EIServiceValidationStatus.NotValidated; // RN-006.3
        LastValidatedAtUtc = null;
    }

    public void RecordValidation(EIServiceValidationStatus status, DateTimeOffset now)
    {
        ValidationStatus = status;
        LastValidatedAtUtc = now;
    }
}

public enum EIServiceValidationStatus { NotValidated, Succeeded, Failed }
```

Um a um por `Integration` (relação 1:1 com `Integration`, que por sua vez já
referencia `Tenant` e `IntegrationProvider` conforme ADR-005). Não é
necessário duplicar `TenantId`/`ProviderId` como nova fronteira — reaproveita
`IntegrationId` como chave estrangeira única.

Campos mínimos assumidos por Paula (usuário, senha, URL/tenant do iService)
são representados como `UsernameCiphertext`, `PasswordCiphertext` e
`BaseUrlCiphertext` (opcional, conforme observação de Paula sobre
possível variação do formato de autenticação do provedor).

Cifra: reaproveita integralmente ADR-004 — chave de dados aleatória por
integração, AES-256-GCM, chave externa via AWS KMS, Postgres armazenando
apenas ciphertext/nonce/tag/versão/identificador de chave. A
responsabilidade de cifrar/decifrar é isolada em um serviço de aplicação
(`ICredentialCipher`, já pode existir parcialmente pela fundação de ADR-004;
caso não exista, deve ser criado pelo `backend-engineer` como componente
compartilhado, não duplicado por este ADR).

#### Endpoints de credenciais

```
PUT /api/tenants/{tenantId}/integrations/{integrationId}/credentials
Autorização: BrowserSession + membership OWNER do tenantId
Body: { "username": "string", "password": "string", "baseUrl": "string?" }
```

- Idempotente por design: cria se não existir, substitui se existir
  (`ReplaceSecret`), sempre resetando `ValidationStatus` para
  `NotValidated` (RN-006.3, RF-006.5). Também invalida qualquer sessão CAS
  associada (ver RF-007).
- Resposta `204 No Content` em sucesso. `403 Forbidden` se não for `OWNER`.
  Nunca retorna as credenciais no corpo.

```
GET /api/tenants/{tenantId}/integrations/{integrationId}/credentials
Autorização: BrowserSession + membership do tenantId
```

Resposta `200 OK`, **sem nenhum campo cifrado/decifrado**:

```json
{
  "hasCredentials": true,
  "validationStatus": "NotValidated|Succeeded|Failed",
  "lastValidatedAtUtc": "2026-08-29T00:00:00Z | null",
  "updatedAtUtc": "2026-08-29T00:00:00Z | null"
}
```

Este contrato satisfaz RF-006.4/RN-006.4 (nunca em claro) e RF-007.3
(cliente vê apenas resultado + timestamp).

Autorização: apenas `OWNER` pode `PUT` (criar/alterar); leitura de status
pode ser liberada a qualquer membership do tenant (não há dado sensível na
leitura). A ambiguidade de permissão do `ADMIN` sobre escrita permanece em
aberto conforme já registrado por Paula — este ADR não a resolve; mantém
apenas `OWNER` como autorizado a escrever, conforme decisão mínima do
requisito.

### RF-007 - Teste de validação

#### Abstração da autenticação no iService (decisão pendente sinalizada)

```csharp
namespace Atua.Api.Application.Integrations;

public interface IIServiceAuthClient
{
    Task<IServiceAuthResult> TryAuthenticateAsync(
        IServiceCredentialPayload credential, CancellationToken cancellationToken);
}

public sealed record IServiceCredentialPayload(string Username, string Password, string? BaseUrl);

public sealed record IServiceAuthResult(bool Succeeded, string? FailureReasonForLog);
```

**Lacuna técnica explícita:** não há, no momento desta análise, documentação
do protocolo real de autenticação do iService (endpoint de login, se é CAS
com redirecionamento/ticket, se é REST com corpo JSON, formato de erro,
tempo de expiração de sessão). ADR-004 usa o termo "sessão CAS" apenas como
hipótese de trabalho. Este ADR **não implementa** `IIServiceAuthClient`; a
implementação real depende de uma das seguintes fontes, ainda não
disponíveis:

- documentação oficial do provedor iService;
- acesso a um ambiente de teste/sandbox do iService para inspeção do fluxo
  de login;
- especificação técnica repassada pelo cliente/usuário do projeto.

Até essa informação existir, recomenda-se implementar uma
`FakeIServiceAuthClient` (implementação de teste/mock, isolada por DI,
substituível sem alterar o restante do sistema) para permitir que
`backend-engineer` implemente e teste o restante do fluxo (endpoint,
persistência do resultado, bloqueio de ativação) de forma independente da
integração real. Essa lacuna deve ser tratada como item pendente e não deve
bloquear a implementação das partes RF-005/RF-006 e da orquestração de
RF-007 em torno da abstração.

#### Endpoint de teste de validação

```
POST /api/tenants/{tenantId}/integrations/{integrationId}/credentials/validate
Autorização: BrowserSession + membership do tenantId (qualquer papel pode
solicitar teste; apenas OWNER cadastra/altera credenciais)
```

Fluxo (`IServiceCredentialValidationService`, novo, em
`Application/Integrations/`):

1. carrega `IServiceCredential` da integração; `404 Not Found` se não
   existir (`{ "error": "credentials_not_configured" }`);
2. decifra username/password/baseUrl em memória (nunca persiste em claro,
   nunca loga);
3. chama `IIServiceAuthClient.TryAuthenticateAsync(...)`;
4. registra o resultado via `IServiceCredential.RecordValidation(status, now)`
   — `Succeeded` ou `Failed`, nunca a razão técnica bruta do provedor
   (RF-007.4, RN-007.4);
5. `Failed` por exceção/timeout/5xx do provedor é tratado como `Failed`
   também para o cliente; o motivo detalhado (timeout vs. credencial
   recusada) pode ser logado apenas em log técnico interno, sem PII/segredo,
   para fins de suporte (caso de borda descrito por Paula).

Resposta `200 OK`:

```json
{ "validationStatus": "Succeeded|Failed", "evaluatedAtUtc": "2026-08-29T00:00:00Z" }
```

Nenhuma sessão obtida durante o teste é persistida como sessão de trabalho
de coleta (RF-007.1, RN-007.3/RN-007.4) — este ADR não introduz nenhuma
tabela de "sessão CAS de trabalho"; isso permanece fora de escopo enquanto o
Agente Coletor estiver pausado.

#### Bloqueio de ativação (apenas o flag, sem lógica de ativação real)

RF-007.2 exige que "o controle de ativação do Agente Coletor" fique
indisponível sem validação `Succeeded`. Como RF-008 (ativação) está fora de
escopo, este ADR define apenas a leitura do flag necessário, sem desenhar
nenhuma lógica de ativação:

O contrato de leitura de status de credenciais
(`GET .../credentials`, acima) já expõe `validationStatus`. O frontend
(Office) usa esse campo para habilitar/desabilitar o botão de ativação do
Agente Coletor quando RF-008 for implementado. Nenhum novo endpoint de
"pode ativar" é necessário agora; a leitura de elegibilidade de ativação
deve compor, no futuro (RF-008), tanto `validationStatus == Succeeded`
quanto a elegibilidade de Trial já exposta por
`GET /api/internal/collector/eligibility` (ADR-015/ADR-017). Este ADR não
cria esse endpoint composto — é decisão do RF-008, explicitamente fora de
escopo aqui.

### Persistência (EF Core)

Nova tabela `iservice_credentials`, chave `Id` (UUIDv7), FK única
`IntegrationId` → `Integration.Id` (`OnDelete: Cascade`), sem índice
adicional além do necessário para a FK. Nenhuma alteração é necessária em
`Tenant`, `TenantMembership`, `Integration` ou `IntegrationProvider`.

## Motivos

- Reaproveitar `Integration`/`IntegrationProvider`/`Tenant` já modelados
  evita duplicar conceitos definidos em ADR-005.
- Uma entidade nova e distinta (`IServiceCredential`) evita colisão semântica
  com `Domain/Identity/ServiceCredential` (credencial de serviço interno do
  Agente Coletor, escopo diferente).
- Isolar a chamada real ao iService atrás de `IIServiceAuthClient` permite
  que backend-engineer implemente e teste todo o fluxo de configuração,
  autorização, cifra e bloqueio de ativação sem depender de uma
  especificação externa ainda ausente — reduz o risco de "inventar" um
  protocolo que depois precise ser refeito.
- Reduzir a exposição de status a apenas `validationStatus` +
  `lastValidatedAtUtc` atende RS-001/RN-006.4/RN-007.4 sem exigir nova
  infraestrutura de auditoria além da já prevista em ADR-004.
- Não introduzir "tenant ativo" como conceito persistido no servidor evita
  complexidade antecipada; o padrão de rota `{tenantId}` com validação de
  membership já é suficiente e já está em uso (`SetTenantTimeZone`).

## Alternativas consideradas

### Adicionar `tenant_id` selecionável como claim de sessão (ex.: novo campo em `AuthSession`)

Rejeitada: contraria diretamente ADR-017 (tenant não deve ser transportado
como contexto confiável de token/sessão) e adicionaria estado mutável de
sessão sem necessidade, já que a validação por rota já resolve o requisito.

### Reaproveitar `Domain/Identity/ServiceCredential` para as credenciais do iService

Rejeitada: essa entidade já tem propósito definido (credencial de serviço
interno do contrato ADR-017, escopo `collector.eligibility.read`).
Reutilizá-la misturaria dois conceitos (credencial de autenticação de
serviço interno vs. credencial de configuração de terceiro) e dificultaria
auditoria e leitura do código.

### Implementar `IIServiceAuthClient` com uma suposição de protocolo CAS genérico agora

Rejeitada: sem especificação real do provedor, qualquer implementação seria
uma adivinhação que arrisca reforço de comportamento incorreto e retrabalho.
Prefere-se mock explícito e lacuna documentada.

### Criar endpoint composto "pode ativar o Agente Coletor" agora

Rejeitada: RF-008 (ativação) está fora de escopo. Antecipar esse contrato
sem os requisitos de RF-008 arrisca desenhar uma interface que precise ser
refeita.

## Consequências

- `backend-engineer` pode implementar RF-005 (endpoint de listagem de
  tenants) e RF-006 (criação de tenant + CRUD de credenciais cifradas) de
  forma completa e independente de qualquer informação externa pendente.
- RF-007 pode ser implementado integralmente **exceto** a implementação real
  de `IIServiceAuthClient`, que depende de informação técnica do provedor
  iService ainda não disponível. Recomenda-se implementar com
  `FakeIServiceAuthClient` até essa informação chegar.
- Uma nova tabela (`iservice_credentials`) e uma nova migration EF Core são
  necessárias.
- Nenhuma alteração é necessária em `AuthService`, `IRefreshTokenStore` ou
  no contrato JWT existente.

## Agentes envolvidos

- product-analyst (Paula): requisitos RF-005/006/007.
- software-architect (Sérgio): contratos técnicos desta ADR.
- backend-engineer: implementação de endpoints, entidades, migrations e
  serviços de aplicação.
- qa-engineer: validação dos critérios de aceite e dos casos de borda,
  incluindo os cenários de falha de validação.

## Data

2026-08-29

## Substitui

Não aplicável. Complementa ADR-004, ADR-005, ADR-015 e ADR-017.
