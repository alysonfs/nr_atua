using Atua.Api.Application.Identity;
using Atua.Api.Domain.Identity;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Tests;

public class UserLocalePreferenceServiceTests
{
    [Fact]
    public async Task LeEAtualizaLocalePersistidoDoUsuario()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.CreateVersion7();
        await using (var seedContext = CreateContext(databaseName))
        {
            seedContext.Users.Add(new User(userId, null, "owner@atua.com", "hash"));
            await seedContext.SaveChangesAsync();
        }

        await using (var updateContext = CreateContext(databaseName))
        {
            var service = new UserLocalePreferenceService(updateContext);

            Assert.Equal(UserLocale.Default,
                await service.GetAsync(userId, CancellationToken.None));
            Assert.True(await service.UpdateAsync(userId, UserLocale.EnglishUnitedStates,
                CancellationToken.None));
        }

        await using var readContext = CreateContext(databaseName);
        Assert.Equal(UserLocale.EnglishUnitedStates,
            (await readContext.Users.FindAsync(userId))!.PreferredLocale);
    }

    [Fact]
    public async Task InformaQuandoUsuarioNaoExiste()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = new UserLocalePreferenceService(context);
        var userId = Guid.CreateVersion7();

        Assert.Null(await service.GetAsync(userId, CancellationToken.None));
        Assert.False(await service.UpdateAsync(userId, UserLocale.Default,
            CancellationToken.None));
    }

    [Fact]
    public async Task NaoPersisteLocaleInvalido()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new UserLocalePreferenceService(context);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(user.Id, "fr-FR", CancellationToken.None));

        Assert.Equal(UserLocale.Default, user.PreferredLocale);
    }

    private static AtuaDbContext CreateContext(string databaseName) => new(
        new DbContextOptionsBuilder<AtuaDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
