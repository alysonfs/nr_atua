using Atua.Api.Domain;
using Atua.Api.Domain.Identity;

namespace Atua.Api.Tests;

public class EmailConfirmationTests
{
    [Fact]
    public void ConfirmaCodigoDentroDaValidade()
    {
        var now = DateTimeOffset.UtcNow;
        var confirmation = new EmailConfirmation(Uuid7.New(), Uuid7.New(),
            "codigo-com-hash", now.AddMinutes(15));

        confirmation.Confirm(now);

        Assert.Equal(now, confirmation.ConfirmedAt);
    }

    [Fact]
    public void RejeitaCodigoExpirado()
    {
        var confirmation = new EmailConfirmation(Uuid7.New(), Uuid7.New(),
            "codigo-com-hash", DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Throws<InvalidOperationException>(() =>
            confirmation.Confirm(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MantemPrimeiraConfirmacaoDoUsuario()
    {
        var user = new User(Uuid7.New(), "Leonardo", "owner@atua.com",
            "$argon2id$v=19$hash");
        var firstConfirmation = DateTimeOffset.UtcNow;

        user.ConfirmEmail(firstConfirmation);
        user.ConfirmEmail(firstConfirmation.AddMinutes(1));

        Assert.Equal(firstConfirmation, user.EmailConfirmedAt);
    }
}