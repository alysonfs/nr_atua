using Amazon.SimpleEmailV2;
using System.Security.Claims;
using System.Text;
using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Application.Integrations;
using Atua.Api.Application.Integrations.CollectorControl;
using Atua.Api.Application.Tenants;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Email;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Atua")
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
    var client = sp.GetRequiredService<IAmazonSimpleEmailServiceV2>();

    return new SesEmailConfirmationSender(client, senderAddress);
});
builder.Services.AddScoped<SignUpService>();
builder.Services.AddScoped<ConfirmEmailService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TimeZonePreferenceService>();
builder.Services.AddScoped<CreateTrialService>();
builder.Services.AddScoped<TrialEligibilityService>();
builder.Services.Configure<CredentialCipherOptions>(
    builder.Configuration.GetSection(CredentialCipherOptions.SectionName));
builder.Services.AddSingleton<ICredentialCipher, AesGcmCredentialCipher>();
builder.Services.AddScoped<TenantOnboardingService>();
builder.Services.AddScoped<IServiceCredentialService>();
builder.Services.AddScoped<IServiceCredentialValidationService>();
builder.Services.AddScoped<ICollectorEligibilityEvaluator, CollectorEligibilityEvaluator>();
builder.Services.AddScoped<CollectorActivationService>();
// TODO(ADR-018): substituir por implementação real quando o protocolo do
// iService estiver especificado. Ver FakeIServiceAuthClient.
builder.Services.AddSingleton<IIServiceAuthClient, FakeIServiceAuthClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapTrialEndpoints();
app.MapTenantEndpoints();
app.MapCollectorActivationEndpoints();

app.Run();
