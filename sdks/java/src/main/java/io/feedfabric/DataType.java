package io.feedfabric;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.JavaType;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.feedfabric.Types.PostData;
import io.feedfabric.Types.PostDataInput;
import java.util.Optional;

/**
 * One of your data types, typed. The API checks data against the type's JSON Schema; this makes the same promise in
 * your code, so a weight reading can't be posted as anything else and reading one back gives your own type.
 *
 * <pre>{@code
 * record WeightReading(double kg, String note) {}
 * static final DataType<WeightReading> WEIGHT = DataType.define("weight-reading", WeightReading.class);
 *
 * member.posts().create(PostInput.data(WEIGHT.of(new WeightReading(82.4, null))).withTags("weight"));
 * WEIGHT.read(post.data()).ifPresent(reading -> ...);
 * }</pre>
 */
public final class DataType<T> {
    private final String key;
    private final JavaType type;

    private DataType(String key, JavaType type) {
        this.key = key;
        this.type = type;
    }

    /** A data type whose values are Ts; key is as registered in the admin panel. */
    public static <T> DataType<T> define(String key, Class<T> type) {
        return new DataType<>(key, Json.MAPPER.constructType(type));
    }

    /** For generic Ts, e.g. {@code new TypeReference<Map<String, Integer>>() {}}. */
    public static <T> DataType<T> define(String key, TypeReference<T> type) {
        return new DataType<>(key, Json.MAPPER.constructType(type));
    }

    public String key() {
        return key;
    }

    /** The post's data, for creating or editing a post. */
    public PostDataInput of(T value) {
        return new PostDataInput(key, Json.MAPPER.valueToTree(value));
    }

    /** Whether a post's data is of this type. */
    public boolean is(PostData data) {
        return data != null && key.equals(data.type());
    }

    /**
     * The post's data as a T, if it has data of this type.
     *
     * @throws IllegalArgumentException if it does, but the value isn't a T
     */
    public Optional<T> read(PostData data) {
        if (!is(data)) {
            return Optional.empty();
        }
        return Optional.of(Json.MAPPER.convertValue(data.value(), type));
    }

    static final class Json {
        static final ObjectMapper MAPPER = FeedFabric.mapper();

        private Json() {
        }
    }
}
