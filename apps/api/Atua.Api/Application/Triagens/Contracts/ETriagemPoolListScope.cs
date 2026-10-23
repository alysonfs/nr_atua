using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Escopo da listagem de triagens no Office (ambiente de gestão, somente
/// leitura).
/// </summary>
public enum ETriagemPoolListScope
{
    /// <summary>Estados do pool (<see cref="Triagem.PoolStatuses"/>). Padrão.</summary>
    Pool,

    /// <summary>Estados pré-pool (<see cref="Triagem.InProgressStatuses"/>).</summary>
    InProgress,

    /// <summary>União de <see cref="Pool"/> e <see cref="InProgress"/>.</summary>
    All,
}
