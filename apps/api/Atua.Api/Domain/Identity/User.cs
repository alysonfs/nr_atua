namespace Atua.Api.Domain.Identity;

public sealed class User
{
    private User()
    {
    }

    public User(Guid id, string? name, string email, string passwordHash,
        EGlobalUserRole globalRole = EGlobalUserRole.User)
    {
        Id = id;
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        GlobalRole = globalRole;
    }

    public Guid Id { get; private set; }

    public string? Name { get; private set; }

    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public EGlobalUserRole GlobalRole { get; private set; }

    public DateTimeOffset? EmailConfirmedAt { get; private set; }

    public void ConfirmEmail(DateTimeOffset confirmedAt)
    {
        EmailConfirmedAt ??= confirmedAt;
    }
}