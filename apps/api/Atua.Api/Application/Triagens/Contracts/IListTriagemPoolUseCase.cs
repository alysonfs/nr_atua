using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Lista as triagens de um tenant para o Office (ambiente de gestão, somente
/// leitura — épico de pool de triagem, "C1 — Lista do pool"). Por padrão
/// (<see cref="ETriagemPoolListScope.Pool"/>) mostra somente os estados de
/// <see cref="Triagem.PoolStatuses"/>; com
/// <see cref="ETriagemPoolListScope.InProgress"/> mostra as triagens ainda em
/// coleta (<see cref="Triagem.InProgressStatuses"/>). Nunca lista
/// <c>AguardandoEnvio</c> nem <c>AssumidaPorOperador</c>.
/// </summary>
public interface IListTriagemPoolUseCase
{
    Task<TriagemPoolListResult> ExecuteAsync(TriagemPoolListQuery query, CancellationToken cancellationToken);
}

/// <param name="TenantId">Tenant do usuário autenticado — isolamento multi-tenant obrigatório.</param>
/// <param name="RequestingUserId">Usuário autenticado (claim <c>sub</c>) — usado apenas para validar o membership no tenant.</param>
/// <param name="StatusFilter">
/// Filtro opcional por um único estado do escopo (critério de aceite 4). Quando
/// <c>null</c>, retorna todos os estados do escopo.
/// </param>
/// <param name="Scope">Escopo da listagem; padrão <see cref="ETriagemPoolListScope.Pool"/>.</param>
public sealed record TriagemPoolListQuery(
    Guid TenantId,
    Guid RequestingUserId,
    ETriagemStatus? StatusFilter,
    ETriagemPoolListScope Scope = ETriagemPoolListScope.Pool);

public enum ETriagemPoolListOutcome
{
    Success,

    /// <summary>Usuário não é membro do tenant.</summary>
    Forbidden,

    /// <summary>
    /// <see cref="TriagemPoolListQuery.StatusFilter"/> informado não pertence
    /// aos estados do <see cref="TriagemPoolListQuery.Scope"/> solicitado.
    /// </summary>
    InvalidStatusFilter,
}

public sealed record TriagemPoolListResult(
    ETriagemPoolListOutcome Outcome,
    int TotalCount,
    IReadOnlyList<TriagemPoolListItem> Items)
{
    public static TriagemPoolListResult Failure(ETriagemPoolListOutcome outcome) => new(outcome, 0, []);
}

/// <summary>Colunas mínimas da lista do pool (épico, "C1 — Lista do pool").</summary>
/// <param name="EnteredPoolAtUtc">
/// Aproximação do momento de entrada no pool: não há coluna dedicada hoje,
/// então usa-se <see cref="Triagem.UpdatedAtUtc"/> no momento em que a
/// triagem transitou para o estado atual do pool (mesma transação que a
/// colocou no estado reportado em <see cref="Status"/>).
/// </param>
/// <param name="HasNewActivity">
/// <c>true</c> quando chegou mensagem/mídia do consumidor após
/// <paramref name="EnteredPoolAtUtc"/> e antes da assunção (critério de
/// aceite 10) — sem expor o conteúdo (decisão do PO).
/// </param>
/// <param name="WorkOrderId">
/// Id da OS vinculada — mesmo id usado pela rota do Office
/// <c>/work-orders/:workOrderId</c> (<c>WorkOrder.Id</c>).
/// </param>
/// <param name="StartedAtUtc">Momento em que a triagem foi criada (<see cref="Triagem.CreatedAtUtc"/>) — início do tempo de triagem no Office.</param>
/// <param name="FinishedAtUtc">Momento em que a coleta da LLM terminou (<see cref="Triagem.ColetaEncerradaEmUtc"/>); <c>null</c> enquanto ainda em coleta.</param>
public sealed record TriagemPoolListItem(
    Guid TriagemId,
    Guid WorkOrderId,
    string? WorkOrderProviderNo,
    string? CustomerName,
    string? ProductModel,
    string? ProductBrand,
    ETriagemStatus Status,
    int ChecklistItemsCompleted,
    int ChecklistItemsTotal,
    DateTimeOffset EnteredPoolAtUtc,
    bool HasNewActivity,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);
