using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Media;

public class MediaService(AppDbContext db, IBlobStorage storage, IOptions<MediaOptions> options, TimeProvider timeProvider) : IMediaService
{
    public async Task<MediaResult<UploadResponse>> CreateUploadAsync(string externalUserId, CreateUploadRequest request, CancellationToken cancellationToken)
    {
        var ownerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (ownerId is null)
        {
            return new(MediaResultStatus.UserNotFound);
        }
        if (!MediaOptions.AllowedContentTypes.Contains(request.ContentType))
        {
            return new(MediaResultStatus.TypeNotAllowed);
        }
        if (request.Size > options.Value.MaxUploadBytes)
        {
            return new(MediaResultStatus.TooLarge);
        }

        var contentType = request.ContentType.ToLowerInvariant();
        var media = new MediaEntity
        {
            OwnerId = ownerId.Value,
            Status = MediaStatus.AwaitingUpload,
            DeclaredContentType = contentType,
            QuarantineKey = "",
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.Media.Add(media);
        media.QuarantineKey = MediaKeys.Quarantine(media.OwnerId, media.Id);
        await db.SaveChangesAsync(cancellationToken);

        var lifetime = options.Value.UploadUrlLifetime;
        return new(MediaResultStatus.Ok, new UploadResponse
        {
            MediaId = media.Id,
            UploadUrl = await storage.GetUploadUrlAsync(media.QuarantineKey, contentType, lifetime, cancellationToken),
            ContentType = contentType,
            ExpiresAt = timeProvider.GetUtcNow().Add(lifetime),
        });
    }

    public async Task<MediaResult<MediaResponse>> CompleteAsync(string externalUserId, Guid mediaId, CancellationToken cancellationToken)
    {
        var (status, media) = await FindOwnAsync(externalUserId, mediaId, cancellationToken);
        if (media is null)
        {
            return new(status);
        }
        if (media.Status != MediaStatus.AwaitingUpload)
        {
            return new(MediaResultStatus.WrongState, await ToResponseAsync(media, cancellationToken));
        }

        // Same SaveChanges as the status change, so the image is checked exactly once it's marked as processing
        media.Status = MediaStatus.Processing;
        db.AddEvent(EventTypes.MediaUploaded, media.Id.ToString(), media.OwnerId,
            new MediaUploadedEvent { MediaId = media.Id, OwnerId = media.OwnerId }, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return new(MediaResultStatus.Ok, await ToResponseAsync(media, cancellationToken));
    }

    public async Task<MediaResult<MediaResponse>> GetAsync(string externalUserId, Guid mediaId, CancellationToken cancellationToken)
    {
        var (status, media) = await FindOwnAsync(externalUserId, mediaId, cancellationToken);
        return media is null ? new(status) : new(MediaResultStatus.Ok, await ToResponseAsync(media, cancellationToken));
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<PostMediaResponse>>> ForPostsAsync(IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if (postIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<PostMediaResponse>>();
        }

        var rows = await db.PostMedia.AsNoTracking()
            .Where(pm => postIds.Contains(pm.PostId))
            .Join(db.Media.Where(m => m.Status == MediaStatus.Ready), pm => pm.MediaId, m => m.Id, (pm, m) => new { pm.PostId, pm.Position, m })
            .OrderBy(x => x.PostId).ThenBy(x => x.Position)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, IReadOnlyList<PostMediaResponse>>();
        foreach (var group in rows.GroupBy(r => r.PostId))
        {
            var items = new List<PostMediaResponse>();
            foreach (var row in group)
            {
                items.Add(new PostMediaResponse
                {
                    Id = row.m.Id,
                    Url = await storage.GetReadUrlAsync(row.m.LargeKey!, options.Value.ReadUrlLifetime, cancellationToken),
                    ThumbnailUrl = await storage.GetReadUrlAsync(row.m.ThumbnailKey!, options.Value.ReadUrlLifetime, cancellationToken),
                    Width = row.m.Width ?? 0,
                    Height = row.m.Height ?? 0,
                });
            }
            result[group.Key] = items;
        }
        return result;
    }

    private async Task<(MediaResultStatus Status, MediaEntity? Media)> FindOwnAsync(string externalUserId, Guid mediaId, CancellationToken cancellationToken)
    {
        var ownerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (ownerId is null)
        {
            return (MediaResultStatus.UserNotFound, null);
        }

        // Someone else's media is reported as missing, so IDs can't be probed
        var media = await db.Media.SingleOrDefaultAsync(m => m.Id == mediaId && m.OwnerId == ownerId, cancellationToken);
        return media is null ? (MediaResultStatus.NotFound, null) : (MediaResultStatus.Ok, media);
    }

    private async Task<MediaResponse> ToResponseAsync(MediaEntity media, CancellationToken cancellationToken)
    {
        var ready = media.Status == MediaStatus.Ready;
        return new MediaResponse
        {
            Id = media.Id,
            Status = MediaStatusNames.Of(media.Status),
            RejectionReason = media.RejectionReason,
            Width = media.Width,
            Height = media.Height,
            Url = ready ? await storage.GetReadUrlAsync(media.LargeKey!, options.Value.ReadUrlLifetime, cancellationToken) : null,
            ThumbnailUrl = ready ? await storage.GetReadUrlAsync(media.ThumbnailKey!, options.Value.ReadUrlLifetime, cancellationToken) : null,
        };
    }
}
