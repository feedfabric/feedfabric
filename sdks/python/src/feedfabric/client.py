from __future__ import annotations

import json
import random
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Dict, List, Literal, Mapping, Optional, Union, cast

from .errors import FeedFabricError
from .types import (
    Comment,
    CommentPage,
    Counted,
    DataTypeInfo,
    DwellView,
    Favourite,
    FeedPage,
    ForYouPage,
    Me,
    Media,
    MentionPage,
    OwnPostsPage,
    PostDataInput,
    ReportReason,
    SavedPost,
    TrendingTag,
    Upload,
)

VERSION = "0.1.0"

def _quote(value: str) -> str:
    return urllib.parse.quote(value, safe="")


class FeedFabric:
    """The SDK's entry point: one per process, shared by every request.

    base_url is where the API runs; api_key is a key from the admin panel (Integrations -> API keys), "ffk_...".
    Requests are retried max_retries times after a 429 or 503 (waiting Retry-After), and after network errors on
    requests that are safe to repeat.
    """

    def __init__(self, base_url: str, api_key: str, *, timeout: float = 30.0, max_retries: int = 2) -> None:
        if not base_url:
            raise ValueError("FeedFabric needs a base_url")
        if not api_key.startswith("ffk_"):
            raise ValueError('FeedFabric needs an API key from the admin panel ("ffk_...")')
        self.base_url = base_url.rstrip("/")
        self._api_key = api_key
        self.timeout = timeout
        self.max_retries = max_retries

    def as_member(
        self,
        external_user_id: str,
        *,
        username: Optional[str] = None,
        display_name: Optional[str] = None,
        avatar_url: Optional[str] = None,
    ) -> Member:
        """Calls made for one of your members: their ID is the one your sign-in tokens carry (Auth:Claims:UserId).
        The profile is used when FeedFabric first sees them, and refreshes theirs at most hourly."""
        if not external_user_id:
            raise ValueError("as_member() needs the member's ID")
        headers = {"FeedFabric-User": _quote(external_user_id)}
        for name, value in (("FeedFabric-Username", username), ("FeedFabric-Display-Name", display_name), ("FeedFabric-Avatar", avatar_url)):
            if value:
                headers[name] = _quote(value)
        return Member(self, headers)

    def _request(
        self,
        method: str,
        path: str,
        headers: Mapping[str, str],
        query: Optional[Mapping[str, Any]] = None,
        body: Any = None,
    ) -> Any:
        url = self.base_url + path
        params = {k: v for k, v in (query or {}).items() if v is not None and v != ""}
        if params:
            url += "?" + urllib.parse.urlencode(params)
        data = None if body is None else json.dumps(body).encode()
        all_headers = {
            "Authorization": f"Bearer {self._api_key}",
            "Accept": "application/json",
            "User-Agent": f"feedfabric-python/{VERSION}",
            **({} if body is None else {"Content-Type": "application/json"}),
            **headers,
        }
        # Safe to send twice: everything but POST. A 429 means nothing happened, so that's always safe.
        repeatable = method != "POST"

        attempt = 0
        while True:
            request = urllib.request.Request(url, data=data, method=method, headers=all_headers)
            try:
                with urllib.request.urlopen(request, timeout=self.timeout) as response:
                    text = response.read().decode()
                    return json.loads(text) if text else None
            except urllib.error.HTTPError as e:
                error = _error(e)
                retry = e.code == 429 or (e.code == 503 and repeatable)
                if retry and attempt < self.max_retries:
                    time.sleep(error.retry_after if error.retry_after is not None else _backoff(attempt))
                    attempt += 1
                    continue
                raise error from None
            except (urllib.error.URLError, OSError) as e:
                if repeatable and attempt < self.max_retries:
                    time.sleep(_backoff(attempt))
                    attempt += 1
                    continue
                raise FeedFabricError(0, "network_error", str(getattr(e, "reason", e))) from e

    def _put(self, url: str, content: bytes, content_type: str) -> None:
        request = urllib.request.Request(url, data=content, method="PUT", headers={"Content-Type": content_type})
        try:
            with urllib.request.urlopen(request, timeout=self.timeout):
                pass
        except urllib.error.HTTPError as e:
            raise _error(e) from None


