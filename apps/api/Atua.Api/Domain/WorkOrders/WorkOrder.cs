namespace Atua.Api.Domain.WorkOrders;

/// <summary>
/// Estado atual de uma OS por tenant (RF-017).
/// Chave de identidade: (TenantId, WorkOrderProviderId) — ver RN-017.6.
/// Status é string crua do provedor, sem enum (DP-017.2 resolvida).
/// </summary>
public sealed class WorkOrder
{
    private WorkOrder() { }

    public WorkOrder(Guid id, Guid tenantId, Guid integrationId, string workOrderProviderId, string status,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        IntegrationId = integrationId;
        WorkOrderProviderId = workOrderProviderId;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Redundante para isolamento multi-tenant (RN-017.6).</summary>
    public Guid TenantId { get; private set; }

    /// <summary>
    /// FK para <see cref="Atua.Api.Domain.Integrations.Integration"/> — permite chegar ao
    /// Tenant e ao Provider a partir da OS (RN-017.7). Obrigatório: toda OS é sempre
    /// coletada por uma integração conhecida.
    /// </summary>
    public Guid IntegrationId { get; private set; }

    /// <summary>Identificador serial externo da OS no provedor (<c>workOrderId</c> no iService).</summary>
    public string WorkOrderProviderId { get; private set; } = string.Empty;

    /// <summary>Número visível da OS no provedor (<c>workOrderNo</c> no iService, ex.: BRWO260909869).</summary>
    public string? WorkOrderProviderNo { get; private set; }

    /// <summary>
    /// Identificador do Service Request (SR) no provedor (<c>serviceRequestId</c> no iService) —
    /// chave usada para obter os dados reais (não mascarados) do consumidor via
    /// <c>getSrOriginalInfo</c>.
    /// </summary>
    public string? ServiceRequestId { get; private set; }

    /// <summary>Valor total da OS informado pelo provedor (<c>totalAmount</c> no iService).</summary>
    public decimal? Amount { get; private set; }

    /// <summary>
    /// Status atual da OS — string crua retornada pelo provedor, sem mapeamento (DP-017.2).
    /// </summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Instante UTC em que a OS foi vista pela primeira vez.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Instante UTC da atualização mais recente.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Data de criação da OS informada pelo provedor (<c>creationDate</c> no iService).</summary>
    public DateTimeOffset? ProviderCreatedAt { get; private set; }

    /// <summary>Data da última atualização da OS informada pelo provedor (<c>lastUpdateDate</c> no iService).</summary>
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }

    /// <summary>Tipo de consumidor cru do provedor (ex.: "Person"/"Company" — <c>customerType</c>).</summary>
    public string? CustomerType { get; private set; }

    /// <summary>Nome do consumidor (derivado de <c>name</c>/<c>firstName</c>/<c>middleName</c>/<c>lastName</c>).</summary>
    public string? CustomerName { get; private set; }

    /// <summary>CPF do consumidor (<c>cpf</c>).</summary>
    public string? CustomerCpf { get; private set; }

    /// <summary>E-mail de contato (<c>email</c>).</summary>
    public string? ContactEmail { get; private set; }

    /// <summary>Telefone de contato, já combinado com DDI (derivado de <c>phoneCountryCode1/2</c> + <c>phoneNumber1/2</c>).</summary>
    public string? ContactPhone { get; private set; }

    /// <summary>Nome da pessoa de contato (<c>contactName</c>, ou o próprio consumidor como fallback).</summary>
    public string? ContactName { get; private set; }

    /// <summary>Endereço do consumidor (preferindo <c>address</c>, com fallback para <c>address1</c>).</summary>
    public string? Address { get; private set; }

    /// <summary>CEP do endereço (<c>zipcode</c>).</summary>
    public string? ZipCode { get; private set; }

    /// <summary>País do endereço (<c>countryName</c>).</summary>
    public string? CountryName { get; private set; }

    /// <summary>Estado do endereço (<c>stateName</c>).</summary>
    public string? StateName { get; private set; }

    /// <summary>Cidade do endereço (<c>cityName</c>).</summary>
    public string? CityName { get; private set; }

    /// <summary>Marca do produto (<c>productBrand</c>).</summary>
    public string? ProductBrand { get; private set; }

    /// <summary>Código do produto no padrão do provedor (<c>pdCode</c>).</summary>
    public string? PdCode { get; private set; }

    /// <summary>Identificador da categoria (<c>categoryId</c>).</summary>
    public string? CategoryId { get; private set; }

    /// <summary>Código da categoria do produto (<c>productCategoryCode</c>).</summary>
    public string? ProductCategoryCode { get; private set; }

    /// <summary>Código do produto (<c>productCode</c>).</summary>
    public string? ProductCode { get; private set; }

    /// <summary>Modelo do produto (<c>productModel</c>).</summary>
    public string? ProductModel { get; private set; }

    /// <summary>Status do produto (<c>productStatus</c>).</summary>
    public string? ProductStatus { get; private set; }

    /// <summary>
    /// Sintoma relatado pelo consumidor. No iService, os campos diretos (<c>symptom</c>,
    /// <c>symptomDescription</c>) estão observados vazios em todas as amostras capturadas
    /// até o momento (Fase QA/Fase 6) — este valor é extraído por uma lista priorizada de
    /// campos candidatos (ver <c>IServiceProviderInteractionOrderAdapter.ExtractSymptom</c>),
    /// mantida para monitorar qual campo realmente carrega o dado quando disponível.
    /// </summary>
    public string? Symptom { get; private set; }

    /// <summary>
    /// Atualiza o status e <see cref="UpdatedAt"/>.
    /// </summary>
    public void UpdateStatus(string newStatus, DateTimeOffset now)
    {
        Status = newStatus;
        UpdatedAt = now;
    }

    /// <summary>
    /// Atualiza apenas <see cref="UpdatedAt"/> (mesmo status — RF-017.2).
    /// </summary>
    public void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
    }

    /// <summary>
    /// Atualiza os campos descritivos (datas do provedor, consumidor, contato, endereço,
    /// produto e sintoma) — chamado em todo upsert, independente de mudança de status.
    /// </summary>
    public void UpdateDetails(WorkOrderDetails details, DateTimeOffset now)
    {
        WorkOrderProviderNo = details.WorkOrderProviderNo;
        ServiceRequestId = details.ServiceRequestId;
        Amount = details.Amount;
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
        UpdatedAt = now;
    }
}

/// <summary>
/// Conjunto de campos descritivos opcionais de uma OS, extraídos do provedor — usado por
/// <see cref="WorkOrder.UpdateDetails"/> e <see cref="WorkOrderHistory"/> para evitar uma
/// assinatura de método com dezenas de parâmetros posicionais.
/// </summary>
public sealed record WorkOrderDetails(
    string? WorkOrderProviderNo,
    string? ServiceRequestId,
    decimal? Amount,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderUpdatedAt,
    string? CustomerType,
    string? CustomerName,
    string? CustomerCpf,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactName,
    string? Address,
    string? ZipCode,
    string? CountryName,
    string? StateName,
    string? CityName,
    string? ProductBrand,
    string? PdCode,
    string? CategoryId,
    string? ProductCategoryCode,
    string? ProductCode,
    string? ProductModel,
    string? ProductStatus,
    string? Symptom);
