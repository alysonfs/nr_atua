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

/// <summary>
/// Testes de <see cref="BedrockTriagemLlmClient"/> com
/// <see cref="IAmazonBedrockRuntime"/> simulado (NSubstitute) — nenhuma
/// chamada real à AWS é feita.
/// </summary>
public class BedrockTriagemLlmClientTests
{
    private static TriagemLlmRequest BuildRequest() => new(
        TenantId: Guid.NewGuid(),
        TriagemId: Guid.NewGuid(),
        TenantNomeComercial: "Posto Teste",
        Produto: "Ar-condicionado Split",
        Marca: "Midea",
        JaApresentouIdentidade: true,
        ChecklistState: [new TriagemLlmChecklistItemState(ETriagemChecklistItem.DescricaoDefeito, false)],
        HistoricoConversa: [new TriagemLlmConversationTurn(ETriagemLlmConversationRole.Consumidor, "não gela")],
        AnexosRecebidos: []);

    private static BedrockTriagemLlmClient BuildClient(
        IAmazonBedrockRuntime bedrockClient, TriagemLlmOptions? options = null) =>
        new(bedrockClient, Options.Create(options ?? new TriagemLlmOptions
        {
            Enabled = true,
            Region = "sa-east-1",
            ModelId = "modelo-de-teste",
            MaxOutputTokens = 256,
            TimeoutSeconds = 5,
        }), NullLogger<BedrockTriagemLlmClient>.Instance);

    private static ConverseResponse BuildSuccessResponse(Dictionary<string, object?> toolInput) => new()
    {
        StopReason = StopReason.Tool_use,
        Usage = new TokenUsage { InputTokens = 123, OutputTokens = 45 },
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
                            Name = TriagemLlmToolSchema.ToolName,
                            ToolUseId = "tool-1",
                            Input = Document.FromObject(toolInput),
                        },
                    },
                ],
            },
        },
    };

    private static Dictionary<string, object?> ValidToolInput() => new()
    {
        ["respostaAoConsumidor"] = "Pode me enviar a foto da nota fiscal?",
        ["camposEstruturados"] = new Dictionary<string, object?>(),
        ["itensAtendidos"] = new List<object?>(),
        ["sinalDesvio"] = new Dictionary<string, object?> { ["detectado"] = false },
    };

    [Fact]
    public async Task MapeiaRespostaValidaEUsoDeTokensCorretamente()
    {
        var bedrockClient = Substitute.For<IAmazonBedrockRuntime>();
        bedrockClient.ConverseAsync(Arg.Any<ConverseRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BuildSuccessResponse(ValidToolInput())));
        var client = BuildClient(bedrockClient);

        var result = await client.ConduzirAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.Success, result.Outcome);
        Assert.Equal("Pode me enviar a foto da nota fiscal?", result.Payload!.RespostaAoConsumidor);
        Assert.NotNull(result.Usage);
        Assert.Equal("modelo-de-teste", result.Usage!.ModelId);
        Assert.Equal(123, result.Usage.InputTokens);
        Assert.Equal(45, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task EnviaModelIdRegiaoETeuToolChoiceConfiguradosParaOSdk()
    {
        var bedrockClient = Substitute.For<IAmazonBedrockRuntime>();
        ConverseRequest? capturedRequest = null;
        bedrockClient.ConverseAsync(Arg.Do<ConverseRequest>(req => capturedRequest = req), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BuildSuccessResponse(ValidToolInput())));
        var client = BuildClient(bedrockClient);

        await client.ConduzirAsync(BuildRequest(), CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("modelo-de-teste", capturedRequest!.ModelId);
        Assert.Equal(256, capturedRequest.InferenceConfig.MaxTokens);
        Assert.Equal(TriagemLlmToolSchema.ToolName, capturedRequest.ToolConfig.ToolChoice.Tool.Name);
        Assert.Single(capturedRequest.ToolConfig.Tools);
        Assert.NotEmpty(capturedRequest.System);
        Assert.NotEmpty(capturedRequest.Messages);
        Assert.Equal(ConversationRole.User, capturedRequest.Messages[^1].Role);
    }

    [Fact]
    public async Task RetornaInvalidoQuandoRespostaNaoUsaAFerramenta()
    {
        var bedrockClient = Substitute.For<IAmazonBedrockRuntime>();
        var response = new ConverseResponse
        {
            StopReason = StopReason.End_turn,
            Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5 },
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Role = ConversationRole.Assistant,
                    Content = [new ContentBlock { Text = "resposta em texto livre, sem tool use" }],
                },
            },
        };
        bedrockClient.ConverseAsync(Arg.Any<ConverseRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        var client = BuildClient(bedrockClient);

        var result = await client.ConduzirAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.InvalidOutput, result.Outcome);
        Assert.NotNull(result.Usage);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetornaInvalidoQuandoSaidaDaFerramentaNaoSeguoEsquema()
    {
        var bedrockClient = Substitute.For<IAmazonBedrockRuntime>();
        var toolInputSemRespostaAoConsumidor = new Dictionary<string, object?>
        {
            ["camposEstruturados"] = new Dictionary<string, object?>(),
            ["itensAtendidos"] = new List<object?>(),
            ["sinalDesvio"] = new Dictionary<string, object?> { ["detectado"] = false },
        };
        bedrockClient.ConverseAsync(Arg.Any<ConverseRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BuildSuccessResponse(toolInputSemRespostaAoConsumidor)));
        var client = BuildClient(bedrockClient);

        var result = await client.ConduzirAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.InvalidOutput, result.Outcome);
        Assert.NotNull(result.Usage);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task RetornaIndisponivelQuandoOSdkLancaExcecaoDoBedrock()
    {
        var bedrockClient = Substitute.For<IAmazonBedrockRuntime>();
        bedrockClient.ConverseAsync(Arg.Any<ConverseRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ThrottlingException("limite de taxa excedido"));
        var client = BuildClient(bedrockClient);

        var result = await client.ConduzirAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, result.Outcome);
        Assert.Null(result.Payload);
        Assert.Null(result.Usage);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task RetornaIndisponivelQuandoChamadaExcedeOTempoLimite()
    {
        var bedrockClient = Substitute.For<IAmazonBedrockRuntime>();
        bedrockClient.ConverseAsync(Arg.Any<ConverseRequest>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                return BuildSuccessResponse(ValidToolInput());
            });
        var client = BuildClient(bedrockClient, new TriagemLlmOptions
        {
            Enabled = true,
            Region = "sa-east-1",
            ModelId = "modelo-de-teste",
            MaxOutputTokens = 256,
            TimeoutSeconds = 1,
        });

        var result = await client.ConduzirAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ETriagemLlmOutcome.ProviderUnavailable, result.Outcome);
        Assert.Equal("tempo limite excedido", result.ErrorReason);
    }
}
