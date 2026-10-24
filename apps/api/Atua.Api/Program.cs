using Amazon.BedrockRuntime;
using Amazon.KeyManagementService;
using Amazon.S3;
using Amazon.SimpleEmailV2;
using System.Security.Claims;
using System.Text;
using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Application.Integrations;
using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Application.LlmUsage;
using Atua.Api.Application.LlmUsage.Contracts;
using Atua.Api.Application.Tenants;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Application.WorkOrders;
using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Email;
using Atua.Api.Infrastructure.Jobs;
using Atua.Api.Infrastructure.Llm;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Atua.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Padrão do projeto: Postgres:ConnectionString (env Postgres__ConnectionString),
// igual ao Collector. ConnectionStrings:Atua é fallback legado (user-data EC2 antigo).
var connectionString = builder.Configuration["Postgres:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("Atua")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Atua")
    ?? "Host=localhost;Database=atua;Username=atua";

builder.Services.AddDbContext<AtuaDbContext>(options => options.UseNpgsql(connectionString));
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
    ?? new AuthOptions();
if (string.IsNullOrWhiteSpace(authOptions.SigningKey) || authOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException("Authentication:SigningKey deve ter ao menos 32 caracteres.");
}
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = authOptions.Issuer,
        ValidateAudience = true, ValidAudience = authOptions.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var sub = context.Principal?.FindFirstValue("sub");
            var sid = context.Principal?.FindFirstValue("sid");
            if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(sid, out var sessionId))
            {
                context.Fail("Invalid session claims.");
                return;
            }
            var db = context.HttpContext.RequestServices.GetRequiredService<AtuaDbContext>();
            var active = await db.AuthSessions.AsNoTracking().AnyAsync(session =>
                session.Id == sessionId && session.UserId == userId && session.RevokedAt == null,
                context.HttpContext.RequestAborted);
            if (!active) context.Fail("Session revoked.");
        }
    };
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
    ServiceCredentialAuthenticationHandler>(ServiceCredentialAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("BrowserSession", policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser());
    options.AddPolicy("CollectorEligibility", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.EligibilityScope));
    options.AddPolicy("CollectorCommandClaim", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ClaimScope));
    options.AddPolicy("CollectorCommandComplete", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.CompleteScope));
    options.AddPolicy("TriageStart", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.TriageStartScope));
    options.AddPolicy("ChannelCommandClaim", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelCommandClaimScope));
    options.AddPolicy("ChannelCommandComplete", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelCommandCompleteScope));
    options.AddPolicy("ChannelMessageIngest", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelMessageIngestScope));
    options.AddPolicy("ChannelMediaIngest", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelMediaIngestScope));
    options.AddPolicy("ChannelSessionReport", policy =>
        policy.AddAuthenticationSchemes(ServiceCredentialAuthenticationHandler.SchemeName)
            .RequireClaim("scope", ServiceCredentialAuthenticationHandler.ChannelSessionReportScope));
});

builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2, AmazonSimpleEmailServiceV2Client>();
builder.Services.AddSingleton<ISecretHasher, Argon2idSecretHasher>();
builder.Services.AddSingleton<ITokenHashService, TokenHashService>();
builder.Services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
builder.Services.AddSingleton<IEmailConfirmationCodeGenerator, EmailConfirmationCodeGenerator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEmailConfirmationSender>(sp =>
{
    var senderAddress = builder.Configuration["Email:SenderAddress"]
        ?? throw new InvalidOperationException("Configuracao 'Email:SenderAddress' nao definida.");

    // Se Email:Smtp:Host estiver configurado (ex.: Mailpit em dev via docker-compose.yml),
    // usa SMTP local em vez de SES. Nunca deve apontar para um host de dev em producao.
    var smtpHost = builder.Configuration["Email:Smtp:Host"];
    if (!string.IsNullOrWhiteSpace(smtpHost))
    {
        var smtpPort = builder.Configuration.GetValue("Email:Smtp:Port", 1025);
        return new SmtpEmailConfirmationSender(smtpHost, smtpPort, senderAddress);
    }

    var client = sp.GetRequiredService<IAmazonSimpleEmailServiceV2>();
    return new SesEmailConfirmationSender(client, senderAddress);
});
builder.Services.AddScoped<SignUpService>();
builder.Services.AddScoped<ConfirmEmailService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TimeZonePreferenceService>();
builder.Services.AddScoped<UserLocalePreferenceService>();
builder.Services.Configure<CredentialCipherOptions>(
    builder.Configuration.GetSection(CredentialCipherOptions.SectionName));
