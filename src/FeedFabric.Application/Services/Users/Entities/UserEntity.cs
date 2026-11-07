namespace FeedFabric.Application.Services.Users;

public class UserEntity : EntityBase
{
    public required string ExternalUserId { get; set; }

    // Unique and @mention-able; from the host's token when FeedFabric creates the user
    public required string Username { get; set; }

    public string? DisplayName { get; set; }

    public string? AvatarUrl { get; set; }

    public UserStatus Status { get; set; }

    public bool IsVerified { get; set; }

    public DateTime SyncedAt { get; set; }
}