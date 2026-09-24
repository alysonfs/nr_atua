using System.Globalization;
using MongoDB.Bson;

namespace Atua.Collector.Consumer;

/// <summary>
/// Adaptador de <c>provider_interactions</c> para o provedor iService (Fase 4 do refactor de
/// persistência, estendido para os campos de triagem/obtenção de peças — ver
/// docs/implementation/PLANO-refactor-provider-interactions-collector.md). Cada OS do array
/// <c>orders</c> é um <see cref="BsonDocument"/> com os mesmos campos brutos devolvidos pelo
/// iService: identificador em <c>workOrderId</c> (ou, no fallback usado pelo enriquecimento
/// de detalhe, <c>id</c>) e status em <c>woStatus</c>.
/// </summary>
public sealed class IServiceProviderInteractionOrderAdapter : IProviderInteractionOrderAdapter
{
    /// <summary>
    /// Formato de data devolvido pelo iService em <c>creationDate</c>/<c>lastUpdateDate</c>
    /// (mesmo padrão usado em <c>IServiceCollectorService.ComputeCreationDateRange</c>, sem
    /// timezone explícito — tratado como UTC).
    /// </summary>
    private const string ProviderDateFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Campos candidatos a carregar o sintoma relatado pelo consumidor, em ordem de
    /// prioridade. Nas amostras capturadas até a Fase 6 (QA), <c>symptom</c> e
    /// <c>symptomDescription</c> vieram vazios e os demais nulos — mantido como lista para
    /// monitorar qual campo o iService de fato preenche quando o consumidor relata o sintoma.
    /// </summary>
    private static readonly string[] SymptomCandidateFields =
    [
        "symptom",
        "symptomDescription",
        "notes",
        "note",
        "remark",
        "closeDescription",
        "returnReason",
        "reassigningReason",
        "errorMessage",
        "suggestedSolution",
        "pendingReason",
    ];

    /// <inheritdoc/>
    public IReadOnlyList<ProviderWorkOrderData> ExtractOrders(BsonArray orders)
    {
        var result = new List<ProviderWorkOrderData>(orders.Count);

        foreach (var item in orders)
        {
            if (item is not BsonDocument order) continue;

            var workOrderProviderId = ExtractWorkOrderProviderId(order);
            var status = ExtractStatus(order);

            if (string.IsNullOrWhiteSpace(workOrderProviderId) || string.IsNullOrWhiteSpace(status))
                continue;

            result.Add(BuildOrderData(order, workOrderProviderId, status));
        }

        return result;
    }

    private static ProviderWorkOrderData BuildOrderData(BsonDocument order, string workOrderProviderId, string status)
    {
        // RF-026/ADR-031 (corrigido 2026-09-23): queryOneWorkOrder devolve todos os campos
        // de contato do cliente como null — não é a fonte de dado desmascarado. O endpoint
        // correto, usado por EnrichPendingDetailOrdersAsync desde a correção, é
        // queryWoExecutionDetail, cujo payload de contato (nome/CPF/e-mail/telefone/endereço,
        // sem máscara) fica em order.orderDetail.customerInfo — não mais em orderDetail
        // diretamente (payload achatado, formato do endpoint antigo). Sem isso, os campos de
        // contato do consumidor permaneciam sempre mascarados ("S****S", "5584****51" etc.),
        // mesmo após o enriquecimento — o detalhe era buscado e auditado, mas nunca lido
        // corretamente de volta.
        var orderDetail = ExtractOrderDetail(order);
        var detail = ExtractCustomerInfo(orderDetail);
        var customerName = ExtractCustomerName(detail) ?? ExtractCustomerName(order);

        return new ProviderWorkOrderData(
            WorkOrderProviderId: workOrderProviderId,
            Status: status,
            WorkOrderProviderNo: ExtractString(order, "workOrderNo"),
            ServiceRequestId: ExtractString(order, "serviceRequestId"),
            Amount: ExtractDecimal(order, "totalAmount"),
            ProviderCreatedAt: ExtractDate(order, "creationDate"),
            ProviderUpdatedAt: ExtractDate(order, "lastUpdateDate"),
            CustomerType: ExtractStringPreferDetail(order, detail, "customerType"),
            CustomerName: customerName,
            CustomerCpf: ExtractStringPreferDetail(order, detail, "cpf"),
            ContactEmail: ExtractStringPreferDetail(order, detail, "email"),
            ContactPhone: ExtractContactPhone(detail) ?? ExtractContactPhone(order),
            ContactName: ExtractStringPreferDetail(order, detail, "contactName") ?? customerName,
            Address: ExtractStringPreferDetail(order, detail, "address") ?? ExtractStringPreferDetail(order, detail, "address1"),
            ZipCode: ExtractStringPreferDetail(order, detail, "zipCode") ?? ExtractString(order, "zipcode"),
            CountryName: ExtractStringPreferDetail(order, detail, "countryName"),
            StateName: ExtractStringPreferDetail(order, detail, "stateName"),
            CityName: ExtractStringPreferDetail(order, detail, "cityName"),
            ProductBrand: ExtractString(order, "productBrand"),
            PdCode: ExtractString(order, "pdCode"),
            CategoryId: ExtractString(order, "categoryId"),
            ProductCategoryCode: ExtractString(order, "productCategoryCode"),
            ProductCode: ExtractString(order, "productCode"),
            ProductModel: ExtractString(order, "productModel"),
            ProductStatus: ExtractString(order, "productStatus"),
            Symptom: ExtractSymptom(order));
    }