// Seleciona a implementação de ICredentialCipher com base na configuração:
// - KmsKeyArn configurado → KmsCredentialCipher (v2, wrap/unwrap via AWS KMS).
// - KmsKeyArn ausente + produção → falha na inicialização (não sobe sem KMS em prod).
// - KmsKeyArn ausente + desenvolvimento → AesGcmCredentialCipher com aviso explícito.
// O KmsCredentialCipher injeta o AesGcmCredentialCipher para coexistência v1/v2.
builder.Services.AddSingleton<AesGcmCredentialCipher>();
var kmsKeyArn = builder.Configuration[$"{CredentialCipherOptions.SectionName}:KmsKeyArn"];
if (!string.IsNullOrWhiteSpace(kmsKeyArn))
{
    builder.Services.AddSingleton<IAmazonKeyManagementService>(_ => new AmazonKeyManagementServiceClient());
    builder.Services.AddSingleton<ICredentialCipher, KmsCredentialCipher>();
}
else
{
    builder.Services.AddSingleton<ICredentialCipher>(sp => sp.GetRequiredService<AesGcmCredentialCipher>());
}
builder.Services.AddScoped<AddTenantUseCase>();
builder.Services.AddScoped<TenantPlanLimitService>();
builder.Services.AddScoped<GetTenantPlanUseCase>();
builder.Services.AddScoped<ChangeTenantPlanUseCase>();
builder.Services.AddScoped<ChangeTenantMembershipRoleUseCase>();
builder.Services.AddScoped<IWorkOrderMonthlySummaryQuery, WorkOrderMonthlySummaryQueryHandler>();
builder.Services.AddScoped<IWorkOrderListByStatusQuery, WorkOrderListByStatusQueryHandler>();
builder.Services.AddScoped<IWorkOrderStatusSummaryQuery, WorkOrderStatusSummaryQueryHandler>();
builder.Services.AddScoped<IWorkOrderAgingSummaryQuery, WorkOrderAgingSummaryQueryHandler>();
builder.Services.AddScoped<IWorkOrderDetailQuery, WorkOrderDetailQueryHandler>();
builder.Services.AddScoped<IWorkOrderMonthlyTrendQuery, WorkOrderMonthlyTrendQueryHandler>();
builder.Services.AddScoped<IWorkOrderCriticalitySummaryQuery, WorkOrderCriticalitySummaryQueryHandler>();
builder.Services.AddScoped<IWorkOrderNextAppointmentSummaryQuery, WorkOrderNextAppointmentSummaryQueryHandler>();
builder.Services.AddScoped<IWorkOrderSubStatusSummaryQuery, WorkOrderSubStatusSummaryQueryHandler>();
builder.Services.AddScoped<IServiceCredentialService>();
builder.Services.AddScoped<IServiceCredentialValidationService>();
builder.Services.AddScoped<ICollectorEligibilityEvaluator, CollectorEligibilityEvaluator>();
builder.Services.AddScoped<CollectorActivationService>();
// TODO(ADR-018): substituir por implementação real quando o protocolo do
// iService estiver especificado. Ver FakeIServiceAuthClient.
builder.Services.AddSingleton<IIServiceAuthClient, FakeIServiceAuthClient>();
builder.Services.Configure<ImmediateCollectionOptions>(
    builder.Configuration.GetSection(ImmediateCollectionOptions.SectionName));
