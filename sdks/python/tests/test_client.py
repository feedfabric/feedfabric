from __future__ import annotations

import json
import threading
import urllib.parse
from dataclasses import asdict, dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, Iterator, List, Optional, Tuple, TypedDict

import pytest

from feedfabric import DataType, FeedFabric, FeedFabricError, SavedPost

# A stand-in API: records each request and answers with whatever the test queued
Reply = Tuple[int, Any, Dict[str, str]]


class FakeApi:
    def __init__(self) -> None:
        self.seen: List[Dict[str, Any]] = []
        self.replies: List[Reply] = []
        self.url = ""

    def reply(self, status: int, body: Any = None, headers: Optional[Dict[str, str]] = None) -> None:
        self.replies.append((status, body, headers or {}))


@pytest.fixture
def api() -> Iterator[FakeApi]:
    fake = FakeApi()

    class Handler(BaseHTTPRequestHandler):
        def _handle(self) -> None:
            length = int(self.headers.get("Content-Length") or 0)
            body = self.rfile.read(length).decode() if length else ""
            fake.seen.append({"method": self.command, "path": self.path, "headers": self.headers, "body": body})
            status, reply, headers = fake.replies.pop(0) if fake.replies else (204, None, {})
            payload = b"" if reply is None else json.dumps(reply).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/problem+json" if status >= 400 else "application/json")
            self.send_header("Content-Length", str(len(payload)))
            for name, value in headers.items():
                self.send_header(name, value)
            self.end_headers()
            self.wfile.write(payload)

        do_GET = do_POST = do_PUT = do_DELETE = _handle

        def log_message(self, *args: Any) -> None:
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    fake.url = f"http://127.0.0.1:{server.server_address[1]}"
    yield fake
    server.shutdown()


def client(api: FakeApi) -> FeedFabric:
    return FeedFabric(api.url, "ffk_test", max_retries=2)


class WeightReading(TypedDict):
    kg: float


Weight = DataType[WeightReading]("weight-reading")

SAVED: SavedPost = {
    "id": "p1", "authorId": "a1", "content": "", "createdAt": "2026-11-10T09:00:00Z", "editedAt": None, "repostOfId": None,
    "isHeld": False, "data": {"type": "weight-reading", "version": 2, "value": {"kg": 82.4}},
}


def test_acts_for_the_member_with_the_key_and_encoded_profile(api: FakeApi) -> None:
    api.reply(200, {"externalUserId": "user 1"})

    me = client(api).as_member("user 1", display_name="Zoë Brontë").me()

    assert me["externalUserId"] == "user 1"
    seen = api.seen[0]
    assert (seen["method"], seen["path"]) == ("GET", "/me")
    assert seen["headers"]["Authorization"] == "Bearer ffk_test"
    assert seen["headers"]["FeedFabric-User"] == "user%201"
    assert urllib.parse.unquote(seen["headers"]["FeedFabric-Display-Name"]) == "Zoë Brontë"


def test_posts_typed_data_and_app_tags_and_reads_it_back(api: FakeApi) -> None:
    api.reply(201, SAVED)

    post = client(api).as_member("u1").posts.create(tags=["weight"], data=Weight.of({"kg": 82.4}))

    assert json.loads(api.seen[0]["body"]) == {"tags": ["weight"], "data": {"type": "weight-reading", "value": {"kg": 82.4}}}
    reading = Weight.read(post)
    assert reading is not None and reading["kg"] == 82.4
    assert DataType[Dict[str, int]]("steps").read(post) is None


def test_dataclasses_round_trip_with_parse_and_dump() -> None:
    @dataclass
    class Reading:
        kg: float
        note: str = ""

    typed = DataType[Reading]("weight-reading", parse=lambda v: Reading(**v), dump=asdict)

    assert typed.of(Reading(82.4)) == {"type": "weight-reading", "value": {"kg": 82.4, "note": ""}}
    assert typed.read(SAVED) == Reading(82.4)


def test_builds_query_strings_and_leaves_out_what_is_unset(api: FakeApi) -> None:
    api.reply(200, {"posts": [], "nextCursor": None})

    client(api).as_member("u1").posts.list(type="weight-reading", limit=5)

    assert api.seen[0]["path"] == "/userfeed?type=weight-reading&limit=5"


def test_errors_carry_the_code_params_and_field_errors(api: FakeApi) -> None:
    api.reply(400, {"code": "post.data_invalid", "detail": "nope", "params": {"type": "weight-reading"},
                    "errors": [{"path": "/kg", "message": "should be a number"}], "traceId": "t-1"})

    with pytest.raises(FeedFabricError) as raised:
        client(api).as_member("u1").posts.create(data=Weight.of({"kg": 1}))

    error = raised.value
    assert (error.status, error.code, error.trace_id) == (400, "post.data_invalid", "t-1")
    assert error.params == {"type": "weight-reading"}
    assert error.errors[0]["path"] == "/kg"


def test_waits_out_a_429_even_for_a_post(api: FakeApi) -> None:
    api.reply(429, {"code": "rate_limited"}, {"Retry-After": "0"})
    api.reply(202, {"counted": 2})

    assert client(api).as_member("u1").signals.impressions(["p1", "p2"])["counted"] == 2
    assert len(api.seen) == 2


def test_repeats_a_get_after_a_503_but_not_a_post(api: FakeApi) -> None:
    api.reply(503, {"code": "service_unavailable"}, {"Retry-After": "0"})
    with pytest.raises(FeedFabricError):
        client(api).as_member("u1").posts.create("hi")
    assert len(api.seen) == 1

    api.reply(503, {"code": "service_unavailable"}, {"Retry-After": "0"})
    api.reply(200, [])
    assert client(api).as_member("u1").favourites() == []
    assert len(api.seen) == 3


def test_uploads_an_image_and_waits_until_it_is_ready(api: FakeApi) -> None:
    api.reply(201, {"mediaId": "m1", "uploadUrl": f"{api.url}/blobs/m1", "contentType": "image/png", "expiresAt": "2026-11-10T10:00:00Z"})
    api.reply(200)
    api.reply(202, {"id": "m1", "status": "processing"})
    api.reply(200, {"id": "m1", "status": "ready", "url": "u", "thumbnailUrl": "t", "width": 1, "height": 1, "rejectionReason": None})

    media = client(api).as_member("u1").media.upload(b"\x01\x02\x03", "image/png", poll=0.01)

    assert media["status"] == "ready"
    assert [f"{s['method']} {s['path']}" for s in api.seen] == ["POST /media/uploads", "PUT /blobs/m1", "POST /media/m1/complete", "GET /media/m1"]
    assert "Authorization" not in api.seen[1]["headers"]


def test_refuses_a_wrong_looking_key() -> None:
    with pytest.raises(ValueError, match="API key"):
        FeedFabric("http://localhost", "eyJhbGciOi...")
