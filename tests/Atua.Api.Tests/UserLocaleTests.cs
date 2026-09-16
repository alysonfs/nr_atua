using Atua.Api.Domain.Identity;

namespace Atua.Api.Tests;

public class UserLocaleTests
{
    [Fact]
    public void NovoUsuarioUsaPortuguesDoBrasilComoLocalePadrao()
    {
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");

        Assert.Equal(UserLocale.Default, user.PreferredLocale);
    }

    [Theory]
    [InlineData(UserLocale.Default)]
    [InlineData(UserLocale.EnglishUnitedStates)]
    [InlineData(UserLocale.SpanishArgentina)]
    public void AceitaSomenteLocalesSuportados(string locale)
    {
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");

        user.SetPreferredLocale(locale);

        Assert.Equal(locale, user.PreferredLocale);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pt-br")]
    [InlineData("fr-FR")]
    public void RejeitaLocaleForaDaListaFechada(string? locale)
    {
        var user = new User(Guid.CreateVersion7(), null, "owner@atua.com", "hash");

        Assert.Throws<ArgumentException>(() => user.SetPreferredLocale(locale));
        Assert.Equal(UserLocale.Default, user.PreferredLocale);
    }
}
