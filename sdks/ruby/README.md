# FeedFabric for Ruby

Call FeedFabric from your servers, for your members, with an API key. It works on Ruby 3.1+ and has no
dependencies. RBS signatures are in `sig/`. [../README.md](../README.md) covers API keys, tags and typed data for
every SDK.

```sh
gem install feedfabric
```

```ruby
require "feedfabric"

feed = FeedFabric::Client.new(base_url: "https://feed.example.com", api_key: ENV.fetch("FEEDFABRIC_API_KEY"))
member = feed.as_member(user.id, username: user.handle, display_name: user.name)

page = member.feeds.for_you(limit: 20)
member.posts.like(page["posts"][0]["id"])
```

Responses are the API's JSON as Hashes, with its own (camelCase, string) keys.

## Typed data

```ruby
WeightReading = Data.define(:kg, :note)

WEIGHT = FeedFabric::DataType.new("weight-reading",
                                  parse: ->(v) { WeightReading.new(kg: Float(v.fetch("kg")), note: v["note"]) },
                                  dump: ->(r) { r.to_h.compact })

member.posts.create(tags: ["weight"], data: WEIGHT.of(WeightReading.new(kg: 82.4, note: nil)))

member.posts.list(type: WEIGHT.key)["posts"].each do |post|
  reading = WEIGHT.read(post) # a WeightReading, or nil
end
```

With Steep, annotate the constant `#: FeedFabric::DataType[WeightReading]`. `of` then only takes a `WeightReading`,
and `read` returns `WeightReading?`.

## Errors

```ruby
begin
  member.posts.create(content: content)
rescue FeedFabric::Error => e
  raise unless e.code == "content.violation"

  show_rules(e.params["category"])
end
```

## Tests

`rake` runs Minitest against a stand-in server, then `rbs validate`.
