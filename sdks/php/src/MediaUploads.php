<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * @phpstan-import-type Upload from Types
 * @phpstan-import-type Media from Types
 */
final class MediaUploads
{
    public function __construct(private readonly Member $m, private readonly FeedFabric $client)
    {
    }

    /**
     * Asks for an upload URL: then PUT the file there and call complete.
     *
     * @return Upload
     */
    public function createUpload(string $contentType, int $size): array
    {
        /** @var Upload */
        return $this->m->call('POST', '/media/uploads', body: ['contentType' => $contentType, 'size' => $size]);
    }

    /** @return Media */
    public function complete(string $mediaId): array
    {
        /** @var Media */
        return $this->m->call('POST', '/media/' . Member::esc($mediaId) . '/complete');
    }

    /** @return Media */
    public function get(string $mediaId): array
    {
        /** @var Media */
        return $this->m->call('GET', '/media/' . Member::esc($mediaId));
    }

    /**
     * The whole upload: asks for a URL, sends the file, and waits for scanning. Returns the ready image, for a post's
     * $mediaIds; throws FeedFabricException "media.rejected" if it's refused, or "media.timeout" if it's still being
     * checked after $timeout seconds.
     *
     * @param string $content The file's bytes
     * @return Media
     */
    public function upload(string $content, string $contentType, float $timeout = 60.0): array
    {
        $upload = $this->createUpload($contentType, strlen($content));
        $this->client->putFile($upload['uploadUrl'], $content, $upload['contentType']);
        $media = $this->complete($upload['mediaId']);
        $deadline = microtime(true) + $timeout;
        while ($media['status'] === 'processing' || $media['status'] === 'awaitingUpload') {
            if (microtime(true) > $deadline) {
                throw new FeedFabricException(0, 'media.timeout', 'The image is still being checked; try get() later.', ['mediaId' => $media['id']]);
            }
            FeedFabric::sleep($this->client->uploadPoll);
            $media = $this->get($upload['mediaId']);
        }
        if ($media['status'] === 'rejected') {
            $reason = $media['rejectionReason'] ?? null;
            throw new FeedFabricException(422, 'media.rejected', 'The image was rejected: ' . ($reason ?? 'unknown reason'),
                ['mediaId' => $media['id'], 'reason' => $reason]);
        }
        return $media;
    }
}
