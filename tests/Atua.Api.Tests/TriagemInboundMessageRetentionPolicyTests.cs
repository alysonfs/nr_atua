using Atua.Api.Domain.Triagens;
using Atua.Api.Infrastructure.Persistence;
using Atua.Api.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Atua.Api.Tests;

public class TriagemInboundMessageRetentionPolicyTests
{
    [Fact]
    public void NewInboundMessage_ExpiresFiveYearsAfterReceipt()
    {
        var receivedAtUtc = new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

        var message = NewMessage(receivedAtUtc);

        Assert.Equal(receivedAtUtc.AddYears(5), message.ExpiresAtUtc);
    }

    [Fact]
    public void ExpirationOnLeapDay_UsesLastValidDayOfFebruary()
    {
        var receivedAtUtc = new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.Zero);

        var message = NewMessage(receivedAtUtc);

        Assert.Equal(new DateTimeOffset(2029, 2, 28, 12, 0, 0, TimeSpan.Zero), message.ExpiresAtUtc);
    }

    [Fact]
    public void PurgeThreshold_IsOneYearAfterExpiration()
    {
        var expirationUtc = new DateTimeOffset(2031, 10, 18, 9, 30, 0, TimeSpan.Zero);

        var purgeThresholdUtc =
            TriagemInboundMessageRetentionPolicy.CalculatePurgeThresholdUtc(expirationUtc.AddYears(1));

        Assert.Equal(expirationUtc, purgeThresholdUtc);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void PurgeThreshold_OnlyIncludesMessagesAfterFullPostExpirationYear(
        int ticksFromPurgeDate,
        bool eligible)
    {
        var expirationUtc = new DateTimeOffset(2031, 10, 18, 9, 30, 0, TimeSpan.Zero);
        var now = expirationUtc.AddYears(1).AddTicks(ticksFromPurgeDate);

        var threshold = TriagemInboundMessageRetentionPolicy.CalculatePurgeThresholdUtc(now);

        Assert.Equal(eligible, expirationUtc <= threshold);
    }

    [Fact]
    public void Persistence_RequiresExpirationAndIndexesItForPurge()
    {
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql("Host=localhost;Database=atua;Username=atua")
            .Options;
        using var context = new AtuaDbContext(options);
        var entity = context.Model.FindEntityType(typeof(TriagemInboundMessage));

        Assert.NotNull(entity);
        var expiration = entity.FindProperty(nameof(TriagemInboundMessage.ExpiresAtUtc));
        Assert.NotNull(expiration);
        Assert.False(expiration.IsNullable);
        Assert.Equal("timestamp with time zone", expiration.GetColumnType());
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Count == 1 && index.Properties[0] == expiration);
    }

    [Fact]
    public void Migration_BackfillsExpirationInUtcBeforeRequiringIt()
    {
        var migration = new AddTriagemInboundMessageRetention();
        var operations = migration.UpOperations;

        var addColumn = Assert.IsType<AddColumnOperation>(operations[0]);
        Assert.Equal("ExpiresAtUtc", addColumn.Name);
        Assert.True(addColumn.IsNullable);
        var backfill = Assert.IsType<SqlOperation>(operations[1]);
        Assert.Contains(
            "((\"ReceivedAtUtc\" AT TIME ZONE 'UTC') + interval '5 years') AT TIME ZONE 'UTC'",
            backfill.Sql);
        var alterColumn = Assert.IsType<AlterColumnOperation>(operations[2]);
        Assert.Equal("ExpiresAtUtc", alterColumn.Name);
        Assert.False(alterColumn.IsNullable);
        var index = Assert.IsType<CreateIndexOperation>(operations[3]);
        Assert.Equal(["ExpiresAtUtc"], index.Columns);
    }

    private static TriagemInboundMessage NewMessage(DateTimeOffset receivedAtUtc) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "channel-message-id",
            "idempotency-key",
            Guid.NewGuid(),
            "15551234567",
            "direct",
            false,
            receivedAtUtc,
            "text",
            "message",
            ETriagemInboundMessageOutcome.SemTriagem,
            null,
            [],
            receivedAtUtc);
}
