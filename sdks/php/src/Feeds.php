<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * @phpstan-import-type FeedPage from Types
 * @phpstan-import-type ForYouPage from Types
 * @phpstan-import-type MentionPage from Types
 * @phpstan-import-type TrendingTag from Types
 */
final class Feeds
{
    public function __construct(private readonly Member $m)
    {
    }

    /**
     * Posts from the accounts the member follows, newest first.
     *
     * @return FeedPage
     */
    public function home(?int $limit = null, ?string $cursor = null): array
    {
        /** @var FeedPage */
        return $this->m->call('GET', '/userfeed/home', ['limit' => $limit, 'cursor' => $cursor]);
    }

    /**
     * Ranked for the member; $profile picks a ranking profile.
     *
     * @return ForYouPage
     */
    public function forYou(?string $profile = null, ?int $limit = null, ?string $cursor = null): array
    {
        /** @var ForYouPage */
        return $this->m->call('GET', '/userfeed/foryou', ['profile' => $profile, 'limit' => $limit, 'cursor' => $cursor]);
    }

    /** @return FeedPage */
    public function favourites(?int $limit = null, ?string $cursor = null): array
    {
        /** @var FeedPage */
        return $this->m->call('GET', '/userfeed/favourites', ['limit' => $limit, 'cursor' => $cursor]);
    }

    /**
     * Posts with a tag (hashtag, inferred topic or app tag), newest first.
     *
     * @return FeedPage
     */
    public function tag(string $tag, ?int $limit = null, ?string $cursor = null): array
    {
        /** @var FeedPage */
        return $this->m->call('GET', '/userfeed/tags/' . Member::esc($tag) . '/posts', ['limit' => $limit, 'cursor' => $cursor]);
    }

    /** @return list<TrendingTag> */
    public function trendingTags(?int $hours = null, ?int $limit = null): array
    {
        /** @var list<TrendingTag> */
        return $this->m->call('GET', '/userfeed/tags/trending', ['hours' => $hours, 'limit' => $limit]);
    }

    /** @return MentionPage */
    public function mentions(?int $limit = null, ?string $cursor = null): array
    {
        /** @var MentionPage */
        return $this->m->call('GET', '/userfeed/mentions', ['limit' => $limit, 'cursor' => $cursor]);
    }
}
