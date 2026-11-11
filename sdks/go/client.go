// Package feedfabric is the FeedFabric SDK for Go: your server calls FeedFabric for your members, with an API key
// from the admin panel (Integrations > API keys).
//
//	client, err := feedfabric.New("https://feed.example.com", os.Getenv("FEEDFABRIC_API_KEY"))
//	member := client.AsMember("user-123", feedfabric.Profile{Username: "sam"})
//	post, err := member.Posts.Create(ctx, feedfabric.PostInput{Content: "Hello #intro"})
package feedfabric

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math/rand"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

const Version = "0.1.0"

// Client is the SDK's entry point: one per process, shared by every request and safe for concurrent use.
type Client struct {
	baseURL string
	apiKey  string
	// HTTPClient sends the requests; its Timeout applies to each attempt
	HTTPClient *http.Client
	// MaxRetries is how many times a request is retried after a 429 or 503 (waiting Retry-After), and after network
	// errors on requests that are safe to repeat
	MaxRetries int
}

// New makes a client. baseURL is where the API runs; apiKey is a key from the admin panel, "ffk_...".
func New(baseURL, apiKey string) (*Client, error) {
	if baseURL == "" {
		return nil, errors.New("feedfabric: New needs a base URL")
	}
	if !strings.HasPrefix(apiKey, "ffk_") {
		return nil, errors.New(`feedfabric: New needs an API key from the admin panel ("ffk_...")`)
	}
	return &Client{
		baseURL:    strings.TrimRight(baseURL, "/"),
		apiKey:     apiKey,
		HTTPClient: &http.Client{Timeout: 30 * time.Second},
		MaxRetries: 2,
	}, nil
}

// Profile is a member's profile: used when FeedFabric first sees them, and refreshing theirs at most hourly.
type Profile struct {
	Username    string
	DisplayName string
	AvatarURL   string
}

// AsMember is the calls made for one of your members: their ID is the one your sign-in tokens carry
// (Auth:Claims:UserId).
func (c *Client) AsMember(externalUserID string, profile Profile) *Member {
	headers := http.Header{}
	headers.Set("FeedFabric-User", url.PathEscape(externalUserID))
	for name, value := range map[string]string{
		"FeedFabric-Username":     profile.Username,
		"FeedFabric-Display-Name": profile.DisplayName,
		"FeedFabric-Avatar":       profile.AvatarURL,
	} {
		if value != "" {
			headers.Set(name, url.PathEscape(value))
		}
	}
	m := &Member{client: c, headers: headers}
	m.Posts = &Posts{m}
	m.Feeds = &Feeds{m}
	m.Comments = &Comments{m}
	m.Media = &MediaUploads{m}
	m.Signals = &Signals{m}
	return m
}

func (c *Client) do(ctx context.Context, method, path string, headers http.Header, query url.Values, body, out any) error {
	target := c.baseURL + path
	if encoded := query.Encode(); encoded != "" {
		target += "?" + encoded
	}
	var payload []byte
	if body != nil {
		var err error
		if payload, err = json.Marshal(body); err != nil {
			return fmt.Errorf("feedfabric: encoding the request: %w", err)
		}
	}
	// Safe to send twice: everything but POST. A 429 means nothing happened, so that's always safe.
	repeatable := method != http.MethodPost

	for attempt := 0; ; attempt++ {
		request, err := http.NewRequestWithContext(ctx, method, target, bytes.NewReader(payload))
		if err != nil {
			return err
		}
		for name, values := range headers {
			request.Header[name] = values
		}
		request.Header.Set("Authorization", "Bearer "+c.apiKey)
		request.Header.Set("Accept", "application/json")
		request.Header.Set("User-Agent", "feedfabric-go/"+Version)
		if body != nil {
			request.Header.Set("Content-Type", "application/json")
		}

		response, err := c.HTTPClient.Do(request)
		if err != nil {
			if ctx.Err() != nil {
				return ctx.Err()
			}
			if repeatable && attempt < c.MaxRetries {
				if err := sleep(ctx, backoff(attempt)); err != nil {
					return err
				}
				continue
			}
			return &Error{Code: "network_error", Message: err.Error()}
		}
		data, err := io.ReadAll(response.Body)
		response.Body.Close()
		if err != nil {
			return &Error{Status: response.StatusCode, Code: "network_error", Message: err.Error()}
		}

		if response.StatusCode >= 300 {
			apiErr := errorFrom(response, data)
			retry := response.StatusCode == 429 || (response.StatusCode == 503 && repeatable)
			if retry && attempt < c.MaxRetries {
				wait := apiErr.RetryAfter
				if !apiErr.hasRetryAfter {
					wait = backoff(attempt)
				}
				if err := sleep(ctx, wait); err != nil {
					return err
				}
				continue
			}
			return apiErr
		}
		if out == nil || len(data) == 0 {
			return nil
		}
		if err := json.Unmarshal(data, out); err != nil {
			return fmt.Errorf("feedfabric: decoding the response: %w", err)
		}
		return nil
	}
}

