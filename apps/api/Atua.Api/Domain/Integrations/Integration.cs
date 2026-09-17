namespace Atua.Api.Domain.Integrations;

public sealed class Integration
{
    /// <summary>RF-025.4/RN-025.2: padrão histórico citado em ADR-003.</summary>
    public const int DefaultRecurrentCollectionIntervalMinutes = 15;

    public const int MinRecurrentCollectionIntervalMinutes = 5;

    public const int MaxRecurrentCollectionIntervalMinutes = 1440;

    private Integration()
    {
    }

    public Integration(Guid id, Guid tenantId, Guid providerId, bool isEnabled)
    {
        Id = id;
        TenantId = tenantId;
        ProviderId = providerId;
        IsEnabled = isEnabled;
        RecurrentCollectionIntervalMinutes = DefaultRecurrentCollectionIntervalMinutes;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProviderId { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastCollectionAtUtc { get; private set; }

    /// <summary>RF-025.1: frequência (minutos) da coleta recorrente configurável.</summary>
    public int RecurrentCollectionIntervalMinutes { get; private set; } = DefaultRecurrentCollectionIntervalMinutes;

    /// <summary>
    /// RF-025.4/RN-025.2: valida limites em profundidade (defesa em
    /// profundidade além da validação do Office). RN-025.5: não altera o
    /// estado de ativação nem comandos pendentes.
    /// </summary>
    public void SetRecurrentCollectionIntervalMinutes(int minutes, DateTimeOffset now)
    {
        if (minutes < MinRecurrentCollectionIntervalMinutes || minutes > MaxRecurrentCollectionIntervalMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes),
                $"O intervalo deve estar entre {MinRecurrentCollectionIntervalMinutes} e " +
                $"{MaxRecurrentCollectionIntervalMinutes} minutos.");
        }

        RecurrentCollectionIntervalMinutes = minutes;
        UpdatedAtUtc = now;
    }

    public void Enable(DateTimeOffset now)
    {
        IsEnabled = true;
        UpdatedAtUtc = now;
    }

    public void Disable(DateTimeOffset now)
    {
        IsEnabled = false;
        UpdatedAtUtc = now;
    }

    /// <summary>Registra o instante da última coleta bem-sucedida (ADR-024).</summary>
    public void RecordCollection(DateTimeOffset now)
    {
        LastCollectionAtUtc = now;
        UpdatedAtUtc = now;
    }
}