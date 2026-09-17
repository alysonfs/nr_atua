namespace Atua.Api.Application.Integrations;

/// <summary>Resultado de leitura/alteração do intervalo de coleta recorrente (RF-025).</summary>
public enum ERecurrentCollectionIntervalStatus
{
    Success,
    Forbidden,
    IntegrationNotFound,
    InvalidInterval
}
