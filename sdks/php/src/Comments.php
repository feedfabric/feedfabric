<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * @phpstan-import-type Comment from Types
 * @phpstan-import-type CommentPage from Types
 * @phpstan-import-type ReportReason from Types
 */
final class Comments
{
    public function __construct(private readonly Member $m)
    {
    }

    /**
     * Top-level comments, oldest first.
     *
     * @return CommentPage
     */
    public function list(string $postId, ?int $limit = null, ?string $cursor = null): array
    {
        /** @var CommentPage */
        return $this->m->call('GET', '/userfeed/posts/' . Member::esc($postId) . '/comments', ['limit' => $limit, 'cursor' => $cursor]);
    }

    /** @return CommentPage */
    public function replies(string $commentId, ?int $limit = null, ?string $cursor = null): array
    {
        /** @var CommentPage */
        return $this->m->call('GET', '/userfeed/comments/' . Member::esc($commentId) . '/replies', ['limit' => $limit, 'cursor' => $cursor]);
    }

    /** @return Comment */
    public function create(string $postId, string $content, ?string $parentCommentId = null): array
    {
        $body = ['content' => $content];
        if ($parentCommentId !== null) {
            $body['parentCommentId'] = $parentCommentId;
        }
        /** @var Comment */
        return $this->m->call('POST', '/userfeed/posts/' . Member::esc($postId) . '/comments', body: $body);
    }

    public function delete(string $commentId): void
    {
        $this->m->call('DELETE', '/userfeed/comments/' . Member::esc($commentId));
    }

    /** @param ReportReason $reason */
    public function report(string $commentId, string $reason): void
    {
        $this->m->call('POST', '/userfeed/comments/' . Member::esc($commentId) . '/report', body: ['reason' => $reason]);
    }
}
