namespace Atua.Api.Application.Triagens;

public enum ETriagemTemplateOperationStatus
{
    Success,
    Forbidden,
    IntegrationNotFound,
    InvalidContent,
    DraftNotFound,
    ValidationFailed,
    ConcurrencyConflict
}
