using System.Security.Cryptography;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Orquestra os endpoints internos do Worker: <c>claim</c> e <c>complete</c>
/// (ADR-021/RF-009). Nunca retorna credenciais em claro no log; a resposta
/// do claim inclui credenciais decifradas protegidas por TLS (ADR-021/D9-B).
/// </summary>
public sealed class ImmediateCollectionCommandService(
    AtuaDbContext dbContext,
    ICredentialCipher cipher,
    CollectorActivationService activationService,
    TimeProvider timeProvider,
    IOptions<ImmediateCollectionOptions> options)
{
    private static readonly TimeSpan ClaimTimeoutFallback = TimeSpan.FromMinutes(30);

    private TimeSpan ClaimTimeout =>
        TimeSpan.FromMinutes(options.Value.ClaimTimeoutMinutes > 0
            ? options.Value.ClaimTimeoutMinutes
            : 30);

    // -----------------------------------------------------------------------
    // Claim
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reivindica o próximo comando <c>Pending</c> da integração informada,
    /// devolvendo as credenciais já decifradas (ADR-021/D9-B). Retorna
    /// <c>null</c> quando não há comando pendente (→ 204 No Content).
    /// </summary>
    public async Task<ClaimCommandResult?> ClaimAsync(
        Guid integrationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // D2 — verificação preguiçosa: expira comando Claimed órfão antes de
        // tentar reivindicar um novo, para liberar a integração.
        await ExpireTimedOutClaimedCommandAsync(integrationId, now, cancellationToken);

        var command = await dbContext.ImmediateCollectionCommands
            .SingleOrDefaultAsync(
                c => c.IntegrationId == integrationId &&
                     c.Status == EImmediateCollectionCommandStatus.Pending,
                cancellationToken);

        if (command is null) return null;

        // D3 — máximo de 1 tentativa: se AttemptCount já atingiu o limite e o
        // comando chegou aqui novamente em Pending (cenário de re-criação
        // futura), o limite é aplicado aqui. No MVP o comando nunca volta a
        // Pending automaticamente após falha, então AttemptCount em Pending
        // sempre será 0 na primeira vez.
        if (!command.TryClaim(now, ClaimTimeout))
        {
            // Não foi possível transitar (concorrência ou estado inválido).
            return null;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Dois Workers tentaram reivindicar o mesmo comando simultaneamente.
            // O Worker que chegou segundo recebe 204 No Content — não é erro do
            // servidor, apenas indica que não há comando disponível para ele.
            // O DbContext é Scoped (uma instância por request HTTP), portanto
            // o contexto é descartado ao fim deste request e não há risco de
            // estado sujo em operações futuras no mesmo scope.
            return null;
        }

        // D9-B — decifrar credenciais em memória e devolver na resposta.
        // A DEK é zerada após as três decifragens.
        var credential = await dbContext.IServiceCredentials.AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.IntegrationId == integrationId,
                cancellationToken);

        if (credential is null)
        {
            // Situação anômala: nenhuma credencial configurada para a integração.
            // Falha o comando imediatamente para não bloquear a integração.
            command.TryFail(ECommandFailureReason.UnexpectedError, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        string username;
        string password;
        string? baseUrl;
        try
        {
            (username, password, baseUrl) = await DecryptCredentialsAsync(credential, cancellationToken);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // Chave mestra/DEK divergente da usada para cifrar a credencial:
            // trata como CredentialRejected em vez de propagar 500 ao Worker
            // (mesmo padrão de IServiceCredentialValidationService.ValidateAsync).
            command.TryFail(ECommandFailureReason.CredentialRejected, now);
            await InvalidateCredentialAndReconcileAsync(
                command.TenantId, integrationId, now, cancellationToken);
            return null;
        }

        return new ClaimCommandResult(
            command.Id,
            command.IntegrationId,
            command.TenantId,
            command.ProviderId,
            command.RequestedAtUtc,
            command.ClaimedAtUtc!.Value,
            command.ClaimExpiresAtUtc!.Value,
            options.Value.HistoryWindowMonths,
            new DecryptedCredential(username, password, baseUrl));
    }

    // -----------------------------------------------------------------------
    // Complete
    // -----------------------------------------------------------------------

    /// <summary>
    /// Conclui o comando com o resultado informado pelo Worker (ADR-021/D4).
    /// Retorna <c>null</c> quando o <c>commandId</c> não pertence à integração.
    /// </summary>
    public async Task<CompleteCommandResult?> CompleteAsync(
        Guid commandId, Guid integrationId,
        EImmediateCollectionCommandStatus outcome,
        ECommandFailureReason? failureReason,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        var command = await dbContext.ImmediateCollectionCommands
            .SingleOrDefaultAsync(
                c => c.Id == commandId && c.IntegrationId == integrationId,
                cancellationToken);

        if (command is null) return null;

        var now = timeProvider.GetUtcNow();

        // Idempotência: comando já em estado terminal → retorna sem efeitos.
        if (command.Status is EImmediateCollectionCommandStatus.Succeeded
            or EImmediateCollectionCommandStatus.Cancelled
            or EImmediateCollectionCommandStatus.Failed)
        {
            return new CompleteCommandResult(command.Id, command.Status, command.CompletedAtUtc ?? now);
        }

        switch (outcome)
        {
            case EImmediateCollectionCommandStatus.Succeeded:
            case EImmediateCollectionCommandStatus.Cancelled:
                command.TryComplete(outcome, now);
                await dbContext.SaveChangesAsync(cancellationToken);
                break;

            case EImmediateCollectionCommandStatus.Failed:
                var reason = failureReason ?? ECommandFailureReason.UnexpectedError;
                command.TryFail(reason, now);

                if (reason == ECommandFailureReason.CredentialRejected)
                {
                    // D4 — invalida ValidationStatus e aciona reconciliação
                    // na mesma unidade de trabalho (padrão de
                    // IServiceCredentialValidationService.ValidateAsync).
                    await InvalidateCredentialAndReconcileAsync(
                        command.TenantId, integrationId, now, cancellationToken);
                }
                else
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                break;

            default:
                return null;
        }

        return new CompleteCommandResult(command.Id, command.Status, command.CompletedAtUtc ?? now);
    }

    // -----------------------------------------------------------------------
    // Timeout de re-claim (job interno e verificação preguiçosa)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Expira todos os comandos <c>Claimed</c> com
    /// <c>ClaimExpiresAtUtc &lt; now</c> para <c>Failed/ClaimTimeout</c>
    /// (ADR-021/D2). Chamado pelo <c>ClaimTimeoutJob</c> (varredura periódica)
    /// e pela verificação preguiçosa no <c>ClaimAsync</c>.
    /// </summary>
    public async Task<int> ExpireTimedOutCommandsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expired = await dbContext.ImmediateCollectionCommands
            .Where(c => c.Status == EImmediateCollectionCommandStatus.Claimed &&
                        c.ClaimExpiresAtUtc != null &&
                        c.ClaimExpiresAtUtc < now)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0) return 0;

        foreach (var command in expired)
        {
            command.TryExpireTimeout(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    // -----------------------------------------------------------------------
    // Helpers privados
    // -----------------------------------------------------------------------

    private async Task ExpireTimedOutClaimedCommandAsync(
        Guid integrationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var timedOut = await dbContext.ImmediateCollectionCommands
            .SingleOrDefaultAsync(
                c => c.IntegrationId == integrationId &&
                     c.Status == EImmediateCollectionCommandStatus.Claimed &&
                     c.ClaimExpiresAtUtc != null &&
                     c.ClaimExpiresAtUtc < now,
                cancellationToken);

        if (timedOut is null) return;

        timedOut.TryExpireTimeout(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// D4 — invalida o ValidationStatus da credencial e aciona
    /// <c>ReconcileEligibilityAsync</c> na mesma transação, seguindo o
    /// padrão transacional de <c>IServiceCredentialValidationService</c>
    /// (linhas 77–92).
    /// </summary>
    private async Task InvalidateCredentialAndReconcileAsync(
        Guid tenantId, Guid integrationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.IServiceCredentials
            .SingleOrDefaultAsync(c => c.IntegrationId == integrationId, cancellationToken);
        credential?.RecordValidation(EIServiceValidationStatus.Failed, now);

        if (dbContext.Database.IsRelational())
        {
            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await activationService.ReconcileEligibilityAsync(tenantId, integrationId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await activationService.ReconcileEligibilityAsync(tenantId, integrationId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Decifra as três credenciais usando a DEK; zera a DEK da memória
    /// imediatamente após o uso (ADR-021/D9-B).
    /// </summary>
    private async Task<(string Username, string Password, string? BaseUrl)> DecryptCredentialsAsync(
        IServiceCredential credential, CancellationToken cancellationToken)
    {
        var dek = await cipher.UnwrapDataKeyAsync(
            credential.DataKeyCiphertext, credential.KmsKeyId, credential.AlgorithmVersion, cancellationToken);
        try
        {
            var username = cipher.Decrypt(dek, credential.UsernameCiphertext,
                credential.Nonce, credential.Tag);

            var (passwordNonce, passwordTag, passwordCiphertext) =
                IServiceCredentialService.Unpack(credential.PasswordCiphertext);
            var password = cipher.Decrypt(dek, passwordCiphertext, passwordNonce, passwordTag);

            string? baseUrl = null;
            if (credential.BaseUrlCiphertext is not null)
            {
                var (baseUrlNonce, baseUrlTag, baseUrlCiphertext) =
                    IServiceCredentialService.Unpack(credential.BaseUrlCiphertext);
                baseUrl = cipher.Decrypt(dek, baseUrlCiphertext, baseUrlNonce, baseUrlTag);
            }

            return (username, password, baseUrl);
        }
        finally
        {
            // Zera a DEK da memória imediatamente após as decifragens
            // (ADR-021/D9-B — controle de vazamento).
            Array.Clear(dek, 0, dek.Length);
        }
    }
}

// ---------------------------------------------------------------------------
// DTOs de resultado
// ---------------------------------------------------------------------------

/// <summary>
/// Resposta do claim: inclui credenciais decifradas para o Worker
/// (ADR-021/D9-B). Nunca deve ser logado pelo middleware de request.
/// </summary>
public sealed record ClaimCommandResult(
    Guid CommandId,
    Guid IntegrationId,
    Guid TenantId,
    Guid ProviderId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    int HistoryWindowMonths,
    DecryptedCredential Credential);

/// <summary>
/// Credenciais decifradas para o Worker (ADR-021/D9-B).
/// Nunca deve aparecer em logs, eventos ou persistência.
/// </summary>
public sealed record DecryptedCredential(string Username, string Password, string? BaseUrl);

/// <summary>
/// Resposta do complete (ADR-021).
/// </summary>
public sealed record CompleteCommandResult(
    Guid CommandId,
    EImmediateCollectionCommandStatus Status,
    DateTimeOffset CompletedAtUtc);
