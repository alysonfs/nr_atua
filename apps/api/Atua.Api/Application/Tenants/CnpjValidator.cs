namespace Atua.Api.Application.Tenants;

/// <summary>
/// Validação de formato de CNPJ (normalização + dígitos verificadores),
/// conforme ADR-018/RF-006. Validação técnica de formato, não regra de
/// negócio de domínio.
/// </summary>
public static class CnpjValidator
{
    /// <summary>
    /// Normaliza removendo caracteres não numéricos. Retorna null se o
    /// resultado não tiver exatamente 14 dígitos ou não passar na validação
    /// de dígitos verificadores.
    /// </summary>
    public static string? Normalize(string? rawCnpj)
    {
        if (string.IsNullOrWhiteSpace(rawCnpj)) return null;

        var digits = new string(rawCnpj.Where(char.IsDigit).ToArray());
        if (digits.Length != 14) return null;

        // Rejeita sequências de dígito repetido (inválidas por definição).
        if (digits.Distinct().Count() == 1) return null;

        if (!HasValidCheckDigits(digits)) return null;

        return digits;
    }

    public static bool IsValid(string? rawCnpj) => Normalize(rawCnpj) is not null;

    private static bool HasValidCheckDigits(string digits)
    {
        var numbers = digits.Select(character => character - '0').ToArray();

        var firstCheckDigit = CalculateCheckDigit(numbers[..12],
            [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        if (firstCheckDigit != numbers[12]) return false;

        var secondCheckDigit = CalculateCheckDigit(numbers[..13],
            [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return secondCheckDigit == numbers[13];
    }

    private static int CalculateCheckDigit(int[] numbers, int[] weights)
    {
        var sum = numbers.Select((number, index) => number * weights[index]).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
