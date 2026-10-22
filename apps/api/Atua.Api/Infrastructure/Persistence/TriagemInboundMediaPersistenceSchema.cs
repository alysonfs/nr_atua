namespace Atua.Api.Infrastructure.Persistence;

internal static class TriagemInboundMediaPersistenceSchema
{
    public const string TableName = "triagem_channel_media";
    public const string IdempotencyUniqueIndexName = "uq_triagem_channel_media_tenant_idempotency";
}
