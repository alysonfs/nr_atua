namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Abstração de armazenamento durável do binário de mídia inbound da
/// triagem (ADR-046). Não vaza o SDK do fornecedor (S3) para o domínio/
/// aplicação — apenas <see cref="PutIfAbsentAsync"/>, <see cref="DeleteAsync"/>
/// e erros tipados.
///
/// A chave do objeto é sempre <c>tenants/{TenantId}/inbound-media/{MediaId}</c>
/// (<see cref="TriagemInboundMediaStorageKey"/>) — nunca contém telefone ou
/// outro identificador do consumidor.
/// </summary>
public interface ITriagemInboundMediaStorage
{
    /// <summary>
    /// Grava o objeto somente se a chave ainda não existir (gravação
    /// condicional, idempotente). Reentrega da mesma chave com o mesmo
    /// conteúdo deve ser inofensiva — o chamador decide antes se o conteúdo
    /// mudou (conflito de idempotência é responsabilidade do repositório,
    /// não deste armazenamento).
    /// </summary>
    Task<ETriagemInboundMediaPutOutcome> PutIfAbsentAsync(
        TriagemInboundMediaStorageKey key,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken);

    /// <summary>
    /// Remove o objeto da chave informada, de forma idempotente: a ausência
    /// prévia do objeto nunca é um erro, apenas um resultado distinto
    /// (<see cref="ETriagemInboundMediaDeleteOutcome.NotFound"/>). Usada pelo
    /// expurgo (ADR-046/047) — o chamador deve apagar o objeto antes de
    /// apagar os metadados correspondentes, nunca na ordem inversa.
    /// </summary>
    Task<ETriagemInboundMediaDeleteOutcome> DeleteAsync(
        TriagemInboundMediaStorageKey key,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gera uma URL temporária e pré-assinada para leitura (GET) do objeto —
    /// usada pelo painel do operador (épico, bloco C — "C2 — Detalhe") para
    /// exibir anexos sem expor o bucket nem credenciais.
    ///
    /// PENDÊNCIA DE INFRAESTRUTURA: a role atual da EC2 só concede
    /// <c>s3:PutObject</c>/<c>s3:DeleteObject</c> (ADR-046) — a assinatura em
    /// si é feita localmente pelo SDK e não exige <c>s3:GetObject</c> para
    /// ser *gerada*, mas a URL gerada só funcionará quando acessada
    /// (navegador/cliente chamando o S3 diretamente) se a role tiver também
    /// <c>s3:GetObject</c> no bucket. Essa permissão precisa ser concedida
    /// via CDK pelo `aws-architect` — não alterada aqui.
    /// </summary>
    Task<Uri> CreatePresignedReadUrlAsync(
        TriagemInboundMediaStorageKey key,
        TimeSpan expiresIn,
        CancellationToken cancellationToken);
}

/// <summary>Chave determinística do objeto S3 — sem telefone/IDs do consumidor (decisão do `software-architect`).</summary>
public sealed record TriagemInboundMediaStorageKey(Guid TenantId, Guid MediaId)
{
    public string Value => $"tenants/{TenantId:D}/inbound-media/{MediaId:D}";

    public override string ToString() => Value;
}

public enum ETriagemInboundMediaPutOutcome
{
    /// <summary>O objeto foi gravado nesta chamada.</summary>
    Created,

    /// <summary>A chave já existia — nada foi sobrescrito.</summary>
    AlreadyExists
}

public enum ETriagemInboundMediaDeleteOutcome
{
    /// <summary>O objeto existia e foi removido nesta chamada.</summary>
    Deleted,

    /// <summary>O objeto já não existia — idempotente, tratado como sucesso.</summary>
    NotFound
}

/// <summary>Base para erros tipados do armazenamento de mídia inbound — nunca vaza exceções do SDK do fornecedor.</summary>
public abstract class TriagemInboundMediaStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Falha transitória de conectividade/infraestrutura do armazenamento — segura para retentativa.</summary>
public sealed class TriagemInboundMediaStorageUnavailableException(string message, Exception? innerException = null)
    : TriagemInboundMediaStorageException(message, innerException);

/// <summary>Falha não transitória (ex.: permissão negada, bucket inexistente) — não deve ser retentada sem intervenção.</summary>
public sealed class TriagemInboundMediaStoragePermanentException(string message, Exception? innerException = null)
    : TriagemInboundMediaStorageException(message, innerException);