func backoff(attempt int) time.Duration {
	wait := min(250*time.Millisecond<<attempt, 4*time.Second)
	return wait + time.Duration(rand.Int63n(int64(100*time.Millisecond)))
}

func sleep(ctx context.Context, d time.Duration) error {
	timer := time.NewTimer(d)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-timer.C:
		return nil
	}
}

// How often Upload checks whether the image is ready
var uploadPoll = time.Second

func esc(s string) string { return url.PathEscape(s) }

// Page is where a page of a list starts and how long it is. Leave fields empty for the API's defaults.
type Page struct {
	Limit int
	// The previous page's NextCursor
	Cursor string
}

func (p Page) values() url.Values {
	q := url.Values{}
	if p.Limit > 0 {
		q.Set("limit", strconv.Itoa(p.Limit))
	}
	if p.Cursor != "" {
		q.Set("cursor", p.Cursor)
	}
	return q
}

// Member is everything a member can do, as that member.
type Member struct {
	client  *Client
	headers http.Header

	Posts    *Posts
	Feeds    *Feeds
	Comments *Comments
	Media    *MediaUploads
	Signals  *Signals
}

func (m *Member) call(ctx context.Context, method, path string, query url.Values, body, out any) error {
	return m.client.do(ctx, method, path, m.headers, query, body, out)
}

// Me is who FeedFabric thinks this is; it also creates the member if they're new.
func (m *Member) Me(ctx context.Context) (*Me, error) {
	var me Me
	return &me, m.call(ctx, "GET", "/me", nil, nil, &me)
}

func (m *Member) Follow(ctx context.Context, externalUserID string) error {
	return m.call(ctx, "PUT", "/follows/"+esc(externalUserID), nil, nil, nil)
}

func (m *Member) Unfollow(ctx context.Context, externalUserID string) error {
	return m.call(ctx, "DELETE", "/follows/"+esc(externalUserID), nil, nil, nil)
}

func (m *Member) Favourites(ctx context.Context) ([]Favourite, error) {
	var out []Favourite
	return out, m.call(ctx, "GET", "/favourites", nil, nil, &out)
}

// AddFavourite adds up to 30; favourite.limit_reached after that.
func (m *Member) AddFavourite(ctx context.Context, externalUserID string) error {
	return m.call(ctx, "PUT", "/favourites/"+esc(externalUserID), nil, nil, nil)
}

func (m *Member) RemoveFavourite(ctx context.Context, externalUserID string) error {
	return m.call(ctx, "DELETE", "/favourites/"+esc(externalUserID), nil, nil, nil)
}

// DataTypes is the registered data types and their schemas.
func (m *Member) DataTypes(ctx context.Context) ([]DataTypeInfo, error) {
	var out []DataTypeInfo
	return out, m.call(ctx, "GET", "/data-types", nil, nil, &out)
}

