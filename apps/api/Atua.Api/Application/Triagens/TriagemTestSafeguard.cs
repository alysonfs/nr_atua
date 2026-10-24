namespace Atua.Api.Application.Triagens;

/// <summary>
/// TEMPORÁRIO — trava do teste E2E controlado do Incremento 5 (ver
/// docs/implementation/PLANO-triagem-templates-channel-baileys.md, seção
/// "Trava temporária do E2E"). Remover após o E2E ser validado.
///
/// - Somente as OS listadas em <see cref="AllowedWorkOrderIds"/> ou cujo
///   identificador externo começa com <see cref="SyntheticWorkOrderProviderIdPrefix"/>
///   (OS sintéticas do E2E repetível) são reivindicadas da outbox; as demais
///   permanecem <c>Pending</c>, sem triagem e sem mensagem (fail-closed).
/// - Todo comando de canal usa um telefone de <see cref="AllowedRecipientPhones"/>:
///   o telefone da OS quando ele está na lista; caso contrário,
///   <see cref="FixedRecipientPhone"/>. Nenhum outro destinatário é usado.
/// </summary>
public static class TriagemTestSafeguard
{
    public const string FixedRecipientPhone = "+5584994241248";

    /// <summary>Destinatários de teste autorizados pelo PO (o primeiro é o padrão).</summary>
    public static readonly string[] AllowedRecipientPhones =
    [
        FixedRecipientPhone,
        "+5584991050065",
    ];

    public const string SyntheticWorkOrderProviderIdPrefix = "E2E-";

    public static readonly Guid[] AllowedWorkOrderIds =
    [
        Guid.Parse("01a0f938-dabc-7ca6-b95c-6f0415aa466e"),
    ];

    /// <summary>
    /// Devolve o telefone autorizado equivalente a <paramref name="phone"/>
    /// (comparação só por dígitos, tolerando a ausência do nono dígito) ou
    /// <see cref="FixedRecipientPhone"/> quando ele não está na lista.
    /// </summary>
    public static string ResolveRecipientPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return FixedRecipientPhone;
        }

        var key = ToPhoneMatchKey(phone);
        return AllowedRecipientPhones.FirstOrDefault(allowed => ToPhoneMatchKey(allowed) == key)
            ?? FixedRecipientPhone;
    }

    private static string ToPhoneMatchKey(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 13 && digits.StartsWith("55", StringComparison.Ordinal) && digits[4] == '9'
            ? digits.Remove(4, 1)
            : digits;
    }
}
