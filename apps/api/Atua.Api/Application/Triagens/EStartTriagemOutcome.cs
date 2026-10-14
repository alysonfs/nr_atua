namespace Atua.Api.Application.Triagens;

/// <summary>
/// Resultado tipado do início idempotente da triagem (ADR-039; arquitetura,
/// seção 3, itens 2/3). Permite ao endpoint mapear corretamente a resposta
/// HTTP sem acoplar-se a exceptions nem a convenções implícitas de status
/// nulo/booleano.
/// </summary>
public enum EStartTriagemOutcome
{
    /// <summary>Triagem criada nesta chamada — primeira aceitação do gatilho para esta OS elegível.</summary>
    Created,

    /// <summary>Triagem já existia para a chave — reentrega do gatilho.</summary>
    Existing,

    /// <summary>Nenhuma OS corresponde à combinação exata de tenant, integração e <c>WorkOrderProviderId</c>.</summary>
    WorkOrderNotFound,

    /// <summary>A OS existe, mas seu status atual não é elegível para iniciar a triagem.</summary>
    WorkOrderNotEligible,
}