builder.Services.AddScoped<ImmediateCollectionCommandService>();
builder.Services.AddHostedService<ClaimTimeoutJob>();
builder.Services.AddScoped<RecurrentCollectionIntervalService>();
builder.Services.AddScoped<RecurrentCollectionSchedulerService>();
builder.Services.AddHostedService<RecurrentCollectionSchedulerJob>();
builder.Services.AddHostedService<TriagemInboundMessagePurgeJob>();

// ADR-040: mesmo lifetime de AtuaDbContext (Scoped) — a implementação usa o
// DbContext injetado diretamente, sem estado próprio.
builder.Services.AddScoped<ITriagemRepository, TriagemRepository>();
builder.Services.AddScoped<ITriagemEligibleWorkOrderQuery, TriagemEligibleWorkOrderQueryHandler>();
builder.Services.AddScoped<TriagemMessageTemplateRenderer>();
builder.Services.AddScoped<ITriagemMessageTemplateResolver>(
    serviceProvider => serviceProvider.GetRequiredService<TriagemMessageTemplateRenderer>());
builder.Services.AddScoped<TriagemMessageTemplateService>();
builder.Services.AddScoped<StartTriagemUseCase>();
builder.Services.AddScoped<IListTriagemPoolUseCase, ListTriagemPoolUseCase>();
builder.Services.AddScoped<IGetTriagemPoolDetailUseCase, GetTriagemPoolDetailUseCase>();
builder.Services.AddScoped<IAssumeTriagemUseCase, AssumeTriagemUseCase>();

