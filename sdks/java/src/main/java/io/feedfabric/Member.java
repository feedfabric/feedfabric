package io.feedfabric;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.JavaType;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.TextNode;
import io.feedfabric.Types.Comment;
import io.feedfabric.Types.CommentPage;
import io.feedfabric.Types.Counted;
import io.feedfabric.Types.DataTypeInfo;
import io.feedfabric.Types.DwellView;
import io.feedfabric.Types.Favourite;
import io.feedfabric.Types.FeedPage;
import io.feedfabric.Types.ForYouPage;
import io.feedfabric.Types.ListQuery;
import io.feedfabric.Types.Me;
import io.feedfabric.Types.Media;
import io.feedfabric.Types.MentionPage;
import io.feedfabric.Types.OwnPostsPage;
import io.feedfabric.Types.PostInput;
import io.feedfabric.Types.ReportReason;
import io.feedfabric.Types.SavedPost;
import io.feedfabric.Types.TrendingTag;
import io.feedfabric.Types.Upload;
import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Everything a member can do, as that member. */
public final class Member {
    private final FeedFabric client;
    private final Map<String, String> headers;
    private final Posts posts = new Posts();
    private final Feeds feeds = new Feeds();
    private final Comments comments = new Comments();
    private final MediaUploads media = new MediaUploads();
    private final Signals signals = new Signals();

    Member(FeedFabric client, Map<String, String> headers) {
        this.client = client;
        this.headers = headers;
    }

    public Posts posts() {
        return posts;
    }

    public Feeds feeds() {
        return feeds;
    }

    public Comments comments() {
        return comments;
    }

    public MediaUploads media() {
        return media;
    }

    public Signals signals() {
        return signals;
    }

    private <T> T call(String method, String path, Object body, Class<T> type) {
        return client.send(method, path, headers, body, type == null ? null : DataType.Json.MAPPER.constructType(type));
    }

    private <T> T call(String method, String path, Object body, TypeReference<T> type) {
        JavaType javaType = DataType.Json.MAPPER.constructType(type);
        return client.send(method, path, headers, body, javaType);
    }

    private void call(String method, String path, Object body) {
        call(method, path, body, (Class<Void>) null);
    }

    private static String esc(String value) {
        return FeedFabric.escape(value);
    }

    private static String query(Object... namesAndValues) {
        var query = new StringBuilder();
        for (var i = 0; i + 1 < namesAndValues.length; i += 2) {
            var value = namesAndValues[i + 1];
            if (value != null && !value.toString().isEmpty()) {
                query.append(query.length() == 0 ? '?' : '&').append(namesAndValues[i]).append('=').append(esc(value.toString()));
            }
        }
        return query.toString();
    }

    /** Who FeedFabric thinks this is; also creates the member if they're new. */
    public Me me() {
        return call("GET", "/me", null, Me.class);
    }

    public void follow(String externalUserId) {
        call("PUT", "/follows/" + esc(externalUserId), null);
    }

    public void unfollow(String externalUserId) {
        call("DELETE", "/follows/" + esc(externalUserId), null);
    }

    public List<Favourite> favourites() {
        return call("GET", "/favourites", null, new TypeReference<List<Favourite>>() { });
    }

    /** Up to 30; favourite.limit_reached after that. */
    public void addFavourite(String externalUserId) {
        call("PUT", "/favourites/" + esc(externalUserId), null);
    }

    public void removeFavourite(String externalUserId) {
        call("DELETE", "/favourites/" + esc(externalUserId), null);
    }

    /** The registered data types and their schemas. */
    public List<DataTypeInfo> dataTypes() {
        return call("GET", "/data-types", null, new TypeReference<List<DataTypeInfo>>() { });
    }

    public DataTypeInfo dataType(String key) {
        return call("GET", "/data-types/" + esc(key), null, DataTypeInfo.class);
    }

    /** Everything held about the member, as one JSON document. A few an hour. */
    public JsonNode exportAccount() {
        return call("GET", "/me/export", null, JsonNode.class);
    }

    /** Erases the member: posts, comments and profile at once, the rest shortly after. */
    public void deleteAccount() {
        call("DELETE", "/me", null);
    }

    public final class Posts {
        private Posts() {
        }

        public SavedPost create(PostInput post) {
            return call("POST", "/userfeed/posts", post, SavedPost.class);
        }

        /** Within an hour of posting. Null tags or data keep the post's; media IDs are ignored. */
        public SavedPost edit(String postId, PostInput post) {
            return call("PUT", "/userfeed/posts/" + esc(postId), new PostInput(post.content(), post.tags(), null, post.data()),
                SavedPost.class);
        }

        public void delete(String postId) {
            call("DELETE", "/userfeed/posts/" + esc(postId), null);
        }

        /** The member's own posts. */
        public OwnPostsPage list(ListQuery q) {
            return call("GET", "/userfeed" + query("type", q.type, "order", q.order, "limit", q.limit, "cursor", q.cursor, "gt", q.gt,
                "gte", q.gte, "lt", q.lt, "lte", q.lte), null, OwnPostsPage.class);
        }

        public OwnPostsPage list() {
            return list(new ListQuery());
        }

        public void like(String postId) {
            call("PUT", "/userfeed/posts/" + esc(postId) + "/like", null);
        }

        public void unlike(String postId) {
            call("DELETE", "/userfeed/posts/" + esc(postId) + "/like", null);
        }

        public void repost(String postId) {
            call("PUT", "/userfeed/posts/" + esc(postId) + "/repost", null);
        }

        public void unrepost(String postId) {
            call("DELETE", "/userfeed/posts/" + esc(postId) + "/repost", null);
        }

        public void report(String postId, ReportReason reason) {
            call("POST", "/userfeed/posts/" + esc(postId) + "/report", Map.of("reason", reason));
        }
    }