func (m *Member) DataType(ctx context.Context, key string) (*DataTypeInfo, error) {
	var out DataTypeInfo
	return &out, m.call(ctx, "GET", "/data-types/"+esc(key), nil, nil, &out)
}

// ExportAccount is everything held about the member, as one JSON document. A few an hour.
func (m *Member) ExportAccount(ctx context.Context) (json.RawMessage, error) {
	var out json.RawMessage
	return out, m.call(ctx, "GET", "/me/export", nil, nil, &out)
}

// DeleteAccount erases the member: posts, comments and profile at once, the rest shortly after.
func (m *Member) DeleteAccount(ctx context.Context) error {
	return m.call(ctx, "DELETE", "/me", nil, nil, nil)
}

// PostInput is a post to create or edit.
type PostInput struct {
	// Required unless the post has Data
	Content string `json:"content,omitempty"`
	// Your app's own tags, written like hashtags without the #; up to 10. On edit, nil keeps the post's.
	Tags []string `json:"tags,omitempty"`
	// Uploaded images (MediaUploads.Upload); create only
	MediaIDs []string `json:"mediaIds,omitempty"`
	// Structured data of a registered type: DataType.Of builds it. On edit, nil keeps the post's.
	Data *PostDataInput `json:"data,omitempty"`
}

type Posts struct{ m *Member }

func (p *Posts) Create(ctx context.Context, input PostInput) (*SavedPost, error) {
	var out SavedPost
	return &out, p.m.call(ctx, "POST", "/userfeed/posts", nil, input, &out)
}

// Edit changes a post within an hour of posting. MediaIDs are ignored.
func (p *Posts) Edit(ctx context.Context, postID string, input PostInput) (*SavedPost, error) {
	input.MediaIDs = nil
	var out SavedPost
	return &out, p.m.call(ctx, "PUT", "/userfeed/posts/"+esc(postID), nil, input, &out)
}

func (p *Posts) Delete(ctx context.Context, postID string) error {
	return p.m.call(ctx, "DELETE", "/userfeed/posts/"+esc(postID), nil, nil, nil)
}

// ListOptions picks which of the member's own posts to list. Post IDs are time-ordered (UUIDv7):
// Gt, Gte, Lt and Lte bound the range.
type ListOptions struct {
	// Only posts with data of this type
	Type string
	// "newest" (the default) or "oldest"
	Order            string
	Limit            int
	Cursor           string
	Gt, Gte, Lt, Lte string
}

// List is the member's own posts.
func (p *Posts) List(ctx context.Context, options ListOptions) (*OwnPostsPage, error) {
	q := Page{Limit: options.Limit, Cursor: options.Cursor}.values()
	for name, value := range map[string]string{"type": options.Type, "order": options.Order, "gt": options.Gt, "gte": options.Gte, "lt": options.Lt, "lte": options.Lte} {
		if value != "" {
			q.Set(name, value)
		}
	}
	var out OwnPostsPage
	return &out, p.m.call(ctx, "GET", "/userfeed", q, nil, &out)
}

func (p *Posts) Like(ctx context.Context, postID string) error {
	return p.m.call(ctx, "PUT", "/userfeed/posts/"+esc(postID)+"/like", nil, nil, nil)
}

func (p *Posts) Unlike(ctx context.Context, postID string) error {
	return p.m.call(ctx, "DELETE", "/userfeed/posts/"+esc(postID)+"/like", nil, nil, nil)
}

func (p *Posts) Repost(ctx context.Context, postID string) error {
	return p.m.call(ctx, "PUT", "/userfeed/posts/"+esc(postID)+"/repost", nil, nil, nil)
}

func (p *Posts) Unrepost(ctx context.Context, postID string) error {
	return p.m.call(ctx, "DELETE", "/userfeed/posts/"+esc(postID)+"/repost", nil, nil, nil)
}

func (p *Posts) Report(ctx context.Context, postID string, reason ReportReason) error {
	return p.m.call(ctx, "POST", "/userfeed/posts/"+esc(postID)+"/report", nil, map[string]any{"reason": reason}, nil)
}

