namespace FeedFabric.Application.Services.Media;

// Blob keys never contain anything the client chose, only IDs
public static class MediaKeys
{
    public static string Quarantine(Guid ownerId, Guid mediaId) => $"quarantine/{ownerId}/{mediaId}";

    public static string Large(Guid mediaId, string extension) => $"media/{mediaId}/large.{extension}";

    public static string Thumbnail(Guid mediaId, string extension) => $"media/{mediaId}/thumb.{extension}";
}
