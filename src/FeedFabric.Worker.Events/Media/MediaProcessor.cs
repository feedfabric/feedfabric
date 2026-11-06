using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Worker.Events.Media;

// Checks an upload in quarantine and either publishes a rebuilt copy or rejects it. In order: size, malware scan,
// real file type from its bytes, decode and rebuild, then the nudity check on the rebuilt image. Every upload is scanned, even ones about to fail the type
// check, so attacks are always logged; and the scan runs before any image decoder touches the file. There is no path
// to Ready that skips it while scanning is on (MalwareScanning:Enabled): if the scanner can't answer, this throws, the
// consumer retries, and the image stays in quarantine.
public class MediaProcessor(
    AppDbContext db,
    IBlobStorage storage,
    IMalwareScanner scanner,
    IOptions<MalwareScanningOptions> scanning,
    NudityClassifier nudity,
    IOptions<ModerationOptions> moderation,
    IOptions<MediaOptions> options,
    TimeProvider timeProvider,
    ILogger<MediaProcessor> logger)
{
    public async Task HandleAsync(EventEnvelope<MediaUploadedEvent> envelope, CancellationToken cancellationToken)
    {
        var media = await db.Media.SingleOrDefaultAsync(m => m.Id == envelope.Data.MediaId, cancellationToken);
        // Already finished: a redelivered event
        if (media is not { Status: MediaStatus.Processing })
        {
            return;
        }

        var data = await ReadUploadAsync(media, cancellationToken);
        if (data is null)
        {
            return;
        }

        if (scanning.Value.Enabled)
        {
            using var content = new MemoryStream(data, writable: false);
            var scan = await scanner.ScanAsync(content, cancellationToken);
            if (!scan.IsClean)
            {
                logger.LogWarning("Malware {Signature} in media {MediaId} uploaded by {OwnerId}", scan.Signature, media.Id, media.OwnerId);
                await RejectAsync(media, MediaRejections.MalwareDetected, cancellationToken);
                return;
            }
        }

        var actualType = FileSniffer.Detect(data);
        if (actualType is null || !MediaOptions.AllowedContentTypes.Contains(actualType))
        {
            await RejectAsync(media, MediaRejections.TypeNotAllowed, cancellationToken);
            return;
        }

        SanitizedImage image;
        try
        {
            image = ImageSanitizer.Process(data, options.Value);
        }
        catch (InvalidImageException ex)
        {
            await RejectAsync(media, ex.Reason, cancellationToken);
            return;
        }

        // On the rebuilt image, so the classifier only ever sees clean pixels. Like the malware scan, there's no way
        // round it: without the model this throws, the consumer retries, and the photo stays in quarantine.
        // Rounded once and used for the decision, the stored score and the log alike, so none of them disagree
        var nudityScore = Math.Round(await nudity.ScoreAsync(image.Large.Data, cancellationToken), 3);
        media.NudityScore = nudityScore;
        if (nudityScore >= moderation.Value.NudityRejectAt)
        {
            db.ModerationEvents.Add(new ModerationEventEntity
            {
                UserId = media.OwnerId,
                Kind = "photo",
                Category = ModerationCategories.Nudity,
                Score = nudityScore,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            });
            logger.LogWarning("Media {MediaId} from {OwnerId} rejected for nudity ({Score:0.00})", media.Id, media.OwnerId, nudityScore);
            await RejectAsync(media, MediaRejections.NudityDetected, cancellationToken);
            return;
        }

        var largeKey = MediaKeys.Large(media.Id, image.Large.Extension);
        var thumbnailKey = MediaKeys.Thumbnail(media.Id, image.Thumbnail.Extension);
        await storage.PutAsync(largeKey, new MemoryStream(image.Large.Data), image.Large.ContentType, cancellationToken);
        await storage.PutAsync(thumbnailKey, new MemoryStream(image.Thumbnail.Data), image.Thumbnail.ContentType, cancellationToken);

        media.Status = MediaStatus.Ready;
        media.LargeKey = largeKey;
        media.ThumbnailKey = thumbnailKey;
        media.ContentType = image.Large.ContentType;
        media.Width = image.Large.Width;
        media.Height = image.Large.Height;
        media.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(media.QuarantineKey, cancellationToken);
        logger.LogInformation("Media {MediaId} scanned clean and published ({Width}x{Height})", media.Id, media.Width, media.Height);
    }

    // The upload, or null if it was rejected for being missing or too big
    private async Task<byte[]?> ReadUploadAsync(MediaEntity media, CancellationToken cancellationToken)
    {
        await using var blob = await storage.GetAsync(media.QuarantineKey, cancellationToken);
        if (blob is null)
        {
            await RejectAsync(media, MediaRejections.UploadMissing, cancellationToken);
            return null;
        }

        // Read one byte past the limit to know it's over without reading all of it
        var limit = options.Value.MaxUploadBytes;
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await blob.Content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
            {
                await RejectAsync(media, MediaRejections.TooLarge, cancellationToken);
                return null;
            }
        }
        return buffer.ToArray();
    }

    private async Task RejectAsync(MediaEntity media, string reason, CancellationToken cancellationToken)
    {
        media.Status = MediaStatus.Rejected;
        media.RejectionReason = reason;
        media.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        // Nothing rejected is kept; the reason on the row is the record
        await storage.DeleteAsync(media.QuarantineKey, cancellationToken);
        logger.LogInformation("Media {MediaId} rejected: {Reason}", media.Id, reason);
    }
}
