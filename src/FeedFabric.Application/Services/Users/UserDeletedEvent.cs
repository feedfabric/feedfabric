namespace FeedFabric.Application.Services.Users;

// Only the ID: the account's details are already gone by the time this is published
public class UserDeletedEvent
{
    public required Guid UserId { get; set; }
}
