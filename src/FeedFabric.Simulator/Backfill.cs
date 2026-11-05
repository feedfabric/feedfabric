using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Safety;
using StackExchange.Redis;

namespace FeedFabric.Simulator;

// Fills an empty platform with BackfillDays of history before the live simulation starts: threads from the
// conversation library posted on their own days and times, the comments arriving over the following hours, and likes
// from people who share the topic. Everything goes through the real services with a clock set to that moment, so
// moderation, tags, mentions, events, fan-out and affinity all happen as they would have. Only runs when there are no
// posts yet; rerunning the simulator keeps what's there.
public class Backfill(
    IServiceScopeFactory scopes,
    IOptions<SimulatorOptions> options,
    IOptions<MediaOptions> mediaOptions,
    TextModerator moderator,
    IConnectionMultiplexer redis,
    TimeProvider timeProvider,
    ILogger<Backfill> logger)
{
    public async Task RunAsync(int userCount, Random random, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using (var check = scopes.CreateScope())
        {
            if (settings.BackfillDays <= 0 || await check.ServiceProvider.GetRequiredService<AppDbContext>().Posts.AnyAsync(cancellationToken))
            {
                return;
            }
        }

        var library = Conversations.Library;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var clock = new SettableClock();
        var threads = 0;
        var comments = 0;
        var likes = 0;

        for (var day = settings.BackfillDays; day >= 1; day--)
        {
            var dayStart = now.Date.AddDays(-day);
            for (var t = 0; t < settings.BackfillThreadsPerDay; t++)
            {
                // A fresh scope per thread keeps EF's change tracker small
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var feedLog = new FeedLog(db, redis, clock);
                var posts = new PostService(db, clock, mediaOptions, moderator);
                var commentService = new CommentService(db, feedLog, moderator, clock);
                var likeService = new LikeService(db, feedLog, clock);

                var thread = library.Threads[random.Next(library.Threads.Count)];
                var persona = Persona.All.First(p => p.Key == thread.Persona);
                var author = UserWith(persona, userCount, random);
                // Busier in the morning and evening, as people are
                var at = dayStart.AddMinutes(random.NextDouble() < 0.6 ? random.Next(7 * 60, 9 * 60) + random.Next(0, 11 * 60) : random.Next(0, 24 * 60));
                clock.Set(at);
                var post = await posts.CreateAsync(External(author), new PostRequest { Content = thread.Post }, cancellationToken);
                if (post.Post is null)
                {
                    continue;
                }
                threads++;

                var created = new List<(Guid Id, string Author)>();
                foreach (var reply in thread.Replies)
                {
                    at = at.AddMinutes(random.Next(3, 180));
                    if (at > now)
                    {
                        break;
                    }
                    var writer = reply.Who switch
                    {
                        "op" => External(author),
                        "helper" => External(UserWith(persona, userCount, random, except: author)),
                        _ => External(random.Next(userCount) + 1),
                    };
                    // A reply to an earlier comment, or a fresh one if that comment didn't make it
                    var parent = reply.Parent is { } index && index < created.Count ? created[index].Id : (Guid?)null;
                    clock.Set(at);
                    var comment = await commentService.CreateAsync(writer, post.Post.Id, new CommentRequest { Content = reply.Text, ParentCommentId = parent }, cancellationToken);
                    created.Add((comment.Comment?.Id ?? Guid.Empty, writer));
                    comments += comment.Comment is null ? 0 : 1;
                }

                // Likes, mostly from people on the same topic, over the day after posting
                var likers = random.Next(settings.BackfillMinLikes, settings.BackfillMaxLikes + 1);
                for (var l = 0; l < likers; l++)
                {
                    var liker = random.NextDouble() < 0.7 ? UserWith(persona, userCount, random, except: author) : random.Next(userCount) + 1;
                    var likedAt = post.Post.CreatedAt.AddMinutes(random.Next(1, 24 * 60));
                    if (likedAt > now || liker == author)
                    {
                        continue;
                    }
                    clock.Set(likedAt);
                    await likeService.LikeAsync(External(liker), post.Post.Id, cancellationToken);
                    likes++;
                }
            }
            logger.LogInformation("Backfill: {Day} days ago done ({Threads} threads, {Comments} comments, {Likes} likes so far)", day, threads, comments, likes);
        }
        logger.LogInformation("Backfill finished: {Threads} threads, {Comments} comments and {Likes} likes over {Days} days", threads, comments, likes, settings.BackfillDays);
    }

    private static string External(int number) => $"{Simulation.UserPrefix}{number}";

    // A random user with this persona: users with the same persona are Persona.All.Length numbers apart
    private static int UserWith(Persona persona, int userCount, Random random, int except = 0)
    {
        var first = Array.IndexOf(Persona.All, persona) + 1;
        var count = (userCount - first) / Persona.All.Length + 1;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var number = first + Persona.All.Length * random.Next(count);
            if (number != except)
            {
                return number;
            }
        }
        return first == except ? first + Persona.All.Length : first;
    }

    // A clock the backfill moves to each post's, comment's and like's own moment
    private sealed class SettableClock : TimeProvider
    {
        private DateTimeOffset now;

        public void Set(DateTime utc) => now = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => now;
    }
}
