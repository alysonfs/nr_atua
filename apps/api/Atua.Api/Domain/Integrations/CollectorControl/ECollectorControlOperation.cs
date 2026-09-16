namespace Atua.Api.Domain.Integrations.CollectorControl;

/// <summary>
/// Operações de mutação do controle do coletor sujeitas a idempotência
/// (ADR-020).
/// </summary>
public enum ECollectorControlOperation
{
    Activate,
    Deactivate
}
