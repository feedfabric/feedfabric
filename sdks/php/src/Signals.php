<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * What the member's reading tells the ranker. Send them from your server as your app reports them.
 *
 * @phpstan-import-type Counted from Types
 * @phpstan-import-type DwellView from Types
 */
final class Signals
{
    public function __construct(private readonly Member $m)
    {
    }

    /**
     * Posts the member saw (at least half on screen for a second); up to 100 at a time.
     *
     * @param list<string> $postIds
     * @return Counted
     */
    public function impressions(array $postIds): array
    {
        /** @var Counted */
        return $this->m->call('POST', '/userfeed/impressions', body: ['postIds' => $postIds]);
    }

    /**
     * The member opened the post's own page.
     *
     * @return Counted
     */
    public function open(string $postId): array
    {
        /** @var Counted */
        return $this->m->call('POST', '/userfeed/posts/' . Member::esc($postId) . '/open');
    }

    /**
     * How long posts stayed on screen; up to 100 at a time.
     *
     * @param list<DwellView> $views
     * @return Counted
     */
    public function dwell(array $views): array
    {
        /** @var Counted */
        return $this->m->call('POST', '/userfeed/dwell', body: ['views' => $views]);
    }

    public function notInterested(string $postId): void
    {
        $this->m->call('POST', '/userfeed/posts/' . Member::esc($postId) . '/not-interested');
    }

    public function showMore(string $postId): void
    {
        $this->m->call('POST', '/userfeed/posts/' . Member::esc($postId) . '/show-more');
    }
}