type Feeds struct{ m *Member }

// Home is posts from the accounts the member follows, newest first.
func (f *Feeds) Home(ctx context.Context, page Page) (*FeedPage, error) {
	var out FeedPage
	return &out, f.m.call(ctx, "GET", "/userfeed/home", page.values(), nil, &out)
}

// ForYou is ranked for the member; profile picks a ranking profile ("" for the default).
func (f *Feeds) ForYou(ctx context.Context, profile string, page Page) (*ForYouPage, error) {
	q := page.values()
	if profile != "" {
		q.Set("profile", profile)
	}
	var out ForYouPage
	return &out, f.m.call(ctx, "GET", "/userfeed/foryou", q, nil, &out)
}

func (f *Feeds) Favourites(ctx context.Context, page Page) (*FeedPage, error) {
	var out FeedPage
	return &out, f.m.call(ctx, "GET", "/userfeed/favourites", page.values(), nil, &out)
}

// Tag is posts with a tag (hashtag, inferred topic or app tag), newest first.
func (f *Feeds) Tag(ctx context.Context, tag string, page Page) (*FeedPage, error) {
	var out FeedPage
	return &out, f.m.call(ctx, "GET", "/userfeed/tags/"+esc(tag)+"/posts", page.values(), nil, &out)
}

// TrendingTags is the most used tags over the last hours (0 for the API's defaults).
func (f *Feeds) TrendingTags(ctx context.Context, hours, limit int) ([]TrendingTag, error) {
	q := url.Values{}
	if hours > 0 {
		q.Set("hours", strconv.Itoa(hours))
	}
	if limit > 0 {
		q.Set("limit", strconv.Itoa(limit))
	}
	var out []TrendingTag
	return out, f.m.call(ctx, "GET", "/userfeed/tags/trending", q, nil, &out)
}

func (f *Feeds) Mentions(ctx context.Context, page Page) (*MentionPage, error) {
	var out MentionPage
	return &out, f.m.call(ctx, "GET", "/userfeed/mentions", page.values(), nil, &out)
}

type Comments struct{ m *Member }

// List is a post's top-level comments, oldest first.
func (c *Comments) List(ctx context.Context, postID string, page Page) (*CommentPage, error) {
	var out CommentPage
	return &out, c.m.call(ctx, "GET", "/userfeed/posts/"+esc(postID)+"/comments", page.values(), nil, &out)
}

func (c *Comments) Replies(ctx context.Context, commentID string, page Page) (*CommentPage, error) {
	var out CommentPage
	return &out, c.m.call(ctx, "GET", "/userfeed/comments/"+esc(commentID)+"/replies", page.values(), nil, &out)
}

// Create comments on a post; parentCommentID ("" for none) makes it a reply.
func (c *Comments) Create(ctx context.Context, postID, content, parentCommentID string) (*Comment, error) {
	body := map[string]any{"content": content}
	if parentCommentID != "" {
		body["parentCommentId"] = parentCommentID
	}
	var out Comment
	return &out, c.m.call(ctx, "POST", "/userfeed/posts/"+esc(postID)+"/comments", nil, body, &out)
}

func (c *Comments) Delete(ctx context.Context, commentID string) error {
	return c.m.call(ctx, "DELETE", "/userfeed/comments/"+esc(commentID), nil, nil, nil)
}

func (c *Comments) Report(ctx context.Context, commentID string, reason ReportReason) error {
	return c.m.call(ctx, "POST", "/userfeed/comments/"+esc(commentID)+"/report", nil, map[string]any{"reason": reason}, nil)
}

type MediaUploads struct{ m *Member }

// CreateUpload asks for an upload URL: then PUT the file there and call Complete.
func (u *MediaUploads) CreateUpload(ctx context.Context, contentType string, size int) (*Upload, error) {
	var out Upload
	return &out, u.m.call(ctx, "POST", "/media/uploads", nil, map[string]any{"contentType": contentType, "size": size}, &out)
}

