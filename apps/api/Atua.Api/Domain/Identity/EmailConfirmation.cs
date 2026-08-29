namespace Atua.Api.Domain.Identity;

public sealed class EmailConfirmation
{
    private EmailConfirmation()
    {
    }

    public EmailConfirmation(Guid id, Guid userId, string codeHash,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string CodeHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public void Confirm(DateTimeOffset confirmedAt)
    {
        if (confirmedAt > ExpiresAt)
        {
            throw new InvalidOperationException("Codigo de confirmacao expirado.");
        }

        ConfirmedAt ??= confirmedAt;
    }

    /// <summary>
    /// Invalida um codigo ainda nao confirmado, antecipando sua expiracao para que
    /// nao possa mais ser utilizado (ex.: reenvio de cadastro pendente).
    /// </summary>
    public void Invalidate(DateTimeOffset invalidatedAt)
    {
        if (ConfirmedAt is not null)
        {
            return;
        }

        ExpiresAt = invalidatedAt.AddTicks(-1);
    }
}