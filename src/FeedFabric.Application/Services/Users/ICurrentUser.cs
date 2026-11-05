namespace FeedFabric.Application.Services.Users;

public interface ICurrentUser
{
    // The user's ID in the host system, from its token (UserEntity.ExternalUserId)
    string ExternalUserId { get; }
}
