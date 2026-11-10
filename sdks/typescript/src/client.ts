import { FeedFabricError } from './errors.js'
import type {
  Comment, CommentPage, Counted, DataTypeInfo, Favourite, FeedPage, ForYouPage, Media, MentionPage, OwnPostsPage, PostDataInput,
  ReportReason, SavedPost, TrendingTag, Upload,
} from './types.js'

export const VERSION = '0.1.0'

export interface FeedFabricOptions {
  // Where the API runs, e.g. "https://feed.example.com"
  baseUrl: string
  // A key from the admin panel (Integrations → API keys): "ffk_…". Keep it on your servers.
  apiKey: string
  // Per request, in milliseconds. Default 30 seconds.
  timeoutMs?: number
  // Retries after a 429 or 503 (waiting Retry-After), and after network errors on requests that are safe to repeat.
  // Default 2.
  maxRetries?: number
  // Defaults to the global fetch
  fetch?: typeof fetch
}

// Who a call is for, and their profile for when FeedFabric first sees them (or refreshes them, at most hourly)
export interface MemberProfile {
  username?: string
  displayName?: string
  avatarUrl?: string
}

export interface PageQuery {
  limit?: number
  cursor?: string
}

export interface OwnPostsQuery extends PageQuery {
  // Only posts with data of this type
  type?: string
  order?: 'newest' | 'oldest'
  // Post IDs are time-ordered (UUIDv7): these bound the range
  gt?: string
  gte?: string
  lt?: string
  lte?: string
}

export interface PostInput<T = unknown> {
  // Required unless the post has data
  content?: string
  // Your app's own tags, written like hashtags without the #; on an edit, leaving them out keeps the post's
  tags?: string[]
  // Ready images from media.upload, in order. Only when creating.
  mediaIds?: string[]
  // Structured data of a registered type; DataType.of builds it. On an edit, leaving it out keeps the post's.
  data?: PostDataInput<T>
}

export interface DwellView {
  postId: string
  ms: number
}

export interface UploadOptions {
  // How long to wait for scanning to finish. Default 60 seconds.
  timeoutMs?: number
  // How often to check. Default 1 second.
  pollMs?: number
}

type Query = Record<string, string | number | undefined>
type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))
const enc = encodeURIComponent

// The SDK's entry point: one per server process, shared by every request
export class FeedFabric {
  private readonly baseUrl: string
  private readonly apiKey: string
  private readonly timeoutMs: number
  private readonly maxRetries: number
  private readonly fetchImpl: typeof fetch

  constructor(options: FeedFabricOptions) {
    if (!options.baseUrl) throw new TypeError('FeedFabric needs a baseUrl')
    if (!options.apiKey?.startsWith('ffk_')) throw new TypeError('FeedFabric needs an API key from the admin panel ("ffk_…")')
    this.baseUrl = options.baseUrl.replace(/\/+$/, '')
    this.apiKey = options.apiKey
    this.timeoutMs = options.timeoutMs ?? 30_000
    this.maxRetries = options.maxRetries ?? 2
    this.fetchImpl = options.fetch ?? globalThis.fetch
  }

  // Calls made for one of your members: their ID is the one your sign-in tokens carry (Auth:Claims:UserId)
  asMember(externalUserId: string, profile: MemberProfile = {}): Member {
    if (!externalUserId) throw new TypeError('asMember() needs the member’s ID')
    const headers: Record<string, string> = { 'FeedFabric-User': enc(externalUserId) }
    if (profile.username) headers['FeedFabric-Username'] = enc(profile.username)
    if (profile.displayName) headers['FeedFabric-Display-Name'] = enc(profile.displayName)
    if (profile.avatarUrl) headers['FeedFabric-Avatar'] = enc(profile.avatarUrl)
    return new Member(this, headers)
  }

  /** @internal */
  async request<T>(method: Method, path: string, headers: Record<string, string>, options: { query?: Query; body?: unknown } = {}): Promise<T> {
    const url = new URL(this.baseUrl + path)
    for (const [key, value] of Object.entries(options.query ?? {})) {
      if (value !== undefined && value !== '') url.searchParams.set(key, String(value))
    }
    const init: RequestInit = {
      method,
      headers: {
        Authorization: `Bearer ${this.apiKey}`,
        Accept: 'application/json',
        'User-Agent': `feedfabric-typescript/${VERSION}`,
        ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...headers,
      },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    }
    // Safe to send twice: everything but POST. A 429 means nothing happened, so that's always safe.
    const repeatable = method !== 'POST'

    for (let attempt = 0; ; attempt++) {
      let response: Response
      try {
        response = await this.fetchImpl(url, { ...init, signal: AbortSignal.timeout(this.timeoutMs) })
      } catch (e) {
        if (repeatable && attempt < this.maxRetries) {
          await sleep(backoff(attempt))
          continue
        }
        throw new FeedFabricError(0, 'network_error', e instanceof Error ? e.message : String(e))
      }
      if (response.ok) {
        if (response.status === 204) return undefined as T
        const text = await response.text()
        return (text ? JSON.parse(text) : undefined) as T
      }
      const error = await FeedFabricError.from(response)
      const retry = response.status === 429 || (response.status === 503 && repeatable)
      if (retry && attempt < this.maxRetries) {
        await sleep(error.retryAfter !== undefined ? error.retryAfter * 1000 : backoff(attempt))
        continue
      }
      throw error
    }
  }

