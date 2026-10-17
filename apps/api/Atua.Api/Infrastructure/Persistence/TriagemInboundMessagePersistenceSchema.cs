namespace Atua.Api.Infrastructure.Persistence;

internal static class TriagemInboundMessagePersistenceSchema
{
    public const string TableName = "triagem_channel_messages";
    public const string IdempotencyUniqueIndexName = "uq_triagem_channel_messages_tenant_idempotency";
}
