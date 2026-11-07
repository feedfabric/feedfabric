namespace FeedFabric.Application.Services.Users;

public class FeedUserResponse
{
    public required string Id {get;set;}

    public required string ExternalUserId {get;set;}

    public required string Username {get;set;}

    public string? AvatarUrl {get;set;}

    public UserStatus Status {get;set;}

    public bool IsVerified {get;set;}

    public DateTime SyncedAt {get;set;}
}
