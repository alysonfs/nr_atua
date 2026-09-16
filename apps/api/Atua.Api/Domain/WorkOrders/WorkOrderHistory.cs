namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Entrada append-only de histórico de status de OS (RF-017).
/// Nova entrada somente quando o status muda (DP-017.1 resolvida — RF-017.2).
/// </summary>
public sealed class WorkOrderHistory
{
    private WorkOrderHistory() { }

    public WorkOrderHistory(
        Guid id,
        Guid workOrderId,
        Guid workOrderSnapshotId,
        Guid tenantId,
        string providerId,
        string status,
        DateTimeOffset createdAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        WorkOrderSnapshotId = workOrderSnapshotId;
        TenantId = tenantId;
        ProviderId = providerId;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>FK para <see cref="WorkOrder.Id"/>.</summary>
    public Guid WorkOrderId { get; private set; }

    /// <summary>
    /// Referência ao _id do documento MongoDB work_order_snapshots (RF-017.4).
    /// Não é FK de banco — rastreabilidade por convenção de aplicação
    /// (ADR-023, nota sobre FK cruzada).
    /// </summary>
    public Guid WorkOrderSnapshotId { get; private set; }

    /// <summary>Redundante para isolamento multi-tenant (RN-017.6).</summary>
    public Guid TenantId { get; private set; }

    public string ProviderId { get; private set; } = string.Empty;

    /// <summary>Status observado no momento desta entrada — string crua (DP-017.2).</summary>
    public string Status { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Data de criação da OS informada pelo provedor no momento desta entrada.</summary>
    public DateTimeOffset? ProviderCreatedAt { get; private set; }

    /// <summary>Data da última atualização da OS informada pelo provedor no momento desta entrada.</summary>
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }

    /// <summary>Tipo de consumidor cru do provedor no momento desta entrada.</summary>
    public string? CustomerType { get; private set; }

    /// <summary>Nome do consumidor no momento desta entrada.</summary>
    public string? CustomerName { get; private set; }

    /// <summary>CPF do consumidor no momento desta entrada.</summary>
    public string? CustomerCpf { get; private set; }

    /// <summary>E-mail de contato no momento desta entrada.</summary>
    public string? ContactEmail { get; private set; }

    /// <summary>Telefone de contato no momento desta entrada.</summary>
    public string? ContactPhone { get; private set; }

    /// <summary>Nome da pessoa de contato no momento desta entrada.</summary>
    public string? ContactName { get; private set; }

    /// <summary>Endereço do consumidor no momento desta entrada.</summary>
    public string? Address { get; private set; }

    /// <summary>CEP do endereço no momento desta entrada.</summary>
    public string? ZipCode { get; private set; }

    /// <summary>País do endereço no momento desta entrada.</summary>
    public string? CountryName { get; private set; }

    /// <summary>Estado do endereço no momento desta entrada.</summary>
    public string? StateName { get; private set; }

    /// <summary>Cidade do endereço no momento desta entrada.</summary>
    public string? CityName { get; private set; }

    /// <summary>Marca do produto no momento desta entrada.</summary>
    public string? ProductBrand { get; private set; }

    /// <summary>Código do produto no padrão do provedor no momento desta entrada.</summary>
    public string? PdCode { get; private set; }

    /// <summary>Identificador da categoria no momento desta entrada.</summary>
    public string? CategoryId { get; private set; }

    /// <summary>Código da categoria do produto no momento desta entrada.</summary>
    public string? ProductCategoryCode { get; private set; }

    /// <summary>Código do produto no momento desta entrada.</summary>
    public string? ProductCode { get; private set; }

    /// <summary>Modelo do produto no momento desta entrada.</summary>
    public string? ProductModel { get; private set; }

    /// <summary>Status do produto no momento desta entrada.</summary>
    public string? ProductStatus { get; private set; }

    /// <summary>Sintoma relatado pelo consumidor no momento desta entrada — ver <see cref="WorkOrder.Symptom"/>.</summary>
    public string? Symptom { get; private set; }

    /// <summary>Preenche os campos descritivos desta entrada de histórico (imutável após criação).</summary>
    public void ApplyDetails(WorkOrderDetails details)
    {
        ProviderCreatedAt = details.ProviderCreatedAt;
        ProviderUpdatedAt = details.ProviderUpdatedAt;
        CustomerType = details.CustomerType;
        CustomerName = details.CustomerName;
        CustomerCpf = details.CustomerCpf;
        ContactEmail = details.ContactEmail;
        ContactPhone = details.ContactPhone;
        ContactName = details.ContactName;
        Address = details.Address;
        ZipCode = details.ZipCode;
        CountryName = details.CountryName;
        StateName = details.StateName;
        CityName = details.CityName;
        ProductBrand = details.ProductBrand;
        PdCode = details.PdCode;
        CategoryId = details.CategoryId;
        ProductCategoryCode = details.ProductCategoryCode;
        ProductCode = details.ProductCode;
        ProductModel = details.ProductModel;
        ProductStatus = details.ProductStatus;
        Symptom = details.Symptom;
    }
}
