package feedfabric

import "encoding/json"

// The API's shapes, as the member routes return them. Times are ISO 8601 UTC strings; IDs are UUID strings.

// ReportReason is why a post or comment is reported.
type ReportReason string

const (
	ReasonSpam       ReportReason = "spam"
	ReasonHarassment ReportReason = "harassment"
	ReasonHate       ReportReason = "hate"
	ReasonThreat     ReportReason = "threat"
	ReasonSexual     ReportReason = "sexual"
	ReasonSelfHarm   ReportReason = "self_harm"
	ReasonOther      ReportReason = "other"
)

// Tag is one of a post's tags. Source is "hashtag", "inferred" or "app" (set by your app in a post's Tags).
type Tag struct {
	Tag    string `json:"tag"`
	Source string `json:"source"`
}

type PostMedia struct {
	ID           string `json:"id"`
	URL          string `json:"url"`
	ThumbnailURL string `json:"thumbnailUrl"`
	Width        int    `json:"width"`
	Height       int    `json:"height"`
}

// PostData is a post's structured data, of a type in GET /data-types. A DataType reads Value as your own type.
type PostData struct {
	Type string `json:"type"`
	// The data type's version the value was checked against
	Version int             `json:"version"`
	Value   json.RawMessage `json:"value"`
}

// PostDataInput is a post's data as you send it; DataType.Of builds one.
type PostDataInput struct {
	Type  string `json:"type"`
	Value any    `json:"value"`
}

type RankingFactor struct {
	Label  string  `json:"label"`
	Value  float64 `json:"value"`
	Detail string  `json:"detail"`
}

// PostRanking is only on For You posts: why the post is where it is.
type PostRanking struct {
	Ranker   string             `json:"ranker"`
	Position int                `json:"position"`
	Score    float64            `json:"score"`
	Explored bool               `json:"explored"`
	Factors  []RankingFactor    `json:"factors"`
	Features map[string]float64 `json:"features"`
}

// Post is a post as feeds return it.
type Post struct {
	ID                   string       `json:"id"`
	AuthorID             string       `json:"authorId"`
	AuthorExternalUserID string       `json:"authorExternalUserId"`
	AuthorUsername       string       `json:"authorUsername"`
	Content              string       `json:"content"`
	CreatedAt            string       `json:"createdAt"`
	EditedAt             *string      `json:"editedAt"`
	RepostOfID           *string      `json:"repostOfId"`
	RepostOf             *Post        `json:"repostOf"`
	IsHeld               bool         `json:"isHeld"`
	Tags                 []Tag        `json:"tags"`
	Media                []PostMedia  `json:"media"`
	Data                 *PostData    `json:"data"`
	Ranking              *PostRanking `json:"ranking"`
	Likes                int          `json:"likes"`
	Comments             int          `json:"comments"`
	Reposts              int          `json:"reposts"`
	Liked                bool         `json:"liked"`
	Reposted             bool         `json:"reposted"`
}

// SavedPost is a post as creating, editing and listing your own posts return it.
type SavedPost struct {
	ID         string    `json:"id"`
	AuthorID   string    `json:"authorId"`
	Content    string    `json:"content"`
	CreatedAt  string    `json:"createdAt"`
	EditedAt   *string   `json:"editedAt"`
	RepostOfID *string   `json:"repostOfId"`
	IsHeld     bool      `json:"isHeld"`
	Data       *PostData `json:"data"`
}

type FeedPage struct {
	Posts []Post `json:"posts"`
	// Pass back as Cursor for the next page; nil at the end
	NextCursor *string `json:"nextCursor"`
	Source     string  `json:"source"`
}

type ForYouPage struct {
	Posts      []Post  `json:"posts"`
	NextCursor *string `json:"nextCursor"`
	Ranker     string  `json:"ranker"`
	Profile    string  `json:"profile"`
	Candidates int     `json:"candidates"`
}

type OwnPostsPage struct {
	Posts      []SavedPost `json:"posts"`
	NextCursor *string     `json:"nextCursor"`
}

type Comment struct {
	ID                   string  `json:"id"`
	PostID               string  `json:"postId"`
	ParentCommentID      *string `json:"parentCommentId"`
	AuthorID             string  `json:"authorId"`
	AuthorExternalUserID string  `json:"authorExternalUserId"`
	AuthorUsername       string  `json:"authorUsername"`
	// Nil once deleted or removed
	Content    *string `json:"content"`
	IsDeleted  bool    `json:"isDeleted"`
	IsHeld     bool    `json:"isHeld"`
	CreatedAt  string  `json:"createdAt"`
	ReplyCount int     `json:"replyCount"`
}

type CommentPage struct {
	Comments   []Comment `json:"comments"`
	NextCursor *string   `json:"nextCursor"`
}

type Mention struct {
	ID                   string  `json:"id"`
	CreatedAt            string  `json:"createdAt"`
	AuthorExternalUserID string  `json:"authorExternalUserId"`
	AuthorUsername       string  `json:"authorUsername"`
	PostID               string  `json:"postId"`
	CommentID            *string `json:"commentId"`
	Content              string  `json:"content"`
}

type MentionPage struct {
	Mentions   []Mention `json:"mentions"`
	NextCursor *string   `json:"nextCursor"`
}

type TrendingTag struct {
	Tag   string `json:"tag"`
	Posts int    `json:"posts"`
}

type Favourite struct {
	ExternalUserID string  `json:"externalUserId"`
	Username       string  `json:"username"`
	DisplayName    *string `json:"displayName"`
	AvatarURL      *string `json:"avatarUrl"`
	AddedAt        string  `json:"addedAt"`
}

type Upload struct {
	MediaID     string `json:"mediaId"`
	UploadURL   string `json:"uploadUrl"`
	ContentType string `json:"contentType"`
	ExpiresAt   string `json:"expiresAt"`
}

// Media is an upload. Status is "awaitingUpload", "processing", "ready" or "rejected".
type Media struct {
	ID              string  `json:"id"`
	Status          string  `json:"status"`
	RejectionReason *string `json:"rejectionReason"`
	Width           *int    `json:"width"`
	Height          *int    `json:"height"`
	URL             *string `json:"url"`
	ThumbnailURL    *string `json:"thumbnailUrl"`
}

type DataTypeInfo struct {
	Key         string  `json:"key"`
	Label       string  `json:"label"`
	Description *string `json:"description"`
	Version     int     `json:"version"`
	// The JSON Schema posts' data of this type is checked against
	Schema    json.RawMessage `json:"schema"`
	UpdatedAt string          `json:"updatedAt"`
	UpdatedBy *string         `json:"updatedBy"`
}

// Counted is what signals return: how many were counted (repeats within 30 minutes aren't).
type Counted struct {
	Counted int `json:"counted"`
}

type DwellView struct {
	PostID string `json:"postId"`
	Ms     int    `json:"ms"`
}

type Me struct {
	ExternalUserID string `json:"externalUserId"`
}
