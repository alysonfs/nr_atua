using Atua.Api.Domain.Triagens;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de domínio de <see cref="TriagemChannelCommand"/> (ADR-042):
/// transições de claim/complete, limite de falhas transitórias e
/// invariantes de construção.
/// </summary>
public class TriagemChannelCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static TriagemChannelCommand NewCommand() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "modelo-contato-inicial", 1, "conteúdo renderizado", "chave-idempotencia", Guid.NewGuid(), Now);

    [Fact]
    public void Construtor_EstadoInicialEhPendingComAttemptCountZero()
    {
        var command = NewCommand();

        Assert.Equal(ETriagemChannelCommandStatus.Pending, command.Status);
        Assert.Equal(0, command.AttemptCount);
        Assert.Null(command.ClaimedAtUtc);
        Assert.Null(command.ClaimExpiresAtUtc);
        Assert.Null(command.CompletedAtUtc);
        Assert.Null(command.LastErrorCode);
        Assert.Equal(Now, command.CreatedAtUtc);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TenantIdVazio_FalhaExplicitamente(string tenantIdText)
    {
        var tenantId = Guid.Parse(tenantIdText);

        Assert.Throws<ArgumentException>(() => new TriagemChannelCommand(
            Guid.NewGuid(), tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "template", 1, "conteúdo", "chave", Guid.NewGuid(), Now));
    }

    [Fact]
    public void IntegrationIdVazio_FalhaExplicitamente()
    {
        Assert.Throws<ArgumentException>(() => new TriagemChannelCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(),
            "template", 1, "conteúdo", "chave", Guid.NewGuid(), Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TemplateIdInvalido_FalhaExplicitamente(string? templateId)
    {
        Assert.Throws<ArgumentException>(() => new TriagemChannelCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            templateId!, 1, "conteúdo", "chave", Guid.NewGuid(), Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IdempotencyKeyInvalida_FalhaExplicitamente(string? idempotencyKey)
    {
        Assert.Throws<ArgumentException>(() => new TriagemChannelCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "template", 1, "conteúdo", idempotencyKey!, Guid.NewGuid(), Now));
    }

    [Fact]
    public void MarkCompletedSuccess_ExigeClaimedENaoPermiteSemClaim()
    {
        var command = NewCommand();

        var exception = Assert.Throws<InvalidOperationException>(() => command.MarkCompletedSuccess(Now));
        Assert.Contains("Status=Claimed", exception.Message);
    }

    [Fact]
    public void MarkCompletedSuccess_AposClaim_ConcluiComoCompleted()
    {
        var command = NewCommand();
        command.Claim(Now, Now.AddSeconds(60));

        command.MarkCompletedSuccess(Now.AddSeconds(5));

        Assert.Equal(ETriagemChannelCommandStatus.Completed, command.Status);
        Assert.Equal(Now.AddSeconds(5), command.CompletedAtUtc);
    }

    [Fact]
    public void MarkCompletedPermanentFailure_AposClaim_ConcluiComoFailedComErrorCode()
    {
        var command = NewCommand();
        command.Claim(Now, Now.AddSeconds(60));

        command.MarkCompletedPermanentFailure("numero-invalido", Now.AddSeconds(5));

        Assert.Equal(ETriagemChannelCommandStatus.Failed, command.Status);
        Assert.Equal("numero-invalido", command.LastErrorCode);
        Assert.Equal(Now.AddSeconds(5), command.CompletedAtUtc);
    }

    [Fact]
    public void RecordTransientFailure_ExigeClaimed()
    {
        var command = NewCommand();

        Assert.Throws<InvalidOperationException>(() => command.RecordTransientFailure("timeout", Now));
    }

    [Fact]
    public void RecordTransientFailure_AbaixoDoLimite_VoltaParaPendingLimpandoALease()
    {
        var command = NewCommand();
        command.Claim(Now, Now.AddSeconds(60));

        command.RecordTransientFailure("timeout-provedor", Now.AddSeconds(5));

        Assert.Equal(ETriagemChannelCommandStatus.Pending, command.Status);
        Assert.Equal(1, command.AttemptCount);
        Assert.Equal("timeout-provedor", command.LastErrorCode);
        // A chave de idempotência nunca muda em retentativa (ADR-039/ADR-042).
        Assert.Equal("chave-idempotencia", command.IdempotencyKey);
        Assert.Null(command.ClaimedAtUtc);
        Assert.Null(command.ClaimExpiresAtUtc);
        Assert.Null(command.CompletedAtUtc);
    }

    [Fact]
    public void RecordTransientFailure_AteAQuintaFalha_TornaSeFailedTerminalPreservandoAChave()
    {
        var command = NewCommand();
        var originalKey = command.IdempotencyKey;

        for (var attempt = 1; attempt <= TriagemChannelCommand.MaxTransientFailures; attempt++)
        {
            command.Claim(Now, Now.AddSeconds(60));
            command.RecordTransientFailure("timeout-provedor", Now.AddSeconds(attempt));
        }

        Assert.Equal(ETriagemChannelCommandStatus.Failed, command.Status);
        Assert.Equal(TriagemChannelCommand.MaxTransientFailures, command.AttemptCount);
        Assert.NotNull(command.CompletedAtUtc);
        Assert.Equal(originalKey, command.IdempotencyKey);
    }

    [Fact]
    public void RecordTransientFailure_QuartaFalha_PermaneceReclamavelComoPending()
    {
        var command = NewCommand();

        for (var attempt = 1; attempt < TriagemChannelCommand.MaxTransientFailures; attempt++)
        {
            command.Claim(Now, Now.AddSeconds(60));
            command.RecordTransientFailure("timeout-provedor", Now.AddSeconds(attempt));
        }

        Assert.Equal(ETriagemChannelCommandStatus.Pending, command.Status);
        Assert.Equal(TriagemChannelCommand.MaxTransientFailures - 1, command.AttemptCount);
        Assert.Null(command.CompletedAtUtc);
    }

    [Fact]
    public void Claim_MarcaClaimedComALeaseInformada()
    {
        var command = NewCommand();
        var claimedAt = Now;
        var expiresAt = Now.AddSeconds(60);

        command.Claim(claimedAt, expiresAt);

        Assert.Equal(ETriagemChannelCommandStatus.Claimed, command.Status);
        Assert.Equal(claimedAt, command.ClaimedAtUtc);
        Assert.Equal(expiresAt, command.ClaimExpiresAtUtc);
    }
}
