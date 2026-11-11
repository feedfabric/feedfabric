// The API's shapes, as the member routes return them. Times are ISO 8601 UTC strings; IDs are UUID strings.

export type ReportReason = 'spam' | 'harassment' | 'hate' | 'threat' | 'sexual' | 'self_harm' | 'other'

export interface Tag {
  tag: string
  // "app" tags are the ones your app set in a post's tags
  source: 'hashtag' | 'inferred' | 'app'
}

export interface PostMedia {
  id: string
  url: string
  thumbnailUrl: string
  width: number
  height: number
}

// A post's structured data, of a type in GET /data-types. Value is unknown until a DataType reads it.
export interface PostData<T = unknown> {
  type: string
  // The data type's version the value was checked against
  version: number
  value: T
}

// A post's data as you send it: the type's key and a value its schema accepts
export interface PostDataInput<T = unknown> {
  type: string
  value: T
}

export interface RankingFactor {
  label: string
  value: number
  detail: string
}

// Only on For You posts: why the post is where it is
export interface PostRanking {
  ranker: string
  position: number
  score: number
  explored: boolean
  factors: RankingFactor[]
  features: Record<string, number>
}

// A post as feeds return it
export interface Post {
  id: string
  authorId: string
  authorExternalUserId: string
  authorUsername: string
  content: string
  createdAt: string
  editedAt: string | null
  repostOfId: string | null
  repostOf: Post | null
  // Held for review: only its author sees it
  isHeld: boolean
  tags: Tag[]
  media: PostMedia[]
  data: PostData | null
  ranking: PostRanking | null
  likes: number
  comments: number
  reposts: number
  liked: boolean
  reposted: boolean
}

// A post as creating, editing and listing your own posts return it
export interface SavedPost {
  id: string
  authorId: string
  content: string
  createdAt: string
  editedAt: string | null
  repostOfId: string | null
  isHeld: boolean
  data: PostData | null
}

export interface FeedPage {
  posts: Post[]
  // Pass back as cursor for the next page; null at the end
  nextCursor: string | null
  // "redis", "postgres" or "redis+postgres"
  source: string
}

export interface ForYouPage {
  posts: Post[]
  nextCursor: string | null
  // "heuristic", "model:<version>", "expression"...
  ranker: string
  profile: string
  candidates: number
}

export interface OwnPostsPage {
  posts: SavedPost[]
  nextCursor: string | null
}

export interface Comment {
  id: string
  postId: string
  parentCommentId: string | null
  authorId: string
  authorExternalUserId: string
  authorUsername: string
  // Null once deleted or removed
  content: string | null
  isDeleted: boolean
  isHeld: boolean
  createdAt: string
  replyCount: number
}

export interface CommentPage {
  comments: Comment[]
  nextCursor: string | null
}

export interface Mention {
  id: string
  createdAt: string
  authorExternalUserId: string
  authorUsername: string
  postId: string
  // Set when the mention is in a comment
  commentId: string | null
  content: string
}

export interface MentionPage {
  mentions: Mention[]
  nextCursor: string | null
}

export interface TrendingTag {
  tag: string
  posts: number
}

export interface Favourite {
  externalUserId: string
  username: string
  displayName: string | null
  avatarUrl: string | null
  addedAt: string
}

export interface Upload {
  mediaId: string
  // PUT the file here with exactly contentType
  uploadUrl: string
  contentType: string
  expiresAt: string
}

export type MediaStatus = 'awaitingUpload' | 'processing' | 'ready' | 'rejected'

export interface Media {
  id: string
  status: MediaStatus
  // Set when rejected, e.g. "malware_detected"
  rejectionReason: string | null
  width: number | null
  height: number | null
  url: string | null
  thumbnailUrl: string | null
}

export interface DataTypeInfo {
  key: string
  label: string
  description: string | null
  version: number
  // The JSON Schema posts' data of this type is checked against
  schema: Record<string, unknown>
  updatedAt: string
  updatedBy: string | null
}

// What signals return: how many were counted (repeats within 30 minutes aren't)
export interface Counted {
  counted: number
}