def _backoff(attempt: int) -> float:
    return min(0.25 * 2.0**attempt, 4.0) + random.random() / 10


def _error(e: urllib.error.HTTPError) -> FeedFabricError:
    try:
        body = json.loads(e.read().decode() or "{}")
    except ValueError:
        # Not Problem Details: a proxy or load balancer answered
        body = {}
    if not isinstance(body, dict):
        body = {}
    retry_after: Optional[float] = None
    header = e.headers.get("Retry-After")
    if header:
        try:
            retry_after = float(header)
        except ValueError:
            pass
    return FeedFabricError(
        e.code,
        body.get("code") or f"http_{e.code}",
        body.get("detail") or f"{e.code} {e.reason}",
        body.get("params"),
        body.get("errors"),
        body.get("traceId"),
        retry_after,
    )


class Member:
    """Everything a member can do, as that member."""

    def __init__(self, client: FeedFabric, headers: Dict[str, str]) -> None:
        self._client = client
        self._headers = headers
        self.posts = Posts(self)
        self.feeds = Feeds(self)
        self.comments = Comments(self)
        self.media = MediaUploads(self, client)
        self.signals = Signals(self)

    def _call(self, method: str, path: str, query: Optional[Mapping[str, Any]] = None, body: Any = None) -> Any:
        return self._client._request(method, path, self._headers, query, body)

    def me(self) -> Me:
        """Who FeedFabric thinks this is; also creates the member if they're new."""
        return cast(Me, self._call("GET", "/me"))

    def follow(self, external_user_id: str) -> None:
        self._call("PUT", f"/follows/{_quote(external_user_id)}")

    def unfollow(self, external_user_id: str) -> None:
        self._call("DELETE", f"/follows/{_quote(external_user_id)}")

    def favourites(self) -> List[Favourite]:
        return cast(List[Favourite], self._call("GET", "/favourites"))

    def add_favourite(self, external_user_id: str) -> None:
        """Up to 30; favourite.limit_reached after that."""
        self._call("PUT", f"/favourites/{_quote(external_user_id)}")

    def remove_favourite(self, external_user_id: str) -> None:
        self._call("DELETE", f"/favourites/{_quote(external_user_id)}")

    def data_types(self) -> List[DataTypeInfo]:
        """The registered data types and their schemas."""
        return cast(List[DataTypeInfo], self._call("GET", "/data-types"))

    def data_type(self, key: str) -> DataTypeInfo:
        return cast(DataTypeInfo, self._call("GET", f"/data-types/{_quote(key)}"))

    def export_account(self) -> Dict[str, Any]:
        """Everything held about the member, as one JSON document. A few an hour."""
        return cast(Dict[str, Any], self._call("GET", "/me/export"))

    def delete_account(self) -> None:
        """Erases the member: posts, comments and profile at once, the rest shortly after."""
        self._call("DELETE", "/me")


def _post_body(content: Optional[str], tags: Optional[List[str]], media_ids: Optional[List[str]], data: Optional[PostDataInput]) -> Dict[str, Any]:
    body: Dict[str, Any] = {}
    if content is not None:
        body["content"] = content
    if tags is not None:
        body["tags"] = tags
    if media_ids is not None:
        body["mediaIds"] = media_ids
    if data is not None:
        body["data"] = data
    return body


