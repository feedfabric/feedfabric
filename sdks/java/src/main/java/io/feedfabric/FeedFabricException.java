package io.feedfabric;

import com.fasterxml.jackson.databind.JsonNode;
import java.time.Duration;
import java.util.List;
import java.util.Map;

/**
 * An API error. Every one has a stable {@link #code()} (docs/errors.md): match on it, not on the message.
 *
 * <pre>{@code
 * catch (FeedFabricException e) { if (e.code().equals("post.not_found")) ... }
 * }</pre>
 */
public final class FeedFabricException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    private final int status;
    private final String code;
    private final transient Map<String, JsonNode> params;
    private final transient List<JsonNode> errors;
    private final String traceId;
    private final Duration retryAfter;

    public FeedFabricException(int status, String code, String message, Map<String, JsonNode> params, List<JsonNode> errors,
                               String traceId, Duration retryAfter, Throwable cause) {
        super(message, cause);
        this.status = status;
        this.code = code;
        this.params = params == null ? Map.of() : params;
        this.errors = errors == null ? List.of() : errors;
        this.traceId = traceId;
        this.retryAfter = retryAfter;
    }

    FeedFabricException(int status, String code, String message, Map<String, JsonNode> params) {
        this(status, code, message, params, null, null, null, null);
    }

    /** The HTTP status; 0 when the request never got a response. */
    public int status() {
        return status;
    }

    /** e.g. "post.not_found", "content.violation", "rate_limited"; "network_error" without a response. */
    public String code() {
        return code;
    }

    public Map<String, JsonNode> params() {
        return params;
    }

    /** validation_failed lists fields; post.data_invalid lists {"path", "message"}. */
    public List<JsonNode> errors() {
        return errors;
    }

    public String traceId() {
        return traceId;
    }

    /** From Retry-After on 429 and 503; null without one. */
    public Duration retryAfter() {
        return retryAfter;
    }

    @Override
    public String toString() {
        return "FeedFabricException: " + status + " " + code + ": " + getMessage();
    }
}
