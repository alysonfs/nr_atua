using Atua.Api.Application.Localization;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Tenants;

namespace Atua.Api.Tests;

public class TimeZoneContextResolverTests
{
    [Fact]
    public void OverrideDeSessaoTemPrecedenciaSobreTenant()
    {
        var timeZone = TimeZoneContextResolver.Resolve(
            "Europe/Lisbon", "America/Sao_Paulo", "pt-BR");

        Assert.Equal("Europe/Lisbon", timeZone);
    }

    [Fact]
    public void TenantTemPrecedenciaSobrePadraoDoIdioma()
    {
        var timeZone = TimeZoneContextResolver.Resolve(null, "America/Manaus", "pt-BR");

        Assert.Equal("America/Manaus", timeZone);
    }

    [Theory]
    [InlineData("pt-BR", "America/Sao_Paulo")]
    [InlineData("en-US", "UTC")]
    public void UsaPadraoPorIdiomaESenaoUtc(string culture, string expected)
    {
        Assert.Equal(expected, TimeZoneContextResolver.Resolve(null, null, culture));
    }

    [Fact]
    public void RejeitaFusoIanaInvalidoNoTenantENaSessao()
    {
        Assert.Throws<ArgumentException>(() =>
            new Tenant(Guid.CreateVersion7(), "Atua", "12345678901234", "not-a-time-zone"));
        Assert.Throws<ArgumentException>(() =>
            new AuthSession(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow,
                "not-a-time-zone"));
    }
}
