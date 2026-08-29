using Atua.Api.Application.Identity;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

public class ConfirmEmailServiceTests
{
    [Fact]
    public async Task ConfirmaEmailQuandoCodigoValidoEDentroDaValidade()
    {
        var registeredAt = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var confirmAt = registeredAt.AddMinutes(5);
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash:senha123");
        context.Users.Add(user);
        var confirmation = new EmailConfirmation(Guid.CreateVersion7(), user.Id,
            "hash:482913", registeredAt.AddMinutes(15));
        context.EmailConfirmations.Add(confirmation);
        await context.SaveChangesAsync();
        var service = CreateService(context, confirmAt);

        var result = await service.ExecuteAsync(new ConfirmEmailCommand(
            "OWNER@atua.com", "482913"), CancellationToken.None);

        var updatedUser = await context.Users.SingleAsync();
        var updatedConfirmation = await context.EmailConfirmations.SingleAsync();

        Assert.Equal(EConfirmEmailStatus.Success, result.Status);
        Assert.Equal(confirmAt, updatedUser.EmailConfirmedAt);
        Assert.Equal(confirmAt, updatedConfirmation.ConfirmedAt);
    }

    [Fact]
    public async Task RejeitaCodigoInvalido()
    {
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash:senha123");
        context.Users.Add(user);
        var confirmation = new EmailConfirmation(Guid.CreateVersion7(), user.Id,
            "hash:482913", DateTimeOffset.UtcNow.AddMinutes(15));
        context.EmailConfirmations.Add(confirmation);
        await context.SaveChangesAsync();
        var service = CreateService(context, DateTimeOffset.UtcNow);

        var result = await service.ExecuteAsync(new ConfirmEmailCommand(
            "owner@atua.com", "000000"), CancellationToken.None);

        Assert.Equal(EConfirmEmailStatus.InvalidCode, result.Status);
        Assert.Null((await context.Users.SingleAsync()).EmailConfirmedAt);
    }

    [Fact]
    public async Task RetornaExpiradoQuandoCodigoJaVenceu()
    {
        var registeredAt = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var confirmAt = registeredAt.AddMinutes(20);
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash:senha123");
        context.Users.Add(user);
        var confirmation = new EmailConfirmation(Guid.CreateVersion7(), user.Id,
            "hash:482913", registeredAt.AddMinutes(15));
        context.EmailConfirmations.Add(confirmation);
        await context.SaveChangesAsync();
        var service = CreateService(context, confirmAt);

        var result = await service.ExecuteAsync(new ConfirmEmailCommand(
            "owner@atua.com", "482913"), CancellationToken.None);

        Assert.Equal(EConfirmEmailStatus.ExpiredCode, result.Status);
        Assert.Null((await context.Users.SingleAsync()).EmailConfirmedAt);
        Assert.Null((await context.EmailConfirmations.SingleAsync()).ConfirmedAt);
    }

    [Fact]
    public async Task RetornaAlreadyConfirmedQuandoUsuarioJaConfirmouEmail()
    {
        await using var context = CreateContext();
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash:senha123");
        user.ConfirmEmail(DateTimeOffset.UtcNow);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = CreateService(context, DateTimeOffset.UtcNow);

        var result = await service.ExecuteAsync(new ConfirmEmailCommand(
            "owner@atua.com", "482913"), CancellationToken.None);

        Assert.Equal(EConfirmEmailStatus.AlreadyConfirmed, result.Status);
    }

    [Fact]
    public async Task RetornaInvalidCodeQuandoUsuarioNaoExiste()
    {
        await using var context = CreateContext();
        var service = CreateService(context, DateTimeOffset.UtcNow);

        var result = await service.ExecuteAsync(new ConfirmEmailCommand(
            "desconhecido@atua.com", "482913"), CancellationToken.None);

        Assert.Equal(EConfirmEmailStatus.InvalidCode, result.Status);
    }

    private static AtuaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AtuaDbContext(options);
    }

    private static ConfirmEmailService CreateService(AtuaDbContext context, DateTimeOffset now)
    {
        return new ConfirmEmailService(context, new FakeSecretHasher(), new FixedTimeProvider(now));
    }

    private sealed class FakeSecretHasher : ISecretHasher
    {
        public string Hash(string value) => $"hash:{value}";

        public bool Verify(string value, string hash) => hash == Hash(value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
