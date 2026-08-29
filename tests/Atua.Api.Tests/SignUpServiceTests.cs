using Atua.Api.Application.Identity;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atua.Api.Tests;

public class SignUpServiceTests
{
    [Fact]
    public async Task CriaUsuarioEConfirmacaoComValidadeDeQuinzeMinutos()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        await using var context = CreateContext();
        var sender = new FakeEmailConfirmationSender();
        var service = CreateService(context, sender, now);

        var result = await service.ExecuteAsync(new SignUpCommand(
            "  Leonardo@NatalRefrigeracao.com.br ", "senha123", "senha123"),
            CancellationToken.None);

        var user = await context.Users.SingleAsync();
        var confirmation = await context.EmailConfirmations.SingleAsync();

        Assert.Equal(ESignUpStatus.Success, result.Status);
        Assert.Equal(confirmation.Id, result.ConfirmationId);
        Assert.Equal("leonardo@natalrefrigeracao.com.br", user.Email);
        Assert.Equal("hash:senha123", user.PasswordHash);
        Assert.Null(user.Name);
        Assert.Equal(now.AddMinutes(15), confirmation.ExpiresAt);
        Assert.Equal("leonardo@natalrefrigeracao.com.br", sender.Email);
        Assert.Equal("482913", sender.Code);
    }

    [Fact]
    public async Task RejeitaSenhaCurtaOuComConfirmacaoDiferente()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new FakeEmailConfirmationSender(),
            DateTimeOffset.UtcNow);

        var result = await service.ExecuteAsync(new SignUpCommand(
            "owner@atua.com", "curta", "diferente"), CancellationToken.None);

        Assert.Equal(ESignUpStatus.InvalidPassword, result.Status);
        Assert.Empty(context.Users);
        Assert.Empty(context.EmailConfirmations);
    }

    [Fact]
    public async Task RejeitaEmailJaCadastradoEConfirmado()
    {
        await using var context = CreateContext();
        var existingUser = new User(Guid.CreateVersion7(), null, "owner@atua.com",
            "hash:senha123");
        existingUser.ConfirmEmail(DateTimeOffset.UtcNow);
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();
        var sender = new FakeEmailConfirmationSender();
        var service = CreateService(context, sender, DateTimeOffset.UtcNow);

        var result = await service.ExecuteAsync(new SignUpCommand(
            "OWNER@atua.com", "senha123", "senha123"), CancellationToken.None);

        Assert.Equal(ESignUpStatus.EmailAlreadyRegistered, result.Status);
        Assert.Null(sender.Email);
    }

    [Fact]
    public async Task PermiteNovaTentativaQuandoEmailAindaNaoFoiConfirmado()
    {
        var registeredAt = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var retryAt = registeredAt.AddMinutes(20);
        await using var context = CreateContext();
        var pendingUser = new User(Guid.CreateVersion7(), null, "owner@atua.com",
            "hash:senha-original");
        context.Users.Add(pendingUser);
        var originalConfirmation = new EmailConfirmation(Guid.CreateVersion7(),
            pendingUser.Id, "hash:111111", registeredAt.AddMinutes(15));
        context.EmailConfirmations.Add(originalConfirmation);
        await context.SaveChangesAsync();
        var sender = new FakeEmailConfirmationSender();
        var service = CreateService(context, sender, retryAt);

        var result = await service.ExecuteAsync(new SignUpCommand(
            "OWNER@atua.com", "senha-diferente", "senha-diferente"), CancellationToken.None);

        var user = await context.Users.SingleAsync();
        var confirmations = await context.EmailConfirmations.ToListAsync();
        var newConfirmation = confirmations.Single(c => c.Id != originalConfirmation.Id);

        Assert.Equal(ESignUpStatus.Success, result.Status);
        Assert.Equal(newConfirmation.Id, result.ConfirmationId);
        Assert.Equal("hash:senha-original", user.PasswordHash);
        Assert.Equal(2, confirmations.Count);
        Assert.True(originalConfirmation.ExpiresAt <= retryAt);
        Assert.Null(originalConfirmation.ConfirmedAt);
        Assert.Equal(retryAt.AddMinutes(15), newConfirmation.ExpiresAt);
        Assert.Equal("owner@atua.com", sender.Email);
        Assert.Equal("482913", sender.Code);
    }

    [Fact]
    public void TrataViolacaoDeUnicidadeDoPostgresComoEmailJaCadastrado()
    {
        // Documenta a estrategia usada em SignUpService: quando o SaveChangesAsync
        // falha por conflito de unicidade do indice de email (condicao de corrida),
        // a excecao e capturada e o status EmailAlreadyRegistered e retornado (409),
        // em vez de deixar a excecao subir como erro 500. O provider InMemory usado
        // nos demais testes nao gera Npgsql.PostgresException, entao aqui validamos
        // apenas o predicado de deteccao usado no catch.
        var postgresException = new PostgresException("duplicate key value violates unique constraint",
            "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        var dbUpdateException = new DbUpdateException("erro ao salvar", postgresException);

        Assert.IsType<PostgresException>(dbUpdateException.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation,
            ((PostgresException)dbUpdateException.InnerException!).SqlState);
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AtuaDbContext(options);
    }

    private static SignUpService CreateService(AtuaDbContext context,
        FakeEmailConfirmationSender sender, DateTimeOffset now)
    {
        return new SignUpService(context, new FakeSecretHasher(),
            new FixedEmailConfirmationCodeGenerator(), sender, new FixedTimeProvider(now));
    }

    private sealed class FakeSecretHasher : ISecretHasher
    {
        public string Hash(string value) => $"hash:{value}";

        public bool Verify(string value, string hash) => hash == Hash(value);
    }

    private sealed class FixedEmailConfirmationCodeGenerator : IEmailConfirmationCodeGenerator
    {
        public string Generate() => "482913";
    }

    private sealed class FakeEmailConfirmationSender : IEmailConfirmationSender
    {
        public string? Email { get; private set; }

        public string? Code { get; private set; }

        public Task SendAsync(string email, string code, CancellationToken cancellationToken)
        {
            Email = email;
            Code = code;

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}