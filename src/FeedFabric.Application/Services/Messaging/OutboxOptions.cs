namespace FeedFabric.Application.Services.Messaging;

public class OutboxOptions
{
    public const string Section = "Outbox";

    // Failed publishes before a message is parked: left unpublished with its LastError, skipped from then on, and
    // counted on the admin panel's pipeline. Unpark it by setting Attempts back to 0 (docs/runbooks.md).
    public int MaxPublishAttempts { get; set; } = 10;
}

public static class OutboxQueries
{
    // Waiting to be published, not parked
    public static IQueryable<OutboxMessageEntity> Waiting(this IQueryable<OutboxMessageEntity> messages, int maxPublishAttempts) =>
        messages.Where(o => o.ProcessedAt == null && o.Attempts < maxPublishAttempts);

    public static IQueryable<OutboxMessageEntity> Parked(this IQueryable<OutboxMessageEntity> messages, int maxPublishAttempts) =>
        messages.Where(o => o.ProcessedAt == null && o.Attempts >= maxPublishAttempts);
}
