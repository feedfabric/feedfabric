namespace FeedFabric.Application.Services.Favourites;

// UserId has marked FavouriteId as a favourite: their posts are always considered for UserId's For You and lifted by
// the favourite boost, and make up UserId's favourites feed. The pair is the key, so adding twice is a no-op.
public class FavouriteEntity
{
    public required Guid UserId { get; set; }

    public required Guid FavouriteId { get; set; }

    public DateTime CreatedAt { get; set; }
}
