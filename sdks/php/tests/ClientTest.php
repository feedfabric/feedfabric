<?php

declare(strict_types=1);

namespace FeedFabric\Tests;

use FeedFabric\DataType;
use FeedFabric\FeedFabric;
use FeedFabric\FeedFabricException;
use FeedFabric\Member;
use PHPUnit\Framework\Attributes\Test;
use PHPUnit\Framework\TestCase;

final class WeightReading
{
    public function __construct(public readonly float $kg, public readonly ?string $note = null)
    {
    }
}

final class ClientTest extends TestCase
{
    /** @var resource|null */
    private static $server = null;
    private static string $url = '';
    private static string $dir = '';

    public static function setUpBeforeClass(): void
    {
        self::$dir = sys_get_temp_dir() . '/feedfabric-php-' . bin2hex(random_bytes(4));
        mkdir(self::$dir);
        $socket = stream_socket_server('tcp://127.0.0.1:0');
        assert($socket !== false);
        $port = (int) substr((string) strrchr((string) stream_socket_get_name($socket, false), ':'), 1);
        fclose($socket);
        self::$url = "http://127.0.0.1:{$port}";
        $server = proc_open([PHP_BINARY, '-S', "127.0.0.1:{$port}", __DIR__ . '/router.php'], [1 => ['file', '/dev/null', 'w'], 2 => ['file', '/dev/null', 'w']],
            $pipes, null, ['FAKE_API_DIR' => self::$dir]);
        assert($server !== false);
        self::$server = $server;
        for ($i = 0; $i < 100 && @fsockopen('127.0.0.1', $port) === false; $i++) {
            usleep(20_000);
        }
    }

    public static function tearDownAfterClass(): void
    {
        if (self::$server !== null) {
            proc_terminate(self::$server);
        }
        array_map('unlink', glob(self::$dir . '/*') ?: []);
        rmdir(self::$dir);
    }

    protected function setUp(): void
    {
        file_put_contents(self::$dir . '/seen.json', '[]');
        file_put_contents(self::$dir . '/replies.json', '[]');
    }

    /** @param array<string, string> $headers */
    private function reply(int $status, mixed $body = null, array $headers = []): void
    {
        $replies = json_decode((string) file_get_contents(self::$dir . '/replies.json'), true);
        assert(is_array($replies));
        $replies[] = ['status' => $status, 'body' => $body, 'headers' => (object) $headers];
        file_put_contents(self::$dir . '/replies.json', json_encode($replies, JSON_PRESERVE_ZERO_FRACTION));
    }

    /** @return list<array{method: string, path: string, headers: array<string, string>, body: string}> */
    private function seen(): array
    {
        /** @var list<array{method: string, path: string, headers: array<string, string>, body: string}> */
        return json_decode((string) file_get_contents(self::$dir . '/seen.json'), true);
    }

    private function member(): Member
    {
        return (new FeedFabric(self::$url, 'ffk_test'))->asMember('u1');
    }

    /** @return DataType<WeightReading> */
    private static function weight(): DataType
    {
        return new DataType('weight-reading',
            parse: function (mixed $v): WeightReading {
                if (!is_array($v) || !is_float($v['kg'] ?? null)) {
                    throw new \UnexpectedValueException('not a weight reading');
                }
                return new WeightReading($v['kg'], is_string($v['note'] ?? null) ? $v['note'] : null);
            },
            dump: fn (WeightReading $r) => array_filter(['kg' => $r->kg, 'note' => $r->note], fn ($x) => $x !== null));
    }

    /** @return array<string, mixed> */
    private static function saved(): array
    {
        return ['id' => 'p1', 'authorId' => 'a1', 'content' => '', 'createdAt' => '2026-11-10T09:00:00Z', 'editedAt' => null, 'repostOfId' => null,
            'isHeld' => false, 'data' => ['type' => 'weight-reading', 'version' => 2, 'value' => ['kg' => 82.4]]];
    }

    #[Test]
    public function actsForTheMemberWithTheKeyAndEncodedProfile(): void
    {
        $this->reply(200, ['externalUserId' => 'user 1']);

        $me = (new FeedFabric(self::$url . '/', 'ffk_test'))->asMember('user 1', displayName: 'Zoë Brontë')->me();

        self::assertSame('user 1', $me['externalUserId']);
        $seen = $this->seen()[0];
        self::assertSame('GET /me', "{$seen['method']} {$seen['path']}");
        self::assertSame('Bearer ffk_test', $seen['headers']['authorization']);
        self::assertSame('user%201', $seen['headers']['feedfabric-user']);
        self::assertSame('Zoë Brontë', rawurldecode($seen['headers']['feedfabric-display-name']));
        self::assertArrayNotHasKey('feedfabric-username', $seen['headers']);
    }

