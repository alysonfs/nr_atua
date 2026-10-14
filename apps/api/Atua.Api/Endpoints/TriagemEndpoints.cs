using System.Security.Claims;
using Atua.Api.Application.Triagens;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Atua.Api.Endpoints;

/// <summary>
/// Endpoint interno de início idempotente da triagem (ADR-039; arquitetura,
/// seção 3, itens 2/3). Autorizado via credencial de serviço
/// (<c>ServiceCredential</c>), com escopo exclusivo <c>triage.start</c> —
/// não reutiliza os escopos do Agente Coletor.
///
/// Rota: <c>POST /api/internal/triagens/start</c>.
///
/// Desde o ADR-041, o fluxo automático de início de triagem NÃO chama mais
/// esta rota: o Collector grava a intenção em <c>triagem_trigger_outbox</c>
/// e o <c>TriagemOutboxDrainWorker</c>, hospedado nesta Master API, chama
/// <see cref="StartTriagemUseCase"/> diretamente, em processo. Esta rota
/// permanece ativa apenas para uso manual/QA/reprocessamento pontual
/// (ex.: reenviar o gatilho de uma OS específica fora do fluxo automático).
///
/// Tenant e integração vêm exclusivamente dos claims da credencial
/// autenticada — nunca do corpo da requisição — para impedir que um
/// chamador referencie uma OS de outro tenant/integração. O corpo contém
/// apenas o identificador externo da OS no provedor
/// (<see cref="StartTriagemRequest.WorkOrderProviderId"/>).
/// </summary>
public static class TriagemEndpoints
{
    /// <summary>Limite de tamanho aceito para <see cref="StartTriagemRequest.WorkOrderProviderId"/>.</summary>
    public const int MaxWorkOrderProviderIdLength = 256;

    public static void MapTriagemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/internal/triagens/start", StartAsync)
            .RequireAuthorization("TriageStart")
            .WithName("StartTriagem")
            .WithSummary(
                "[Manual/QA/reprocessamento] Inicia idempotentemente a triagem automatizada de uma " +
                "OS elegível. Desde o ADR-041, o fluxo automático não chama mais esta rota.")
            .WithDescription(
                "Uso manual/QA/reprocessamento: desde o ADR-041, o início automático da triagem é " +
                "disparado pelo TriagemOutboxDrainWorker (outbox PostgreSQL escrita pelo Collector), " +
                "não mais por esta rota. Esta rota permanece disponível para reenviar o gatilho de " +
                "uma OS específica fora do fluxo automático (ex.: QA, suporte, reprocessamento " +
                "pontual). Aceita o gatilho de início da triagem uma única vez por OS elegível " +
                "(tenant + integração da credencial de serviço + WorkOrderProviderId do corpo). " +
                "Reentregas do mesmo gatilho retornam 200 com o estado atual, sem recriar nem " +
                "resetar estado já avançado. O Location retornado em 201 identifica o recurso " +
                "interno da triagem (/api/internal/triagens/{id}); este incremento não expõe um GET " +
                "para esse recurso — fica para um incremento futuro, quando houver um consumidor " +
                "real para ele.")
            .Produces<StartTriagemResponse>(StatusCodes.Status201Created)
            .Produces<StartTriagemResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Created<StartTriagemResponse>, Ok<StartTriagemResponse>, NotFound,
        Conflict<ProblemDetails>, UnauthorizedHttpResult, ValidationProblem>> StartAsync(
        StartTriagemRequest request,
        ClaimsPrincipal user,
        StartTriagemUseCase useCase,
        CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        var integrationId = GetGuidClaim(user, "integration_id");
        if (tenantId is null || integrationId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.WorkOrderProviderId)
            || request.WorkOrderProviderId.Length > MaxWorkOrderProviderIdLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["workOrderProviderId"] =
                [
                    $"Obrigatório, não pode exceder {MaxWorkOrderProviderIdLength} caracteres.",
                ],
            });
        }

        var result = await useCase.ExecuteAsync(tenantId.Value, integrationId.Value,
            request.WorkOrderProviderId, cancellationToken);

        return result.Outcome switch
        {
            EStartTriagemOutcome.WorkOrderNotFound => TypedResults.NotFound(),

            EStartTriagemOutcome.WorkOrderNotEligible => TypedResults.Conflict(new ProblemDetails
            {
                Title = "Work order not eligible for triage",
                Detail = "A OS informada não está em um status elegível para iniciar a triagem.",
                Status = StatusCodes.Status409Conflict,
            }),

            EStartTriagemOutcome.Created => TypedResults.Created(
                $"/api/internal/triagens/{result.TriagemId!.Value}",
                ToResponse(result, created: true)),

            EStartTriagemOutcome.Existing => TypedResults.Ok(ToResponse(result, created: false)),

            _ => throw new InvalidOperationException($"Outcome não tratado: {result.Outcome}."),
        };
    }

    private static StartTriagemResponse ToResponse(StartTriagemResult result, bool created) =>
        new(result.TriagemId!.Value, result.Status!.Value.ToString(), created);

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

/// <summary>
/// Corpo da requisição de início idempotente da triagem. Contém somente o
/// identificador externo da OS no provedor — tenant e integração vêm
/// exclusivamente dos claims da credencial de serviço autenticada (nunca do
/// corpo), impedindo referência cruzada entre tenants/integrações.
/// </summary>
public sealed record StartTriagemRequest
{
    /// <summary>
    /// Identificador serial externo da OS no provedor (ver
    /// <see cref="Atua.Api.Domain.WorkOrders.WorkOrder.WorkOrderProviderId"/>).
    /// Obrigatório; até <see cref="TriagemEndpoints.MaxWorkOrderProviderIdLength"/> caracteres.
    /// </summary>
    public required string WorkOrderProviderId { get; init; }
}

/// <summary>
/// DTO de resposta do início idempotente da triagem — nunca expõe a
/// entidade de domínio/EF <see cref="Atua.Api.Domain.Triagens.Triagem"/> ao chamador externo.
/// </summary>
/// <param name="TriagemId">Id da triagem (nova ou já existente).</param>
/// <param name="Status">Status atual da triagem, como texto legível (nome do <see cref="Atua.Api.Domain.Triagens.ETriagemStatus"/>).</param>
/// <param name="Created"><c>true</c> quando esta chamada criou a triagem; <c>false</c> em reentregas.</param>
public sealed record StartTriagemResponse(Guid TriagemId, string Status, bool Created);
