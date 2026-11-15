<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * The API's shapes, as the member routes return them, for PHPStan and Psalm. IDs are UUID strings; times are ISO 8601
 * UTC strings. Import them with `@phpstan-import-type Post from \FeedFabric\Types`.
 *
 * @phpstan-type Tag array{tag: string, source: 'hashtag'|'inferred'|'app'}
 * @phpstan-type PostMedia array{id: string, url: string, thumbnailUrl: string, width: int, height: int}
 * @phpstan-type PostData array{type: string, version: int, value: mixed}
 * @phpstan-type PostDataInput array{type: string, value: mixed}
 * @phpstan-type Post array{id: string, authorId: string, authorExternalUserId: string, authorUsername: string, content: string, createdAt: string, editedAt: ?string, repostOfId: ?string, repostOf?: ?array<string, mixed>, isHeld: bool, tags: list<Tag>, media: list<PostMedia>, data: ?PostData, ranking?: ?array<string, mixed>, likes: int, comments: int, reposts: int, liked: bool, reposted: bool}
 * @phpstan-type SavedPost array{id: string, authorId: string, content: string, createdAt: string, editedAt: ?string, repostOfId: ?string, isHeld: bool, data: ?PostData}
 * @phpstan-type FeedPage array{posts: list<Post>, nextCursor: ?string, source?: string}
 * @phpstan-type ForYouPage array{posts: list<Post>, nextCursor: ?string, ranker: string, profile: ?string, candidates: int}
 * @phpstan-type OwnPostsPage array{posts: list<SavedPost>, nextCursor: ?string}
 * @phpstan-type Comment array{id: string, postId: string, parentCommentId: ?string, authorId: string, authorExternalUserId: string, authorUsername: string, content: ?string, isDeleted: bool, isHeld: bool, createdAt: string, replyCount: int}
 * @phpstan-type CommentPage array{comments: list<Comment>, nextCursor: ?string}
 * @phpstan-type Mention array{id: string, createdAt: string, authorExternalUserId: string, authorUsername: string, postId: string, commentId: ?string, content: string}
 * @phpstan-type MentionPage array{mentions: list<Mention>, nextCursor: ?string}
 * @phpstan-type TrendingTag array{tag: string, posts: int}
 * @phpstan-type Favourite array{externalUserId: string, username: string, displayName: ?string, avatarUrl: ?string, addedAt: string}
 * @phpstan-type Upload array{mediaId: string, uploadUrl: string, contentType: string, expiresAt: string}
 * @phpstan-type Media array{id: string, status: 'awaitingUpload'|'processing'|'ready'|'rejected', rejectionReason?: ?string, width?: ?int, height?: ?int, url?: ?string, thumbnailUrl?: ?string}
 * @phpstan-type DataTypeInfo array{key: string, label: string, description: ?string, version: int, schema: array<string, mixed>, updatedAt: string, updatedBy: ?string}
 * @phpstan-type Counted array{counted: int}
 * @phpstan-type DwellView array{postId: string, ms: int}
 * @phpstan-type ReportReason 'spam'|'harassment'|'hate'|'threat'|'sexual'|'self_harm'|'other'
 */
final class Types
{
    private function __construct()
    {
    }
}
