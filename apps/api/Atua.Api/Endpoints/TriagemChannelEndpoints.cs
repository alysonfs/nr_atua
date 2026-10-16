using System.Security.Claims;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Atua.Api.Endpoints;

/// <summary>
/// Endpoints internos do processo do canal WhatsApp (ADR-042). Autorizados
/// via credencial de serviço (<c>ServiceCredential</c>), com escopos
/// exclusivos <c>channel.command.claim</c> e <c>channel.command.complete</c>
/// — não reutilizam os escopos do Agente Coletor nem o de início da
/// triagem.
///
/// Rotas:
/// <list type="bullet">
///   <item><c>POST /api/internal/triagens/canal/claim</c></item>
///   <item><c>POST /api/internal/triagens/canal/complete</c></item>
/// </list>
///
/// Tenant e integração vêm exclusivamente dos claims da credencial
/// autenticada — nunca do corpo da requisição (ADR-042: "O corpo das
/// requisições não poderá ampliar esse escopo").
/// </summary>
public static class TriagemChannelEndpoints
{
    public static void MapTriagemChannelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/internal/triagens/canal/claim", ClaimAsync)
            .RequireAuthorization("ChannelCommandClaim")
            .WithName("ClaimTriagemChannelCommand")
            .WithSummary("Reivindica o próximo comando de canal elegível (ADR-042).")
            .WithDescription(
                "Reivindica atomicamente o próximo comando Pending (ou Claimed com lease expirada) da " +
                "tenant/integração da credencial de serviço autenticada, marcando-o Claimed com uma nova " +
                "lease. Retorna 204 quando não há comando elegível. O processo do canal deve chamar esta " +
                "rota em intervalo de até 30 segundos (arquitetura, seção 2.4).")
            .Produces<ClaimChannelCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/api/internal/triagens/canal/complete", CompleteAsync)
            .RequireAuthorization("ChannelCommandComplete")
            .WithName("CompleteTriagemChannelCommand")
            .WithSummary("Conclui um comando de canal reivindicado, com o resultado do envio (ADR-042).")
            .WithDescription(
                "Aceita o resultado estruturado do envio do template: sucesso move a triagem para " +
                "ContatoIniciado; falha definitiva move para FalhaDeContato; falha transitória agenda nova " +
                "tentativa, e a quinta falha transitória consecutiva move a triagem para FalhaDeContato. " +
                "Idempotente: reentrega de um comando já concluído retorna o mesmo resultado sem repetir " +
                "o efeito. Exige o token de lease (ClaimedAtUtc) devolvido pelo claim: quando o comando já " +
                "não está mais sob a lease apresentada (expirada e, possivelmente, substituída por outro " +
                "claim), devolve 409 ProblemDetails sem alterar o comando nem a triagem.")
            .Produces<CompleteChannelCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    // -----------------------------------------------------------------------
    // Claim
    // -----------------------------------------------------------------------

    private static async Task<Results<Ok<ClaimChannelCommandResponse>, NoContent, UnauthorizedHttpResult>> ClaimAsync(
        ClaimsPrincipal user,
        TriagemChannelCommandService service,
        CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        var integrationId = GetGuidClaim(user, "integration_id");
        if (tenantId is null || integrationId is null)
        {
            return TypedResults.Unauthorized();
        }

        var command = await service.ClaimAsync(tenantId.Value, integrationId.Value, cancellationToken);
        if (command is null)
        {
            return TypedResults.NoContent();
        }

        return TypedResults.Ok(new ClaimChannelCommandResponse(
            command.Id,
            command.TriagemId,
            command.WorkOrderId,
            command.TemplateId,
            command.TemplateVersion,
            command.IdempotencyKey,
            command.CorrelationId,
            command.AttemptCount,
            command.ClaimedAtUtc!.Value,
            command.ClaimExpiresAtUtc!.Value,
            command.RenderedContent));
    }

    // -----------------------------------------------------------------------
    // Complete
    // -----------------------------------------------------------------------

