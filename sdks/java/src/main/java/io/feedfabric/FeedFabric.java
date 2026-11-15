package io.feedfabric;

import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.JavaType;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.feedfabric.Types.Profile;
import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ThreadLocalRandom;

/**
 * The SDK's entry point: one per process, shared by every request and safe for concurrent use.
 *
 * <pre>{@code
 * var feed = new FeedFabric("https://feed.example.com", System.getenv("FEEDFABRIC_API_KEY"));
 * var member = feed.asMember("user-123", new Profile("sam", "Sam", null));
 * var post = member.posts().create(PostInput.text("Hello #intro"));
 * }</pre>
 */
public final class FeedFabric {
    public static final String VERSION = "0.1.0";

    private final String baseUrl;
    private final String apiKey;
    private final HttpClient http;
    private final Duration timeout;
    private final int maxRetries;
    // How often uploads check whether the image is ready; shortened in tests
    Duration uploadPoll = Duration.ofSeconds(1);

    /** baseUrl is where the API runs; apiKey is a key from the admin panel (Integrations → API keys), "ffk_…". */
    public FeedFabric(String baseUrl, String apiKey) {
        this(baseUrl, apiKey, Duration.ofSeconds(30), 2);
    }

    /**
     * @param timeout    per request
     * @param maxRetries retries after a 429 or 503 (waiting Retry-After), and after network errors on requests that are
     *                   safe to repeat
     */
    public FeedFabric(String baseUrl, String apiKey, Duration timeout, int maxRetries) {
        if (baseUrl == null || baseUrl.isEmpty()) {
            throw new IllegalArgumentException("FeedFabric needs a base URL");
        }
        if (apiKey == null || !apiKey.startsWith("ffk_")) {
            throw new IllegalArgumentException("FeedFabric needs an API key from the admin panel (\"ffk_…\")");
        }
        this.baseUrl = baseUrl.replaceAll("/+$", "");
        this.apiKey = apiKey;
        this.timeout = timeout;
        this.maxRetries = maxRetries;
        this.http = HttpClient.newBuilder().connectTimeout(timeout).build();
    }

    /** Calls made for one of your members: their ID is the one your sign-in tokens carry (Auth:Claims:UserId). */
    public Member asMember(String externalUserId) {
        return asMember(externalUserId, Profile.NONE);
    }

    /** As {@link #asMember(String)}, with the member's profile for when FeedFabric first sees them. */
    public Member asMember(String externalUserId, Profile profile) {
        if (externalUserId == null || externalUserId.isEmpty()) {
            throw new IllegalArgumentException("asMember needs the member's ID");
        }
        var headers = new LinkedHashMap<String, String>();
        headers.put("FeedFabric-User", escape(externalUserId));
        putIfSet(headers, "FeedFabric-Username", profile.username());
        putIfSet(headers, "FeedFabric-Display-Name", profile.displayName());
        putIfSet(headers, "FeedFabric-Avatar", profile.avatarUrl());
        return new Member(this, Map.copyOf(headers));
    }

    private static void putIfSet(Map<String, String> headers, String name, String value) {
        if (value != null && !value.isEmpty()) {
            headers.put(name, escape(value));
        }
    }

    static String escape(String value) {
        // URLEncoder is for forms: a space is "+" there, "%20" in a path or header
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }

    static ObjectMapper mapper() {
        return new ObjectMapper()
            .configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false)
            .setDefaultPropertyInclusion(JsonInclude.Value.construct(JsonInclude.Include.NON_NULL, JsonInclude.Include.NON_NULL));
    }

    <T> T send(String method, String path, Map<String, String> headers, Object body, JavaType type) {
        byte[] payload;
        try {
            payload = body == null ? null : DataType.Json.MAPPER.writeValueAsBytes(body);
        } catch (JsonProcessingException e) {
            throw new IllegalArgumentException("Couldn't write the request as JSON", e);
        }
        var builder = HttpRequest.newBuilder(URI.create(baseUrl + path))
            .timeout(timeout)
            .header("Authorization", "Bearer " + apiKey)
            .header("Accept", "application/json")
            .header("User-Agent", "feedfabric-java/" + VERSION)
            .method(method, payload == null ? HttpRequest.BodyPublishers.noBody() : HttpRequest.BodyPublishers.ofByteArray(payload));
        if (payload != null) {
            builder.header("Content-Type", "application/json");
        }
        headers.forEach(builder::header);
        var request = builder.build();
        // Safe to send twice: everything but POST. A 429 means nothing happened, so that's always safe.
        var repeatable = !method.equals("POST");

        for (var attempt = 0; ; attempt++) {
            HttpResponse<byte[]> response;
            try {
                response = http.send(request, HttpResponse.BodyHandlers.ofByteArray());
            } catch (IOException e) {
                if (repeatable && attempt < maxRetries) {
                    sleep(backoff(attempt));
                    continue;
                }
                throw new FeedFabricException(0, "network_error", String.valueOf(e.getMessage()), null, null, null, null, e);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new FeedFabricException(0, "network_error", "Interrupted", null, null, null, null, e);
            }

            var status = response.statusCode();
            if (status >= 300) {
                var error = errorFrom(response);
                var retry = status == 429 || (status == 503 && repeatable);
                if (retry && attempt < maxRetries) {
                    sleep(error.retryAfter() != null ? error.retryAfter() : backoff(attempt));
                    continue;
                }
                throw error;
            }
            if (type == null || response.body().length == 0) {
                return null;
            }
            try {
                return DataType.Json.MAPPER.readValue(response.body(), type);
            } catch (IOException e) {
                throw new FeedFabricException(status, "invalid_response", "Couldn't read the response: " + e.getMessage(), null,
                    null, null, null, e);
            }
        }
    }

    // To the storage's own signed URL, which doesn't take the API key
    void putFile(String url, byte[] content, String contentType) {
        var request = HttpRequest.newBuilder(URI.create(url)).timeout(timeout).header("Content-Type", contentType)
            .PUT(HttpRequest.BodyPublishers.ofByteArray(content)).build();
        try {
            var response = http.send(request, HttpResponse.BodyHandlers.ofByteArray());
            if (response.statusCode() >= 300) {
                throw errorFrom(response);
            }
        } catch (IOException e) {
            throw new FeedFabricException(0, "network_error", String.valueOf(e.getMessage()), null, null, null, null, e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new FeedFabricException(0, "network_error", "Interrupted", null, null, null, null, e);
        }
    }

    static FeedFabricException errorFrom(HttpResponse<byte[]> response) {
        var status = response.statusCode();
        JsonNode problem;
        try {
            problem = DataType.Json.MAPPER.readTree(response.body());
        } catch (IOException e) {
            // Not Problem Details: a proxy or load balancer answered
            problem = null;
        }
        if (problem == null || !problem.isObject()) {
            problem = DataType.Json.MAPPER.createObjectNode();
        }
        var params = new LinkedHashMap<String, JsonNode>();
        problem.path("params").properties().forEach(e -> params.put(e.getKey(), e.getValue()));
        var errors = new ArrayList<JsonNode>();
        problem.path("errors").forEach(errors::add);
        Duration retryAfter = null;
        var header = response.headers().firstValue("Retry-After");
        if (header.isPresent()) {
            try {
                retryAfter = Duration.ofMillis((long) (Double.parseDouble(header.get()) * 1000));
            } catch (NumberFormatException ignored) {
                // An HTTP date: fall back to the usual backoff
            }
        }
        return new FeedFabricException(status,
            problem.path("code").asText("http_" + status),
            problem.path("detail").asText("HTTP " + status),
            params, List.copyOf(errors), problem.path("traceId").asText(null), retryAfter, null);
    }

    private static Duration backoff(int attempt) {
        return Duration.ofMillis(Math.min(250L << attempt, 4000) + ThreadLocalRandom.current().nextInt(100));
    }

    static void sleep(Duration duration) {
        try {
            Thread.sleep(duration.toMillis());
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new FeedFabricException(0, "network_error", "Interrupted", null, null, null, null, e);
        }
    }
}
