# FeedFabric for Go

Call FeedFabric from your servers, for your members, with an API key. It works on Go 1.22+ and uses only the
standard library. [../README.md](../README.md) covers API keys, tags and typed data for every SDK.

```sh
go get github.com/feedfabric/feedfabric/sdks/go
```

```go
import feedfabric "github.com/feedfabric/feedfabric/sdks/go"

client, err := feedfabric.New("https://feed.example.com", os.Getenv("FEEDFABRIC_API_KEY"))
member := client.AsMember(user.ID, feedfabric.Profile{Username: user.Handle, DisplayName: user.Name})

page, err := member.Feeds.ForYou(ctx, "", feedfabric.Page{Limit: 20})
err = member.Posts.Like(ctx, page.Posts[0].ID)
```

Every call takes a `context.Context`, and a `Client` is safe to share between goroutines. To change the timeout or
the transport, set `client.HTTPClient`.

## Typed data

```go
type WeightReading struct {
	Kg   float64 `json:"kg"`
	Note string  `json:"note,omitempty"`
}

var Weight = feedfabric.NewDataType[WeightReading]("weight-reading")

_, err = member.Posts.Create(ctx, feedfabric.PostInput{Tags: []string{"weight"}, Data: Weight.Of(WeightReading{Kg: 82.4})})

page, err := member.Posts.List(ctx, feedfabric.ListOptions{Type: Weight.Key})
for _, post := range page.Posts {
	reading, ok, err := Weight.Read(post.Data) // ok is false for other data; err if it doesn't decode
}
```

To generate the struct from the registered schema (`GET /data-types/weight-reading/schema`), run
`quicktype --lang go --src-lang schema`.

## Errors

```go
var ffErr *feedfabric.Error
if errors.As(err, &ffErr) && ffErr.Code == "content.violation" {
	showRules(ffErr.Params["category"])
}
```

## Tests

`go test ./...`