    private static async Task<Results<Ok<CompleteChannelCommandResponse>, NotFound<ProblemDetails>,
        Conflict<ProblemDetails>, UnauthorizedHttpResult, ValidationProblem>> CompleteAsync(
        CompleteChannelCommandRequest request,
        ClaimsPrincipal user,
        TriagemChannelCommandService service,
        CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        var integrationId = GetGuidClaim(user, "integration_id");
        if (tenantId is null || integrationId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TryParseOutcome(request.Outcome, out var outcome))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["outcome"] = ["Obrigatório — deve ser 'Success', 'TransientFailure' ou 'PermanentFailure'."],
            });
        }

        var result = await service.CompleteAsync(request.CommandId, tenantId.Value, integrationId.Value,
            request.ClaimedAtUtc, outcome, request.ErrorCode, request.ErrorDescription, cancellationToken);

        if (result is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Channel command not found",
                Detail = "O comando informado não foi encontrado para a tenant/integração autenticada.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        if (result.Outcome == ETriagemChannelCompleteOutcome.LeaseLost)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Channel command lease lost",
                Detail = "O token de lease apresentado não é mais o vigente — a lease expirou e pode ter sido " +
                    "substituída por outro claim. Nenhum efeito foi aplicado ao comando ou à triagem.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        return TypedResults.Ok(new CompleteChannelCommandResponse(
            result.CommandId, result.Status.ToString(), result.CompletedAtUtc));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool TryParseOutcome(string? value, out ETriagemChannelSendOutcome outcome)
    {
        outcome = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value, ignoreCase: true, out outcome);
    }

    private static Guid? GetGuidClaim(ClaimsPrincipal user, string type) =>
        Guid.TryParse(user.FindFirstValue(type), out var value) ? value : null;
}

/// <summary>
/// Resposta do claim do comando de canal. Nunca transporta credenciais —
/// apenas os identificadores e metadados necessários para o canal enviar o
/// template (ADR-042; arquitetura, seção 5.1), incluindo o snapshot
/// renderizado e imutável selecionado no aceite da triagem.
/// </summary>
/// <param name="CommandId">Identificador do comando — usado em <c>complete</c>.</param>
/// <param name="TriagemId">Identificador interno da triagem associada.</param>
/// <param name="WorkOrderId">Identificador interno da OS associada.</param>
/// <param name="TemplateId">Identificador abstrato do template a enviar.</param>
/// <param name="TemplateVersion">Versão do template.</param>
/// <param name="IdempotencyKey">Chave estável de idempotência (ADR-039) — preservada em toda retentativa.</param>
/// <param name="CorrelationId">Identificador de correlação entre comando, resposta e evento de entrega.</param>
/// <param name="AttemptCount">Quantidade de falhas transitórias já registradas para este comando.</param>
/// <param name="ClaimedAtUtc">Instante em que esta reivindicação ocorreu.</param>
/// <param name="ClaimExpiresAtUtc">Instante em que a lease desta reivindicação expira.</param>
public sealed record ClaimChannelCommandResponse(
    Guid CommandId,
    Guid TriagemId,
    Guid WorkOrderId,
    string TemplateId,
    int TemplateVersion,
    string IdempotencyKey,
    Guid CorrelationId,
    int AttemptCount,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    string RenderedContent = "");

/// <summary>
/// Corpo da requisição de conclusão do comando de canal. Tenant e
/// integração nunca são aceitos aqui — vêm exclusivamente dos claims da
/// credencial de serviço autenticada.
/// </summary>
/// <param name="CommandId">Identificador do comando reivindicado por <c>claim</c>.</param>
/// <param name="ClaimedAtUtc">
/// Token de lease devolvido pelo <c>claim</c> (<c>ClaimChannelCommandResponse.ClaimedAtUtc</c>).
/// O efeito só é aplicado quando o comando ainda está <c>Claimed</c> e este
/// token é exatamente o vigente — protege contra um chamador com lease
/// expirada (e possivelmente já substituída por outro claim) completar uma
/// lease que não é mais sua (ADR-042, correção de concorrência).
/// </param>
/// <param name="Outcome">
/// Resultado do envio — <c>"Success"</c>, <c>"TransientFailure"</c> ou
/// <c>"PermanentFailure"</c> (ver <see cref="ETriagemChannelSendOutcome"/>).
/// </param>
/// <param name="ErrorCode">Código seguro de erro reportado pelo canal — nunca texto livre sensível. <c>null</c> em sucesso.</param>
/// <param name="ErrorDescription">Descrição segura do erro — nunca conteúdo de mensagem/dado sensível. <c>null</c> em sucesso.</param>
public sealed record CompleteChannelCommandRequest(
    Guid CommandId,
    DateTimeOffset ClaimedAtUtc,
    string? Outcome,
    string? ErrorCode,
    string? ErrorDescription);

/// <summary>Resposta da conclusão do comando de canal — nunca expõe a entidade de domínio/EF.</summary>
/// <param name="CommandId">Identificador do comando.</param>
/// <param name="Status">Status atual do comando (<see cref="ETriagemChannelCommandStatus"/>), como texto legível.</param>
/// <param name="CompletedAtUtc">Instante de conclusão (sucesso ou falha definitiva).</param>
public sealed record CompleteChannelCommandResponse(Guid CommandId, string Status, DateTimeOffset CompletedAtUtc);
