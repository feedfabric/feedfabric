---
layout: ../../layouts/Doc.astro
title: API client
description: Types that match the API's JSON, one request function that signs every call and turns Problem Details into errors with a stable code, and the endpoints a member app uses.
---

## Types

The API speaks JSON with camelCase names. IDs are UUID strings, times are ISO 8601 strings in UTC, and absent values
are `null` rather than missing. These match the C# response classes (`HomeFeedPostResponse`, `ForYouResponse`,
`CommentResponse`, `MediaResponse` and friends in `src/FeedFabric.Application/Services`).

```ts
// src/types.ts
export interface Tag { tag: string; source: 'hashtag' | 'inferred' }

// Signed, short-lived URLs (an hour by default): use as given, never store
export interface PostMedia { id: string; url: string; thumbnailUrl: string; width: number; height: number }

export interface RankingFactor { label: string; value: number; detail: string }

export interface PostRanking {
  ranker: string               // "model:<version>", "heuristic", "expression…" or "newest"
  position: number             // 0-based place in this For You session
  score: number
  explored: boolean            // in an exploration slot rather than earned by score
  factors: RankingFactor[]     // the reasons, in words
  features: Record<string, number>
}

export interface FeedPost {
  id: string
  authorId: string
  authorExternalUserId: string
  authorUsername: string
  content: string              // "" on a repost
  createdAt: string
  editedAt: string | null
  repostOfId: string | null
  repostOf: FeedPost | null    // on a repost: the post it shares
  isHeld: boolean              // held for review: only its author is ever shown it
  tags: Tag[]
  media: PostMedia[]
  ranking: PostRanking | null  // For You only
}

export interface HomeFeed { posts: FeedPost[]; nextCursor: string | null; source: 'redis' | 'postgres' | 'redis+postgres' }

export interface ForYouFeed {
  posts: FeedPost[]
  nextCursor: string | null    // an offset such as "20": pass it back unchanged
  ranker: string
  profile: string              // "balanced", or "running-club@3" for a saved profile
  candidates: number
}

export interface CreatedPost {
  id: string; authorId: string; content: string; createdAt: string; editedAt: string | null
  repostOfId: string | null; isHeld: boolean
}

export interface Comment {
  id: string; postId: string; parentCommentId: string | null
  authorId: string; authorExternalUserId: string; authorUsername: string
  content: string | null       // null when deleted or removed
  isDeleted: boolean; isHeld: boolean
  createdAt: string
  replyCount: number           // direct, visible replies
}
export interface CommentPage { comments: Comment[]; nextCursor: string | null }

export interface Mention {
  id: string; createdAt: string; authorExternalUserId: string; authorUsername: string
  postId: string; commentId: string | null; content: string
}
export interface MentionPage { mentions: Mention[]; nextCursor: string | null }

export interface Favourite { externalUserId: string; username: string; displayName: string | null; avatarUrl: string | null; addedAt: string }
export interface TrendingTag { tag: string; posts: number }

export interface Upload { mediaId: string; uploadUrl: string; contentType: string; expiresAt: string }
export interface Media {
  id: string
  status: 'awaitingUpload' | 'processing' | 'ready' | 'rejected'
  rejectionReason: string | null
  width: number | null; height: number | null
  url: string | null; thumbnailUrl: string | null
}

export type ReportReason = 'spam' | 'harassment' | 'hate' | 'threat' | 'sexual' | 'self_harm' | 'other'
```

## Errors: Problem Details with a code

Every error, including auth, routing and unhandled exceptions, is
[RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`) with two additions:
**`code`**, always present and stable, and **`params`**, the values a message needs. `detail` is English for
developers and logs: don't show it to members or match on it.

```json
{ "status": 409, "title": "Conflict", "detail": "Posts can only be edited within 60 minutes of posting.",
  "code": "post.edit_window_closed", "params": { "editWindowMinutes": 60 }, "traceId": "00-…" }
```

Validation problems (`validation_failed`, and `profile.invalid` for ranking profiles) list each one in `errors`:

```json
{ "code": "validation_failed",
  "errors": [ { "field": "content", "code": "required" },
              { "field": "limit", "code": "out_of_range", "params": { "min": 1, "max": 100 } } ] }
```

Turn all of that into one error type, and keep your own message per code (translated, if you translate). Codes are
never renamed but new ones may be added, so always have a fallback. The full list is in [Errors](/api/errors/).

```ts
// src/api/errors.ts
export interface FieldError { field?: string; code: string; params?: Record<string, unknown> }

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly params: Record<string, unknown> = {},
    readonly errors: FieldError[] = [],
    readonly traceId?: string,
  ) {
    super(`${status} ${code}`)
  }
}

