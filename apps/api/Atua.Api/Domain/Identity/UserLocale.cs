using System.Diagnostics.CodeAnalysis;

namespace Atua.Api.Domain.Identity;

public static class UserLocale
{
    public const string Default = "pt-BR";
    public const string EnglishUnitedStates = "en-US";
    public const string SpanishArgentina = "es-AR";

    private static readonly IReadOnlySet<string> SupportedLocales =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Default,
            EnglishUnitedStates,
            SpanishArgentina
        };

    public static bool IsSupported([NotNullWhen(true)] string? locale) =>
        locale is not null && SupportedLocales.Contains(locale);
}
