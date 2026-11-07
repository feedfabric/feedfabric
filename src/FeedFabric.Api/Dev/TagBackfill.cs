using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Api.Dev;

// Development only: tags posts created before tagging existed, so tag pages and trending cover them too.
// Mentions aren't backfilled: nobody wrote any before they were linked.
public static class TagBackfill
{
    private const int BatchSize = 2000;

    public static async Task RunAsync(AppDbContext db, TopicRules rules, ILogger logger)
    {
        var topics = (await rules.CurrentAsync(CancellationToken.None)).Matcher;
        var tagged = 0;
        // Keyset paging, so posts whose wording matches no tag are passed over rather than picked up again
        var after = Guid.Empty;
        while (true)
        {
            var from = after;
            var posts = await db.Posts.AsNoTracking()
                .Where(p => p.Id > from && !db.PostTags.Any(t => t.PostId == p.Id))
                .OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.Content, p.CreatedAt })
                .Take(BatchSize)
                .ToListAsync();
            if (posts.Count == 0)
            {
                break;
            }

            var tags = posts.SelectMany(p => TextTokens.TagsFor(p.Id, p.Content, p.CreatedAt, topics)).ToList();
            db.PostTags.AddRange(tags);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            tagged += posts.Count;
            after = posts[^1].Id;
        }

        if (tagged > 0)
        {
            logger.LogInformation("Tagged {Count} existing posts", tagged);
        }
    }
}
