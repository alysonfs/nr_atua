using System.Security.Claims;
using System.Text.Json;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Atua.Api.Endpoints;

/// <summary>
/// Endpoints internos do processo do canal WhatsApp (ADR-042). Autorizados
/// via credencial de serviço (<c>ServiceCredential</c>), com escopos
/// exclusivos para claim, complete e ingestão de mensagens — não reutilizam
/// os escopos do Agente Coletor nem o de início da triagem.
///
/// Rotas:
/// <list type="bullet">
///   <item><c>POST /api/internal/triagens/canal/claim</c></item>
///   <item><c>POST /api/internal/triagens/canal/complete</c></item>
///   <item><c>POST /api/internal/triagens/canal/mensagens</c></item>
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

        endpoints.MapPost("/api/internal/triagens/canal/retransmissao", RetransmissionAsync)
            .RequireAuthorization("ChannelCommandClaim")
            .WithName("GetTriagemChannelRetransmission")
            .WithSummary("Recupera o texto de uma mensagem enviada na última hora para retransmissão.")
            .WithDescription(
                "Usado somente quando o aparelho do destinatário pede retransmissão e o canal perdeu o " +
                "original da memória (reinício). Restrito à tenant/integração da credencial e a comandos " +
                "concluídos com sucesso na última hora. Não cria, reabre nem reenvia comandos; o canal " +
                "valida o destinatário antes de responder ao WhatsApp.")
            .Produces<ChannelRetransmissionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

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

        endpoints.MapPost("/api/internal/triagens/canal/mensagens", IngestMessageAsync)
            .RequireAuthorization("ChannelMessageIngest")
            .WithName("IngestTriagemChannelMessage")
            .WithSummary("Registra uma mensagem recebida pelo canal WhatsApp (ADR-043).")
            .WithDescription(
                "Deduplica o evento por tenant e chave idempotente, localiza triagens ativas pelo número " +
                "do remetente e inicia a coleta quando existe exatamente uma correspondência. Eventos " +
                "ambíguos são persistidos sem associação e aguardam resolução manual.")
            .Produces<IngestTriagemChannelMessageResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/api/internal/triagens/canal/midias", IngestMediaAsync)
            .RequireAuthorization("ChannelMediaIngest")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(
                IngestTriagemChannelMediaUseCase.MaxContentLengthBytes + 64 * 1024))
            .WithName("IngestTriagemChannelMedia")
            .WithSummary("Registra uma mídia (áudio, imagem, vídeo ou PDF) recebida pelo canal WhatsApp.")
            .WithDescription(
                "Corpo multipart/form-data com a parte 'metadata' (JSON do envelope) e a parte 'file' " +
                "(binário). Deduplica por tenant e chave idempotente; reentrega com conteúdo diferente " +
                "retorna 409. Rejeita tecnicamente (sem armazenar o binário) tipo/tamanho fora do " +
                "permitido, mensagens de grupo ou da própria conta. Resolve a triagem pelo telefone do " +
                "remetente, em qualquer status — apenas armazena o binário quando há exatamente uma " +
                "triagem candidata, e só transiciona para EmColeta quando ela está em ContatoIniciado.")
            .Produces<IngestTriagemChannelMediaResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
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
            command.RenderedContent,
            command.RecipientPhone));
    }

    // -----------------------------------------------------------------------
    // Retransmissão
    // -----------------------------------------------------------------------

    private static readonly System.Text.RegularExpressions.Regex MessageIdPattern =
        new("^[A-F0-9]{32}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static async Task<Results<Ok<ChannelRetransmissionResponse>, NotFound, BadRequest,
        UnauthorizedHttpResult>> RetransmissionAsync(
        ChannelRetransmissionRequest request,
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

        if (request.MessageId is null || !MessageIdPattern.IsMatch(request.MessageId))
        {
            return TypedResults.BadRequest();
        }

        var command = await service.FindForRetransmissionAsync(tenantId.Value, integrationId.Value,
            request.MessageId, cancellationToken);
        if (command is null || string.IsNullOrWhiteSpace(command.RecipientPhone))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new ChannelRetransmissionResponse(command.RenderedContent, command.RecipientPhone));
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

    private static async Task<Results<Ok<IngestTriagemChannelMessageResponse>, UnauthorizedHttpResult,
        ValidationProblem>> IngestMessageAsync(
        IngestTriagemChannelMessageRequest request,
        ClaimsPrincipal user,
        IIngestTriagemChannelMessageUseCase useCase,
        CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        var integrationId = GetGuidClaim(user, "integration_id");
        if (tenantId is null || integrationId is null)
        {
            return TypedResults.Unauthorized();
        }

        var errors = ValidateInboundMessage(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await useCase.ExecuteAsync(new TriagemInboundMessageCommand(
            tenantId.Value,
            integrationId.Value,
            request.ChannelMessageId!,
            request.ChannelIdempotencyKey!,
            request.CorrelationId!.Value,
            request.SenderPhone!,
            request.ConversationKind!,
            request.FromMe!.Value,
            request.ReceivedAtUtc!.Value,
            request.Kind!,
            request.Text), cancellationToken);

        return TypedResults.Ok(new IngestTriagemChannelMessageResponse(
            result.ChannelMessageId,
            result.Outcome.ToString(),
            result.TriagemId,
            result.CandidateCount));
    }

    // -----------------------------------------------------------------------
    // Mídia
    // -----------------------------------------------------------------------

    private static readonly JsonSerializerOptions MediaMetadataJsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<Results<Ok<IngestTriagemChannelMediaResponse>, UnauthorizedHttpResult,
        ValidationProblem, Conflict<ProblemDetails>>> IngestMediaAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IIngestTriagemChannelMediaUseCase useCase,
        CancellationToken cancellationToken)
    {
        var tenantId = GetGuidClaim(user, "tenant_id");
        var integrationId = GetGuidClaim(user, "integration_id");
        if (tenantId is null || integrationId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!request.HasFormContentType)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Deve ser multipart/form-data com as partes 'metadata' e 'file'."],
            });
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Corpo multipart malformado."],
            });
        }

        var metadataJson = form["metadata"].ToString();
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["metadata"] = ["Obrigatória — parte 'metadata' com o envelope JSON."],
            });
        }

        IngestTriagemChannelMediaRequest? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<IngestTriagemChannelMediaRequest>(
                metadataJson, MediaMetadataJsonOptions);
        }
        catch (JsonException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["metadata"] = ["JSON inválido na parte 'metadata'."],
            });
        }

        if (metadata is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["metadata"] = ["Obrigatória."],
            });
        }

        var file = form.Files["file"];
        var errors = ValidateInboundMedia(metadata, file);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var command = new TriagemInboundMediaCommand(
            tenantId.Value,
            integrationId.Value,
            metadata.ChannelMessageId!,
            metadata.ChannelIdempotencyKey!,
            metadata.CorrelationId!.Value,
            metadata.SenderPhone!,
            metadata.ConversationKind!,
            metadata.FromMe!.Value,
            metadata.ReceivedAtUtc!.Value,
            ParseMediaKind(metadata.MediaKind!),
            metadata.ContentType!,
            metadata.SizeBytes!.Value,
            metadata.IsVoiceNote!.Value,
            metadata.DurationSeconds,
            metadata.Caption);

        await using var fileStream = file!.OpenReadStream();
        var useCaseResult = await useCase.ExecuteAsync(command, fileStream, cancellationToken);

        if (useCaseResult.IsIdempotencyConflict)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Channel media idempotency conflict",
                Detail = "A chave idempotente informada já está associada a uma mídia com conteúdo " +
                    "diferente (contentType/sizeBytes). O Channel não deve reutilizar a mesma chave para " +
                    "mídias diferentes.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        var result = useCaseResult.Result!;
        return TypedResults.Ok(new IngestTriagemChannelMediaResponse(
            result.ChannelMessageId,
            result.Outcome.ToString(),
            result.TriagemId,
            result.CandidateCount));
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

    private static ETriagemInboundMediaKind ParseMediaKind(string value) => value switch
    {
        "audio" => ETriagemInboundMediaKind.Audio,
        "image" => ETriagemInboundMediaKind.Image,
        "video" => ETriagemInboundMediaKind.Video,
        "document" => ETriagemInboundMediaKind.Document,
        _ => throw new InvalidOperationException(
            $"mediaKind '{value}' deveria ter sido rejeitado pela validação do envelope."),
    };

    private static Dictionary<string, string[]> ValidateInboundMedia(
        IngestTriagemChannelMediaRequest request, IFormFile? file)
    {
        var errors = new Dictionary<string, string[]>();
        void AddError(string field, string message) => errors[field] = [message];

        if (request.ContractVersion != "1") AddError("contractVersion", "Deve ser '1'.");
        if (string.IsNullOrWhiteSpace(request.ChannelMessageId) || request.ChannelMessageId.Length > 256)
            AddError("channelMessageId", "Obrigatório, com no máximo 256 caracteres.");
        if (string.IsNullOrWhiteSpace(request.ChannelIdempotencyKey) || request.ChannelIdempotencyKey.Length > 128)
            AddError("channelIdempotencyKey", "Obrigatória, com no máximo 128 caracteres.");
        if (request.CorrelationId is null || request.CorrelationId == Guid.Empty)
            AddError("correlationId", "Deve ser um UUID válido.");
        if (request.SenderPhone is null || !System.Text.RegularExpressions.Regex.IsMatch(
                request.SenderPhone, "^[1-9][0-9]{7,14}$"))
            AddError("senderPhone", "Deve conter de 8 a 15 dígitos internacionais.");
        if (request.ConversationKind is not ("individual" or "group"))
            AddError("conversationKind", "Deve ser 'individual' ou 'group'.");
        if (request.FromMe is null) AddError("fromMe", "Obrigatório.");
        if (request.ReceivedAtUtc is null
            || request.ReceivedAtUtc == DateTimeOffset.MinValue
            || request.ReceivedAtUtc.Value.Offset != TimeSpan.Zero)
            AddError("receivedAtUtc", "Deve conter um instante UTC válido.");
        if (request.MediaKind is not ("audio" or "image" or "video" or "document"))
            AddError("mediaKind", "Deve ser 'audio', 'image', 'video' ou 'document'.");
        if (string.IsNullOrWhiteSpace(request.ContentType) || request.ContentType.Length > 128)
            AddError("contentType", "Obrigatório, com no máximo 128 caracteres.");
        if (request.SizeBytes is null || request.SizeBytes <= 0)
            AddError("sizeBytes", "Deve ser um inteiro positivo.");
        if (request.Sha256 is null || !System.Text.RegularExpressions.Regex.IsMatch(request.Sha256, "^[a-f0-9]{64}$"))
            AddError("sha256", "Deve conter 64 caracteres hexadecimais minúsculos.");
        if (request.IsVoiceNote is null) AddError("isVoiceNote", "Obrigatório.");
        if (request.DurationSeconds is < 0) AddError("durationSeconds", "Deve ser zero ou positivo quando informado.");
        if (request.Caption?.Length > 4_096) AddError("caption", "Deve ter no máximo 4096 caracteres.");
        if (file is null || file.Length == 0) AddError("file", "Obrigatório — parte 'file' com o binário da mídia.");

        return errors;
    }

    private static Dictionary<string, string[]> ValidateInboundMessage(
        IngestTriagemChannelMessageRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        void AddError(string field, string message) => errors[field] = [message];

        if (request.ContractVersion != "1") AddError("contractVersion", "Deve ser '1'.");
        if (string.IsNullOrWhiteSpace(request.ChannelMessageId) || request.ChannelMessageId.Length > 256)
            AddError("channelMessageId", "Obrigatório, com no máximo 256 caracteres.");
        if (string.IsNullOrWhiteSpace(request.ChannelIdempotencyKey) || request.ChannelIdempotencyKey.Length > 128)
            AddError("channelIdempotencyKey", "Obrigatória, com no máximo 128 caracteres.");
        if (request.CorrelationId is null || request.CorrelationId == Guid.Empty)
            AddError("correlationId", "Deve ser um UUID válido.");
        if (request.SenderPhone is null || !System.Text.RegularExpressions.Regex.IsMatch(
                request.SenderPhone, "^[1-9][0-9]{7,14}$"))
            AddError("senderPhone", "Deve conter de 8 a 15 dígitos internacionais.");
        if (request.ConversationKind is not ("individual" or "group"))
            AddError("conversationKind", "Deve ser 'individual' ou 'group'.");
        if (request.FromMe is null) AddError("fromMe", "Obrigatório.");
        if (request.ReceivedAtUtc is null
            || request.ReceivedAtUtc == DateTimeOffset.MinValue
            || request.ReceivedAtUtc.Value.Offset != TimeSpan.Zero)
            AddError("receivedAtUtc", "Deve conter um instante UTC válido.");
        if (request.Kind is not ("text" or "empty"))
            AddError("kind", "Deve ser 'text' ou 'empty'.");
        if (request.Text?.Length > 65_536)
            AddError("text", "Deve ter no máximo 65536 caracteres.");

        return errors;
    }
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
    string RenderedContent = "",
    string? RecipientPhone = null);

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

