package feedfabric

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"reflect"
	"sync"
	"testing"
	"time"
)

// A stand-in API: records each request and answers with whatever the test queued
type reply struct {
	status  int
	body    any
	headers map[string]string
}

type seen struct {
	method, path string
	headers      http.Header
	body         string
}

type fakeAPI struct {
	*httptest.Server
	mu      sync.Mutex
	seen    []seen
	replies []reply
}

func newAPI(t *testing.T) *fakeAPI {
	api := &fakeAPI{}
	api.Server = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		api.mu.Lock()
		api.seen = append(api.seen, seen{r.Method, r.URL.RequestURI(), r.Header, string(body)})
		next := reply{status: 204}
		if len(api.replies) > 0 {
			next, api.replies = api.replies[0], api.replies[1:]
		}
		api.mu.Unlock()
		for name, value := range next.headers {
			w.Header().Set(name, value)
		}
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(next.status)
		if next.body != nil {
			_ = json.NewEncoder(w).Encode(next.body)
		}
	}))
	t.Cleanup(api.Close)
	return api
}

func (a *fakeAPI) reply(status int, body any, headers ...string) {
	r := reply{status: status, body: body, headers: map[string]string{}}
	for i := 0; i+1 < len(headers); i += 2 {
		r.headers[headers[i]] = headers[i+1]
	}
	a.replies = append(a.replies, r)
}

func (a *fakeAPI) member(t *testing.T) *Member {
	client, err := New(a.URL, "ffk_test")
	if err != nil {
		t.Fatal(err)
	}
	return client.AsMember("u1", Profile{})
}

type WeightReading struct {
	Kg   float64 `json:"kg"`
	Note string  `json:"note,omitempty"`
}

var Weight = NewDataType[WeightReading]("weight-reading")

var saved = map[string]any{
	"id": "p1", "authorId": "a1", "content": "", "createdAt": "2026-11-10T09:00:00Z", "isHeld": false,
	"data": map[string]any{"type": "weight-reading", "version": 2, "value": map[string]any{"kg": 82.4}},
}

var ctx = context.Background()

func TestActsForTheMemberWithTheKeyAndEncodedProfile(t *testing.T) {
	api := newAPI(t)
	api.reply(200, map[string]any{"externalUserId": "user 1"})
	client, _ := New(api.URL+"/", "ffk_test")

	me, err := client.AsMember("user 1", Profile{DisplayName: "Zoë Brontë"}).Me(ctx)

	if err != nil || me.ExternalUserID != "user 1" {
		t.Fatalf("got %+v, %v", me, err)
	}
	s := api.seen[0]
	if s.method != "GET" || s.path != "/me" {
		t.Errorf("sent %s %s", s.method, s.path)
	}
	if got := s.headers.Get("Authorization"); got != "Bearer ffk_test" {
		t.Errorf("Authorization %q", got)
	}
	if got := s.headers.Get("FeedFabric-User"); got != "user%201" {
		t.Errorf("FeedFabric-User %q", got)
	}
	if got, _ := url.PathUnescape(s.headers.Get("FeedFabric-Display-Name")); got != "Zoë Brontë" {
		t.Errorf("FeedFabric-Display-Name %q", got)
	}
	if s.headers.Get("FeedFabric-Username") != "" {
		t.Error("sent an empty username")
	}
}

func TestPostsTypedDataAndAppTagsAndReadsItBack(t *testing.T) {
	api := newAPI(t)
	api.reply(201, saved)

	post, err := api.member(t).Posts.Create(ctx, PostInput{Tags: []string{"weight"}, Data: Weight.Of(WeightReading{Kg: 82.4})})
	if err != nil {
		t.Fatal(err)
	}

	var sent any
	_ = json.Unmarshal([]byte(api.seen[0].body), &sent)
	want := map[string]any{"tags": []any{"weight"}, "data": map[string]any{"type": "weight-reading", "value": map[string]any{"kg": 82.4}}}
	if !reflect.DeepEqual(sent, want) {
		t.Errorf("sent %v", sent)
	}
	reading, ok, err := Weight.Read(post.Data)
	if !ok || err != nil || reading.Kg != 82.4 {
		t.Errorf("read %+v, %v, %v", reading, ok, err)
	}
	if _, ok, _ := NewDataType[map[string]int]("steps").Read(post.Data); ok {
		t.Error("read a weight reading as steps")
	}
	if Weight.Is(nil) {
		t.Error("a post without data is a weight reading")
	}
}

