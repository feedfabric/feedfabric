"""The API's shapes, as the member routes return them. Times are ISO 8601 UTC strings; IDs are UUID strings."""

from __future__ import annotations

from typing import Any, Dict, List, Literal, Optional, TypedDict

ReportReason = Literal["spam", "harassment", "hate", "threat", "sexual", "self_harm", "other"]
MediaStatus = Literal["awaitingUpload", "processing", "ready", "rejected"]


class Tag(TypedDict):
    tag: str
    # "app" tags are the ones your app set in a post's tags
    source: Literal["hashtag", "inferred", "app"]


class PostMedia(TypedDict):
    id: str
    url: str
    thumbnailUrl: str
    width: int
    height: int


class PostData(TypedDict):
    """A post's structured data, of a type in GET /data-types. A DataType reads value as your own type."""

    type: str
    # The data type's version the value was checked against
    version: int
    value: Any


class PostDataInput(TypedDict):
    """A post's data as you send it; DataType.of builds one."""

    type: str
    value: Any


class RankingFactor(TypedDict):
    label: str
    value: float
    detail: str


class PostRanking(TypedDict):
    ranker: str
    position: int
    score: float
    explored: bool
    factors: List[RankingFactor]
    features: Dict[str, float]


class Post(TypedDict):
    """A post as feeds return it."""

    id: str
    authorId: str
    authorExternalUserId: str
    authorUsername: str
    content: str
    createdAt: str
    editedAt: Optional[str]
    repostOfId: Optional[str]
    repostOf: Optional[Post]
    # Held for review: only its author sees it
    isHeld: bool
    tags: List[Tag]
    media: List[PostMedia]
    data: Optional[PostData]
    ranking: Optional[PostRanking]
    likes: int
    comments: int
    reposts: int
    liked: bool
    reposted: bool


class SavedPost(TypedDict):
    """A post as creating, editing and listing your own posts return it."""

    id: str
    authorId: str
    content: str
    createdAt: str
    editedAt: Optional[str]
    repostOfId: Optional[str]
    isHeld: bool
    data: Optional[PostData]


class FeedPage(TypedDict):
    posts: List[Post]
    # Pass back as cursor for the next page; None at the end
    nextCursor: Optional[str]
    source: str


class ForYouPage(TypedDict):
    posts: List[Post]
    nextCursor: Optional[str]
    ranker: str
    profile: str
    candidates: int


class OwnPostsPage(TypedDict):
    posts: List[SavedPost]
    nextCursor: Optional[str]


class Comment(TypedDict):
    id: str
    postId: str
    parentCommentId: Optional[str]
    authorId: str
    authorExternalUserId: str
    authorUsername: str
    # None once deleted or removed
    content: Optional[str]
    isDeleted: bool
    isHeld: bool
    createdAt: str
    replyCount: int


class CommentPage(TypedDict):
    comments: List[Comment]
    nextCursor: Optional[str]


class Mention(TypedDict):
    id: str
    createdAt: str
    authorExternalUserId: str
    authorUsername: str
    postId: str
    commentId: Optional[str]
    content: str


class MentionPage(TypedDict):
    mentions: List[Mention]
    nextCursor: Optional[str]


class TrendingTag(TypedDict):
    tag: str
    posts: int


class Favourite(TypedDict):
    externalUserId: str
    username: str
    displayName: Optional[str]
    avatarUrl: Optional[str]
    addedAt: str


class Upload(TypedDict):
    mediaId: str
    uploadUrl: str
    contentType: str
    expiresAt: str


class Media(TypedDict):
    id: str
    status: MediaStatus
    rejectionReason: Optional[str]
    width: Optional[int]
    height: Optional[int]
    url: Optional[str]
    thumbnailUrl: Optional[str]


class DataTypeInfo(TypedDict):
    key: str
    label: str
    description: Optional[str]
    version: int
    # The JSON Schema posts' data of this type is checked against
    schema: Dict[str, Any]
    updatedAt: str
    updatedBy: Optional[str]


class Counted(TypedDict):
    counted: int


class DwellView(TypedDict):
    postId: str
    ms: int


class Me(TypedDict):
    externalUserId: str
