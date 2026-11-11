<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * An API error. Every one has a stable $code (docs/errors.md): match on it, not on the message.
 *
 * ```php
 * } catch (FeedFabricException $e) {
 *     if ($e->errorCode === 'post.not_found') { ... }
 * }
 * ```
 */
final class FeedFabricException extends \RuntimeException
{
    /**
     * @param int $status The HTTP status; 0 when the request never got a response
     * @param string $errorCode e.g. "post.not_found", "content.violation", "rate_limited"; "network_error" without a response
     * @param array<string, mixed> $params
     * @param list<mixed> $errors validation_failed lists fields; post.data_invalid lists ["path" => ..., "message" => ...]
     * @param float|null $retryAfter Seconds, from Retry-After on 429 and 503
     */
    public function __construct(
        public readonly int $status,
        public readonly string $errorCode,
        string $message,
        public readonly array $params = [],
        public readonly array $errors = [],
        public readonly ?string $traceId = null,
        public readonly ?float $retryAfter = null,
        ?\Throwable $previous = null,
    ) {
        parent::__construct($message, $status, $previous);
    }

    /** @param array<string, string> $headers Lower-cased names */
    public static function fromResponse(int $status, string $body, array $headers): self
    {
        // Not Problem Details when a proxy or load balancer answered
        $problem = json_decode($body, true);
        if (!is_array($problem)) {
            $problem = [];
        }
        $retryAfter = $headers['retry-after'] ?? null;
        $params = $problem['params'] ?? [];
        $errors = $problem['errors'] ?? [];
        $code = $problem['code'] ?? null;
        $detail = $problem['detail'] ?? null;
        $traceId = $problem['traceId'] ?? null;
        return new self(
            $status,
            is_string($code) ? $code : "http_{$status}",
            is_string($detail) ? $detail : "HTTP {$status}",
            is_array($params) ? self::keyed($params) : [],
            is_array($errors) ? array_values($errors) : [],
            is_string($traceId) ? $traceId : null,
            $retryAfter !== null && is_numeric($retryAfter) ? (float) $retryAfter : null,
        );
    }

    /**
     * @param array<mixed> $values
     * @return array<string, mixed>
     */
    private static function keyed(array $values): array
    {
        $keyed = [];
        foreach ($values as $key => $value) {
            $keyed[(string) $key] = $value;
        }
        return $keyed;
    }
}
