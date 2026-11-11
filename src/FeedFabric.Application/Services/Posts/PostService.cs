using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.DataTypes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Tags;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Posts;

public class PostService(AppDbContext db, TimeProvider timeProvider, IOptions<MediaOptions> mediaOptions, TextModerator moderator, TopicRules topics,
    DataTypeStore dataTypes)
    : IPostService
{
    public static readonly TimeSpan EditWindow = TimeSpan.FromHours(1);

    public async Task<PostResult> CreateAsync(string externalUserId, PostRequest request, CancellationToken cancellationToken)
    {
        var authorId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (authorId is null)
        {
            return new PostResult(PostResultStatus.UserNotFound);
        }

        // A post that's only data has no words to check
        var verdict = string.IsNullOrWhiteSpace(request.Content) ? null : await moderator.CheckAsync(request.Content, cancellationToken);
        if (verdict is { Action: ModerationAction.Block })
        {
            await RecordAsync(authorId.Value, verdict, request.Content, null, cancellationToken);
            return new PostResult(PostResultStatus.ContentViolation, Violation: verdict.Category);
        }

        var mediaIds = request.MediaIds?.Distinct().ToList() ?? [];
        var mediaStatus = await CheckMediaAsync(authorId.Value, mediaIds, cancellationToken);
        if (mediaStatus != PostResultStatus.Ok)
        {
            return new PostResult(mediaStatus);
        }

        var data = request.Data is { } requested ? await dataTypes.CheckAsync(requested.Type, requested.Value, cancellationToken) : null;
        if (DataProblem(data) is { } dataProblem)
        {
            return dataProblem;
        }

        var post = new PostEntity
        {
            AuthorId = authorId.Value,
            Content = request.Content,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            Status = verdict is { Action: ModerationAction.Hold } ? ContentStatus.Held : ContentStatus.Visible,
            DataType = request.Data?.Type,
            DataVersion = data?.Version,
            Data = data?.Json,
        };
        db.Posts.Add(post);
        if (verdict is not null)
        {
            // Saved with the post, so a held or flagged post is always in the review queue
            db.ModerationEvents.Add(ModerationEventEntity.For(post.AuthorId, "post", verdict, post.Content, post.Id, null, post.CreatedAt));
        }
        // Same SaveChanges as the post, so the event exists exactly when the post does
        db.AddEvent(EventTypes.PostCreated, post.Id.ToString(), post.AuthorId, new PostCreatedEvent
        {
            PostId = post.Id,
            AuthorId = post.AuthorId,
            CreatedAt = post.CreatedAt,
        }, post.CreatedAt);
        db.PostTags.AddRange(TextTokens.TagsFor(post.Id, post.Content, post.CreatedAt, (await topics.CurrentAsync(cancellationToken)).Matcher,
            request.Tags));
        db.PostMedia.AddRange(mediaIds.Select((id, i) => new PostMediaEntity { PostId = post.Id, MediaId = id, Position = i }));
        await db.AddMentionsAsync(post.AuthorId, post.Id, null, post.Content, post.CreatedAt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return PostResult.Ok(post);
    }

    public async Task<PostResult> EditAsync(string externalUserId, Guid postId, PostRequest request, CancellationToken cancellationToken)
    {
        var (status, post) = await FindOwnPostAsync(externalUserId, postId, cancellationToken);
        if (post is null)
        {
            return new PostResult(status);
        }

        if (post.RepostOfId is not null)
        {
            return new PostResult(PostResultStatus.IsRepost);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now - post.CreatedAt > EditWindow)
        {
            return new PostResult(PostResultStatus.EditWindowClosed);
        }

        // A post that's only data has no words to check
        var verdict = string.IsNullOrWhiteSpace(request.Content) ? null : await moderator.CheckAsync(request.Content, cancellationToken);
        if (verdict is { Action: ModerationAction.Block })
        {
            await RecordAsync(post.AuthorId, verdict, request.Content, post.Id, cancellationToken);
            return new PostResult(PostResultStatus.ContentViolation, Violation: verdict.Category);
        }

        var data = request.Data is { } requested ? await dataTypes.CheckAsync(requested.Type, requested.Value, cancellationToken) : null;
        if (DataProblem(data) is { } dataProblem)
        {
            return dataProblem;
        }
        if (data is not null)
        {
            post.DataType = request.Data!.Type;
            post.DataVersion = data.Version;
            post.Data = data.Json;
        }

        post.Content = request.Content;
        post.EditedAt = now;
        // An edit that needs a look holds or flags the post again, even if a reviewer approved the earlier text. A held
        // post stays held until reviewed, however it's edited.
        if (verdict is not null && post.Status == ContentStatus.Visible)
        {
            if (verdict.Action == ModerationAction.Hold)
            {
                post.Status = ContentStatus.Held;
            }
            db.ModerationEvents.Add(ModerationEventEntity.For(post.AuthorId, "post", verdict, post.Content, post.Id, null, now));
        }

        // New text, new tags; the app's tags stay unless the edit sets new ones. Mentions that were removed go; new ones
        // are added and announced.
        var oldTags = await db.PostTags.Where(t => t.PostId == post.Id).ToListAsync(cancellationToken);
        var appTags = request.Tags ?? [.. oldTags.Where(t => t.Source == TagSource.App).Select(t => t.Tag)];
        db.PostTags.RemoveRange(oldTags);
        db.PostTags.AddRange(TextTokens.TagsFor(post.Id, post.Content, post.CreatedAt, (await topics.CurrentAsync(cancellationToken)).Matcher, appTags));
        var usernames = TextTokens.Mentions(post.Content);
        db.Mentions.RemoveRange(await db.Mentions
            .Where(m => m.PostId == post.Id && m.CommentId == null
                && !db.Users.Any(u => u.Id == m.MentionedUserId && usernames.Contains(u.Username)))
            .ToListAsync(cancellationToken));
        await db.AddMentionsAsync(post.AuthorId, post.Id, null, post.Content, now, cancellationToken);
        db.AddEvent(EventTypes.PostUpdated, post.Id.ToString(), post.AuthorId, new PostCreatedEvent
        {
            PostId = post.Id,
            AuthorId = post.AuthorId,
            CreatedAt = post.CreatedAt,
        }, now);
        await db.SaveChangesAsync(cancellationToken);

        return PostResult.Ok(post);
    }

    public async Task<PostResult> DeleteAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var (status, post) = await FindOwnPostAsync(externalUserId, postId, cancellationToken);
        if (post is null)
        {
            return new PostResult(status);
        }

        post.DeletedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        return PostResult.Ok(post);
    }

    private static PostResult? DataProblem(DataCheckResult? data) => data?.Status switch
    {
        null or DataCheckStatus.Ok => null,
        DataCheckStatus.UnknownType => new PostResult(PostResultStatus.DataTypeUnknown),
        DataCheckStatus.TooLarge => new PostResult(PostResultStatus.DataTooLarge),
        _ => new PostResult(PostResultStatus.DataInvalid, DataErrors: data.Errors),
    };

    // A block is recorded on its own, since nothing else is saved
    private async Task RecordAsync(Guid userId, ModerationVerdict verdict, string text, Guid? postId, CancellationToken cancellationToken)
    {
        db.ModerationEvents.Add(ModerationEventEntity.For(userId, "post", verdict, text, postId, null, timeProvider.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(cancellationToken);
    }


    // Only the author's own images that passed scanning and aren't on another post can be attached
    private async Task<PostResultStatus> CheckMediaAsync(Guid authorId, List<Guid> mediaIds, CancellationToken cancellationToken)
    {
        if (mediaIds.Count == 0)
        {
            return PostResultStatus.Ok;
        }
        if (mediaIds.Count > mediaOptions.Value.MaxPerPost)
        {
            return PostResultStatus.TooManyMedia;
        }

        var media = await db.Media
            .Where(m => mediaIds.Contains(m.Id) && m.OwnerId == authorId && !db.PostMedia.Any(pm => pm.MediaId == m.Id))
            .Select(m => m.Status)
            .ToListAsync(cancellationToken);
        if (media.Count < mediaIds.Count)
        {
            return PostResultStatus.MediaNotFound;
        }
        return media.All(s => s == MediaStatus.Ready) ? PostResultStatus.Ok : PostResultStatus.MediaNotReady;
    }

    private async Task<(PostResultStatus Status, PostEntity? Post)> FindOwnPostAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null && p.Status != ContentStatus.Removed, cancellationToken);
        if (post is null)
        {
            return (PostResultStatus.NotFound, null);
        }

        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        return post.AuthorId == userId
            ? (PostResultStatus.Ok, post)
            : (PostResultStatus.Forbidden, null);
    }
}