func TestReadReportsDataThatDoesNotDecode(t *testing.T) {
	_, ok, err := Weight.Read(&PostData{Type: "weight-reading", Value: json.RawMessage(`{"kg":"heavy"}`)})
	if ok || err == nil {
		t.Errorf("got %v, %v", ok, err)
	}
}

func TestBuildsQueryStringsAndLeavesOutWhatIsUnset(t *testing.T) {
	api := newAPI(t)
	api.reply(200, map[string]any{"posts": []any{}, "nextCursor": nil})

	if _, err := api.member(t).Posts.List(ctx, ListOptions{Type: "weight-reading", Limit: 5}); err != nil {
		t.Fatal(err)
	}

	if got := api.seen[0].path; got != "/userfeed?limit=5&type=weight-reading" {
		t.Errorf("path %q", got)
	}
}

func TestErrorsCarryTheCodeParamsAndFieldErrors(t *testing.T) {
	api := newAPI(t)
	api.reply(400, map[string]any{"code": "post.data_invalid", "detail": "nope", "params": map[string]any{"type": "weight-reading"},
		"errors": []any{map[string]any{"path": "/kg", "message": "should be a number"}}, "traceId": "t-1"})

	_, err := api.member(t).Posts.Create(ctx, PostInput{Data: Weight.Of(WeightReading{Kg: 1})})

	var apiErr *Error
	if !errors.As(err, &apiErr) {
		t.Fatalf("got %v", err)
	}
	if apiErr.Status != 400 || apiErr.Code != "post.data_invalid" || apiErr.TraceID != "t-1" || apiErr.Params["type"] != "weight-reading" {
		t.Errorf("got %+v", apiErr)
	}
	var field struct{ Path string }
	_ = json.Unmarshal(apiErr.Errors[0], &field)
	if field.Path != "/kg" {
		t.Errorf("errors %s", apiErr.Errors[0])
	}
}

func TestWaitsOutA429EvenForAPost(t *testing.T) {
	api := newAPI(t)
	api.reply(429, map[string]any{"code": "rate_limited"}, "Retry-After", "0")
	api.reply(202, map[string]any{"counted": 2})

	counted, err := api.member(t).Signals.Impressions(ctx, []string{"p1", "p2"})

	if err != nil || counted.Counted != 2 || len(api.seen) != 2 {
		t.Errorf("got %+v, %v after %d requests", counted, err, len(api.seen))
	}
}

func TestRepeatsAGetAfterA503ButNotAPost(t *testing.T) {
	api := newAPI(t)
	member := api.member(t)
	api.reply(503, map[string]any{"code": "service_unavailable"}, "Retry-After", "0")
	if _, err := member.Posts.Create(ctx, PostInput{Content: "hi"}); err == nil || len(api.seen) != 1 {
		t.Errorf("got %v after %d requests", err, len(api.seen))
	}

	api.reply(503, map[string]any{"code": "service_unavailable"}, "Retry-After", "0")
	api.reply(200, []any{})
	if favourites, err := member.Favourites(ctx); err != nil || len(favourites) != 0 || len(api.seen) != 3 {
		t.Errorf("got %v, %v after %d requests", favourites, err, len(api.seen))
	}
}

func TestUploadsAnImageAndWaitsUntilItIsReady(t *testing.T) {
	uploadPoll = 10 * time.Millisecond
	api := newAPI(t)
	api.reply(201, map[string]any{"mediaId": "m1", "uploadUrl": api.URL + "/blobs/m1", "contentType": "image/png", "expiresAt": "2026-11-10T10:00:00Z"})
	api.reply(200, nil)
	api.reply(202, map[string]any{"id": "m1", "status": "processing"})
	api.reply(200, map[string]any{"id": "m1", "status": "ready", "url": "u", "thumbnailUrl": "t", "width": 1, "height": 1})

	media, err := api.member(t).Media.Upload(ctx, []byte{1, 2, 3}, "image/png")

	if err != nil || media.Status != "ready" {
		t.Fatalf("got %+v, %v", media, err)
	}
	var sent []string
	for _, s := range api.seen {
		sent = append(sent, s.method+" "+s.path)
	}
	if want := []string{"POST /media/uploads", "PUT /blobs/m1", "POST /media/m1/complete", "GET /media/m1"}; !reflect.DeepEqual(sent, want) {
		t.Errorf("sent %v", sent)
	}
	if api.seen[1].headers.Get("Authorization") != "" {
		t.Error("sent the API key to the storage")
	}
}

func TestRefusesAWrongLookingKey(t *testing.T) {
	if _, err := New("http://localhost", "eyJhbGciOi..."); err == nil {
		t.Error("took a token as an API key")
	}
}