  /** @internal */
  async put(url: string, body: Blob | ArrayBuffer | Uint8Array, contentType: string): Promise<void> {
    const response = await this.fetchImpl(url, {
      method: 'PUT', headers: { 'Content-Type': contentType }, body: body as BodyInit, signal: AbortSignal.timeout(this.timeoutMs),
    })
    if (!response.ok) throw await FeedFabricError.from(response)
  }
}

const backoff = (attempt: number) => Math.min(250 * 2 ** attempt, 4000) + Math.random() * 100

// Everything a member can do, as that member
export class Member {
  readonly posts: Posts
  readonly feeds: Feeds
  readonly comments: Comments
  readonly media: MediaUploads
  readonly signals: Signals

  constructor(private readonly client: FeedFabric, private readonly headers: Record<string, string>) {
    this.posts = new Posts(this)
    this.feeds = new Feeds(this)
    this.comments = new Comments(this)
    this.media = new MediaUploads(this, client)
    this.signals = new Signals(this)
  }

  /** @internal */
  call<T>(method: Method, path: string, options?: { query?: Query; body?: unknown }): Promise<T> {
    return this.client.request<T>(method, path, this.headers, options)
  }

  // Who FeedFabric thinks this is; also creates the member if they're new
  me(): Promise<{ externalUserId: string }> {
    return this.call('GET', '/me')
  }

  follow(externalUserId: string): Promise<void> {
    return this.call('PUT', `/follows/${enc(externalUserId)}`)
  }

  unfollow(externalUserId: string): Promise<void> {
    return this.call('DELETE', `/follows/${enc(externalUserId)}`)
  }

  favourites(): Promise<Favourite[]> {
    return this.call('GET', '/favourites')
  }

  // Up to 30; favourite.limit_reached after that
  addFavourite(externalUserId: string): Promise<void> {
    return this.call('PUT', `/favourites/${enc(externalUserId)}`)
  }

  removeFavourite(externalUserId: string): Promise<void> {
    return this.call('DELETE', `/favourites/${enc(externalUserId)}`)
  }

  // The registered data types and their schemas
  dataTypes(): Promise<DataTypeInfo[]> {
    return this.call('GET', '/data-types')
  }

  dataType(key: string): Promise<DataTypeInfo> {
    return this.call('GET', `/data-types/${enc(key)}`)
  }

  // Everything held about the member, as one JSON document. A few an hour.
  exportAccount(): Promise<Record<string, unknown>> {
    return this.call('GET', '/me/export')
  }

  // Erases the member: posts, comments and profile at once, the rest shortly after
  deleteAccount(): Promise<void> {
    return this.call('DELETE', '/me')
  }
}

class Posts {
  constructor(private readonly member: Member) {}

  create<T>(post: PostInput<T>): Promise<SavedPost> {
    return this.member.call('POST', '/userfeed/posts', { body: post })
  }

  // Within an hour of posting
  edit<T>(postId: string, post: PostInput<T>): Promise<SavedPost> {
    return this.member.call('PUT', `/userfeed/posts/${enc(postId)}`, { body: post })
  }

  delete(postId: string): Promise<void> {
    return this.member.call('DELETE', `/userfeed/posts/${enc(postId)}`)
  }

  // The member's own posts
  list(query: OwnPostsQuery = {}): Promise<OwnPostsPage> {
    return this.member.call('GET', '/userfeed', { query: { ...query } })
  }

  like(postId: string): Promise<void> {
    return this.member.call('PUT', `/userfeed/posts/${enc(postId)}/like`)
  }

  unlike(postId: string): Promise<void> {
    return this.member.call('DELETE', `/userfeed/posts/${enc(postId)}/like`)
  }

  repost(postId: string): Promise<void> {
    return this.member.call('PUT', `/userfeed/posts/${enc(postId)}/repost`)
  }

  unrepost(postId: string): Promise<void> {
    return this.member.call('DELETE', `/userfeed/posts/${enc(postId)}/repost`)
  }

  report(postId: string, reason: ReportReason): Promise<void> {
    return this.member.call('POST', `/userfeed/posts/${enc(postId)}/report`, { body: { reason } })
  }
}

class Feeds {
  constructor(private readonly member: Member) {}

  // Posts from the accounts the member follows, newest first
  home(query: PageQuery = {}): Promise<FeedPage> {
    return this.member.call('GET', '/userfeed/home', { query: { ...query } })
  }

