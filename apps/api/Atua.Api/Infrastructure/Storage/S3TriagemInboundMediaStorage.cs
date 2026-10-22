using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Atua.Api.Application.Triagens.Contracts;

namespace Atua.Api.Infrastructure.Storage;

/// <summary>
/// Implementação S3 de <see cref="ITriagemInboundMediaStorage"/> (ADR-046).
/// Usa gravação condicional (<c>If-None-Match: *</c>) para um
/// <c>PutIfAbsent</c> atômico — nunca sobrescreve um objeto já gravado sob a
/// mesma chave. Não vaza <see cref="AmazonS3Exception"/> nem qualquer outro
/// tipo do SDK para os chamadores — apenas os erros tipados do contrato.
///
/// Esta classe nunca é exercitada por testes automatizados desta sessão
/// (nenhuma chamada real à AWS é feita) — cobertura de comportamento é feita
/// via <c>FakeTriagemInboundMediaStorage</c>, usado pelos testes de API.
/// </summary>
public sealed class S3TriagemInboundMediaStorage(IAmazonS3 s3Client, TriagemInboundMediaStorageOptions options)
    : ITriagemInboundMediaStorage
{
    public async Task<ETriagemInboundMediaPutOutcome> PutIfAbsentAsync(
        TriagemInboundMediaStorageKey key,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = key.Value,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
        };
        request.Headers.ContentLength = contentLength;
        // Gravação condicional: só grava se a chave ainda não existir no bucket.
        // Suportado nativamente pelo S3 (conditional writes) — evita a corrida
        // check-then-put de um HeadObject seguido de PutObject.
        request.IfNoneMatch = "*";

        try
        {
            await s3Client.PutObjectAsync(request, cancellationToken);
            return ETriagemInboundMediaPutOutcome.Created;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return ETriagemInboundMediaPutOutcome.AlreadyExists;
        }
        catch (AmazonS3Exception exception) when (IsTransient(exception))
        {
            throw new TriagemInboundMediaStorageUnavailableException(
                "Falha transitória ao gravar a mídia inbound em S3.", exception);
        }
        catch (AmazonS3Exception exception)
        {
            throw new TriagemInboundMediaStoragePermanentException(
                "Falha ao gravar a mídia inbound em S3.", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new TriagemInboundMediaStorageUnavailableException(
                "Falha transitória de conectividade ao gravar a mídia inbound em S3.", exception);
        }
    }

    public async Task<ETriagemInboundMediaDeleteOutcome> DeleteAsync(
        TriagemInboundMediaStorageKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            await s3Client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = options.BucketName, Key = key.Value },
                cancellationToken);
            // S3 responde 204 mesmo quando a chave não existia (semântica
            // idempotente nativa do DeleteObject) — não há como distinguir
            // "já não existia" de "foi removido agora" sem um HeadObject
            // prévio (corrida check-then-delete que preferimos evitar).
            // Ambos os casos são sucesso para o chamador (expurgo).
            return ETriagemInboundMediaDeleteOutcome.Deleted;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return ETriagemInboundMediaDeleteOutcome.NotFound;
        }
        catch (AmazonS3Exception exception) when (IsTransient(exception))
        {
            throw new TriagemInboundMediaStorageUnavailableException(
                "Falha transitória ao excluir a mídia inbound em S3.", exception);
        }
        catch (AmazonS3Exception exception)
        {
            throw new TriagemInboundMediaStoragePermanentException(
                "Falha ao excluir a mídia inbound em S3.", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new TriagemInboundMediaStorageUnavailableException(
                "Falha transitória de conectividade ao excluir a mídia inbound em S3.", exception);
        }
    }

    public Task<Uri> CreatePresignedReadUrlAsync(
        TriagemInboundMediaStorageKey key,
        TimeSpan expiresIn,
        CancellationToken cancellationToken)
    {
        // Assinatura local via SDK (não chama a AWS) — exige apenas que o
        // client esteja configurado com credenciais válidas. O acesso
        // efetivo da URL resultante depende de s3:GetObject na role (ver
        // pendência documentada em ITriagemInboundMediaStorage).
        var request = new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
            Key = key.Value,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiresIn),
        };

        var url = s3Client.GetPreSignedURL(request);
        return Task.FromResult(new Uri(url));
    }

    private static bool IsTransient(AmazonS3Exception exception) =>
        exception.StatusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.RequestTimeout
            or (HttpStatusCode)429
        || exception.ErrorCode is "RequestTimeout" or "SlowDown" or "InternalError";
}

/// <summary>Configuração do bucket de mídia inbound da triagem — nunca inclui credenciais (ADR-046).</summary>
public sealed class TriagemInboundMediaStorageOptions
{
    public const string SectionName = "TriagemInboundMedia";

    public string BucketName { get; set; } = "";
}