    #[Test]
    public function postsTypedDataAndAppTagsAndReadsItBack(): void
    {
        $this->reply(201, self::saved());
        $weight = self::weight();

        $post = $this->member()->posts->create(tags: ['weight'], data: $weight->of(new WeightReading(82.4)));

        self::assertEquals(['tags' => ['weight'], 'data' => ['type' => 'weight-reading', 'value' => ['kg' => 82.4]]],
            json_decode($this->seen()[0]['body'], true));
        self::assertEquals(new WeightReading(82.4), $weight->read($post));
        /** @var DataType<array{count: int}> $steps */
        $steps = new DataType('steps');
        self::assertNull($steps->read($post));
        self::assertFalse($weight->is(['data' => null]));
    }

    #[Test]
    public function readThrowsForDataThatIsNotTheType(): void
    {
        $this->expectException(\UnexpectedValueException::class);

        self::weight()->read(['data' => ['type' => 'weight-reading', 'version' => 1, 'value' => ['kg' => 'heavy']]]);
    }

    #[Test]
    public function buildsQueryStringsAndLeavesOutWhatIsUnset(): void
    {
        $this->reply(200, ['posts' => [], 'nextCursor' => null]);

        $this->member()->posts->list(type: 'weight-reading', order: 'oldest', limit: 5);

        self::assertSame('/userfeed?type=weight-reading&order=oldest&limit=5', $this->seen()[0]['path']);
    }

    #[Test]
    public function errorsCarryTheCodeParamsAndFieldErrors(): void
    {
        $this->reply(400, ['code' => 'post.data_invalid', 'detail' => 'nope', 'params' => ['type' => 'weight-reading'],
            'errors' => [['path' => '/kg', 'message' => 'should be a number']], 'traceId' => 't-1']);

        try {
            $this->member()->posts->create(data: self::weight()->of(new WeightReading(1.0)));
            self::fail('no error');
        } catch (FeedFabricException $e) {
            self::assertSame([400, 'post.data_invalid', 't-1'], [$e->status, $e->errorCode, $e->traceId]);
            self::assertSame(['type' => 'weight-reading'], $e->params);
            self::assertSame([['path' => '/kg', 'message' => 'should be a number']], $e->errors);
        }
    }

    #[Test]
    public function waitsOutA429EvenForAPost(): void
    {
        $this->reply(429, ['code' => 'rate_limited'], ['Retry-After' => '0']);
        $this->reply(202, ['counted' => 2]);

        self::assertSame(2, $this->member()->signals->impressions(['p1', 'p2'])['counted']);
        self::assertCount(2, $this->seen());
    }

    #[Test]
    public function repeatsAGetAfterA503ButNotAPost(): void
    {
        $member = $this->member();
        $this->reply(503, ['code' => 'service_unavailable'], ['Retry-After' => '0']);
        try {
            $member->posts->create('hi');
            self::fail('no error');
        } catch (FeedFabricException $e) {
            self::assertSame(503, $e->status);
        }
        self::assertCount(1, $this->seen());

        $this->reply(503, ['code' => 'service_unavailable'], ['Retry-After' => '0']);
        $this->reply(200, []);
        self::assertSame([], $member->favourites());
        self::assertCount(3, $this->seen());
    }

    #[Test]
    public function uploadsAnImageAndWaitsUntilItIsReady(): void
    {
        $this->reply(201, ['mediaId' => 'm1', 'uploadUrl' => self::$url . '/blobs/m1', 'contentType' => 'image/png', 'expiresAt' => '2026-11-10T10:00:00Z']);
        $this->reply(200);
        $this->reply(202, ['id' => 'm1', 'status' => 'processing']);
        $this->reply(200, ['id' => 'm1', 'status' => 'ready', 'url' => 'u', 'thumbnailUrl' => 't', 'width' => 1, 'height' => 1]);
        $client = new FeedFabric(self::$url, 'ffk_test');
        $client->uploadPoll = 0.01;

        $media = $client->asMember('u1')->media->upload("\x01\x02\x03", 'image/png');

        self::assertSame('ready', $media['status']);
        $seen = $this->seen();
        self::assertSame(['POST /media/uploads', 'PUT /blobs/m1', 'POST /media/m1/complete', 'GET /media/m1'],
            array_map(fn ($s) => "{$s['method']} {$s['path']}", $seen));
        self::assertArrayNotHasKey('authorization', $seen[1]['headers']);
    }

    #[Test]
    public function refusesAWrongLookingKey(): void
    {
        $this->expectException(\InvalidArgumentException::class);

        new FeedFabric('http://localhost', 'eyJhbGciOi...');
    }
}
