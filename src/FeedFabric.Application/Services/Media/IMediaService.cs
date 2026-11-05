namespace FeedFabric.Application.Services.Media;

public interface IMediaService
{
    // Issues a quarantine upload URL for one image
    Task<MediaResult<UploadResponse>> CreateUploadAsync(string externalUserId, CreateUploadRequest request, CancellationToken cancellationToken);

    // The client finished uploading: queues the image for checking. Owner only.
    Task<MediaResult<MediaResponse>> CompleteAsync(string externalUserId, Guid mediaId, CancellationToken cancellationToken);

    // Status and, once ready, URLs. Owner only.
    Task<MediaResult<MediaResponse>> GetAsync(string externalUserId, Guid mediaId, CancellationToken cancellationToken);

    // Ready images on each of the posts, in order, with fresh read URLs
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PostMediaResponse>>> ForPostsAsync(IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken);
}
