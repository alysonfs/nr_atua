using Atua.Api.Domain.Integrations;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atua.Api.Application.Integrations;

/// <summary>
/// Orquestra o teste de validação de credenciais junto ao iService
/// (RF-007/ADR-018). A implementação real de <see cref="IIServiceAuthClient"/>
/// é uma lacuna pendente (ver FakeIServiceAuthClient); este serviço trata o
/// resultado de forma agnóstica à implementação concreta.
/// </summary>
public sealed class IServiceCredentialValidationService(
    AtuaDbContext dbContext,
    ICredentialCipher cipher,
    IIServiceAuthClient authClient,
    TimeProvider timeProvider,
    ILogger<IServiceCredentialValidationService> logger)
{
    public async Task<ValidateCredentialsResult?> ValidateAsync(Guid userId, Guid tenantId,
        Guid integrationId, CancellationToken cancellationToken)
    {
        var isMember = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId && membership.TenantId == tenantId, cancellationToken);
        if (!isMember) return null;

        var credential = await dbContext.IServiceCredentials.SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.TenantId == tenantId, cancellationToken);
        if (credential is null) return ValidateCredentialsResult.NotConfigured();

        var dataKeyPlaintext = cipher.UnwrapDataKey(credential.DataKeyCiphertext, credential.KmsKeyId);

        var username = cipher.Decrypt(dataKeyPlaintext, credential.UsernameCiphertext,
            credential.Nonce, credential.Tag);
        var (passwordNonce, passwordTag, passwordCiphertext) =
            IServiceCredentialService.Unpack(credential.PasswordCiphertext);
        var password = cipher.Decrypt(dataKeyPlaintext, passwordCiphertext, passwordNonce, passwordTag);
        string? baseUrl = null;
        if (credential.BaseUrlCiphertext is not null)
        {
            var (baseUrlNonce, baseUrlTag, baseUrlCiphertext) =
                IServiceCredentialService.Unpack(credential.BaseUrlCiphertext);
            baseUrl = cipher.Decrypt(dataKeyPlaintext, baseUrlCiphertext, baseUrlNonce, baseUrlTag);
        }

        EIServiceValidationStatus status;
        try
        {
            var result = await authClient.TryAuthenticateAsync(
                new IServiceCredentialPayload(username, password, baseUrl), cancellationToken);
            status = result.Succeeded ? EIServiceValidationStatus.Succeeded : EIServiceValidationStatus.Failed;
            if (!result.Succeeded)
            {
                // Log técnico interno, sem PII/segredo (RN-007.4).
                logger.LogInformation(
                    "Validação de credenciais iService falhou para IntegrationId={IntegrationId}. Motivo={Reason}",
                    integrationId, result.FailureReasonForLog);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Falha por exceção/timeout/5xx do provedor é tratada como Failed
            // também para o cliente (ADR-018).
            status = EIServiceValidationStatus.Failed;
            logger.LogWarning(exception,
                "Erro ao validar credenciais iService para IntegrationId={IntegrationId}.", integrationId);
        }

        var now = timeProvider.GetUtcNow();
        credential.RecordValidation(status, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ValidateCredentialsResult.Evaluated(status, now);
    }
}

public sealed record ValidateCredentialsResult(bool CredentialsConfigured,
    EIServiceValidationStatus? ValidationStatus, DateTimeOffset? EvaluatedAtUtc)
{
    public static ValidateCredentialsResult NotConfigured() => new(false, null, null);

    public static ValidateCredentialsResult Evaluated(EIServiceValidationStatus status,
        DateTimeOffset evaluatedAtUtc) => new(true, status, evaluatedAtUtc);
}