/// <summary>Evento de mensagem recebida pelo Channel, conforme o ADR-043.</summary>
public sealed record IngestTriagemChannelMessageRequest(
    string? ContractVersion,
    string? ChannelMessageId,
    string? ChannelIdempotencyKey,
    Guid? CorrelationId,
    string? SenderPhone,
    string? ConversationKind,
    bool? FromMe,
    DateTimeOffset? ReceivedAtUtc,
    string? Kind,
    string? Text);

/// <summary>Resultado do processamento idempotente de uma mensagem recebida.</summary>
public sealed record IngestTriagemChannelMessageResponse(
    string ChannelMessageId,
    string Outcome,
    Guid? TriagemId,
    int CandidateCount);

/// <summary>
/// Envelope JSON da parte 'metadata' do evento de mídia recebido pelo
/// Channel (multipart/form-data, parte 'file' carrega o binário). Campos
/// herdados do ADR-043 têm o mesmo significado do evento de texto.
/// </summary>
/// <param name="MediaKind">Categoria declarada — <c>"audio"</c>, <c>"image"</c>, <c>"video"</c> ou <c>"document"</c> (apenas PDF é aceito como documento).</param>
/// <param name="ContentType">MIME type declarado do arquivo — validado contra a assinatura binária real.</param>
/// <param name="SizeBytes">Tamanho declarado em bytes — confrontado com o tamanho real lido do stream.</param>
/// <param name="Sha256">SHA-256 declarado pelo Channel — nunca usado como autoridade; o servidor recalcula o hash a partir do binário recebido.</param>
/// <param name="IsVoiceNote">Indica se o áudio é uma nota de voz (PTT) do WhatsApp.</param>
/// <param name="DurationSeconds">Duração em segundos, quando aplicável (áudio/vídeo).</param>
/// <param name="Caption">Legenda opcional anexada à mídia pelo remetente.</param>
public sealed record IngestTriagemChannelMediaRequest(
    string? ContractVersion,
    string? ChannelMessageId,
    string? ChannelIdempotencyKey,
    Guid? CorrelationId,
    string? SenderPhone,
    string? ConversationKind,
    bool? FromMe,
    DateTimeOffset? ReceivedAtUtc,
    string? MediaKind,
    string? ContentType,
    long? SizeBytes,
    string? Sha256,
    bool? IsVoiceNote,
    int? DurationSeconds,
    string? Caption);

/// <summary>Resultado do processamento idempotente de um evento de mídia recebido.</summary>
public sealed record IngestTriagemChannelMediaResponse(
    string ChannelMessageId,
    string Outcome,
    Guid? TriagemId,
    int CandidateCount);

public sealed record ChannelRetransmissionRequest(string? MessageId);

public sealed record ChannelRetransmissionResponse(string Text, string RecipientPhone);
