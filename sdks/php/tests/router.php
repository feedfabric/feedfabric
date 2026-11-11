<?php

// The stand-in API behind `php -S`: records each request and answers with whatever the test queued, through files
// in FAKE_API_DIR

declare(strict_types=1);

$dir = (string) getenv('FAKE_API_DIR');
$headers = [];
foreach (getallheaders() as $name => $value) {
    $headers[strtolower((string) $name)] = $value;
}
$seen = json_decode((string) file_get_contents("{$dir}/seen.json"), true);
$seen = is_array($seen) ? $seen : [];
$seen[] = [
    'method' => $_SERVER['REQUEST_METHOD'],
    'path' => $_SERVER['REQUEST_URI'],
    'headers' => $headers,
    'body' => (string) file_get_contents('php://input'),
];
file_put_contents("{$dir}/seen.json", json_encode($seen));

/** @var list<array{status: int, body: mixed, headers: array<string, string>}> $replies */
$replies = json_decode((string) file_get_contents("{$dir}/replies.json"), true);
$reply = array_shift($replies) ?? ['status' => 204, 'body' => null, 'headers' => []];
file_put_contents("{$dir}/replies.json", json_encode($replies));

http_response_code($reply['status']);
header('Content-Type: application/json');
foreach ($reply['headers'] as $name => $value) {
    header("{$name}: {$value}");
}
if ($reply['body'] !== null) {
    echo json_encode($reply['body'], JSON_PRESERVE_ZERO_FRACTION);
}
