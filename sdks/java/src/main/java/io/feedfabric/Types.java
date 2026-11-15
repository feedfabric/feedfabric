package io.feedfabric;

import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.annotation.JsonProperty;
import com.fasterxml.jackson.annotation.JsonValue;
import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Map;

/** The API's shapes, as the member routes return them. IDs are UUID strings; times are ISO 8601 UTC strings. */
public final class Types {
    private Types() {
    }

    /** Why a post or comment is reported. */
    public enum ReportReason {
        SPAM, HARASSMENT, HATE, THREAT, SEXUAL, SELF_HARM, OTHER;

        @JsonValue
        public String wire() {
            return name().toLowerCase();
        }
    }

    /** One of a post's tags. Source is "hashtag", "inferred" or "app" (set by your app in a post's tags). */
    public record Tag(String tag, String source) {
    }

    public record PostMedia(String id, String url, String thumbnailUrl, int width, int height) {
    }

    /**
     * A post's structured data, of a type in GET /data-types. A {@link DataType} reads it as your own type.
     *
     * @param version the data type's version the value was checked against
     */
    public record PostData(String type, int version, JsonNode value) {
    }

    /** A post's data as you send it; {@link DataType#of} builds one. */
    public record PostDataInput(String type, JsonNode value) {
    }

    public record RankingFactor(String label, double value, String detail) {
    }

    /** Only on For You posts: why the post is where it is. */
    public record PostRanking(String ranker, int position, double score, boolean explored, List<RankingFactor> factors,
                              Map<String, Double> features) {
    }

    /** A post as feeds return it. */
    public record Post(String id, String authorId, String authorExternalUserId, String authorUsername, String content,
                       String createdAt, String editedAt, String repostOfId, Post repostOf, boolean isHeld, List<Tag> tags,
                       List<PostMedia> media, PostData data, PostRanking ranking, int likes, int comments, int reposts,
                       boolean liked, boolean reposted) {
    }

    /** A post as creating, editing and listing your own posts return it. */
    public record SavedPost(String id, String authorId, String content, String createdAt, String editedAt, String repostOfId,
                            boolean isHeld, PostData data) {
    }

    /** @param nextCursor pass back as the cursor for the next page; null at the end */
    public record FeedPage(List<Post> posts, String nextCursor, String source) {
    }

    public record ForYouPage(List<Post> posts, String nextCursor, String ranker, String profile, int candidates) {
    }

    public record OwnPostsPage(List<SavedPost> posts, String nextCursor) {
    }

    /** @param content null once deleted or removed */
    public record Comment(String id, String postId, String parentCommentId, String authorId, String authorExternalUserId,
                          String authorUsername, String content, boolean isDeleted, boolean isHeld, String createdAt,
                          int replyCount) {
    }

    public record CommentPage(List<Comment> comments, String nextCursor) {
    }

    public record Mention(String id, String createdAt, String authorExternalUserId, String authorUsername, String postId,
                          String commentId, String content) {
    }

    public record MentionPage(List<Mention> mentions, String nextCursor) {
    }

    public record TrendingTag(String tag, int posts) {
    }

    public record Favourite(String externalUserId, String username, String displayName, String avatarUrl, String addedAt) {
    }

    public record Upload(String mediaId, String uploadUrl, String contentType, String expiresAt) {
    }

    /** An upload. Status is "awaitingUpload", "processing", "ready" or "rejected". */
    public record Media(String id, String status, String rejectionReason, Integer width, Integer height, String url,
                        String thumbnailUrl) {
    }

    /** @param schema the JSON Schema posts' data of this type is checked against */
    public record DataTypeInfo(String key, String label, String description, int version, JsonNode schema, String updatedAt,
                               String updatedBy) {
    }

    /** How many signals were counted (repeats within 30 minutes aren't). */
    public record Counted(int counted) {
    }

    public record DwellView(String postId, int ms) {
    }

    public record Me(String externalUserId) {
    }

    /** A member's profile: used when FeedFabric first sees them, and refreshing theirs at most hourly. Any part may be null. */
    public record Profile(String username, String displayName, String avatarUrl) {
        public static final Profile NONE = new Profile(null, null, null);
    }

    /**
     * A post to create or edit; build one with {@link #text} or {@link #data(PostDataInput)} and the with-methods.
     *
     * @param content  required unless the post has data
     * @param tags     your app's own tags, written like hashtags without the #; up to 10. On an edit, null keeps the post's.
     * @param mediaIds ready images from {@link Member.MediaUploads#upload}, in order. Only when creating.
     * @param data     structured data of a registered type. On an edit, null keeps the post's.
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record PostInput(String content, List<String> tags, @JsonProperty("mediaIds") List<String> mediaIds,
                            PostDataInput data) {
        public static PostInput text(String content) {
            return new PostInput(content, null, null, null);
        }

        public static PostInput data(PostDataInput data) {
            return new PostInput(null, null, null, data);
        }

        public PostInput withContent(String content) {
            return new PostInput(content, tags, mediaIds, data);
        }

        public PostInput withTags(String... tags) {
            return new PostInput(content, List.of(tags), mediaIds, data);
        }

        public PostInput withMedia(String... mediaIds) {
            return new PostInput(content, tags, List.of(mediaIds), data);
        }

        public PostInput withData(PostDataInput data) {
            return new PostInput(content, tags, mediaIds, data);
        }
    }

    /** Which of the member's own posts to list; null parts are left to the API. */
    public static final class ListQuery {
        String type;
        String order;
        Integer limit;
        String cursor;
        String gt;
        String gte;
        String lt;
        String lte;

        /** Only posts with data of this type. */
        public ListQuery type(String type) {
            this.type = type;
            return this;
        }

        /** "newest" (the default) or "oldest". */
        public ListQuery order(String order) {
            this.order = order;
            return this;
        }

        public ListQuery limit(int limit) {
            this.limit = limit;
            return this;
        }

        public ListQuery cursor(String cursor) {
            this.cursor = cursor;
            return this;
        }

        // Post IDs are time-ordered (UUIDv7): these bound the range

        public ListQuery gt(String postId) {
            this.gt = postId;
            return this;
        }

        public ListQuery gte(String postId) {
            this.gte = postId;
            return this;
        }

        public ListQuery lt(String postId) {
            this.lt = postId;
            return this;
        }

        public ListQuery lte(String postId) {
            this.lte = postId;
            return this;
        }
    }
}
