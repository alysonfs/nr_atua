using System.Diagnostics;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Atua.Api.Application.Triagens.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Adaptador Bedrock de <see cref="ITriagemAttachmentVisionClient"/>
/// (decisão do PO: adiantar a conferência automática de nota fiscal e
/// etiqueta via WhatsApp), via Converse API com imagem ou documento PDF e tool
/// use forçado — mesmo padrão de <see cref="BedrockTriagemLlmClient"/>,
/// reaproveitando o mesmo modelo/região/timeout configurados em
/// <see cref="TriagemLlmOptions"/> (é a mesma LLM, só que com uma chamada
/// de propósito diferente). Nunca vaza exceções do SDK para o chamador —
/// qualquer falha do fornecedor vira <c>ProviderUnavailable</c>, e
/// qualquer saída fora do esquema vira <c>InvalidOutput</c>.
///
/// Testes automatizados usam o SDK simulado, sem chamadas à AWS.
/// </summary>
public sealed class BedrockTriagemAttachmentVisionClient(
    IAmazonBedrockRuntime bedrockClient,
    IOptions<TriagemLlmOptions> options,
    ILogger<BedrockTriagemAttachmentVisionClient> logger) : ITriagemAttachmentVisionClient
{
    public async Task<TriagemAttachmentVisionResult> ClassificarAsync(
        TriagemAttachmentVisionRequest request, CancellationToken cancellationToken)
    {
        var opts = options.Value;

        var isPdf = string.Equals(request.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf && !TryMapImageFormat(request.ContentType, out _))
        {
            logger.LogWarning("Tipo de anexo não suportado. MediaId={MediaId}", request.MediaId);
            return TriagemAttachmentVisionResult.Invalid("tipo de anexo não suportado");
        }

        using var source = new MemoryStream(request.ContentBytes, writable: false);
        ContentBlock attachment;
        if (isPdf)
        {
            attachment = new ContentBlock
            {
                Document = new DocumentBlock
                {
                    Name = "anexo triagem",
                    Format = DocumentFormat.Pdf,
                    Source = new DocumentSource { Bytes = source },
                    Citations = new CitationsConfig { Enabled = true },
                },
            };
        }
        else
        {
            TryMapImageFormat(request.ContentType, out var format);
            attachment = new ContentBlock
            {
                Image = new ImageBlock { Format = format, Source = new ImageSource { Bytes = source } },
            };
        }

        var converseRequest = new ConverseRequest
        {
            ModelId = opts.ModelId,
            System = [new SystemContentBlock { Text = TriagemAttachmentVisionPromptBuilder.SystemPrompt }],
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content =
                    [
                        attachment,
                        new ContentBlock { Text = "Classifique este anexo conforme as instruções do sistema." },
                    ],
                },
            ],
            InferenceConfig = new InferenceConfiguration { MaxTokens = opts.MaxOutputTokens },
            ToolConfig = new ToolConfiguration
            {
                Tools = [TriagemAttachmentVisionToolSchema.BuildTool()],
                ToolChoice = new ToolChoice
                {
                    Tool = new SpecificToolChoice { Name = TriagemAttachmentVisionToolSchema.ToolName },
                },
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
            logger.LogWarning(
                "Chamada de visão ao Bedrock excedeu o tempo limite de {TimeoutSeconds}s (mídia {MediaId}, triagem {TriagemId}).",
                opts.TimeoutSeconds, request.MediaId, request.TriagemId);
            return TriagemAttachmentVisionResult.Unavailable("tempo limite excedido");
        }
        catch (AmazonBedrockRuntimeException exception)
        {
            logger.LogWarning(exception,
                "Falha do Bedrock ao classificar a mídia {MediaId} (triagem {TriagemId}, tenant {TenantId}).",
                request.MediaId, request.TriagemId, request.TenantId);
            return TriagemAttachmentVisionResult.Unavailable($"falha do fornecedor: {exception.GetType().Name}");
        }
        stopwatch.Stop();

        var usage = new TriagemLlmUsageInfo(
            opts.ModelId, response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0, stopwatch.Elapsed);

        var toolUse = response.Output?.Message?.Content?
            .FirstOrDefault(block => block.ToolUse is { Name: TriagemAttachmentVisionToolSchema.ToolName })?
            .ToolUse;
        if (toolUse is null)
        {
            return TriagemAttachmentVisionResult.Invalid("resposta não usou a ferramenta obrigatória", usage);
        }

        if (!TriagemAttachmentVisionToolResultParser.TryParse(toolUse.Input, out var payload, out var parseError))
        {
            return TriagemAttachmentVisionResult.Invalid(parseError ?? "saída fora do esquema", usage);
        }

        return TriagemAttachmentVisionResult.Success(payload!, usage);
    }

    private static bool TryMapImageFormat(string contentType, out ImageFormat format)
    {
        switch (contentType.ToLowerInvariant())
        {
            case "image/jpeg":
            case "image/jpg":
                format = ImageFormat.Jpeg;
                return true;
            case "image/png":
                format = ImageFormat.Png;
                return true;
            case "image/webp":
                format = ImageFormat.Webp;
                return true;
            default:
                format = default!;
                return false;
        }
    }
}