func (u *MediaUploads) Complete(ctx context.Context, mediaID string) (*Media, error) {
	var out Media
	return &out, u.m.call(ctx, "POST", "/media/"+esc(mediaID)+"/complete", nil, nil, &out)
}

func (u *MediaUploads) Get(ctx context.Context, mediaID string) (*Media, error) {
	var out Media
	return &out, u.m.call(ctx, "GET", "/media/"+esc(mediaID), nil, nil, &out)
}

// Upload is the whole upload: it asks for a URL, sends the file, and waits for scanning (bounded by ctx, and a
// minute at most). It returns the ready image, for a post's MediaIDs; an *Error "media.rejected" if it's refused, or
// "media.timeout" if it's still being checked.
func (u *MediaUploads) Upload(ctx context.Context, content []byte, contentType string) (*Media, error) {
	upload, err := u.CreateUpload(ctx, contentType, len(content))
	if err != nil {
		return nil, err
	}
	// The upload URL is the storage's own, signed: it doesn't take the API key
	request, err := http.NewRequestWithContext(ctx, "PUT", upload.UploadURL, bytes.NewReader(content))
	if err != nil {
		return nil, err
	}
	request.Header.Set("Content-Type", upload.ContentType)
	response, err := u.m.client.HTTPClient.Do(request)
	if err != nil {
		return nil, &Error{Code: "network_error", Message: err.Error()}
	}
	data, _ := io.ReadAll(response.Body)
	response.Body.Close()
	if response.StatusCode >= 300 {
		return nil, errorFrom(response, data)
	}

	media, err := u.Complete(ctx, upload.MediaID)
	deadline := time.Now().Add(time.Minute)
	for err == nil && (media.Status == "processing" || media.Status == "awaitingUpload") {
		if time.Now().After(deadline) {
			return nil, &Error{Code: "media.timeout", Message: "The image is still being checked; try Get later.", Params: map[string]any{"mediaId": media.ID}}
		}
		if err = sleep(ctx, uploadPoll); err == nil {
			media, err = u.Get(ctx, upload.MediaID)
		}
	}
	if err != nil {
		return nil, err
	}
	if media.Status == "rejected" {
		reason := "unknown reason"
		if media.RejectionReason != nil {
			reason = *media.RejectionReason
		}
		return nil, &Error{Status: 422, Code: "media.rejected", Message: "The image was rejected: " + reason,
			Params: map[string]any{"mediaId": media.ID, "reason": media.RejectionReason}}
	}
	return media, nil
}

// Signals is what the member's reading tells the ranker. Send them from your server as your app reports them.
type Signals struct{ m *Member }

// Impressions is posts the member saw (at least half on screen for a second); up to 100 at a time.
func (s *Signals) Impressions(ctx context.Context, postIDs []string) (*Counted, error) {
	var out Counted
	return &out, s.m.call(ctx, "POST", "/userfeed/impressions", nil, map[string]any{"postIds": postIDs}, &out)
}

// Open is the member opening the post's own page.
func (s *Signals) Open(ctx context.Context, postID string) (*Counted, error) {
	var out Counted
	return &out, s.m.call(ctx, "POST", "/userfeed/posts/"+esc(postID)+"/open", nil, nil, &out)
}

// Dwell is how long posts stayed on screen; up to 100 at a time.
func (s *Signals) Dwell(ctx context.Context, views []DwellView) (*Counted, error) {
	var out Counted
	return &out, s.m.call(ctx, "POST", "/userfeed/dwell", nil, map[string]any{"views": views}, &out)
}

func (s *Signals) NotInterested(ctx context.Context, postID string) error {
	return s.m.call(ctx, "POST", "/userfeed/posts/"+esc(postID)+"/not-interested", nil, nil, nil)
}

func (s *Signals) ShowMore(ctx context.Context, postID string) error {
	return s.m.call(ctx, "POST", "/userfeed/posts/"+esc(postID)+"/show-more", nil, nil, nil)
}