const messages: Record<string, (p: Record<string, unknown>) => string> = {
  'user.suspended': () => "Your account is suspended, so you can read but not post or interact for now.",
  'post.not_found': () => 'That post has been deleted.',
  'comment.not_found': () => 'That comment has been deleted.',
  'post.edit_window_closed': p => `Posts can only be edited for ${p.editWindowMinutes} minutes.`,
  'content.violation': p => `This breaks our community rules (${String(p.category).replaceAll('_', ' ')}).`,
  'media.type_not_allowed': () => 'Photos must be JPEG, PNG or WebP.',
  'media.too_large': () => 'That photo is too big.',
  'media.not_ready': () => 'A photo is still being checked, or was rejected. Remove it or wait.',
  'post.too_many_media': () => 'Too many photos on one post.',
  'favourite.limit_reached': p => `You can have up to ${p.max} favourites. Remove one first.`,
  'unauthorized': () => 'Please sign in again.',
}

export function describe(e: unknown): string {
  if (e instanceof ApiError) return messages[e.code]?.(e.params) ?? 'Something went wrong. Please try again.'
  if (e instanceof TypeError) return "Couldn't reach the server. Check your connection."
  return 'Something went wrong. Please try again.'
}
```

## Signing in

The client needs one thing from your sign-in code: a function that returns a current access token for FeedFabric's
audience, refreshing it when needed. Your identity provider's SDK provides that (`getAccessTokenSilently` in Auth0's
SPA SDK, `acquireTokenSilent` in MSAL, `getUser()` in oidc-client-ts).

```ts
// src/api/auth.ts
export type TokenProvider = () => Promise<string | null>

export const auth = {
  // Your SDK's "get a fresh access token", e.g. () => auth0.getAccessTokenSilently()
  getToken: (async () => null) as TokenProvider,
  // Development only: act as this user when there's no token (sent as X-Dev-User)
  devUser: import.meta.env.DEV ? 'sim-2' : null as string | null,
  // Called on a 401: send the member to sign in again
  onUnauthorized: () => {},
}
```

In Development (`Auth:Mode` `Dev`) the API trusts `X-Dev-User` and unsigned tokens; nothing else will work in
production, where it's `Jwt` and needs a real token. See [Sign-in and users](/auth/).

## One request function

```ts
// src/api/request.ts
import { ApiError } from './errors'
import { auth } from './auth'

export async function request<T>(path: string, init: { method?: string; body?: unknown; keepalive?: boolean } = {}): Promise<T> {
  const headers: Record<string, string> = {}
  const token = await auth.getToken()
  if (token) headers.Authorization = `Bearer ${token}`
  else if (auth.devUser) headers['X-Dev-User'] = auth.devUser
  if (init.body !== undefined) headers['Content-Type'] = 'application/json'

  const res = await fetch(path, {
    method: init.method ?? 'GET',
    headers,
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
    keepalive: init.keepalive,
  })

  if (!res.ok) {
    const problem = await res.json().catch(() => ({}))
    if (res.status === 401) auth.onUnauthorized()
    throw new ApiError(res.status, problem.code ?? `http_${res.status}`, problem.params, problem.errors, problem.traceId)
  }
  // 204 has no body; 201 and 202 do
  return (res.status === 204 ? undefined : await res.json()) as T
}
```

## The endpoints

```ts
// src/api/index.ts
import { request } from './request'
import type {
  CommentPage, Comment, CreatedPost, Favourite, ForYouFeed, HomeFeed, Media, MentionPage, ReportReason, TrendingTag, Upload,
} from '../types'

const q = (params: Record<string, string | number | null | undefined>) => {
  const s = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== null && v !== undefined && v !== '') s.set(k, String(v))
  const text = s.toString()
  return text ? `?${text}` : ''
}
const id = encodeURIComponent

