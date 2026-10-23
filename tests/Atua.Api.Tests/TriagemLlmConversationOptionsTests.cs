using Atua.Api.Application.Triagens;
using Xunit;

namespace Atua.Api.Tests;

public sealed class TriagemLlmConversationOptionsTests
{
    private static readonly Guid TenantId = Guid.Parse("01a0888b-acd8-773b-93b7-7362973d7ea8");

    private static TriagemLlmConversationOptions Options() => new()
    {
        ConversationEnabled = true,
        AllowedTenantIds = [TenantId],
        AllowedPhones = ["+5584994241248"],
    };

    [Theory]
    [InlineData("5584994241248")]
    [InlineData("558494241248")]
    [InlineData("+55 (84) 99424-1248")]
    public void IsEligible_AceitaTelefoneComOuSemNonoDigito(string senderPhone) =>
        Assert.True(Options().IsEligible(TenantId, senderPhone));

    [Fact]
    public void IsEligible_RejeitaOutroTelefone() =>
        Assert.False(Options().IsEligible(TenantId, "558494243623"));
}
