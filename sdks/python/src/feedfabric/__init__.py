"""FeedFabric for your servers: posts, feeds, comments, signals and typed post data, with an API key."""

from .client import VERSION, FeedFabric, Member
from .data_type import DataType
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
    Mention,
    MentionPage,
    OwnPostsPage,
    Post,
    PostData,
    PostDataInput,
    PostMedia,
    PostRanking,
    RankingFactor,
    ReportReason,
    SavedPost,
    Tag,
    TrendingTag,
    Upload,
)

__version__ = VERSION
__all__ = [
    "VERSION", "FeedFabric", "Member", "DataType", "FeedFabricError", "Comment", "CommentPage", "Counted", "DataTypeInfo", "DwellView",
    "Favourite", "FeedPage", "ForYouPage", "Me", "Media", "Mention", "MentionPage", "OwnPostsPage", "Post", "PostData", "PostDataInput",
    "PostMedia", "PostRanking", "RankingFactor", "ReportReason", "SavedPost", "Tag", "TrendingTag", "Upload",
]
