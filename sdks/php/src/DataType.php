<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * One of your data types, typed. The API checks data against the type's JSON Schema; this makes the same promise in
 * your code, so a weight reading can't be posted as anything else and reading one back gives your own type.
 *
 * ```php
 * final class WeightReading {
 *     public function __construct(public readonly float $kg, public readonly ?string $note = null) {}
 * }
 * // @var DataType<WeightReading>
 * $weight = new DataType('weight-reading',
 *     parse: fn (array $v) => new WeightReading($v['kg'], $v['note'] ?? null),
 *     dump: fn (WeightReading $r) => array_filter(['kg' => $r->kg, 'note' => $r->note], fn ($x) => $x !== null));
 *
 * $member->posts->create(tags: ['weight'], data: $weight->of(new WeightReading(82.4)));
 * $reading = $weight->read($post);   // ?WeightReading
 * ```
 *
 * Without parse and dump, T is the decoded JSON array itself, e.g. `DataType<array{kg: float, note?: string}>`.
 *
 * @template T
 * @phpstan-import-type PostDataInput from Types
 */
final class DataType
{
    /** @var (callable(mixed): T)|null */
    private $parse;

    /** @var (callable(T): mixed)|null */
    private $dump;

    /**
     * @param string $key As registered in the admin panel
     * @param (callable(mixed): T)|null $parse Turns the decoded JSON into a T; throw if it isn't one
     * @param (callable(T): mixed)|null $dump Turns a T into something json_encode writes as the type's JSON
     */
    public function __construct(public readonly string $key, ?callable $parse = null, ?callable $dump = null)
    {
        $this->parse = $parse;
        $this->dump = $dump;
    }

    /**
     * The post's data, for creating or editing a post.
     *
     * @param T $value
     * @return PostDataInput
     */
    public function of(mixed $value): array
    {
        return ['type' => $this->key, 'value' => $this->dump !== null ? ($this->dump)($value) : $value];
    }

    /**
     * Whether a post (anything with its "data") has data of this type.
     *
     * @param array<string, mixed> $post
     */
    public function is(array $post): bool
    {
        $data = $post['data'] ?? null;
        return is_array($data) && ($data['type'] ?? null) === $this->key;
    }

    /**
     * The post's data as a T, or null when it has none of this type.
     *
     * @param array<string, mixed> $post
     * @return T|null
     */
    public function read(array $post): mixed
    {
        if (!$this->is($post)) {
            return null;
        }
        /** @var array{value: mixed} $data */
        $data = $post['data'];
        if ($this->parse !== null) {
            return ($this->parse)($data['value']);
        }
        /** @var T */
        return $data['value'];
    }
}
