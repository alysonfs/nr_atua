namespace Atua.Api.Domain.Integrations;

/// <summary>
/// Credenciais de configuração do iService informadas pelo cliente (RF-006).
///
/// Atenção de nomenclatura (ADR-018): esta entidade é distinta de
/// <see cref="Atua.Api.Domain.Identity.ServiceCredential"/>, que representa a
/// credencial de serviço interno do coletor (escopo
/// <c>collector.eligibility.read</c>). Não devem ser confundidas.
/// </summary>
public sealed class IServiceCredential
{
    private IServiceCredential() { }

    public IServiceCredential(Guid id, Guid tenantId, Guid integrationId,
        string usernameCiphertext, string passwordCiphertext,
        string? baseUrlCiphertext, byte[] nonce, byte[] tag,
        string dataKeyCiphertext, string kmsKeyId, int algorithmVersion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        UsernameCiphertext = usernameCiphertext;
        PasswordCiphertext = passwordCiphertext;
        BaseUrlCiphertext = baseUrlCiphertext;
        Nonce = nonce;
        Tag = tag;
        DataKeyCiphertext = dataKeyCiphertext;
        KmsKeyId = kmsKeyId;
        AlgorithmVersion = algorithmVersion;
        CreatedAtUtc = createdAtUtc;
        ValidationStatus = EIServiceValidationStatus.NotValidated;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid IntegrationId { get; private set; }

    // Campos cifrados (AES-256-GCM, ADR-004). Nunca expostos em claro.
    public string UsernameCiphertext { get; private set; } = null!;
    public string PasswordCiphertext { get; private set; } = null!;
    public string? BaseUrlCiphertext { get; private set; }
    public byte[] Nonce { get; private set; } = null!;
    public byte[] Tag { get; private set; } = null!;
    public string DataKeyCiphertext { get; private set; } = null!; // chave de dados cifrada por KMS
    public string KmsKeyId { get; private set; } = null!;
    public int AlgorithmVersion { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public EIServiceValidationStatus ValidationStatus { get; private set; }
    public DateTimeOffset? LastValidatedAtUtc { get; private set; }

    public void ReplaceSecret(string usernameCiphertext, string passwordCiphertext,
        string? baseUrlCiphertext, byte[] nonce, byte[] tag, string dataKeyCiphertext,
        string kmsKeyId, int algorithmVersion, DateTimeOffset now)
    {
        UsernameCiphertext = usernameCiphertext;
        PasswordCiphertext = passwordCiphertext;
        BaseUrlCiphertext = baseUrlCiphertext;
        Nonce = nonce;
        Tag = tag;
        DataKeyCiphertext = dataKeyCiphertext;
        KmsKeyId = kmsKeyId;
        AlgorithmVersion = algorithmVersion;
        UpdatedAtUtc = now;
        ValidationStatus = EIServiceValidationStatus.NotValidated; // RN-006.3
        LastValidatedAtUtc = null;
    }

    public void RecordValidation(EIServiceValidationStatus status, DateTimeOffset now)
    {
        ValidationStatus = status;
        LastValidatedAtUtc = now;
    }
}
