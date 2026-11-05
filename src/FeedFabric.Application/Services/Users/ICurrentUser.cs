namespace FeedFabric.Application.Services.Users;

public interface ICurrentUser
{
    // The JWT "sub" claim: the user's ID in the external system (UserEntity.ExternalUserId)
    string ExternalUserId { get; }
}
