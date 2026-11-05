import { session } from './session'
import type {
  CommentPage, Comment, Dashboard, Engagement, ForYouFeed, HomeFeed, MediaStatus, MentionPage, PostDetail, PostStats, Profile, ProfileDetail,
  ProfileError, ProfileSummary, ProfileVersion, RankingProfile, RankingVocabulary, TrendingTag, Upload, UserRef,
} from './types'

export class ApiError extends Error {
  constructor(readonly status: number, readonly code: string | undefined, message: string, readonly params: Record<string, unknown> = {},
    readonly errors: unknown[] = []) {
    super(message)
  }
}

const Violations: Record<string, string> = {
  severe_toxicity: 'severely abusive language',
  threat: 'a threat',
  hate: 'hateful language about who someone is',
  insult: 'insulting someone',
  obscene: 'very obscene language',
  blocked_term: "a word or phrase that isn't allowed",
  link_spam: 'too many links',
  flooding: 'repeated characters',
}

// An error as a person would want to read it: what was wrong, not the request that failed
export function describeError(e: unknown): string {
  if (e instanceof ApiError && e.code === 'profile.invalid') {
    return 'Some settings need changing: ' + profileErrors(e).map(x => x.message).join(' ')
  }
  if (e instanceof ApiError && e.code === 'profile.read_only') {
    return "Presets can't be changed. Use Save as to keep your own copy."
  }
  if (e instanceof ApiError && e.code === 'profile.invalid_key') {
    return 'Names for the URL use 2–40 lowercase letters, digits and dashes, starting with a letter.'
  }
  if (e instanceof ApiError && e.code === 'content.violation') {
    const category = String(e.params.category ?? '')
    return `This breaks the community rules: ${Violations[category] ?? category}. Please change it and try again.`
  }
  return e instanceof Error ? e.message : String(e)
}

// The settings a profile.invalid error is about, each with its message
export function profileErrors(e: unknown): ProfileError[] {
  return e instanceof ApiError && e.code === 'profile.invalid' ? (e.errors as ProfileError[]) : []
}

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const headers: Record<string, string> = { 'X-Dev-User': session.actingAs }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  const res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  if (!res.ok) {
    let code: string | undefined
    let params: Record<string, unknown> = {}
    let errors: unknown[] = []
    try {
      const problem = await res.json()
      code = problem.code
      params = problem.params ?? {}
      errors = problem.errors ?? []
    } catch {
      // Not a Problem Details body
    }
    throw new ApiError(res.status, code, `${method} ${path} failed: ${res.status}${code ? ` ${code}` : ''}`, params, errors)
  }
  return (res.status === 204 ? undefined : await res.json()) as T
}

const viewer = () => `viewer=${encodeURIComponent(session.actingAs)}`
const id = (value: string) => encodeURIComponent(value)