// ADR-042: comando de canal persistido atomicamente com o aceite da
// triagem, reivindicado por polling (claim) e concluído (complete) pelo
// processo separado do canal — nunca chamado em processo.
builder.Services.AddOptions<TriagemChannelOptions>()
    .Bind(builder.Configuration.GetSection(TriagemChannelOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<ITriagemChannelCommandRepository, TriagemChannelCommandRepository>();
builder.Services.AddScoped<TriagemChannelCommandService>();
builder.Services.AddScoped<ITriagemInboundMessageRepository, TriagemInboundMessageRepository>();
builder.Services.AddScoped<IIngestTriagemChannelMessageUseCase, IngestTriagemChannelMessageUseCase>();

// ADR-049: pareamento via QR — store em memória (singleton), sem migration
// nova. API e Channel compartilham a mesma EC2, recriada do zero a cada
// `make down`; o último estado reportado não precisa sobreviver a isso.
builder.Services.AddSingleton<IWhatsAppSessionStateStore, InMemoryWhatsAppSessionStateStore>();
builder.Services.AddScoped<IReportWhatsAppSessionStateUseCase, ReportWhatsAppSessionStateUseCase>();
builder.Services.AddScoped<IGetWhatsAppSessionStateUseCase, GetWhatsAppSessionStateUseCase>();

// Incremento de mídia (ADR-045/046): mesmo escopo exclusivo isolado dos
// demais eventos de canal. Em produção, exige bucket S3 configurado — sem
// isso, a aplicação não sobe (mesmo princípio já aplicado ao KmsKeyArn
// acima: nenhuma falha silenciosa de persistência durável).
var triagemInboundMediaBucketName = builder.Configuration[$"{TriagemInboundMediaStorageOptions.SectionName}:BucketName"];
if (!string.IsNullOrWhiteSpace(triagemInboundMediaBucketName))
{
    builder.Services.AddSingleton<IAmazonS3, AmazonS3Client>();
    builder.Services.AddSingleton(new TriagemInboundMediaStorageOptions { BucketName = triagemInboundMediaBucketName });
    builder.Services.AddSingleton<ITriagemInboundMediaStorage, S3TriagemInboundMediaStorage>();
}
else
{
    builder.Services.AddSingleton<ITriagemInboundMediaStorage, FakeTriagemInboundMediaStorage>();
}
builder.Services.AddScoped<ITriagemInboundMediaRepository, TriagemInboundMediaRepository>();
builder.Services.AddScoped<IIngestTriagemChannelMediaUseCase, IngestTriagemChannelMediaUseCase>();

// Expurgo de mídia inbound pendente (ADR-046/047): job local que apaga o
// objeto em S3 antes dos metadados, nunca o inverso — ver
// TriagemInboundMediaPurgeService.
builder.Services.AddScoped<TriagemInboundMediaPurgeService>();
builder.Services.AddHostedService<TriagemInboundMediaPurgeJob>();

// ADR-041: drenagem da outbox do gatilho de triagem gravada pelo Collector.
builder.Services.AddOptions<TriagemOutboxOptions>()
    .Bind(builder.Configuration.GetSection(TriagemOutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TriagemOutboxOptions>, TriagemOutboxOptionsValidator>();
builder.Services.AddScoped<ITriagemOutboxRepository, TriagemOutboxRepository>();
builder.Services.AddScoped<TriagemOutboxDrainService>();
builder.Services.AddHostedService<TriagemOutboxDrainWorker>();

// ADR-048: LLM da triagem no Amazon Bedrock (sa-east-1), atrás de
// ITriagemLlmClient — trocar de modelo/fornecedor é só configuração e
// adaptador (M1 do plano, docs/implementation/PLANO-triagem-llm-jev.md).
// Enabled=false (padrão) usa o fake: nenhuma chamada à AWS é feita em
// desenvolvimento nem nos testes.
builder.Services.AddOptions<TriagemLlmOptions>()
    .Bind(builder.Configuration.GetSection(TriagemLlmOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TriagemLlmOptions>, TriagemLlmOptionsValidator>();
var triagemLlmEnabled = builder.Configuration.GetValue($"{TriagemLlmOptions.SectionName}:Enabled", false);
if (triagemLlmEnabled)
{
    builder.Services.AddSingleton<IAmazonBedrockRuntime>(sp =>
    {
        var region = builder.Configuration[$"{TriagemLlmOptions.SectionName}:Region"] ?? "sa-east-1";
        return new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.GetBySystemName(region));
    });
    builder.Services.AddScoped<ITriagemLlmClient, BedrockTriagemLlmClient>();
    // Mesma flag/modelo do adaptador conversacional — classificação de
    // visão de anexos é apenas mais uma chamada de LLM da triagem (decisão
    // do PO: adiantar a conferência automática de nota fiscal/etiqueta).
    builder.Services.AddScoped<ITriagemAttachmentVisionClient, BedrockTriagemAttachmentVisionClient>();
}
else
{
    builder.Services.AddSingleton<ITriagemLlmClient, FakeTriagemLlmClient>();
    builder.Services.AddSingleton<ITriagemAttachmentVisionClient, FakeTriagemAttachmentVisionClient>();
}
builder.Services.AddSingleton<ITriagemLlmResponseVerifier, TriagemLlmResponseVerifier>();
builder.Services.AddScoped<ITriagemLlmUsageRepository, TriagemLlmUsageRepository>();
builder.Services.AddScoped<ITriagemAttachmentVisionRepository, TriagemAttachmentVisionRepository>();
builder.Services.AddScoped<IClassifyTriagemAttachmentsUseCase, ClassifyTriagemAttachmentsUseCase>();

// Relatório de consumo/custo de LLM por tenant para o Office (ADR-048,
// pendência "Relatório de consumo por tenant no Office"). Preço por modelo
// configurável em "TriagemLlm:Pricing"; modelo sem preço custa 0.
var llmPricingEntries = builder.Configuration.GetSection(TriagemLlmPricingCatalog.SectionName)
    .Get<List<TriagemLlmModelPricing>>();
builder.Services.AddSingleton(new TriagemLlmPricingCatalog(llmPricingEntries));
builder.Services.AddScoped<ITriagemLlmUsageAggregateQuery, TriagemLlmUsageAggregateQueryHandler>();
builder.Services.AddScoped<IGetLlmUsageSummaryUseCase, GetLlmUsageSummaryUseCase>();

// Plano M2: loop conversacional da LLM dentro da Master API, sem worker
// novo — atrás da feature flag/allowlist de TriagemLlmConversationOptions
// (mesma seção "TriagemLlm", restrita a tenant/telefone de teste;
// ConversationEnabled=false + allowlists vazias por padrão = nenhum efeito
// observável, "fora da allowlist, comportamento atual inalterado").
builder.Services.AddOptions<TriagemLlmConversationOptions>()
    .Bind(builder.Configuration.GetSection(TriagemLlmConversationOptions.SectionName));
builder.Services.AddScoped<ITriagemLlmConversationContextRepository, TriagemLlmConversationContextRepository>();
builder.Services.AddSingleton<ITriagemLlmRetryDelay, TaskDelayTriagemLlmRetryDelay>();
builder.Services.AddScoped<TriagemLlmConversationService>();

// CORS: as origens de produção são injetadas pelo user-data da EC2 via
// Cors__AllowedOrigins__*. O fallback aberto abaixo existe apenas para
// desenvolvimento local; produção deve sempre configurar a lista.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin();  // fallback apenas; produção deve sempre configurar Cors:AllowedOrigins
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Verificação de segurança: KmsKeyArn obrigatório em produção (Achado 1 / D9).
// Em produção, cifrar credenciais de cliente com chave local é uma falha de
// segurança silenciosa inaceitável — preferimos não subir a aplicação.
var startupLogger = app.Logger;
if (string.IsNullOrWhiteSpace(kmsKeyArn))
{
    if (app.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Integrations:CredentialCipher:KmsKeyArn não está configurado. " +
            "Em produção, o AWS KMS é obrigatório para cifrar credenciais de clientes. " +
            "Configure a variável de ambiente Integrations__CredentialCipher__KmsKeyArn " +
            "com o ARN da CMK antes de iniciar a aplicação.");
    }

    startupLogger.LogWarning(
        "KMS NÃO ESTÁ ATIVO: credenciais de integração serão cifradas com chave local " +
        "(AesGcmCredentialCipher, AlgorithmVersion=1). " +
        "Isso é aceitável em desenvolvimento, mas NUNCA deve ocorrer em produção. " +
        "Configure Integrations__CredentialCipher__KmsKeyArn para ativar o KMS.");
}

// Verificação de segurança análoga para o bucket S3 de mídia inbound da
// triagem (ADR-046): sem bucket configurado, o binário é mantido apenas em
// memória e se perde a cada reinício — inaceitável em produção.
if (string.IsNullOrWhiteSpace(triagemInboundMediaBucketName))
{
    if (app.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            $"{TriagemInboundMediaStorageOptions.SectionName}:BucketName não está configurado. " +
            "Em produção, um bucket S3 é obrigatório para armazenar mídia inbound da triagem de forma " +
            "durável. Configure a variável de ambiente correspondente antes de iniciar a aplicação.");
    }

    startupLogger.LogWarning(
        "ARMAZENAMENTO DE MÍDIA EM MEMÓRIA: nenhum bucket S3 configurado — mídia inbound da triagem " +
        "será mantida apenas em memória (FakeTriagemInboundMediaStorage) e perdida a cada reinício. " +
        "Isso é aceitável em desenvolvimento, mas NUNCA deve ocorrer em produção. " +
        $"Configure {TriagemInboundMediaStorageOptions.SectionName}:BucketName para ativar o S3.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapTimeZoneEndpoints();
app.MapUserLocaleEndpoints();
app.MapCollectorActivationEndpoints();
app.MapCollectorEligibilityEndpoints();
app.MapCollectorCommandEndpoints();
app.MapRecurrentCollectionIntervalEndpoints();
app.MapWorkOrderEndpoints();
app.MapTriagemEndpoints();
app.MapTriagemChannelEndpoints();
app.MapWhatsAppSessionEndpoints();
app.MapTriagemMessageTemplateEndpoints();
app.MapTriagemPoolEndpoints();
app.MapLlmUsageEndpoints();

app.Run();
