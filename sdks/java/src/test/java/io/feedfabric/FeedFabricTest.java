package io.feedfabric;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.sun.net.httpserver.Headers;
import com.sun.net.httpserver.HttpServer;
import io.feedfabric.Types.PostData;
import io.feedfabric.Types.PostInput;
import io.feedfabric.Types.Profile;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayDeque;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CopyOnWriteArrayList;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class FeedFabricTest {
    // A stand-in API: records each request and answers with whatever the test queued
    record Seen(String method, String path, Headers headers, String body) {
    }

    record Reply(int status, Object body, String retryAfter) {
    }

    record WeightReading(double kg, String note) {
    }

    static final DataType<WeightReading> WEIGHT = DataType.define("weight-reading", WeightReading.class);
    static final ObjectMapper JSON = new ObjectMapper();
    static final Map<String, Object> SAVED = Map.of("id", "p1", "authorId", "a1", "content", "", "createdAt", "2026-11-10T09:00:00Z",
        "isHeld", false, "data", Map.of("type", "weight-reading", "version", 2, "value", Map.of("kg", 82.4)));

    final List<Seen> seen = new CopyOnWriteArrayList<>();
    final ArrayDeque<Reply> replies = new ArrayDeque<>();
    HttpServer server;
    String url;

    @BeforeEach
    void start() throws IOException {
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/", exchange -> {
            var body = new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
            seen.add(new Seen(exchange.getRequestMethod(), exchange.getRequestURI().getRawPath()
                + (exchange.getRequestURI().getRawQuery() == null ? "" : "?" + exchange.getRequestURI().getRawQuery()),
                exchange.getRequestHeaders(), body));
            Reply reply;
            synchronized (replies) {
                reply = replies.isEmpty() ? new Reply(204, null, null) : replies.poll();
            }
            var payload = reply.body() == null ? new byte[0] : JSON.writeValueAsBytes(reply.body());
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            if (reply.retryAfter() != null) {
                exchange.getResponseHeaders().set("Retry-After", reply.retryAfter());
            }
            exchange.sendResponseHeaders(reply.status(), payload.length == 0 ? -1 : payload.length);
            exchange.getResponseBody().write(payload);
            exchange.close();
        });
        server.start();
        url = "http://127.0.0.1:" + server.getAddress().getPort();
    }

    @AfterEach
    void stop() {
        server.stop(0);
    }

    void reply(int status, Object body) {
        reply(status, body, null);
    }

    void reply(int status, Object body, String retryAfter) {
        synchronized (replies) {
            replies.add(new Reply(status, body, retryAfter));
        }
    }

    Member member() {
        return new FeedFabric(url, "ffk_test").asMember("u1");
    }

    @Test
    void actsForTheMemberWithTheKeyAndEncodedProfile() {
        reply(200, Map.of("externalUserId", "user 1"));

        var me = new FeedFabric(url + "/", "ffk_test").asMember("user 1", new Profile(null, "Zoë Brontë", null)).me();

        assertEquals("user 1", me.externalUserId());
        var s = seen.get(0);
        assertEquals("GET /me", s.method() + " " + s.path());
        assertEquals("Bearer ffk_test", s.headers().getFirst("Authorization"));
        assertEquals("user%201", s.headers().getFirst("FeedFabric-User"));
        assertEquals("Zoë Brontë", URLDecoder.decode(s.headers().getFirst("FeedFabric-Display-Name"), StandardCharsets.UTF_8));
        assertFalse(s.headers().containsKey("FeedFabric-Username"));
    }

    @Test
    void postsTypedDataAndAppTagsAndReadsItBack() throws IOException {
        reply(201, SAVED);

        var post = member().posts().create(PostInput.data(WEIGHT.of(new WeightReading(82.4, null))).withTags("weight"));

        assertEquals(JSON.readTree("{\"tags\":[\"weight\"],\"data\":{\"type\":\"weight-reading\",\"value\":{\"kg\":82.4}}}"),
            JSON.readTree(seen.get(0).body()));
        assertEquals(new WeightReading(82.4, null), WEIGHT.read(post.data()).orElseThrow());
        assertTrue(DataType.define("steps", new TypeReference<Map<String, Integer>>() { }).read(post.data()).isEmpty());
        assertFalse(WEIGHT.is(null));
    }

    @Test
    void readThrowsForDataThatIsNotTheType() throws IOException {
        var data = new PostData("weight-reading", 1, JSON.readTree("{\"kg\":\"heavy\"}"));

        assertThrows(IllegalArgumentException.class, () -> WEIGHT.read(data));
    }

    @Test
    void buildsQueryStringsAndLeavesOutWhatIsUnset() {
        reply(200, Map.of("posts", List.of()));

        member().posts().list(new Types.ListQuery().type("weight-reading").order("oldest").limit(5));

        assertEquals("/userfeed?type=weight-reading&order=oldest&limit=5", seen.get(0).path());
    }

    @Test
    void errorsCarryTheCodeParamsAndFieldErrors() {
        reply(400, Map.of("code", "post.data_invalid", "detail", "nope", "params", Map.of("type", "weight-reading"),
            "errors", List.of(Map.of("path", "/kg", "message", "should be a number")), "traceId", "t-1"));

        var error = assertThrows(FeedFabricException.class,
            () -> member().posts().create(PostInput.data(WEIGHT.of(new WeightReading(1, null)))));

        assertEquals(400, error.status());
        assertEquals("post.data_invalid", error.code());
        assertEquals("t-1", error.traceId());
        assertEquals("weight-reading", error.params().get("type").asText());
        assertEquals("/kg", error.errors().get(0).path("path").asText());
    }

    @Test
    void waitsOutA429EvenForAPost() {
        reply(429, Map.of("code", "rate_limited"), "0");
        reply(202, Map.of("counted", 2));

        assertEquals(2, member().signals().impressions(List.of("p1", "p2")).counted());
        assertEquals(2, seen.size());
    }

    @Test
    void repeatsAGetAfterA503ButNotAPost() {
        var member = member();
        reply(503, Map.of("code", "service_unavailable"), "0");
        assertThrows(FeedFabricException.class, () -> member.posts().create(PostInput.text("hi")));
        assertEquals(1, seen.size());

        reply(503, Map.of("code", "service_unavailable"), "0");
        reply(200, List.of());
        assertTrue(member.favourites().isEmpty());
        assertEquals(3, seen.size());
    }

    @Test
    void uploadsAnImageAndWaitsUntilItIsReady() {
        reply(201, Map.of("mediaId", "m1", "uploadUrl", url + "/blobs/m1", "contentType", "image/png", "expiresAt", "2026-11-10T10:00:00Z"));
        reply(200, null);
        reply(202, Map.of("id", "m1", "status", "processing"));
        reply(200, Map.of("id", "m1", "status", "ready", "url", "u", "thumbnailUrl", "t", "width", 1, "height", 1));
        var client = new FeedFabric(url, "ffk_test");
        client.uploadPoll = Duration.ofMillis(10);

        var media = client.asMember("u1").media().upload(new byte[] {1, 2, 3}, "image/png");

        assertEquals("ready", media.status());
        assertEquals(List.of("POST /media/uploads", "PUT /blobs/m1", "POST /media/m1/complete", "GET /media/m1"),
            seen.stream().map(s -> s.method() + " " + s.path()).toList());
        assertNull(seen.get(1).headers().getFirst("Authorization"));
    }

    @Test
    void refusesAWrongLookingKey() {
        assertThrows(IllegalArgumentException.class, () -> new FeedFabric("http://localhost", "eyJhbGciOi..."));
    }
}
