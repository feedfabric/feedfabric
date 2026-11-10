<?php

declare(strict_types=1);

namespace FeedFabric;

/**
 * The SDK's entry point: one per process, shared by every request.
 *
 * ```php
 * $feed = new FeedFabric('https://feed.example.com', getenv('FEEDFABRIC_API_KEY'));
 * $member = $feed->asMember('user-123', username: 'sam');
 * $post = $member->posts->create('Hello #intro');
 * ```
 */
final class FeedFabric
{
    public const VERSION = '0.1.0';

    private readonly string $baseUrl;

    /** How often uploads check whether the image is ready, in seconds; shortened in tests */
    public float $uploadPoll = 1.0;

    /**
     * @param string $baseUrl Where the API runs
     * @param string $apiKey A key from the admin panel (Integrations → API keys), "ffk_…". Keep it on your servers.
     * @param float $timeout Per request, in seconds
     * @param int $maxRetries Retries after a 429 or 503 (waiting Retry-After), and after network errors on requests
     *     that are safe to repeat
     */
    public function __construct(
        string $baseUrl,
        private readonly string $apiKey,
        private readonly float $timeout = 30.0,
        private readonly int $maxRetries = 2,
    ) {
        if ($baseUrl === '') {
            throw new \InvalidArgumentException('FeedFabric needs a base URL');
        }
        if (!str_starts_with($apiKey, 'ffk_')) {
            throw new \InvalidArgumentException('FeedFabric needs an API key from the admin panel ("ffk_…")');
        }
        $this->baseUrl = rtrim($baseUrl, '/');
    }

    /**
     * Calls made for one of your members: their ID is the one your sign-in tokens carry (Auth:Claims:UserId). The
     * profile is used when FeedFabric first sees them, and refreshes theirs at most hourly.
     */
    public function asMember(string $externalUserId, ?string $username = null, ?string $displayName = null, ?string $avatarUrl = null): Member
    {
        if ($externalUserId === '') {
            throw new \InvalidArgumentException('asMember() needs the member\'s ID');
        }
        $headers = ['FeedFabric-User' => rawurlencode($externalUserId)];
        foreach (['FeedFabric-Username' => $username, 'FeedFabric-Display-Name' => $displayName, 'FeedFabric-Avatar' => $avatarUrl] as $name => $value) {
            if ($value !== null && $value !== '') {
                $headers[$name] = rawurlencode($value);
            }
        }
        return new Member($this, $headers);
    }

    /**
     * @internal
     * @param 'GET'|'POST'|'PUT'|'DELETE' $method
     * @param array<string, string> $headers
     * @param array<string, string|int|null> $query
     */
    public function request(string $method, string $path, array $headers, array $query = [], mixed $body = null): mixed
    {
        $url = $this->baseUrl . $path;
        $params = array_filter($query, fn ($v) => $v !== null && $v !== '');
        if ($params !== []) {
            $url .= '?' . http_build_query($params, '', '&', PHP_QUERY_RFC3986);
        }
        $payload = $body === null ? null : json_encode($body, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES | JSON_PRESERVE_ZERO_FRACTION);
        $all = [
            'Authorization' => "Bearer {$this->apiKey}",
            'Accept' => 'application/json',
            'User-Agent' => 'feedfabric-php/' . self::VERSION,
        ] + ($payload === null ? [] : ['Content-Type' => 'application/json']) + $headers;
        // Safe to send twice: everything but POST. A 429 means nothing happened, so that's always safe.
        $repeatable = $method !== 'POST';

        for ($attempt = 0; ; $attempt++) {
            try {
                [$status, $text, $responseHeaders] = $this->send($method, $url, $all, $payload);
            } catch (FeedFabricException $e) {
                if ($repeatable && $attempt < $this->maxRetries) {
                    self::sleep(self::backoff($attempt));
                    continue;
                }
                throw $e;
            }
            if ($status < 300) {
                return $text === '' ? null : json_decode($text, true, 512, JSON_THROW_ON_ERROR);
            }
            $error = FeedFabricException::fromResponse($status, $text, $responseHeaders);
            $retry = $status === 429 || ($status === 503 && $repeatable);
            if ($retry && $attempt < $this->maxRetries) {
                self::sleep($error->retryAfter ?? self::backoff($attempt));
                continue;
            }
            throw $error;
        }
    }

    /**
     * To the storage's own signed URL, which doesn't take the API key.
     *
     * @internal
     */
    public function putFile(string $url, string $content, string $contentType): void
    {
        [$status, $text, $headers] = $this->send('PUT', $url, ['Content-Type' => $contentType], $content);
        if ($status >= 300) {
            throw FeedFabricException::fromResponse($status, $text, $headers);
        }
    }

    /**
     * @param 'GET'|'POST'|'PUT'|'DELETE' $method
     * @param array<string, string> $headers
     * @return array{int, string, array<string, string>}
     */
    private function send(string $method, string $url, array $headers, ?string $body): array
    {
        $responseHeaders = [];
        $curl = curl_init($url);
        $lines = [];
        foreach ($headers as $name => $value) {
            $lines[] = "{$name}: {$value}";
        }
        curl_setopt_array($curl, [
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_HTTPHEADER => $lines,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT_MS => (int) ($this->timeout * 1000),
            CURLOPT_HEADERFUNCTION => function (\CurlHandle $_, string $line) use (&$responseHeaders): int {
                $parts = explode(':', $line, 2);
                if (count($parts) === 2) {
                    $responseHeaders[strtolower(trim($parts[0]))] = trim($parts[1]);
                }
                return strlen($line);
            },
        ]);
        if ($body !== null) {
            curl_setopt($curl, CURLOPT_POSTFIELDS, $body);
        }
        $text = curl_exec($curl);
        if (!is_string($text)) {
            throw new FeedFabricException(0, 'network_error', curl_error($curl));
        }
        /** @var array<string, string> $responseHeaders */
        return [curl_getinfo($curl, CURLINFO_RESPONSE_CODE), $text, $responseHeaders];
    }

    private static function backoff(int $attempt): float
    {
        return min(0.25 * 2 ** $attempt, 4.0) + mt_rand(0, 100) / 1000;
    }

    /** @internal */
    public static function sleep(float $seconds): void
    {
        usleep((int) ($seconds * 1_000_000));
    }
}
