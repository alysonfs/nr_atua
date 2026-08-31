namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Estado de execução do consumer de Change Streams (ADR-023, seção 4.1).
/// Persiste o resume token do MongoDB Change Stream para retomada after crash/restart.
/// </summary>
public sealed class ConsumerState
{
    private ConsumerState() { }

    public ConsumerState(string consumerId, string? resumeToken, DateTimeOffset updatedAt)
    {
        ConsumerId = consumerId;
        ResumeToken = resumeToken;
        UpdatedAt = updatedAt;
    }

    /// <summary>Chave primária — identificador do consumer (ex.: "snapshot-to-work-order").</summary>
    public string ConsumerId { get; private set; } = string.Empty;

    /// <summary>Resume token do Change Stream serializado como JSON (JSONB no Postgres).</summary>
    public string? ResumeToken { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Atualiza o resume token e o timestamp.</summary>
    public void SetResumeToken(string? token, DateTimeOffset now)
    {
        ResumeToken = token;
        UpdatedAt = now;
    }
}
