using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Application.Services.Mentions;

public static class Mentions
{
    // Queues mention rows and UserMentioned events for the @usernames in content, to save with the caller's
    // next SaveChanges. Unknown usernames stay plain text; mentioning yourself, or someone already mentioned
    // in this post or comment, adds nothing.
    public static async Task AddMentionsAsync(this AppDbContext db, Guid authorId, Guid postId, Guid? commentId, string content,
        DateTime createdAt, CancellationToken cancellationToken)
    {
        var usernames = TextTokens.Mentions(content);
        if (usernames.Count == 0)
        {
            return;
        }

        var userIds = await db.Users
            .Where(u => usernames.Contains(u.Username) && u.Id != authorId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        var already = await db.Mentions
            .Where(m => m.PostId == postId && m.CommentId == commentId)
            .Select(m => m.MentionedUserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds.Except(already))
        {
            db.Mentions.Add(new MentionEntity
            {
                MentionedUserId = userId,
                AuthorId = authorId,
                PostId = postId,
                CommentId = commentId,
                CreatedAt = createdAt,
            });
            db.AddEvent(EventTypes.UserMentioned, postId.ToString(), authorId,
                new UserMentionedEvent { MentionedUserId = userId, PostId = postId, CommentId = commentId }, createdAt);
        }
    }
}
