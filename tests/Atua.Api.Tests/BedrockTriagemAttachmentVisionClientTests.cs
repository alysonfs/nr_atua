using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using Atua.Api.Application.Triagens.Contracts;
using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Atua.Api.Tests;

public class BedrockTriagemAttachmentVisionClientTests
{
    private static BedrockTriagemAttachmentVisionClient CreateClient(IAmazonBedrockRuntime sdk) =>
        new(sdk, Options.Create(new TriagemLlmOptions { ModelId = "teste", MaxOutputTokens = 256 }),
            NullLogger<BedrockTriagemAttachmentVisionClient>.Instance);

    private static TriagemAttachmentVisionRequest Request(string contentType) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3], contentType);

    private static ConverseResponse Response() => new()
    {
        Usage = new TokenUsage { InputTokens = 100, OutputTokens = 20 },
        Output = new ConverseOutput
        {
            Message = new Message
            {
                Role = ConversationRole.Assistant,
                Content =
                [
                    new ContentBlock
                    {
                        ToolUse = new ToolUseBlock
                        {
                            Name = TriagemAttachmentVisionToolSchema.ToolName,
                            ToolUseId = "classificacao",
                            Input = Document.FromObject(new Dictionary<string, object?>
                            {
                                ["tipo"] = "NotaFiscal",
                                ["legivel"] = true,
                                ["confianca"] = 0.95,
                            }),
                        },
                    },
                ],
            },
        },
    };

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task ClassificarAsync_EnviaFormatoCorretoERegistraUso(string contentType)
    {
        var sdk = Substitute.For<IAmazonBedrockRuntime>();
        ConverseRequest? captured = null;
        byte[]? sentBytes = null;
        sdk.ConverseAsync(Arg.Do<ConverseRequest>(request =>
        {
            captured = request;
            var attachment = request.Messages[0].Content[0];
            sentBytes = (attachment.Document?.Source.Bytes ?? attachment.Image.Source.Bytes).ToArray();
        }), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Response()));

        var result = await CreateClient(sdk).ClassificarAsync(Request(contentType), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.Success, result.Outcome);
        Assert.Equal(ETriagemAttachmentVisionTipo.NotaFiscal, result.Payload!.Tipo);
        Assert.Equal(100, result.Usage!.InputTokens);
        Assert.Equal(new byte[] { 1, 2, 3 }, sentBytes);
        Assert.NotNull(captured);
        Assert.Equal(256, captured.InferenceConfig.MaxTokens);
        Assert.Equal(TriagemAttachmentVisionToolSchema.ToolName, captured.ToolConfig.ToolChoice.Tool.Name);
        var block = captured.Messages[0].Content[0];
        Assert.False(string.IsNullOrWhiteSpace(captured.Messages[0].Content[1].Text));
        if (contentType == "application/pdf")
        {
            Assert.Null(block.Image);
            Assert.Equal(DocumentFormat.Pdf, block.Document.Format);
            Assert.Equal("anexo triagem", block.Document.Name);
            Assert.True(block.Document.Citations.Enabled);
        }
        else
        {
            Assert.Null(block.Document);
            Assert.Equal(contentType.Split('/')[1], block.Image.Format.Value);
        }
    }

    [Fact]
    public async Task ClassificarAsync_PdfRejeitadoPeloProvedor_NaoValidaDocumento()
    {
        var sdk = Substitute.For<IAmazonBedrockRuntime>();
        sdk.ConverseAsync(Arg.Any<ConverseRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonBedrockRuntimeException("documento inválido ou protegido"));

        var result = await CreateClient(sdk).ClassificarAsync(Request("application/pdf"), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, result.Outcome);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ClassificarAsync_TipoNaoSuportado_NaoInvocaProvedor()
    {
        var sdk = Substitute.For<IAmazonBedrockRuntime>();

        var result = await CreateClient(sdk).ClassificarAsync(Request("video/mp4"), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.InvalidOutput, result.Outcome);
        await sdk.DidNotReceive().ConverseAsync(Arg.Any<ConverseRequest>(), Arg.Any<CancellationToken>());
    }
}