  // Ranked for the member; profile picks a ranking profile
  forYou(query: PageQuery & { profile?: string } = {}): Promise<ForYouPage> {
    return this.member.call('GET', '/userfeed/foryou', { query: { ...query } })
  }

  favourites(query: PageQuery = {}): Promise<FeedPage> {
    return this.member.call('GET', '/userfeed/favourites', { query: { ...query } })
  }

  // Posts with a tag (hashtag, inferred topic or app tag), newest first
  tag(tag: string, query: PageQuery = {}): Promise<FeedPage> {
    return this.member.call('GET', `/userfeed/tags/${enc(tag)}/posts`, { query: { ...query } })
  }

  trendingTags(query: { hours?: number; limit?: number } = {}): Promise<TrendingTag[]> {
    return this.member.call('GET', '/userfeed/tags/trending', { query: { ...query } })
  }

  mentions(query: PageQuery = {}): Promise<MentionPage> {
    return this.member.call('GET', '/userfeed/mentions', { query: { ...query } })
  }
}

class Comments {
  constructor(private readonly member: Member) {}

  // Top-level comments, oldest first
  list(postId: string, query: PageQuery = {}): Promise<CommentPage> {
    return this.member.call('GET', `/userfeed/posts/${enc(postId)}/comments`, { query: { ...query } })
  }

  replies(commentId: string, query: PageQuery = {}): Promise<CommentPage> {
    return this.member.call('GET', `/userfeed/comments/${enc(commentId)}/replies`, { query: { ...query } })
  }

  create(postId: string, content: string, parentCommentId?: string): Promise<Comment> {
    return this.member.call('POST', `/userfeed/posts/${enc(postId)}/comments`, { body: { content, parentCommentId } })
  }

  delete(commentId: string): Promise<void> {
    return this.member.call('DELETE', `/userfeed/comments/${enc(commentId)}`)
  }

  report(commentId: string, reason: ReportReason): Promise<void> {
    return this.member.call('POST', `/userfeed/comments/${enc(commentId)}/report`, { body: { reason } })
  }
}

class MediaUploads {
  constructor(private readonly member: Member, private readonly client: FeedFabric) {}

  // Asks for an upload URL: then PUT the file there and call complete
  createUpload(contentType: string, size: number): Promise<Upload> {
    return this.member.call('POST', '/media/uploads', { body: { contentType, size } })
  }

  complete(mediaId: string): Promise<Media> {
    return this.member.call('POST', `/media/${enc(mediaId)}/complete`)
  }

  get(mediaId: string): Promise<Media> {
    return this.member.call('GET', `/media/${enc(mediaId)}`)
  }

  // The whole upload: asks for a URL, sends the file, and waits for scanning. Resolves with the ready image, to put
  // in a post's mediaIds; throws a FeedFabricError "media.rejected" if it's refused.
  async upload(file: Blob | ArrayBuffer | Uint8Array, contentType: string, options: UploadOptions = {}): Promise<Media> {
    const size = file instanceof Blob ? file.size : file.byteLength
    const upload = await this.createUpload(contentType, size)
    await this.client.put(upload.uploadUrl, file, upload.contentType)
    let media = await this.complete(upload.mediaId)
    const deadline = Date.now() + (options.timeoutMs ?? 60_000)
    while (media.status === 'processing' || media.status === 'awaitingUpload') {
      if (Date.now() > deadline) throw new FeedFabricError(0, 'media.timeout', 'The image is still being checked; try get() later.', { mediaId: media.id })
      await sleep(options.pollMs ?? 1000)
      media = await this.get(upload.mediaId)
    }
    if (media.status === 'rejected') {
      throw new FeedFabricError(422, 'media.rejected', `The image was rejected: ${media.rejectionReason ?? 'unknown reason'}`,
        { mediaId: media.id, reason: media.rejectionReason })
    }
    return media
  }
}

// What the member's reading tells the ranker. Send them from your server as your app reports them.
class Signals {
  constructor(private readonly member: Member) {}

  // Posts the member saw (at least half on screen for a second); up to 100 at a time
  impressions(postIds: string[]): Promise<Counted> {
    return this.member.call('POST', '/userfeed/impressions', { body: { postIds } })
  }

  // The member opened the post's own page
  open(postId: string): Promise<Counted> {
    return this.member.call('POST', `/userfeed/posts/${enc(postId)}/open`)
  }

  // How long posts stayed on screen; up to 100 at a time
  dwell(views: DwellView[]): Promise<Counted> {
    return this.member.call('POST', '/userfeed/dwell', { body: { views } })
  }

  notInterested(postId: string): Promise<void> {
    return this.member.call('POST', `/userfeed/posts/${enc(postId)}/not-interested`)
  }

  showMore(postId: string): Promise<void> {
    return this.member.call('POST', `/userfeed/posts/${enc(postId)}/show-more`)
  }
}

export type { Posts, Feeds, Comments, MediaUploads, Signals }
