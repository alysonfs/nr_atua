using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Atua.Api.Application.Integrations;

/// <summary>
/// Orquestra a criação/atualização/leitura de credenciais de configuração do
/// iService (RF-006/ADR-018). Nunca retorna segredos em claro.
/// </summary>
public sealed class IServiceCredentialService(
    AtuaDbContext dbContext,
    ICredentialCipher cipher,
    TimeProvider timeProvider,
    CollectorControl.CollectorActivationService collectorActivationService)
{
    public async Task<ESetCredentialsStatus> SetCredentialsAsync(Guid userId, Guid tenantId,
        Guid integrationId, string username, string password, string? baseUrl,
        CancellationToken cancellationToken)
    {
        if (!await IsOwnerAsync(userId, tenantId, cancellationToken))
        {
            return ESetCredentialsStatus.Forbidden;
        }

        var integration = await dbContext.Integrations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == integrationId && item.TenantId == tenantId, cancellationToken);
        if (integration is null)
        {
            return ESetCredentialsStatus.IntegrationNotFound;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return ESetCredentialsStatus.InvalidCredentials;
        }

        var now = timeProvider.GetUtcNow();
        var existing = await dbContext.IServiceCredentials.SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId, cancellationToken);

        var dataKey = cipher.CreateDataKey();
        var usernameCipher = cipher.Encrypt(dataKey.Plaintext, username);
        // Reutiliza o mesmo nonce lógico por campo: cada campo cifrado carrega
        // seu próprio nonce/tag; persistimos apenas o do username e senha
        // combinados abaixo por simplicidade do MVP (um envelope por campo).
        var passwordCipher = cipher.Encrypt(dataKey.Plaintext, password);
        var baseUrlCipher = baseUrl is null ? null : cipher.Encrypt(dataKey.Plaintext, baseUrl);

        // Como cada campo produz seu próprio nonce/tag, persistimos o conjunto
        // do username como referência primária (Nonce/Tag da entidade) e
        // embutimos os demais nonces/tags no próprio ciphertext codificado,
        // evitando expandir o schema além do definido no ADR-018.
        var packedPassword = Pack(passwordCipher);
        var packedBaseUrl = baseUrlCipher is null ? null : Pack(baseUrlCipher);

        if (existing is null)
        {
            var credential = new IServiceCredential(Guid.CreateVersion7(), tenantId, integrationId,
                usernameCipher.CiphertextBase64, packedPassword, packedBaseUrl,
                usernameCipher.Nonce, usernameCipher.Tag, dataKey.CiphertextBase64,
                dataKey.KmsKeyId, AlgorithmVersion, now);
            dbContext.IServiceCredentials.Add(credential);
        }
        else
        {
            existing.ReplaceSecret(usernameCipher.CiphertextBase64, packedPassword, packedBaseUrl,
                usernameCipher.Nonce, usernameCipher.Tag, dataKey.CiphertextBase64,
                dataKey.KmsKeyId, AlgorithmVersion, now);
        }

        // ADR-020/RF-008.6: gravar a credencial zera o ValidationStatus
        // (RN-006.3), o que pode tornar a elegibilidade falsa. A alteração é
        // liberada e reconciliada na mesma unidade de trabalho, para que o
        // Agente seja desativado e o comando Pendente cancelado.
        await SaveWithReconciliationAsync(tenantId, integrationId, cancellationToken);
        return ESetCredentialsStatus.Success;
    }

    public async Task<GetCredentialsResult?> GetStatusAsync(Guid userId, Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        var isMember = await dbContext.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId && membership.TenantId == tenantId, cancellationToken);
        if (!isMember) return null;

        var credential = await dbContext.IServiceCredentials.AsNoTracking().SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.TenantId == tenantId, cancellationToken);

        if (credential is null)
        {
            return new GetCredentialsResult(false, EIServiceValidationStatus.NotValidated, null, null);
        }

        return new GetCredentialsResult(true, credential.ValidationStatus,
            credential.LastValidatedAtUtc, credential.UpdatedAtUtc);
    }

    internal async Task<bool> IsOwnerAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.TenantMemberships.AsNoTracking().AnyAsync(membership =>
            membership.UserId == userId && membership.TenantId == tenantId &&
            membership.Role == ETenantMembershipRole.Owner, cancellationToken);

    private const int AlgorithmVersion = 1;

    /// <summary>
    /// Persiste a alteração da credencial e reconcilia a ativação do coletor
    /// na mesma transação (ADR-020/RF-008.6). A reconciliação consulta o estado
    /// já liberado, por isso ocorre após o primeiro <c>SaveChanges</c>; o
    /// commit só acontece quando ambas as gravações têm êxito.
    /// </summary>
    private async Task SaveWithReconciliationAsync(Guid tenantId, Guid integrationId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await collectorActivationService.ReconcileEligibilityAsync(tenantId, integrationId,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await collectorActivationService.ReconcileEligibilityAsync(tenantId, integrationId,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Pack(CipherResult cipherResult) =>
        Convert.ToBase64String(cipherResult.Nonce) + "." + Convert.ToBase64String(cipherResult.Tag) +
        "." + cipherResult.CiphertextBase64;

    internal static (byte[] Nonce, byte[] Tag, string CiphertextBase64) Unpack(string packed)
    {
        var parts = packed.Split('.', 3);
        return (Convert.FromBase64String(parts[0]), Convert.FromBase64String(parts[1]), parts[2]);
    }
}

public enum ESetCredentialsStatus
{
    Success,
    Forbidden,
    IntegrationNotFound,
    InvalidCredentials
}

public sealed record GetCredentialsResult(bool HasCredentials,
    EIServiceValidationStatus ValidationStatus, DateTimeOffset? LastValidatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
