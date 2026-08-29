using Amazon.SimpleEmailV2;
using Atua.Api.Application.Identity;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Email;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Atua")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Atua")
    ?? "Host=localhost;Database=atua;Username=atua";

builder.Services.AddDbContext<AtuaDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2, AmazonSimpleEmailServiceV2Client>();
builder.Services.AddSingleton<ISecretHasher, Argon2idSecretHasher>();
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapAuthEndpoints();

app.Run();
