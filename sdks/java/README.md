# FeedFabric for Java

Call FeedFabric from your servers, for your members, with an API key. It works on Java 17+ and from Kotlin. It
uses `java.net.http` and Jackson. [../README.md](../README.md) covers API keys, tags and typed data for every SDK.

```xml
<dependency>
  <groupId>io.feedfabric</groupId>
  <artifactId>feedfabric-java</artifactId>
  <version>0.1.0</version>
</dependency>
```

```java
import io.feedfabric.FeedFabric;
import io.feedfabric.Types.*;

var feed = new FeedFabric("https://feed.example.com", System.getenv("FEEDFABRIC_API_KEY"));
var member = feed.asMember(user.id(), new Profile(user.handle(), user.name(), null));

var page = member.feeds().forYou(null, 20, null);
member.posts().like(page.posts().get(0).id());
```

Calls block, and a `FeedFabric` is safe to share between threads. Run them on virtual threads for concurrency.

## Typed data

```java
record WeightReading(double kg, String note) {}

static final DataType<WeightReading> WEIGHT = DataType.define("weight-reading", WeightReading.class);

member.posts().create(PostInput.data(WEIGHT.of(new WeightReading(82.4, null))).withTags("weight"));

for (var post : member.posts().list(new ListQuery().type(WEIGHT.key())).posts()) {
    WEIGHT.read(post.data()).ifPresent(reading -> ...); // Optional<WeightReading>
}
```

For generic values, use `DataType.define("steps", new TypeReference<Map<String, Integer>>() {})`. To generate the
class from the registered schema (`GET /data-types/weight-reading/schema`), use jsonschema2pojo or
`quicktype --lang java --src-lang schema`.

## Errors

```java
try {
    member.posts().create(PostInput.text(content));
} catch (FeedFabricException e) {
    if (!e.code().equals("content.violation")) throw e;
    showRules(e.params().get("category").asText());
}
```

## Tests

`mvn test`
