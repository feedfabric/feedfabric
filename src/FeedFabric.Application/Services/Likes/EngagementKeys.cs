namespace FeedFabric.Application.Services.Likes;

// Redis keys the worker's LikeHandler writes and the API reads
public static class EngagementKeys
{
    // Set of user IDs who like the post; its size is the like count
    public static string Likers(Guid postId) => $"post:{postId}:likers";

    // Sorted set per viewer: member = author ID, score = how many of their posts the viewer likes
    public static string Affinity(Guid userId) => $"affinity:{userId}";

    // Sorted set per viewer: member = tag, score = likes and comments on posts with that tag, weighted by tag confidence
    public static string Topics(Guid userId) => $"topics:{userId}";
}
