import { session } from './session'
import type {
  AdminPost, AdminPostDetail, AdminReport, AdminUser, AdminUserDetail, ContentState, Dashboard, Decision, ForYouFeed, LogEntry, Me,
  ModerationAction, ModerationOverview, Page, ProfileDetail, ProfileError, ProfileSummary, ProfileVersion, RankingProfile, RankingVocabulary,
  ReviewItem, SystemInfo, UserRef, UserStatus, Upload, MediaStatus, AdminMedia, CreatedPost,
} from './types'

export class ApiError extends Error {
  constructor(readonly status: number, readonly code: string | undefined, message: string, readonly params: Record<string, unknown> = {},
    readonly errors: unknown[] = []) {
    super(message)
  }
}

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const headers: Record<string, string> = session.token ? { Authorization: `Bearer ${session.token}` } : { 'X-Dev-User': session.devUser }
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
    throw new ApiError(res.status, code, describe(res.status, code, `${method} ${path}`), params, errors)
  }
  return (res.status === 204 ? undefined : await res.json()) as T
}

// What went wrong, for a person rather than a log
function describe(status: number, code: string | undefined, what: string): string {
  if (status === 401) return 'Not signed in. Paste a token from your identity provider, or run the API in Development.'
  if (status === 403) return "This account doesn't have the staff role this page needs."
  if (code === 'post.not_found' || code === 'comment.not_found') return 'It was deleted before you got to it.'
  if (code === 'user.not_found') return 'No such user.'
  if (code === 'media.not_ready') return 'A photo is still being checked, or was rejected. Remove it or wait.'
  return `${what} failed: ${status}${code ? ` ${code}` : ''}`
}

// An error as a person would want to read it
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
  return e instanceof Error ? e.message : String(e)
}

// The settings a profile.invalid error is about, each with its message
export function profileErrors(e: unknown): ProfileError[] {
  return e instanceof ApiError && e.code === 'profile.invalid' ? (e.errors as ProfileError[]) : []
}

const id = (value: string) => encodeURIComponent(value)
const query = (params: Record<string, string | number | boolean | null | undefined>) => {
  const q = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '' && value !== false) q.set(key, String(value))
  }
  const s = q.toString()
  return s ? `?${s}` : ''
}

export interface UserSearch { q?: string; status?: UserStatus | null; sort?: 'username' | 'posts' | 'reports' | 'flags'; offset?: number; limit?: number }
export interface PostSearch { q?: string; author?: string; status?: ContentState | null; reported?: boolean; offset?: number; limit?: number }

export const api = {
  me: () => request<Me>('/admin/me'),
  dashboard: (minutes: number) => request<Dashboard>(`/admin/dashboard?minutes=${minutes}`),
  system: () => request<SystemInfo>('/admin/system'),

  // Moderation
  overview: () => request<ModerationOverview>('/admin/moderation/overview'),
  queue: () => request<ReviewItem[]>('/admin/moderation/queue'),
  log: (action?: ModerationAction | null, limit = 100) => request<LogEntry[]>(`/admin/moderation/log${query({ action, limit })}`),
  reports: (limit = 100) => request<AdminReport[]>(`/admin/moderation/reports?limit=${limit}`),
  decide: (item: { postId: string; commentId: string | null }, decision: Decision) =>
    item.commentId
      ? request<void>(`/admin/moderation/comments/${id(item.commentId)}/decision`, 'POST', { decision })
      : request<void>(`/admin/moderation/posts/${id(item.postId)}/decision`, 'POST', { decision }),

  // Users and posts
  users: (search: UserSearch) => request<Page<AdminUser>>(`/admin/users${query({ ...search })}`),
  searchUsers: async (q: string): Promise<UserRef[]> => (await api.users({ q, limit: 12 })).items,
  user: (externalUserId: string) => request<AdminUserDetail>(`/admin/users/${id(externalUserId)}`),
  setUserStatus: (externalUserId: string, status: 'active' | 'suspended') =>
    request<void>(`/admin/users/${id(externalUserId)}/status`, 'PUT', { status }),
  setVerified: (externalUserId: string, verified: boolean) =>
    request<void>(`/admin/users/${id(externalUserId)}/verified`, 'PUT', { verified }),
  posts: (search: PostSearch) => request<Page<AdminPost>>(`/admin/posts${query({ ...search })}`),
  post: (postId: string) => request<AdminPostDetail>(`/admin/posts/${id(postId)}`),

  // Ranking profiles (ranking admins)
  profiles: () => request<ProfileSummary[]>('/ranking/profiles'),
  rankingProfile: (key: string) => request<ProfileDetail>(`/ranking/profiles/${id(key)}`),
  profileVersions: (key: string) => request<ProfileVersion[]>(`/ranking/profiles/${id(key)}/versions`),
  profileVersion: (key: string, version: number) => request<ProfileDetail>(`/ranking/profiles/${id(key)}/versions/${version}`),
  saveProfile: (key: string, settings: RankingProfile) => request<ProfileDetail>(`/ranking/profiles/${id(key)}`, 'PUT', settings),
  deleteProfile: (key: string) => request<void>(`/ranking/profiles/${id(key)}`, 'DELETE'),
  previewProfile: (settings: RankingProfile, viewerId: string, limit = 30) =>
    request<ForYouFeed>(`/ranking/preview?viewer=${id(viewerId)}&limit=${limit}`, 'POST', settings),
  vocabulary: () => request<RankingVocabulary>('/ranking/vocabulary'),

  // Posting as the signed-in user, through the endpoints members use: get an upload slot, PUT the file there
  // (uploadFile), complete it, poll until the worker has checked it, then post with the ready images
  createUpload: (contentType: string, size: number) => request<Upload>('/media/uploads', 'POST', { contentType, size }),
  completeUpload: (mediaId: string) => request<MediaStatus>(`/media/${id(mediaId)}/complete`, 'POST'),
  media: (mediaId: string) => request<MediaStatus>(`/media/${id(mediaId)}`),
  adminMedia: (mediaId: string) => request<AdminMedia>(`/admin/media/${id(mediaId)}`),
  createPost: (content: string, mediaIds: string[]) => request<CreatedPost>('/userfeed/posts', 'POST', { content, mediaIds }),
}

// PUTs a file to an upload URL, reporting progress (fetch can't report upload progress yet)
export function uploadFile(url: string, file: Blob, contentType: string, onProgress: (percent: number) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    xhr.setRequestHeader('Content-Type', contentType)
    xhr.upload.onprogress = e => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100))
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new ApiError(xhr.status, undefined, `Upload failed: ${xhr.status}`)))
    xhr.onerror = () => reject(new Error('Upload failed: network error'))
    xhr.send(file)
  })
}
