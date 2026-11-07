using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Users;
using SkiaSharp;
using StackExchange.Redis;

namespace FeedFabric.Worker.Tests;

[Collection("pipeline")]
public class PipelineTests(PipelineFixture pipeline)
{
    private async Task<Guid> PostAsync(UserEntity author, string content)
    {
        using var scope = pipeline.Api.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPostService>()
            .CreateAsync(author.ExternalUserId, new PostRequest { Content = content }, TestContext.Current.CancellationToken);
        Assert.Equal(PostResultStatus.Ok, result.Status);
        return result.Post!.Id;
    }

    [Fact]
    public async Task A_post_fans_out_to_followers_home_feeds()
    {
        Assert.SkipUnless(PipelineFixture.ModelsPresent, "Needs the moderation models (make models)");
        var author = await pipeline.UserAsync("author");
        var follower = await pipeline.UserAsync("follower");
        using (var scope = pipeline.Api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Follows.Add(new FollowEntity { FollowerId = follower.Id, FolloweeId = author.Id, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var postId = await PostAsync(author, "Morning run done, legs feel great");

        var redis = pipeline.Api.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await PipelineFixture.EventuallyAsync(
            async () => await redis.SortedSetScoreAsync(FeedKeys.ForUser(follower.Id), postId.ToString()) is not null, "the fan-out");
        using (var scope = pipeline.Api.CreateScope())
        {
            var feed = await scope.ServiceProvider.GetRequiredService<IHomeFeedService>()
                .GetHomeFeedAsync(follower.ExternalUserId, new HomeFeedQuery(), TestContext.Current.CancellationToken);
            Assert.NotNull(feed);
            Assert.StartsWith("redis", feed.Source);
            Assert.Contains(feed.Posts, p => p.Id == postId);
        }
    }

    [Fact]
    public async Task Likes_and_comments_reach_the_signals_the_ranker_reads()
    {
        Assert.SkipUnless(PipelineFixture.ModelsPresent, "Needs the moderation models (make models)");
        var cancellationToken = TestContext.Current.CancellationToken;
        var author = await pipeline.UserAsync("author");
        var reader = await pipeline.UserAsync("reader");
        var postId = await PostAsync(author, "Anyone else sleeping badly this week?");

        using (var scope = pipeline.Api.CreateScope())
        {
            Assert.Equal(LikeResultStatus.Ok, await scope.ServiceProvider.GetRequiredService<ILikeService>()
                .LikeAsync(reader.ExternalUserId, postId, cancellationToken));
            var comment = await scope.ServiceProvider.GetRequiredService<ICommentService>()
                .CreateAsync(reader.ExternalUserId, postId, new CommentRequest { Content = "Yes, every night" }, cancellationToken);
            Assert.Equal(CommentResultStatus.Ok, comment.Status);
        }

        var redis = pipeline.Api.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await PipelineFixture.EventuallyAsync(
            async () => await redis.SetContainsAsync(EngagementKeys.Likers(postId), reader.Id.ToString()), "the like to be counted");
        Assert.Equal(1, await redis.SetLengthAsync(EngagementKeys.Likers(postId)));
        // The like (1) and the comment (CommentHandler's weight) both raise the reader's affinity for the author
        await PipelineFixture.EventuallyAsync(
            async () => await redis.SortedSetScoreAsync(EngagementKeys.Affinity(reader.Id), author.Id.ToString()) > 1, "the comment to be counted");
    }

    [Fact]
    public async Task Reported_posts_are_held_then_approved_or_removed()
    {
        Assert.SkipUnless(PipelineFixture.ModelsPresent, "Needs the moderation models (make models)");
        var cancellationToken = TestContext.Current.CancellationToken;
        var author = await pipeline.UserAsync("author");
        var kept = await PostAsync(author, "My knee is still sore after the hike");
        var dropped = await PostAsync(author, "Selling cheap followers, message me");

        using var scope = pipeline.Api.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        foreach (var postId in new[] { kept, dropped })
        {
            for (var i = 0; i < 3; i++)
            {
                var reporter = await pipeline.UserAsync("reporter");
                Assert.Equal(ReportResultStatus.Ok, await reports.ReportPostAsync(reporter.ExternalUserId, postId, ReportReasons.Spam, cancellationToken));
            }
        }
        var review = scope.ServiceProvider.GetRequiredService<ReviewService>();
        var queue = await review.QueueAsync(cancellationToken);
        Assert.Contains(queue, item => item.PostId == kept);
        Assert.Contains(queue, item => item.PostId == dropped);

        Assert.Equal(ReviewResultStatus.Ok, await review.DecideAsync("moderator-1", kept, null, ReviewDecisions.Approved, cancellationToken));
        Assert.Equal(ReviewResultStatus.Ok, await review.DecideAsync("moderator-1", dropped, null, ReviewDecisions.Removed, cancellationToken));

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ContentStatus.Visible, (await db.Posts.AsNoTracking().SingleAsync(p => p.Id == kept, cancellationToken)).Status);
        Assert.Equal(ContentStatus.Removed, (await db.Posts.AsNoTracking().SingleAsync(p => p.Id == dropped, cancellationToken)).Status);
        Assert.DoesNotContain(await review.QueueAsync(cancellationToken), item => item.PostId == kept || item.PostId == dropped);
    }

    [Fact]
    public async Task An_upload_is_processed_into_a_ready_image()
    {
        Assert.SkipUnless(PipelineFixture.ModelsPresent, "Needs the nudity model (make models)");
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await pipeline.UserAsync("uploader");
        byte[] png;
        using (var bitmap = new SKBitmap(64, 48))
        {
            bitmap.Erase(SKColors.SteelBlue);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
        }

        Guid mediaId;
        using (var scope = pipeline.Api.CreateScope())
        {
            var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
            var upload = await media.CreateUploadAsync(owner.ExternalUserId, new CreateUploadRequest { ContentType = "image/png", Size = png.Length },
                cancellationToken);
            Assert.Equal(MediaResultStatus.Ok, upload.Status);
            mediaId = upload.Value!.MediaId;
            // What the client's PUT to the signed URL does
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = (await db.Media.AsNoTracking().SingleAsync(m => m.Id == mediaId, cancellationToken)).QuarantineKey;
            await scope.ServiceProvider.GetRequiredService<IBlobStorage>().PutAsync(key, new MemoryStream(png), "image/png", cancellationToken);
            Assert.Equal(MediaResultStatus.Ok, (await media.CompleteAsync(owner.ExternalUserId, mediaId, cancellationToken)).Status);
        }

        MediaEntity? processed = null;
        await PipelineFixture.EventuallyAsync(async () =>
        {
            using var scope = pipeline.Api.CreateScope();
            processed = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Media.AsNoTracking()
                .SingleAsync(m => m.Id == mediaId, cancellationToken);
            return processed.Status is MediaStatus.Ready or MediaStatus.Rejected;
        }, "the media to be processed");
        Assert.Equal(MediaStatus.Ready, processed!.Status);
        Assert.Equal(64, processed.Width);
        Assert.NotNull(processed.LargeKey);
    }
}