class Posts:
    def __init__(self, member: Member) -> None:
        self._m = member

    def create(
        self,
        content: Optional[str] = None,
        *,
        tags: Optional[List[str]] = None,
        media_ids: Optional[List[str]] = None,
        data: Optional[PostDataInput] = None,
    ) -> SavedPost:
        """content is required unless the post has data. tags are your app's own, written like hashtags without the #.
        data is structured data of a registered type: DataType.of builds it."""
        return cast(SavedPost, self._m._call("POST", "/userfeed/posts", body=_post_body(content, tags, media_ids, data)))

    def edit(
        self,
        post_id: str,
        content: Optional[str] = None,
        *,
        tags: Optional[List[str]] = None,
        data: Optional[PostDataInput] = None,
    ) -> SavedPost:
        """Within an hour of posting. Leaving tags or data out keeps the post's."""
        return cast(SavedPost, self._m._call("PUT", f"/userfeed/posts/{_quote(post_id)}", body=_post_body(content, tags, None, data)))

    def delete(self, post_id: str) -> None:
        self._m._call("DELETE", f"/userfeed/posts/{_quote(post_id)}")

    def list(
        self,
        *,
        type: Optional[str] = None,
        order: Optional[Literal["newest", "oldest"]] = None,
        limit: Optional[int] = None,
        cursor: Optional[str] = None,
        gt: Optional[str] = None,
        gte: Optional[str] = None,
        lt: Optional[str] = None,
        lte: Optional[str] = None,
    ) -> OwnPostsPage:
        """The member's own posts; type keeps only those with data of that type."""
        query = {"type": type, "order": order, "limit": limit, "cursor": cursor, "gt": gt, "gte": gte, "lt": lt, "lte": lte}
        return cast(OwnPostsPage, self._m._call("GET", "/userfeed", query))

    def like(self, post_id: str) -> None:
        self._m._call("PUT", f"/userfeed/posts/{_quote(post_id)}/like")

    def unlike(self, post_id: str) -> None:
        self._m._call("DELETE", f"/userfeed/posts/{_quote(post_id)}/like")

    def repost(self, post_id: str) -> None:
        self._m._call("PUT", f"/userfeed/posts/{_quote(post_id)}/repost")

    def unrepost(self, post_id: str) -> None:
        self._m._call("DELETE", f"/userfeed/posts/{_quote(post_id)}/repost")

    def report(self, post_id: str, reason: ReportReason) -> None:
        self._m._call("POST", f"/userfeed/posts/{_quote(post_id)}/report", body={"reason": reason})


class Feeds:
    def __init__(self, member: Member) -> None:
        self._m = member

    def home(self, *, limit: Optional[int] = None, cursor: Optional[str] = None) -> FeedPage:
        """Posts from the accounts the member follows, newest first."""
        return cast(FeedPage, self._m._call("GET", "/userfeed/home", {"limit": limit, "cursor": cursor}))

    def for_you(self, *, profile: Optional[str] = None, limit: Optional[int] = None, cursor: Optional[str] = None) -> ForYouPage:
        """Ranked for the member; profile picks a ranking profile."""
        return cast(ForYouPage, self._m._call("GET", "/userfeed/foryou", {"profile": profile, "limit": limit, "cursor": cursor}))

    def favourites(self, *, limit: Optional[int] = None, cursor: Optional[str] = None) -> FeedPage:
        return cast(FeedPage, self._m._call("GET", "/userfeed/favourites", {"limit": limit, "cursor": cursor}))

    def tag(self, tag: str, *, limit: Optional[int] = None, cursor: Optional[str] = None) -> FeedPage:
        """Posts with a tag (hashtag, inferred topic or app tag), newest first."""
        return cast(FeedPage, self._m._call("GET", f"/userfeed/tags/{_quote(tag)}/posts", {"limit": limit, "cursor": cursor}))

    def trending_tags(self, *, hours: Optional[int] = None, limit: Optional[int] = None) -> List[TrendingTag]:
        return cast(List[TrendingTag], self._m._call("GET", "/userfeed/tags/trending", {"hours": hours, "limit": limit}))

    def mentions(self, *, limit: Optional[int] = None, cursor: Optional[str] = None) -> MentionPage:
        return cast(MentionPage, self._m._call("GET", "/userfeed/mentions", {"limit": limit, "cursor": cursor}))


