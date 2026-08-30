namespace Atua.Api.Application.Integrations.CollectorControl;

/// <summary>
/// Motivo operacional pelo qual a ativação está indisponível (ADR-020, DTO de
/// leitura). Não expõe segredo nem detalhe de plano/integração.
/// </summary>
public enum EActivationBlockReason
{
    None,
    TrialIneligible,
    CredentialsNotValidated
}
