# FeedFabric for PHP

Call FeedFabric from your servers, for your members, with an API key. It works on PHP 8.1+ with ext-curl.
[../README.md](../README.md) covers API keys, tags and typed data for every SDK.

```sh
composer require feedfabric/feedfabric-php
```

```php
use FeedFabric\FeedFabric;
use FeedFabric\FeedFabricException;

$feed = new FeedFabric('https://feed.example.com', getenv('FEEDFABRIC_API_KEY'));
$member = $feed->asMember($user->id, username: $user->handle, displayName: $user->name);

$page = $member->feeds->forYou(limit: 20);
$member->posts->like($page['posts'][0]['id']);
```

Responses are the API's JSON as arrays. Their shapes are declared for PHPStan and Psalm in `FeedFabric\Types`, so
`$page['posts'][0]['id']` is known to be a string.

## Typed data

```php
final class WeightReading
{
    public function __construct(public readonly float $kg, public readonly ?string $note = null) {}
}

/** @var DataType<WeightReading> $weight */
$weight = new DataType('weight-reading',
    parse: fn (array $v) => new WeightReading($v['kg'], $v['note'] ?? null),
    dump: fn (WeightReading $r) => array_filter(['kg' => $r->kg, 'note' => $r->note], fn ($x) => $x !== null));

$member->posts->create(tags: ['weight'], data: $weight->of(new WeightReading(82.4)));

foreach ($member->posts->list(type: $weight->key)['posts'] as $post) {
    $reading = $weight->read($post); // ?WeightReading
}
```

`DataType` is generic (`@template T`). PHPStan and Psalm check that `of` gets a `WeightReading` and that `read`
returns one.

## Errors

```php
try {
    $member->posts->create($content);
} catch (FeedFabricException $e) {
    if ($e->errorCode !== 'content.violation') {
        throw $e;
    }
    showRules($e->params['category'] ?? null);
}
```

## Tests

`composer test` runs PHPStan at its strictest level, then PHPUnit against a stand-in server.
