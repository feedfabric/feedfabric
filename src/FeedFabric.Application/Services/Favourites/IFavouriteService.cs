using FeedFabric.Application.Services.Feed;

namespace FeedFabric.Application.Services.Favourites;

public interface IFavouriteService
{
    Task<IReadOnlyList<FavouriteResponse>?> ListAsync(string externalUserId, CancellationToken cancellationToken);

    Task<FavouriteResultStatus> AddAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken);

    Task<FavouriteResultStatus> RemoveAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken);

    Task<HomeFeedResponse?> FeedAsync(string externalUserId, HomeFeedQuery query, CancellationToken cancellationToken);
}

public enum FavouriteResultStatus
{
    Ok,
    UserNotFound,
    TargetNotFound,
    CannotFavouriteSelf,
    // Already FavouriteService.MaxFavourites
    LimitReached,
}

public class FavouriteResponse
{
    public required string ExternalUserId { get; set; }

    public required string Username { get; set; }

    public string? DisplayName { get; set; }

    public string? AvatarUrl { get; set; }

    public DateTime AddedAt { get; init; }
}