class Comments:
    def __init__(self, member: Member) -> None:
        self._m = member

    def list(self, post_id: str, *, limit: Optional[int] = None, cursor: Optional[str] = None) -> CommentPage:
        """Top-level comments, oldest first."""
        return cast(CommentPage, self._m._call("GET", f"/userfeed/posts/{_quote(post_id)}/comments", {"limit": limit, "cursor": cursor}))

    def replies(self, comment_id: str, *, limit: Optional[int] = None, cursor: Optional[str] = None) -> CommentPage:
        return cast(CommentPage, self._m._call("GET", f"/userfeed/comments/{_quote(comment_id)}/replies", {"limit": limit, "cursor": cursor}))

    def create(self, post_id: str, content: str, *, parent_comment_id: Optional[str] = None) -> Comment:
        body: Dict[str, Any] = {"content": content}
        if parent_comment_id:
            body["parentCommentId"] = parent_comment_id
        return cast(Comment, self._m._call("POST", f"/userfeed/posts/{_quote(post_id)}/comments", body=body))

    def delete(self, comment_id: str) -> None:
        self._m._call("DELETE", f"/userfeed/comments/{_quote(comment_id)}")

    def report(self, comment_id: str, reason: ReportReason) -> None:
        self._m._call("POST", f"/userfeed/comments/{_quote(comment_id)}/report", body={"reason": reason})


class MediaUploads:
    def __init__(self, member: Member, client: FeedFabric) -> None:
        self._m = member
        self._client = client

    def create_upload(self, content_type: str, size: int) -> Upload:
        """Asks for an upload URL: then PUT the file there and call complete."""
        return cast(Upload, self._m._call("POST", "/media/uploads", body={"contentType": content_type, "size": size}))

    def complete(self, media_id: str) -> Media:
        return cast(Media, self._m._call("POST", f"/media/{_quote(media_id)}/complete"))

    def get(self, media_id: str) -> Media:
        return cast(Media, self._m._call("GET", f"/media/{_quote(media_id)}"))

    def upload(self, content: Union[bytes, bytearray], content_type: str, *, timeout: float = 60.0, poll: float = 1.0) -> Media:
        """The whole upload: asks for a URL, sends the file, and waits for scanning. Returns the ready image, for a
        post's media_ids; raises FeedFabricError "media.rejected" if it's refused."""
        upload = self.create_upload(content_type, len(content))
        self._client._put(upload["uploadUrl"], bytes(content), upload["contentType"])
        media = self.complete(upload["mediaId"])
        deadline = time.monotonic() + timeout
        while media["status"] in ("processing", "awaitingUpload"):
            if time.monotonic() > deadline:
                raise FeedFabricError(0, "media.timeout", "The image is still being checked; try get() later.", {"mediaId": media["id"]})
            time.sleep(poll)
            media = self.get(upload["mediaId"])
        if media["status"] == "rejected":
            raise FeedFabricError(
                422, "media.rejected", f"The image was rejected: {media.get('rejectionReason') or 'unknown reason'}",
                {"mediaId": media["id"], "reason": media.get("rejectionReason")},
            )
        return media


class Signals:
    """What the member's reading tells the ranker. Send them from your server as your app reports them."""

    def __init__(self, member: Member) -> None:
        self._m = member

    def impressions(self, post_ids: List[str]) -> Counted:
        """Posts the member saw (at least half on screen for a second); up to 100 at a time."""
        return cast(Counted, self._m._call("POST", "/userfeed/impressions", body={"postIds": post_ids}))

    def open(self, post_id: str) -> Counted:
        """The member opened the post's own page."""
        return cast(Counted, self._m._call("POST", f"/userfeed/posts/{_quote(post_id)}/open"))

    def dwell(self, views: List[DwellView]) -> Counted:
        """How long posts stayed on screen; up to 100 at a time."""
        return cast(Counted, self._m._call("POST", "/userfeed/dwell", body={"views": views}))

    def not_interested(self, post_id: str) -> None:
        self._m._call("POST", f"/userfeed/posts/{_quote(post_id)}/not-interested")

    def show_more(self, post_id: str) -> None:
        self._m._call("POST", f"/userfeed/posts/{_quote(post_id)}/show-more")
