using Amazon.KeyManagementService;
using Amazon.SimpleEmailV2;
using System.Security.Claims;
using System.Text;
using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Application.Integrations;
using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Application.Tenants;
using Atua.Api.Application.WorkOrders;
using Atua.Api.Application.WorkOrders.Contracts;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Email;
using Atua.Api.Infrastructure.Jobs;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

app.Run();