export const api = {
  activeUser: () => request<{ externalUserId: string }>('/dev/active-user'),
  searchUsers: (q: string) => request<UserRef[]>(`/dev/users?q=${encodeURIComponent(q)}`),
  profile: (externalUserId: string) => request<Profile>(`/dev/users/${id(externalUserId)}?${viewer()}`),
  post: (postId: string) => request<PostDetail>(`/dev/posts/${id(postId)}?${viewer()}`),
  postStats: (postIds: string[]) =>
    postIds.length ? request<PostStats[]>(`/dev/likes?ids=${postIds.join(',')}&${viewer()}`) : Promise.resolve([]),
  engagement: () => request<Engagement>(`/dev/engagement?${viewer()}`),
  userByUsername: (username: string) => request<UserRef>(`/dev/users/by-username/${id(username)}`),

  forYou: (cursor?: string | null, profile?: string | null) =>
    request<ForYouFeed>(`/userfeed/foryou?limit=20${cursor ? `&cursor=${cursor}` : ''}${profile ? `&profile=${id(profile)}` : ''}`),
  homeFeed: (cursor?: string | null) => request<HomeFeed>(`/userfeed/home?limit=30${cursor ? `&cursor=${cursor}` : ''}`),
  tagPosts: (tag: string, cursor?: string | null) =>
    request<HomeFeed>(`/userfeed/tags/${id(tag)}/posts?limit=30${cursor ? `&cursor=${cursor}` : ''}`),
  trending: (hours = 24) => request<TrendingTag[]>(`/userfeed/tags/trending?hours=${hours}&limit=12`),
  mentions: (cursor?: string | null) => request<MentionPage>(`/userfeed/mentions?limit=30${cursor ? `&cursor=${cursor}` : ''}`),
  recordImpressions: (postIds: string[]) => request<{ counted: number }>('/userfeed/impressions', 'POST', { postIds }),
  recordDwell: (views: { postId: string; ms: number }[]) => request<{ counted: number }>('/userfeed/dwell', 'POST', { views }),
  notInterested: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/not-interested`, 'POST'),
  openPost: (postId: string) => request<{ counted: boolean }>(`/userfeed/posts/${id(postId)}/open`, 'POST'),
  // Ranking profiles (admin only outside Development)
  profiles: () => request<ProfileSummary[]>('/ranking/profiles'),
  rankingProfile: (key: string) => request<ProfileDetail>(`/ranking/profiles/${id(key)}`),
  profileVersions: (key: string) => request<ProfileVersion[]>(`/ranking/profiles/${id(key)}/versions`),
  profileVersion: (key: string, version: number) => request<ProfileDetail>(`/ranking/profiles/${id(key)}/versions/${version}`),
  saveProfile: (key: string, settings: RankingProfile) => request<ProfileDetail>(`/ranking/profiles/${id(key)}`, 'PUT', settings),
  deleteProfile: (key: string) => request<void>(`/ranking/profiles/${id(key)}`, 'DELETE'),
  previewProfile: (settings: RankingProfile, viewerId: string, limit = 30) =>
    request<ForYouFeed>(`/ranking/preview?viewer=${id(viewerId)}&limit=${limit}`, 'POST', settings),
  vocabulary: () => request<RankingVocabulary>('/ranking/vocabulary'),
  dashboard: (minutes: number) => request<Dashboard>(`/dev/dashboard?minutes=${minutes}`),

  createPost: (content: string, mediaIds: string[] = []) =>
    request<{ id: string }>('/userfeed/posts', 'POST', { content, mediaIds }),

  // Image uploads: get a quarantine URL, PUT the file there (uploadFile), complete, then poll until scanned
  createUpload: (contentType: string, size: number) => request<Upload>('/media/uploads', 'POST', { contentType, size }),
  completeUpload: (mediaId: string) => request<MediaStatus>(`/media/${id(mediaId)}/complete`, 'POST'),
  media: (mediaId: string) => request<MediaStatus>(`/media/${id(mediaId)}`),

  like: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/like`, 'PUT'),
  unlike: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/like`, 'DELETE'),
  repost: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/repost`, 'PUT'),
  unrepost: (postId: string) => request<void>(`/userfeed/posts/${id(postId)}/repost`, 'DELETE'),
  follow: (externalUserId: string) => request<void>(`/follows/${id(externalUserId)}`, 'PUT'),
  unfollow: (externalUserId: string) => request<void>(`/follows/${id(externalUserId)}`, 'DELETE'),

  comments: (postId: string) => request<CommentPage>(`/userfeed/posts/${id(postId)}/comments?limit=50`),
  replies: (commentId: string) => request<CommentPage>(`/userfeed/comments/${id(commentId)}/replies?limit=50`),
  addComment: (postId: string, content: string, parentCommentId: string | null) =>
    request<Comment>(`/userfeed/posts/${id(postId)}/comments`, 'POST', { content, parentCommentId }),
  deleteComment: (commentId: string) => request<void>(`/userfeed/comments/${id(commentId)}`, 'DELETE'),
}

// PUTs a file straight to its upload URL (local /blobs or S3), reporting progress from 0 to 100.
// XMLHttpRequest rather than fetch, as fetch can't report upload progress.
export function uploadFile(url: string, file: Blob, contentType: string, onProgress: (percent: number) => void, signal?: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    xhr.setRequestHeader('Content-Type', contentType)
    xhr.upload.onprogress = e => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100))
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new ApiError(xhr.status, undefined, `Upload failed: ${xhr.status}`)))
    xhr.onerror = () => reject(new Error('Upload failed: network error'))
    xhr.onabort = () => reject(new DOMException('Upload cancelled', 'AbortError'))
    signal?.addEventListener('abort', () => xhr.abort())
    xhr.send(file)
  })
}
