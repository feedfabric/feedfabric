namespace FeedFabric.Application.Services;

public abstract class EntityBase
{
    // Left empty on create; Npgsql fills in a time-ordered UUIDv7 when the entity is added
    public Guid Id { get; set; }
}