    /** Feeds; a null limit or cursor is left to the API. */
    public final class Feeds {
        private Feeds() {
        }

        /** Posts from the accounts the member follows, newest first. */
        public FeedPage home(Integer limit, String cursor) {
            return call("GET", "/userfeed/home" + query("limit", limit, "cursor", cursor), null, FeedPage.class);
        }

        /** Ranked for the member; profile (or null) picks a ranking profile. */
        public ForYouPage forYou(String profile, Integer limit, String cursor) {
            return call("GET", "/userfeed/foryou" + query("profile", profile, "limit", limit, "cursor", cursor), null, ForYouPage.class);
        }

        public FeedPage favourites(Integer limit, String cursor) {
            return call("GET", "/userfeed/favourites" + query("limit", limit, "cursor", cursor), null, FeedPage.class);
        }

        /** Posts with a tag (hashtag, inferred topic or app tag), newest first. */
        public FeedPage tag(String tag, Integer limit, String cursor) {
            return call("GET", "/userfeed/tags/" + esc(tag) + "/posts" + query("limit", limit, "cursor", cursor), null, FeedPage.class);
        }

        public List<TrendingTag> trendingTags(Integer hours, Integer limit) {
            return call("GET", "/userfeed/tags/trending" + query("hours", hours, "limit", limit), null,
                new TypeReference<List<TrendingTag>>() { });
        }

        public MentionPage mentions(Integer limit, String cursor) {
            return call("GET", "/userfeed/mentions" + query("limit", limit, "cursor", cursor), null, MentionPage.class);
        }
    }

    public final class Comments {
        private Comments() {
        }

        /** Top-level comments, oldest first. */
        public CommentPage list(String postId, Integer limit, String cursor) {
            return call("GET", "/userfeed/posts/" + esc(postId) + "/comments" + query("limit", limit, "cursor", cursor), null,
                CommentPage.class);
        }

        public CommentPage replies(String commentId, Integer limit, String cursor) {
            return call("GET", "/userfeed/comments/" + esc(commentId) + "/replies" + query("limit", limit, "cursor", cursor), null,
                CommentPage.class);
        }

        /** parentCommentId (or null) makes it a reply. */
        public Comment create(String postId, String content, String parentCommentId) {
            var body = new HashMap<String, Object>();
            body.put("content", content);
            if (parentCommentId != null) {
                body.put("parentCommentId", parentCommentId);
            }
            return call("POST", "/userfeed/posts/" + esc(postId) + "/comments", body, Comment.class);
        }

        public void delete(String commentId) {
            call("DELETE", "/userfeed/comments/" + esc(commentId), null);
        }

        public void report(String commentId, ReportReason reason) {
            call("POST", "/userfeed/comments/" + esc(commentId) + "/report", Map.of("reason", reason));
        }
    }

    public final class MediaUploads {
        private MediaUploads() {
        }

        /** Asks for an upload URL: then PUT the file there and call complete. */
        public Upload createUpload(String contentType, long size) {
            return call("POST", "/media/uploads", Map.of("contentType", contentType, "size", size), Upload.class);
        }

        public Media complete(String mediaId) {
            return call("POST", "/media/" + esc(mediaId) + "/complete", null, Media.class);
        }

        public Media get(String mediaId) {
            return call("GET", "/media/" + esc(mediaId), null, Media.class);
        }

        /**
         * The whole upload: asks for a URL, sends the file, and waits for scanning (a minute at most). Returns the
         * ready image, for a post's media IDs; throws a FeedFabricException "media.rejected" if it's refused, or
         * "media.timeout" if it's still being checked.
         */
        public Media upload(byte[] content, String contentType) {
            var upload = createUpload(contentType, content.length);
            client.putFile(upload.uploadUrl(), content, upload.contentType());
            var media = complete(upload.mediaId());
            var deadline = Instant.now().plus(Duration.ofMinutes(1));
            while (media.status().equals("processing") || media.status().equals("awaitingUpload")) {
                if (Instant.now().isAfter(deadline)) {
                    throw new FeedFabricException(0, "media.timeout", "The image is still being checked; try get() later.",
                        Map.of("mediaId", TextNode.valueOf(media.id())));
                }
                FeedFabric.sleep(client.uploadPoll);
                media = get(upload.mediaId());
            }
            if (media.status().equals("rejected")) {
                var reason = media.rejectionReason() == null ? "unknown reason" : media.rejectionReason();
                var params = new LinkedHashMap<String, JsonNode>();
                params.put("mediaId", TextNode.valueOf(media.id()));
                params.put("reason", TextNode.valueOf(media.rejectionReason()));
                throw new FeedFabricException(422, "media.rejected", "The image was rejected: " + reason, params);
            }
            return media;
        }
    }

    /** What the member's reading tells the ranker. Send them from your server as your app reports them. */
    public final class Signals {
        private Signals() {
        }

        /** Posts the member saw (at least half on screen for a second); up to 100 at a time. */
        public Counted impressions(List<String> postIds) {
            return call("POST", "/userfeed/impressions", Map.of("postIds", postIds), Counted.class);
        }

        /** The member opened the post's own page. */
        public Counted open(String postId) {
            return call("POST", "/userfeed/posts/" + esc(postId) + "/open", null, Counted.class);
        }

        /** How long posts stayed on screen; up to 100 at a time. */
        public Counted dwell(List<DwellView> views) {
            return call("POST", "/userfeed/dwell", Map.of("views", views), Counted.class);
        }

        public void notInterested(String postId) {
            call("POST", "/userfeed/posts/" + esc(postId) + "/not-interested", null);
        }

        public void showMore(String postId) {
            call("POST", "/userfeed/posts/" + esc(postId) + "/show-more", null);
        }
    }
}
