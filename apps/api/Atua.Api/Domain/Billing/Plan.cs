namespace Atua.Api.Domain.Billing;

/// <summary>
/// Catálogo de planos comerciais (RF-020/ADR-024). Os limites de uso
/// (<see cref="MaxIntegrations"/>, <see cref="MaxUsers"/>) são dados de
/// configuração do plano, não valores fixos em código (RN-020.1).
/// </summary>
public sealed class Plan
{
    private Plan() { }

    public Plan(Guid id, string code, string name, bool isFree, decimal value,
        int? durationDays, int maxIntegrations, int maxUsers, bool isActive)
    {
        Id = id;
        Code = code;
        Name = name;
        IsFree = isFree;
        Value = value;
        DurationDays = durationDays;
        MaxIntegrations = maxIntegrations;
        MaxUsers = maxUsers;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool IsFree { get; private set; }

    public decimal Value { get; private set; }

    /// <summary>Nulo = sem expiração automática.</summary>
    public int? DurationDays { get; private set; }

    public int MaxIntegrations { get; private set; }

    public int MaxUsers { get; private set; }

    public bool IsActive { get; private set; }
}
