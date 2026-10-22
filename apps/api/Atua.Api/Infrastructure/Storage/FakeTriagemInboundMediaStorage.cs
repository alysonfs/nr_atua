using System.Collections.Concurrent;
using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Infrastructure.Storage;

/// <summary>
/// Implementação em memória de <see cref="ITriagemInboundMediaStorage"/>,
/// usada em desenvolvimento (quando nenhum bucket S3 está configurado — ver
/// <c>Program.cs</c>) e em testes de integração/unidade. Nunca deve ser
/// usada em produção: o conteúdo não é durável e se perde a cada reinício
/// do processo.
/// </summary>
public sealed class FakeTriagemInboundMediaStorage : ITriagemInboundMediaStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Content, string ContentType)> objects = new();

    /// <summary>
    /// Chaves para as quais <see cref="DeleteAsync"/> deve lançar
    /// <see cref="TriagemInboundMediaStorageUnavailableException"/> — usado
    /// apenas por testes do job de expurgo para simular falha transitória de
    /// storage sem depender de AWS real.
    /// </summary>
    public HashSet<string> FailDeleteForKeys { get; } = [];

    public IReadOnlyDictionary<string, (byte[] Content, string ContentType)> Objects => objects;

    public Task<ETriagemInboundMediaPutOutcome> PutIfAbsentAsync(
        TriagemInboundMediaStorageKey key,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();

        var added = objects.TryAdd(key.Value, (bytes, contentType));
        return Task.FromResult(added
            ? ETriagemInboundMediaPutOutcome.Created
            : ETriagemInboundMediaPutOutcome.AlreadyExists);
    }

    public Task<ETriagemInboundMediaDeleteOutcome> DeleteAsync(
        TriagemInboundMediaStorageKey key,
        CancellationToken cancellationToken)
    {
        if (FailDeleteForKeys.Contains(key.Value))
        {
            throw new TriagemInboundMediaStorageUnavailableException(
                $"Falha simulada ao excluir a chave '{key.Value}'.");
        }

        var removed = objects.TryRemove(key.Value, out _);
        return Task.FromResult(removed
            ? ETriagemInboundMediaDeleteOutcome.Deleted
            : ETriagemInboundMediaDeleteOutcome.NotFound);
    }
}
