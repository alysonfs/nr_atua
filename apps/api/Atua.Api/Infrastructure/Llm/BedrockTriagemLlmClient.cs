using System.Diagnostics;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Atua.Api.Application.Triagens;
using Atua.Api.Application.Triagens.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Adaptador Bedrock de <see cref="ITriagemLlmClient"/> (ADR-048), via
/// Converse API com tool use forçado (<see cref="TriagemLlmToolSchema"/>) —
/// a única forma de obter a saída estruturada exigida por
/// `triagem-llm-conducao.md` §2. Nunca vaza exceções do SDK para o
/// chamador: qualquer falha do fornecedor (indisponibilidade, timeout,
/// throttling) vira <see cref="ETriagemLlmOutcome.ProviderUnavailable"/>, e
/// qualquer saída fora do esquema vira
/// <see cref="ETriagemLlmOutcome.InvalidOutput"/> — nunca uma exceção não
/// tratada, e o texto não validado nunca é retornado ao chamador.
///
/// Esta classe nunca é exercitada com uma chamada real à AWS nos testes
/// desta sessão — cobertura de comportamento é feita com
/// <see cref="IAmazonBedrockRuntime"/> simulado (NSubstitute) e com
/// <see cref="FakeTriagemLlmClient"/> para os testes de fluxo que não
/// precisam do mapeamento Bedrock específico.
/// </summary>
public sealed class BedrockTriagemLlmClient(
    IAmazonBedrockRuntime bedrockClient,
    IOptions<TriagemLlmOptions> options,
    ILogger<BedrockTriagemLlmClient> logger) : ITriagemLlmClient
{
    public async Task<TriagemLlmResult> ConduzirAsync(TriagemLlmRequest request, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var systemPrompt = TriagemLlmSystemPromptBuilder.Build(request);
        var converseRequest = new ConverseRequest
        {
            ModelId = opts.ModelId,
            System = [new SystemContentBlock { Text = systemPrompt }],
            Messages = BuildMessages(request),
            InferenceConfig = new InferenceConfiguration { MaxTokens = opts.MaxOutputTokens },
            ToolConfig = new ToolConfiguration
            {
                Tools = [TriagemLlmToolSchema.BuildTool()],
                ToolChoice = new ToolChoice { Tool = new SpecificToolChoice { Name = TriagemLlmToolSchema.ToolName } },
            },
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(opts.TimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        ConverseResponse response;
        try
        {
            response = await bedrockClient.ConverseAsync(converseRequest, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Chamada ao Bedrock excedeu o tempo limite de {TimeoutSeconds}s (triagem {TriagemId}).",
                opts.TimeoutSeconds, request.TriagemId);
            return TriagemLlmResult.Unavailable("tempo limite excedido");
        }
        catch (AmazonBedrockRuntimeException exception)
        {
            logger.LogWarning(exception,
                "Falha do Bedrock ao conduzir a triagem {TriagemId} (tenant {TenantId}).",
                request.TriagemId, request.TenantId);
            return TriagemLlmResult.Unavailable($"falha do fornecedor: {exception.GetType().Name}");
        }
        stopwatch.Stop();

        var usage = BuildUsage(opts.ModelId, response, stopwatch.Elapsed);

        var toolUse = FindToolUse(response);
        if (toolUse is null)
        {
            return TriagemLlmResult.Invalid("resposta não usou a ferramenta obrigatória", usage);
        }

        if (!TriagemLlmToolResultParser.TryParse(toolUse.Input, out var payload, out var parseError))
        {
            return TriagemLlmResult.Invalid(parseError ?? "saída fora do esquema", usage);
        }

        return TriagemLlmResult.Success(payload!, usage);
    }

    private static List<Message> BuildMessages(TriagemLlmRequest request)
    {
        var messages = request.HistoricoConversa
            .Select(turn => new Message
            {
                Role = turn.Role == Domain.Triagens.ETriagemLlmConversationRole.Consumidor
                    ? ConversationRole.User
                    : ConversationRole.Assistant,
                Content = [new ContentBlock { Text = turn.Texto }],
            })
            .ToList();

        // A Converse API exige que a conversa comece e termine com o papel
        // "user" — se o histórico minimizado não tiver nenhum turno do
        // consumidor (ex.: só mídia recebida, sem texto), sintetiza um turno
        // de contexto mínimo para não quebrar a chamada.
        if (messages.Count == 0 || messages[0].Role != ConversationRole.User)
        {
            messages.Insert(0, new Message
            {
                Role = ConversationRole.User,
                Content = [new ContentBlock { Text = "(sem mensagem de texto nesta janela — ver anexos no contexto do sistema)" }],
            });
        }

        if (messages[^1].Role != ConversationRole.User)
        {
            messages.Add(new Message
            {
                Role = ConversationRole.User,
                Content = [new ContentBlock { Text = "(continue a triagem com base no checklist pendente)" }],
            });
        }

        return messages;
    }

    private static ToolUseBlock? FindToolUse(ConverseResponse response) =>
        response.Output?.Message?.Content?
            .FirstOrDefault(block => block.ToolUse is { Name: TriagemLlmToolSchema.ToolName })?
            .ToolUse;

    private static TriagemLlmUsageInfo BuildUsage(string modelId, ConverseResponse response, TimeSpan elapsed) =>
        new(modelId, response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0, elapsed);
}