    /// <summary>
    /// Extrai o sub-documento <c>orderDetail</c> (payload <c>data</c> de
    /// <c>queryWoExecutionDetail</c>, mesclado por <c>EnrichPendingDetailOrdersAsync</c>),
    /// quando esta OS foi enriquecida. Ausente em OS obtidas apenas via <c>list_query</c>.
    /// </summary>
    private static BsonDocument? ExtractOrderDetail(BsonDocument order) =>
        order.TryGetValue("orderDetail", out var value) && value is BsonDocument detail ? detail : null;

    /// <summary>
    /// Extrai o sub-documento <c>customerInfo</c> de dentro de <c>orderDetail</c> — é aí que
    /// <c>queryWoExecutionDetail</c> devolve nome/CPF/e-mail/telefone/endereço completos e
    /// sem máscara (confirmado com payload real em produção, 2026-09-23). O restante de
    /// <c>orderDetail</c> (ex.: <c>workOrder</c>, <c>productInfo</c>) tem apenas dados
    /// operacionais, não usados por este adapter.
    /// </summary>
    private static BsonDocument? ExtractCustomerInfo(BsonDocument? orderDetail) =>
        orderDetail is not null && orderDetail.TryGetValue("customerInfo", out var value) && value is BsonDocument customerInfo
            ? customerInfo
            : null;

    /// <summary>
    /// Lê um campo string preferindo o sub-documento <c>orderDetail</c> (não mascarado)
    /// quando presente, com fallback para o mesmo campo no nível raiz de <paramref name="order"/>
    /// (mascarado na listagem, mas é o único dado disponível quando não há detalhe).
    /// </summary>
    private static string? ExtractStringPreferDetail(BsonDocument order, BsonDocument? detail, string field) =>
        (detail is not null ? ExtractString(detail, field) : null) ?? ExtractString(order, field);

    private static string? ExtractWorkOrderProviderId(BsonDocument order)
    {
        if (order.TryGetValue("workOrderId", out var workOrderId) && !workOrderId.IsBsonNull)
            return workOrderId.ToString();

        return order.TryGetValue("id", out var id) && !id.IsBsonNull ? id.ToString() : null;
    }

    private static string? ExtractStatus(BsonDocument order)
    {
        if (!order.TryGetValue("woStatus", out var value) || value.IsBsonNull)
            return null;

        var status = value.ToString();
        return string.IsNullOrWhiteSpace(status) ? null : status;
    }

    /// <summary>
    /// Lê um campo string cru do documento, tratando ausência/null/vazio como null.
    /// </summary>
    private static string? ExtractString(BsonDocument order, string field)
    {
        if (!order.TryGetValue(field, out var value) || value.IsBsonNull)
            return null;

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// Lê e converte uma data no formato <see cref="ProviderDateFormat"/> (UTC, sem
    /// timezone explícito no payload do iService).
    /// </summary>
    private static DateTimeOffset? ExtractDate(BsonDocument order, string field)
    {
        var raw = ExtractString(order, field);
        if (raw is null) return null;

        return DateTime.TryParseExact(
            raw, ProviderDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// Lê e converte um campo numérico cru do documento (ex.: <c>totalAmount</c>), tratando
    /// ausência/null como null.
    /// </summary>
    private static decimal? ExtractDecimal(BsonDocument order, string field)
    {
        if (!order.TryGetValue(field, out var value) || value.IsBsonNull)
            return null;

        return value.BsonType switch
        {
            BsonType.Decimal128 => (decimal)value.AsDecimal128,
            BsonType.Double => (decimal)value.AsDouble,
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.String => decimal.TryParse(value.AsString, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null,
            _ => null,
        };
    }

    /// <summary>
    /// Deriva o nome do consumidor: prefere <c>name</c> quando preenchido; caso contrário,
    /// concatena <c>firstName</c> + <c>middleName</c> + <c>lastName</c> (ignorando partes
    /// vazias). Amostras reais mostraram <c>name</c>/<c>firstName</c>/<c>middleName</c> vazios
    /// com o nome completo apenas em <c>lastName</c> — por isso o fallback concatenado, não
    /// apenas <c>lastName</c> isolado.
    /// </summary>
    private static string? ExtractCustomerName(BsonDocument? order)
    {
        if (order is null) return null;

        var name = ExtractString(order, "name");
        if (name is not null) return name;

        var parts = new[]
        {
            ExtractString(order, "firstName"),
            ExtractString(order, "middleName"),
            ExtractString(order, "lastName"),
        }.Where(part => part is not null);

        var combined = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    /// <summary>
    /// Combina DDI + número, preferindo o par 1 (<c>phoneCountryCode1</c>/<c>phoneNumber1</c>)
    /// quando <c>phoneNumber1</c> está preenchido; senão tenta o par 2.
    /// </summary>
    private static string? ExtractContactPhone(BsonDocument? order)
    {
        if (order is null) return null;

        var number1 = ExtractString(order, "phoneNumber1");
        if (number1 is not null)
        {
            var code1 = ExtractString(order, "phoneCountryCode1");
            return code1 is not null ? $"{code1}{number1}" : number1;
        }

        var number2 = ExtractString(order, "phoneNumber2");
        if (number2 is not null)
        {
            var code2 = ExtractString(order, "phoneCountryCode2");
            return code2 is not null ? $"{code2}{number2}" : number2;
        }

        return null;
    }

    /// <summary>
    /// Percorre <see cref="SymptomCandidateFields"/> em ordem e retorna o primeiro valor
    /// string não vazio encontrado — ver comentário em <see cref="SymptomCandidateFields"/>.
    /// </summary>
    private static string? ExtractSymptom(BsonDocument order)
    {
        foreach (var field in SymptomCandidateFields)
        {
            var value = ExtractString(order, field);
            if (value is not null) return value;
        }

        return null;
    }
}
