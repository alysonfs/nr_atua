namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Registro de requisição de mutação do controle do coletor (ADR-020).
/// Unicidade em <c>(IntegrationId, Operation, IdempotencyKey)</c>.
///
/// Reusar uma chave com conteúdo diferente devolve conflito, sem executar nova
/// mutação.
/// </summary>
public sealed class CollectorControlIdempotency
{
    private CollectorControlIdempotency() { }

    public CollectorControlIdempotency(Guid id, Guid tenantId, Guid integrationId,
        ECollectorControlOperation operation, string idempotencyKey, string requestHash,
        string responseSnapshot, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        Operation = operation;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        ResponseSnapshot = responseSnapshot;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid IntegrationId { get; private set; }

    public ECollectorControlOperation Operation { get; private set; }

    /// <summary>Chave opaca enviada pelo cliente. Não é autorização.</summary>
    public string IdempotencyKey { get; private set; } = null!;

    public string RequestHash { get; private set; } = null!;

    /// <summary>Resposta final sanitizada, para repetição segura da requisição.</summary>
    public string ResponseSnapshot { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }
}
