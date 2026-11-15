<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * Everything a member can do, as that member.
 *
 * @phpstan-import-type Favourite from Types
 * @phpstan-import-type DataTypeInfo from Types
 */
final class Member
{
    public readonly Posts $posts;
    public readonly Feeds $feeds;
    public readonly Comments $comments;
    public readonly MediaUploads $media;
    public readonly Signals $signals;

    /** @param array<string, string> $headers */
    public function __construct(private readonly FeedFabric $client, private readonly array $headers)
    {
        $this->posts = new Posts($this);
        $this->feeds = new Feeds($this);
        $this->comments = new Comments($this);
        $this->media = new MediaUploads($this, $client);
        $this->signals = new Signals($this);
    }

    /**
     * @internal
     * @param 'GET'|'POST'|'PUT'|'DELETE' $method
     * @param array<string, string|int|null> $query
     */
    public function call(string $method, string $path, array $query = [], mixed $body = null): mixed
    {
        return $this->client->request($method, $path, $this->headers, $query, $body);
    }

    /** @internal */
    public static function esc(string $value): string
    {
        return rawurlencode($value);
    }

    /**
     * Who FeedFabric thinks this is; also creates the member if they're new.
     *
     * @return array{externalUserId: string}
     */
    public function me(): array
    {
        /** @var array{externalUserId: string} */
        return $this->call('GET', '/me');
    }

    public function follow(string $externalUserId): void
    {
        $this->call('PUT', '/follows/' . self::esc($externalUserId));
    }

    public function unfollow(string $externalUserId): void
    {
        $this->call('DELETE', '/follows/' . self::esc($externalUserId));
    }

    /** @return list<Favourite> */
    public function favourites(): array
    {
        /** @var list<Favourite> */
        return $this->call('GET', '/favourites');
    }

    /** Up to 30; favourite.limit_reached after that. */
    public function addFavourite(string $externalUserId): void
    {
        $this->call('PUT', '/favourites/' . self::esc($externalUserId));
    }

    public function removeFavourite(string $externalUserId): void
    {
        $this->call('DELETE', '/favourites/' . self::esc($externalUserId));
    }

    /**
     * The registered data types and their schemas.
     *
     * @return list<DataTypeInfo>
     */
    public function dataTypes(): array
    {
        /** @var list<DataTypeInfo> */
        return $this->call('GET', '/data-types');
    }

    /** @return DataTypeInfo */
    public function dataType(string $key): array
    {
        /** @var DataTypeInfo */
        return $this->call('GET', '/data-types/' . self::esc($key));
    }

    /**
     * Everything held about the member, as one JSON document. A few an hour.
     *
     * @return array<string, mixed>
     */
    public function exportAccount(): array
    {
        /** @var array<string, mixed> */
        return $this->call('GET', '/me/export');
    }

    /** Erases the member: posts, comments and profile at once, the rest shortly after. */
    public function deleteAccount(): void
    {
        $this->call('DELETE', '/me');
    }
}
