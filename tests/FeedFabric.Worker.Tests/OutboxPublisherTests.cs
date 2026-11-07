using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Worker.Events.Outbox;

namespace FeedFabric.Worker.Tests;

[Collection("worker")]
public class OutboxPublisherTests(WorkerFixture fixture)
{
    [Fact]
    public async Task A_failing_message_holds_its_key_back_until_it_is_parked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = $"key-{Guid.NewGuid()}";
        Guid bad, good;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Clear what other tests left, so the batch is only these two
            await db.OutboxMessages.ExecuteDeleteAsync(cancellationToken);
            var now = DateTime.UtcNow;
            // No topic for this type, so it can never be published
            var first = new OutboxMessageEntity { Type = "NoSuchEvent", Payload = "{}", PartitionKey = key, CreatedAt = now };
            var second = new OutboxMessageEntity { Type = EventTypes.PostCreated, Payload = "{}", PartitionKey = key, CreatedAt = now.AddMilliseconds(1) };
            db.OutboxMessages.AddRange(first, second);
            await db.SaveChangesAsync(cancellationToken);
            (bad, good) = (first.Id, second.Id);
        }

        var publisher = new OutboxPublisher(fixture.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Producer,
            Options.Create(new OutboxOptions { MaxPublishAttempts = 2 }), TimeProvider.System, WorkerFixture.Logger<OutboxPublisher>());

        async Task<(OutboxMessageEntity Bad, OutboxMessageEntity Good)> ReadAsync()
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return (await db.OutboxMessages.SingleAsync(m => m.Id == bad, cancellationToken),
                await db.OutboxMessages.SingleAsync(m => m.Id == good, cancellationToken));
        }

        // First failure: the message behind it waits rather than overtaking it
        await publisher.PublishBatchAsync(cancellationToken);
        var (b, g) = await ReadAsync();
        Assert.Equal(1, b.Attempts);
        Assert.NotNull(b.LastError);
        Assert.Null(g.ProcessedAt);
        Assert.Equal(0, g.Attempts);

        // Second failure parks it
        await publisher.PublishBatchAsync(cancellationToken);
        (b, g) = await ReadAsync();
        Assert.Equal(2, b.Attempts);
        Assert.Null(g.ProcessedAt);

        // Parked messages are skipped, so the key carries on
        await publisher.PublishBatchAsync(cancellationToken);
        (b, g) = await ReadAsync();
        Assert.Null(b.ProcessedAt);
        Assert.Equal(2, b.Attempts);
        Assert.NotNull(g.ProcessedAt);
    }
}
