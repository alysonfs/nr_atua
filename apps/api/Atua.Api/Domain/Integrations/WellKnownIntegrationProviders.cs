namespace Atua.Api.Domain.Integrations;

/// <summary>
/// Identificadores fixos de <see cref="IntegrationProvider"/> conhecidos pelo
/// sistema no MVP. No MVP existe exatamente um provedor de integração
/// (iService) e toda criação de Tenant cria automaticamente uma
/// <see cref="Integration"/> associada a este provedor (ADR-018, emenda
/// "Resolução de integrationId").
/// </summary>
public static class WellKnownIntegrationProviders
{
    /// <summary>
    /// Id fixo (seed via migration) do provedor "iService". Não deve ser
    /// alterado; é referenciado tanto pela migration de seed quanto pelo
    /// <c>TenantOnboardingService</c>.
    /// </summary>
    public static readonly Guid IServiceProviderId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
}