export const api = {
  // Feeds: newest first, or ranked; pass the previous page's nextCursor back as cursor
  home: (cursor?: string | null, limit = 20) => request<HomeFeed>(`/userfeed/home${q({ cursor, limit })}`),
  forYou: (cursor?: string | null, profile?: string | null, limit = 20) =>
    request<ForYouFeed>(`/userfeed/foryou${q({ cursor, profile, limit })}`),
  favouritesFeed: (cursor?: string | null) => request<HomeFeed>(`/userfeed/favourites${q({ cursor })}`),
  tagPosts: (tag: string, cursor?: string | null) => request<HomeFeed>(`/userfeed/tags/${id(tag)}/posts${q({ cursor })}`),
  trending: (hours = 24, limit = 10) => request<TrendingTag[]>(`/userfeed/tags/trending${q({ hours, limit })}`),
  mentions: (cursor?: string | null) => request<MentionPage>(`/userfeed/mentions${q({ cursor })}`),

  // Posts
  createPost: (content: string, mediaIds: string[] = []) => request<CreatedPost>('/userfeed/posts', { method: 'POST', body: { content, mediaIds } }),
  editPost: (postId: string, content: string) => request<CreatedPost>(`/userfeed/posts/${id(postId)}`, { method: 'PUT', body: { content } }),
  deletePost: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}`, { method: 'DELETE' }),

  // Engagement: PUT and DELETE are idempotent, so repeating one is harmless
  like: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/like`, { method: 'PUT' }),
  unlike: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/like`, { method: 'DELETE' }),
  repost: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/repost`, { method: 'PUT' }),
  unrepost: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/repost`, { method: 'DELETE' }),
  follow: (externalUserId: string) => request<void>(`/follows/${id(externalUserId)}`, { method: 'PUT' }),
  unfollow: (externalUserId: string) => request<void>(`/follows/${id(externalUserId)}`, { method: 'DELETE' }),
  favourites: () => request<Favourite[]>('/favourites'),
  addFavourite: (externalUserId: string) => request<void>(`/favourites/${id(externalUserId)}`, { method: 'PUT' }),
  removeFavourite: (externalUserId: string) => request<void>(`/favourites/${id(externalUserId)}`, { method: 'DELETE' }),

  // Comments
  comments: (postId: string, cursor?: string | null) => request<CommentPage>(`/userfeed/posts/${id(postId)}/comments${q({ cursor })}`),
  replies: (commentId: string, cursor?: string | null) => request<CommentPage>(`/userfeed/comments/${id(commentId)}/replies${q({ cursor })}`),
  comment: (postId: string, content: string, parentCommentId: string | null = null) =>
    request<Comment>(`/userfeed/posts/${id(postId)}/comments`, { method: 'POST', body: { content, parentCommentId } }),
  deleteComment: (commentId: string) => request<void>(`/userfeed/comments/${id(commentId)}`, { method: 'DELETE' }),

  // Reports
  reportPost: (postId: string, reason: ReportReason) => request<void>(`/userfeed/posts/${id(postId)}/report`, { method: 'POST', body: { reason } }),
  reportComment: (commentId: string, reason: ReportReason) =>
    request<void>(`/userfeed/comments/${id(commentId)}/report`, { method: 'POST', body: { reason } }),

  // Reading signals (see "Reading signals"); keepalive lets the last batch out as the page closes
  impressions: (postIds: string[], keepalive = false) =>
    request<{ counted: number }>('/userfeed/impressions', { method: 'POST', body: { postIds }, keepalive }),
  dwell: (views: { postId: string; ms: number }[], keepalive = false) =>
    request<{ counted: number }>('/userfeed/dwell', { method: 'POST', body: { views }, keepalive }),
  open: (postId: string) => request<{ counted: boolean }>(`/userfeed/posts/${id(postId)}/open`, { method: 'POST' }),
  notInterested: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/not-interested`, { method: 'POST' }),
  showMore: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/show-more`, { method: 'POST' }),

  // Photos (see "Composing with photos")
  createUpload: (contentType: string, size: number) => request<Upload>('/media/uploads', { method: 'POST', body: { contentType, size } }),
  completeUpload: (mediaId: string) => request<Media>(`/media/${id(mediaId)}/complete`, { method: 'POST' }),
  media: (mediaId: string) => request<Media>(`/media/${id(mediaId)}`),
}
```

## Handling errors well

| Situation | What to do |
|---|---|
| `401 unauthorized` | Token missing, expired or for the wrong audience: refresh it or sign in again. |
| `403 user.suspended` | Switch the app to read-only (hide composer and actions) and say why. Reading and reading signals still work. |
| `404 user.not_synced` | The member's user wasn't created yet (provisioning off, or a race on the very first request). Retry once; if it persists your back end needs to create users. |
| `404 post.not_found` / `comment.not_found` | It was deleted (or you used a repost's own ID). Remove it from the screen. |
| `422 content.violation` | Keep the member's text, show the rule (`params.category`), let them edit. |
| `validation_failed` | Show each `errors[].code` next to its `field`. |
| `500 internal_error` | Show a generic message; log `traceId`, which is what the server's logs are keyed on. |
| Network error (`TypeError` from `fetch`) | Offline or the server is down: offer to retry. |

Fire-and-forget calls (signals, likes after an optimistic update) should catch their own errors so a failure never
surfaces as an unhandled rejection.
