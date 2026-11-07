using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Retention;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Worker.Tests;

[Collection("worker")]
public class RetentionTests(WorkerFixture fixture)
{
    private sealed class RecordingStorage : IBlobStorage
    {
        public List<string> Deleted { get; } = [];

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Deleted.Add(key);
            return Task.CompletedTask;
        }

        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BlobDownload?> GetAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> GetUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Old_rows_go_and_recent_parked_or_undecided_ones_stay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var old = now.AddDays(-400);
        var recent = now.AddHours(-1);
        Guid Id(DateTime at) => Guid.CreateVersion7(new DateTimeOffset(at));

        var user = new UserEntity { Id = Id(old), ExternalUserId = $"u-{Guid.NewGuid()}", Username = $"u{Guid.NewGuid():N}"[..20] };
        OutboxMessageEntity Outbox(DateTime at, bool published, int attempts = 0) => new()
        {
            Id = Id(at), Type = "x", Payload = "{}", PartitionKey = "k", CreatedAt = at,
            ProcessedAt = published ? at : null, Attempts = attempts,
        };
        var oldPublished = Outbox(old, true);
        var oldParked = Outbox(old, false, attempts: 10);
        var recentPublished = Outbox(recent, true);
        var oldEvent = new ProcessedEventEntity { EventId = Id(old), Consumer = "c", ProcessedAt = old };
        var recentEvent = new ProcessedEventEntity { EventId = Id(recent), Consumer = "c", ProcessedAt = recent };
        FeedLogEntity Served(DateTime at) => new()
        {
            Id = Id(at), ViewerId = user.Id, PostId = Guid.NewGuid(), Feed = FeedNames.ForYou, Ranker = "heuristic", Features = "{}", ServedAt = at,
        };
        var oldServed = Served(old);
        var recentServed = Served(recent);
        ModerationEventEntity Moderation(string action, string? decision) => new()
        {
            Id = Id(old), UserId = user.Id, Action = action, Kind = "post", Category = ModerationCategories.Insult, CreatedAt = old,
            Decision = decision,
        };
        var oldBlocked = Moderation(ModerationActions.Blocked, null);
        var oldDecided = Moderation(ModerationActions.Held, ReviewDecisions.Approved);
        var oldUndecided = Moderation(ModerationActions.Held, null);
        MediaEntity Media(MediaStatus status, DateTime at) => new()
        {
            Id = Id(at), OwnerId = user.Id, Status = status, DeclaredContentType = "image/png", QuarantineKey = $"q/{Guid.NewGuid()}",
            CreatedAt = at,
        };
        var abandoned = Media(MediaStatus.AwaitingUpload, now.AddDays(-2));
        var rejected = Media(MediaStatus.Rejected, old);
        var uploading = Media(MediaStatus.AwaitingUpload, recent);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            db.OutboxMessages.AddRange(oldPublished, oldParked, recentPublished);
            db.ProcessedEvents.AddRange(oldEvent, recentEvent);
            db.FeedLog.AddRange(oldServed, recentServed);
            db.ModerationEvents.AddRange(oldBlocked, oldDecided, oldUndecided);
            db.Media.AddRange(abandoned, rejected, uploading);
            await db.SaveChangesAsync(cancellationToken);
        }

        var redis = fixture.Redis.GetDatabase();
        var oldPost = Id(now.AddDays(-60)).ToString();
        var newPost = Id(recent).ToString();
        await redis.SortedSetAddAsync(ImpressionKeys.PostTotals, [new(oldPost, 5), new(newPost, 3)]);
        await redis.SortedSetAddAsync(ImpressionKeys.OpenTotals, [new(oldPost, 1)]);
        var unexpiring = ImpressionKeys.Viewers(Guid.NewGuid());
        await redis.HyperLogLogAddAsync(unexpiring, "someone");

        var storage = new RecordingStorage();
        using (var scope = fixture.Services.CreateScope())
        {
            var service = new RetentionService(scope.ServiceProvider.GetRequiredService<AppDbContext>(), storage, fixture.Redis,
                Options.Create(new RetentionOptions()), TimeProvider.System, WorkerFixture.Logger<RetentionService>());
            await service.RunAsync(cancellationToken);
        }

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outbox = await db.OutboxMessages.Select(m => m.Id).ToListAsync(cancellationToken);
            Assert.DoesNotContain(oldPublished.Id, outbox);
            Assert.Contains(oldParked.Id, outbox);
            Assert.Contains(recentPublished.Id, outbox);

            var events = await db.ProcessedEvents.Select(e => e.EventId).ToListAsync(cancellationToken);
            Assert.DoesNotContain(oldEvent.EventId, events);
            Assert.Contains(recentEvent.EventId, events);

            var served = await db.FeedLog.Select(f => f.Id).ToListAsync(cancellationToken);
            Assert.DoesNotContain(oldServed.Id, served);
            Assert.Contains(recentServed.Id, served);

            var moderation = await db.ModerationEvents.Select(e => e.Id).ToListAsync(cancellationToken);
            Assert.DoesNotContain(oldBlocked.Id, moderation);
            Assert.DoesNotContain(oldDecided.Id, moderation);
            // Still in the review queue, however old
            Assert.Contains(oldUndecided.Id, moderation);

            var media = await db.Media.Select(m => m.Id).ToListAsync(cancellationToken);
            Assert.Equal([uploading.Id], media.Where(id => id == abandoned.Id || id == rejected.Id || id == uploading.Id));
            Assert.Contains(abandoned.QuarantineKey, storage.Deleted);
            Assert.Contains(rejected.QuarantineKey, storage.Deleted);
        }

        Assert.Null(await redis.SortedSetScoreAsync(ImpressionKeys.PostTotals, oldPost));
        Assert.Null(await redis.SortedSetScoreAsync(ImpressionKeys.OpenTotals, oldPost));
        Assert.Equal(3, await redis.SortedSetScoreAsync(ImpressionKeys.PostTotals, newPost));
        Assert.NotNull(await redis.KeyTimeToLiveAsync(unexpiring));
    }
}
