using Atua.Api.Infrastructure.Llm;
using Xunit;

namespace Atua.Api.Tests;

public sealed class TriagemLlmWhatsAppFormattingTests
{
    [Fact]
    public void NormalizeForWhatsApp_ConverteQuebrasEscapadasENegritoMarkdown()
    {
        var resultado = TriagemLlmToolResultParser.NormalizeForWhatsApp(
            "Obrigada!\\n\\n• **Há quanto tempo?**\\r\\nFim ");

        Assert.Equal("Obrigada!\n\n• *Há quanto tempo?*\nFim", resultado);
    }

    [Fact]
    public void NormalizeForWhatsApp_PreservaTextoJaFormatado() =>
        Assert.Equal("Olá\n*Endereço*", TriagemLlmToolResultParser.NormalizeForWhatsApp("Olá\n*Endereço*"));
}
