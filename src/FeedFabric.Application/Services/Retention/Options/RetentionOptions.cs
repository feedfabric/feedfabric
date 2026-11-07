namespace FeedFabric.Application.Services.Retention;

// How long data is kept before the worker's retention job deletes it (docs/data-retention.md is the policy these
// implement). Shorten rather than lengthen: members write about their health, so keep only what has a use.
public class RetentionOptions
{
    public const string Section = "Retention";

    public bool Enabled { get; set; } = true;

    // How often the job runs; one worker instance runs it at a time
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    // Published outbox messages. Parked ones stay until someone deals with them.
    public TimeSpan PublishedOutbox { get; set; } = TimeSpan.FromDays(7);

    // The consumers' record of handled events, which stops a redelivered event being applied twice. Longer than an
    // event can come back: Kafka's retention, and the dead-letter topics' for replays.
    public TimeSpan ProcessedEvents { get; set; } = TimeSpan.FromDays(35);

    // One row per post served, the ranker's training data
    public TimeSpan FeedLog { get; set; } = TimeSpan.FromDays(180);

    // Decided moderation events, and blocks (which saved nothing). Undecided ones stay in the review queue.
    public TimeSpan ModerationEvents { get; set; } = TimeSpan.FromDays(365);

    // An upload URL issued but never used, or a ready photo never put on a post
    public TimeSpan AbandonedMedia { get; set; } = TimeSpan.FromDays(1);

    // Rejected uploads' records (their files are already gone)
    public TimeSpan RejectedMedia { get; set; } = TimeSpan.FromDays(30);

    // Posts this old leave the Redis impression and open totals
    public TimeSpan PostTotals { get; set; } = TimeSpan.FromDays(30);
}
