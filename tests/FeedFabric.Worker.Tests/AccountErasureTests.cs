using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Users;
using FeedFabric.Worker.Events.Accounts;

namespace FeedFabric.Worker.Tests;

[Collection("worker")]
public class AccountErasureTests(WorkerFixture fixture)
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

    private static UserEntity NewUser() => new() { ExternalUserId = $"x-{Guid.NewGuid()}", Username = $"u{Guid.NewGuid():N}"[..20] };

    [Fact]
    public async Task Erases_their_rows_redis_keys_and_files_and_leaves_other_people_alone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var gone = NewUser();
        var friend = NewUser();
        PostEntity theirPost, friendPost;
        MediaEntity photo;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.AddRange(gone, friend);
            await db.SaveChangesAsync(cancellationToken);
            theirPost = new PostEntity { AuthorId = gone.Id, Content = "", CreatedAt = now, DeletedAt = now };
            friendPost = new PostEntity { AuthorId = friend.Id, Content = "friend's", CreatedAt = now };
            db.Posts.AddRange(theirPost, friendPost);
            photo = new MediaEntity
            {
                OwnerId = gone.Id, Status = MediaStatus.Ready, DeclaredContentType = "image/png", QuarantineKey = "q/1",
                LargeKey = "media/1/large.png", ThumbnailKey = "media/1/thumb.png", CreatedAt = now,
            };
            db.Media.Add(photo);
            await db.SaveChangesAsync(cancellationToken);
            db.PostMedia.Add(new PostMediaEntity { PostId = theirPost.Id, MediaId = photo.Id });
            db.Follows.AddRange(new FollowEntity { FollowerId = friend.Id, FolloweeId = gone.Id }, new FollowEntity { FollowerId = gone.Id, FolloweeId = friend.Id });
            db.Likes.AddRange(new LikeEntity { UserId = gone.Id, PostId = friendPost.Id }, new LikeEntity { UserId = friend.Id, PostId = theirPost.Id });
            db.FeedLog.Add(new FeedLogEntity
            {
                ViewerId = gone.Id, PostId = friendPost.Id, Feed = FeedNames.ForYou, Ranker = "heuristic", Features = "{}", ServedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var redis = fixture.Redis.GetDatabase();
        await redis.SetAddAsync(EngagementKeys.Likers(friendPost.Id), [gone.Id.ToString(), friend.Id.ToString()]);
        await redis.SortedSetAddAsync(FeedKeys.ForUser(friend.Id), [new(theirPost.Id.ToString(), 1), new(friendPost.Id.ToString(), 2)]);
        await redis.SortedSetAddAsync(FeedKeys.ForUser(gone.Id), friendPost.Id.ToString(), 2);
        await redis.SortedSetAddAsync(EngagementKeys.Affinity(friend.Id), gone.Id.ToString(), 3);
        await redis.SortedSetAddAsync(EngagementKeys.Topics(gone.Id), "sleep", 1);
        await redis.StringSetAsync($"foryou:{gone.Id}:balanced", "{}");

        var storage = new RecordingStorage();
        using (var scope = fixture.Services.CreateScope())
        {
            var handler = new AccountErasureHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>(), fixture.Redis, storage,
                new EmbeddingStore(fixture.Redis), WorkerFixture.Logger<AccountErasureHandler>());
            var envelope = new EventEnvelope<UserDeletedEvent>
            {
                EventId = Guid.NewGuid(), Type = EventTypes.UserDeleted, OccurredAt = now, ActorId = gone.Id, CorrelationId = "test",
                Data = new UserDeletedEvent { UserId = gone.Id },
            };
            await handler.HandleAsync(envelope, cancellationToken);
            // Redelivered: nothing left to do, and no error
            await handler.HandleAsync(envelope, cancellationToken);
        }

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Follows.AnyAsync(f => f.FollowerId == gone.Id || f.FolloweeId == gone.Id, cancellationToken));
            Assert.False(await db.Likes.AnyAsync(l => l.UserId == gone.Id || l.PostId == theirPost.Id, cancellationToken));
            Assert.False(await db.FeedLog.AnyAsync(f => f.ViewerId == gone.Id, cancellationToken));
            Assert.False(await db.Media.AnyAsync(m => m.OwnerId == gone.Id, cancellationToken));
            Assert.True(await db.Posts.AnyAsync(p => p.Id == friendPost.Id && p.Content == "friend's", cancellationToken));
        }
        Assert.Equal(["q/1", "media/1/large.png", "media/1/thumb.png"], storage.Deleted.Distinct());

        Assert.False(await redis.SetContainsAsync(EngagementKeys.Likers(friendPost.Id), gone.Id.ToString()));
        Assert.True(await redis.SetContainsAsync(EngagementKeys.Likers(friendPost.Id), friend.Id.ToString()));
        Assert.Null(await redis.SortedSetScoreAsync(FeedKeys.ForUser(friend.Id), theirPost.Id.ToString()));
        Assert.NotNull(await redis.SortedSetScoreAsync(FeedKeys.ForUser(friend.Id), friendPost.Id.ToString()));
        Assert.Null(await redis.SortedSetScoreAsync(EngagementKeys.Affinity(friend.Id), gone.Id.ToString()));
        Assert.False(await redis.KeyExistsAsync(FeedKeys.ForUser(gone.Id)));
        Assert.False(await redis.KeyExistsAsync(EngagementKeys.Topics(gone.Id)));
        Assert.False(await redis.KeyExistsAsync($"foryou:{gone.Id}:balanced"));
    }
}
