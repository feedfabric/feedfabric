<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * @phpstan-import-type SavedPost from Types
 * @phpstan-import-type OwnPostsPage from Types
 * @phpstan-import-type PostDataInput from Types
 * @phpstan-import-type ReportReason from Types
 */
final class Posts
{
    public function __construct(private readonly Member $m)
    {
    }

    /**
     * $content is required unless the post has data. $tags are your app's own, written like hashtags without the #;
     * up to 10. $data is structured data of a registered type: DataType::of builds it.
     *
     * @param list<string>|null $tags
     * @param list<string>|null $mediaIds Ready images from MediaUploads::upload, in order
     * @param PostDataInput|null $data
     * @return SavedPost
     */
    public function create(?string $content = null, ?array $tags = null, ?array $mediaIds = null, ?array $data = null): array
    {
        /** @var SavedPost */
        return $this->m->call('POST', '/userfeed/posts', body: self::body($content, $tags, $mediaIds, $data));
    }

    /**
     * Within an hour of posting. Leaving $tags or $data out keeps the post's.
     *
     * @param list<string>|null $tags
     * @param PostDataInput|null $data
     * @return SavedPost
     */
    public function edit(string $postId, ?string $content = null, ?array $tags = null, ?array $data = null): array
    {
        /** @var SavedPost */
        return $this->m->call('PUT', '/userfeed/posts/' . Member::esc($postId), body: self::body($content, $tags, null, $data));
    }

    public function delete(string $postId): void
    {
        $this->m->call('DELETE', '/userfeed/posts/' . Member::esc($postId));
    }

    /**
     * The member's own posts; $type keeps only those with data of that type. Post IDs are time-ordered (UUIDv7):
     * $gt, $gte, $lt and $lte bound the range.
     *
     * @param 'newest'|'oldest'|null $order
     * @return OwnPostsPage
     */
    public function list(
        ?string $type = null,
        ?string $order = null,
        ?int $limit = null,
        ?string $cursor = null,
        ?string $gt = null,
        ?string $gte = null,
        ?string $lt = null,
        ?string $lte = null,
    ): array {
        /** @var OwnPostsPage */
        return $this->m->call('GET', '/userfeed', [
            'type' => $type, 'order' => $order, 'limit' => $limit, 'cursor' => $cursor, 'gt' => $gt, 'gte' => $gte, 'lt' => $lt, 'lte' => $lte,
        ]);
    }

    public function like(string $postId): void
    {
        $this->m->call('PUT', '/userfeed/posts/' . Member::esc($postId) . '/like');
    }

    public function unlike(string $postId): void
    {
        $this->m->call('DELETE', '/userfeed/posts/' . Member::esc($postId) . '/like');
    }

    public function repost(string $postId): void
    {
        $this->m->call('PUT', '/userfeed/posts/' . Member::esc($postId) . '/repost');
    }

    public function unrepost(string $postId): void
    {
        $this->m->call('DELETE', '/userfeed/posts/' . Member::esc($postId) . '/repost');
    }

    /** @param ReportReason $reason */
    public function report(string $postId, string $reason): void
    {
        $this->m->call('POST', '/userfeed/posts/' . Member::esc($postId) . '/report', body: ['reason' => $reason]);
    }

    /**
     * @param list<string>|null $tags
     * @param list<string>|null $mediaIds
     * @param PostDataInput|null $data
     * @return array<string, mixed>
     */
    private static function body(?string $content, ?array $tags, ?array $mediaIds, ?array $data): array
    {
        return array_filter(['content' => $content, 'tags' => $tags, 'mediaIds' => $mediaIds, 'data' => $data], fn ($v) => $v !== null);
    }
}
