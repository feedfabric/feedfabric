// Shapes the API returns to the admin panel: /admin/* (src/FeedFabric.Application/Services/Admin, Safety/ReviewService.cs),
// /admin/dashboard and /ranking/*

export interface UserRef {
  externalUserId: string
  username: string
}

export type ModerationAction = 'blocked' | 'held' | 'reported' | 'reviewed'
export type Decision = 'approved' | 'removed'

export interface ModerationOverview {
  waiting: number
  oldestWaitingSince: string | null
  blockedToday: number
  heldToday: number
  reportsToday: number
  reviewedToday: number
  removedToday: number
}

export interface ReviewReason {
  action: ModerationAction
  category: string
  score: number
  at: string
}

export interface ReviewItem {
  kind: 'post' | 'comment'
  postId: string
  commentId: string | null
  author: UserRef
  content: string
  createdAt: string
  heldAt: string
  reasons: ReviewReason[]
  // Reports since it was last reviewed, by reason
  reports: Record<string, number>
}

export interface LogEntry {
  id: string
  kind: 'post' | 'comment' | 'photo' | 'user'
  action: ModerationAction
  category: string
  score: number
  excerpt: string | null
  createdAt: string
  author: UserRef
  postId: string | null
  commentId: string | null
  // approved / removed for posts and comments; suspended / reinstated for accounts
  decision: Decision | 'suspended' | 'reinstated' | null
  reviewedBy: string | null
  reviewedAt: string | null
  // Deleted by its author before anyone reviewed it
  authorDeleted: boolean
}

// The signed-in user, and what the panel should show them
export interface Me {
  externalUserId: string
  username: string | null
  displayName: string | null
  moderator: boolean
  rankingAdmin: boolean
  environment: string
}

export interface Page<T> {
  items: T[]
  total: number
}

export type UserStatus = 'active' | 'suspended' | 'deleted'
export type ContentState = 'visible' | 'held' | 'removed' | 'deleted'

export interface AdminUser {
  externalUserId: string
  username: string
  displayName: string | null
  avatarUrl: string | null
  status: UserStatus
  isVerified: boolean
  syncedAt: string
  posts: number
  followers: number
  reportsAgainst: number
  flags: number
}

export interface AdminPost {
  id: string
  author: UserRef
  content: string
  status: ContentState
  createdAt: string
  editedAt: string | null
  likes: number
  comments: number
  reposts: number
  reports: number
}

export interface AdminComment {
  id: string
  postId: string
  parentCommentId: string | null
  author: UserRef
  content: string
  status: ContentState
  createdAt: string
  reports: number
}

export interface AdminReport {
  id: string
  reason: string
  createdAt: string
  reporter: UserRef
  kind: 'post' | 'comment'
  postId: string
  commentId: string | null
  targetAuthor: UserRef
  excerpt: string
  targetStatus: ContentState
}

export interface AdminUserDetail {
  user: AdminUser
  following: number
  comments: number
  likes: number
  recentPosts: AdminPost[]
  recentComments: AdminComment[]
  history: LogEntry[]
  reportsAgainst: AdminReport[]
}

export interface AdminPostDetail {
  post: AdminPost
  tags: Tag[]
  media: PostMedia[]
  comments: AdminComment[]
  reports: AdminReport[]
  history: LogEntry[]
}

export interface SystemModel {
  key: string
  present: boolean
  kind?: string | null
  source?: string | null
  tunedAt?: string | null
  labels?: number | null
  tests?: Record<string, unknown> | null
}

export interface SystemInfo {
  environment: string
  auth: {
    mode: string
    authority: string | null
    audience: string | null
    issuer: string | null
    signingKeySet: boolean
    claims: { userId: string; username: string[]; displayName: string[]; avatar: string; roles: string[] }
    roles: { moderator: string[]; rankingAdmin: string[] }
    provisionUsers: boolean
  }
  moderation: {
    blockAt: Record<string, number>
    holdAt: Record<string, number>
    reportsToHold: number
    nudityRejectAt: number
    maxLinks: number
    maxRepeatedCharacters: number
    blockedTerms: number
  }
  models: SystemModel[]
  ranker: { loaded: boolean; version?: string; trainedAt?: string; trainingRows?: number }
  users: Record<string, number>
  pipeline: { outboxWaiting: number; oldestWaitingSeconds: number | null; consumers: { consumer: string; lastProcessedAt: string }[] }
}

export interface Tag {
  tag: string
  source: 'hashtag' | 'inferred'
}

// A scanned, rebuilt image on a post; the URLs are short-lived
export interface PostMedia {
  id: string
  url: string
  thumbnailUrl: string
  width: number
  height: number
}

// Why a For You post is where it is
export interface PostRanking {
  ranker: string
  position: number
  score: number
  explored: boolean
  factors: { label: string; value: number; detail: string }[]
  features: Record<string, number>
}

export interface ForYouFeed {
  posts: HomeFeedPost[]
  nextCursor: string | null
  // "heuristic", "model:<version>", "expression" (or "expression+model:<version>") or "newest"
  ranker: string
  // The ranking profile, "<key>" for a preset or "<key>@<version>"
  profile: string
  candidates: number
}

export interface HomeFeedPost {
  id: string
  authorExternalUserId: string
  authorUsername: string
  content: string
  createdAt: string
  tags: Tag[]
  media?: PostMedia[]
  ranking?: PostRanking | null
  // On a repost: the post it shares. The repost's author is who reposted it, and createdAt when.
  repostOf?: HomeFeedPost | null
  // Held for review: only its author is shown it
  isHeld?: boolean
}

export interface TrendingTag {
  tag: string
  posts: number
}

export interface DashboardPost {
  id: string
  content: string
  authorUsername: string
  authorExternalUserId: string
  likes: number
  comments: number
  impressions: number
  viewers?: number
  opens?: number
  averageDwellSeconds?: number | null
  clickThroughRate?: number
  engagementRate?: number
}

export interface RankerModelInfo {
  version: string
  heads?: Record<string, string>
  weights?: Record<string, number>
  exploredShare?: number
  source?: 'real' | 'synthetic' | 'all'
  trainedAt: string
  trainingRows: number
  positiveRate: number
  metrics: Record<string, number>
  importance: Record<string, number>
}

export interface FeedExperiment {
  feed: 'foryou' | 'following'
  served: number
  seen: number
  openRate: number
  likeRate: number
  commentRate: number
  engagementRate: number
  averageDwellSeconds: number | null
  samePersonaShownShare: number | null
  samePersonaEngagedShare: number | null
}


export interface Dashboard {
  windowMinutes: number
  generatedAt: string
  ranking: {
    ranker: string
    model: RankerModelInfo | null
    experiment: FeedExperiment[]
    trainingRows: number
    trainingPositives: number
    exploration: { explored: boolean; seen: number; engagementRate: number }[]
    notInterested: number
    profiles: ProfileExperiment[]
  }
  safety: {
    total: number
    blocked: { kind: string; category: string; action: ModerationAction; count: number }[]
    latest: { kind: string; category: string; action: ModerationAction; score: number; excerpt: string | null; createdAt: string; username: string; externalUserId: string }[]
    // Held posts and comments no reviewer has looked at yet
    awaitingReview: number
  }
  window: {
    posts: number
    likes: number
    comments: number
    impressions: number
    activeUsers: number
    engagementRate: number
    samePersonaLikeShare: number | null
    opens: number
    clickThroughRate: number
    averageDwellSeconds: number | null
  }
  totals: { users: number; posts: number; likes: number; comments: number; follows: number; mentions: number }
  series: { minute: string; posts: number; likes: number; comments: number; impressions: number; opens: number; averageDwellSeconds: number | null }[]
  dwellDistribution: { label: string; views: number }[]
  mostSeen: DashboardPost[]
  mostLiked: DashboardPost[]
  topAuthors: { externalUserId: string; username: string; likes: number; comments: number }[]
  trending: TrendingTag[]
  media: {
    byStatus: { status: string; count: number }[]
    rejections: { reason: string | null; count: number }[]
    lastProcessedAt: string | null
  }
  pipeline: {
    outboxWaiting: number
    oldestWaitingSeconds: number | null
    consumers: { consumer: string; lastProcessedAt: string }[]
  }
}

// For You by ranking profile, on the dashboard
export interface ProfileExperiment {
  profile: string
  served: number
  seen: number
  openRate: number
  likeRate: number
  commentRate: number
  engagementRate: number
  notInterestedRate: number
  averageDwellSeconds: number | null
}

// Ranking profiles: src/FeedFabric.Application/Services/Ranking/Profiles/RankingProfile.cs
export type ScoringMode = 'model' | 'formula' | 'expression' | 'newest'
export type BoostMatch = 'tag' | 'author' | 'hasMedia' | 'following' | 'notFollowing'
export interface SourceSetting { enabled: boolean; limit: number }
export interface Goals { open: number; like: number; comment: number; longRead: number; skip: number }
export interface BoostRule { match: BoostMatch; value: string | null; multiplier: number }

export interface RankingProfile {
  name: string
  description: string
  sources: {
    following: SourceSetting
    topics: SourceSetting
    similarReaders: SourceSetting
    similarMeaning: SourceSetting
    topTopics: number
    lookbackHours: number
  }
  scoring: { mode: ScoringMode; expression: string | null; goals: Goals }
  freshness: { halfLifeHours: number | null; maxAgeHours: number | null }
  variety: { authorCap: number; topicRun: number; outOfNetwork: number; seenPenalty: number }
  exploration: { slots: number; fromRank: number }
  boosts: BoostRule[]
  filters: { excludeTags: string[]; onlyTags: string[]; mediaOnly: boolean; minLikes: number }
}

export interface ProfileSummary {
  key: string
  name: string
  description: string
  preset: boolean
  version: number
  savedAt: string | null
  isDefault: boolean
}

export interface ProfileDetail {
  key: string
  version: number
  preset: boolean
  label: string
  isDefault: boolean
  settings: RankingProfile
}

export interface ProfileVersion { version: number; savedAt: string; savedBy: string | null }

export interface ProfileError { field: string; message: string; position?: number | null }

export interface RankingVocabulary {
  variables: { name: string; description: string }[]
  functions: { name: string; signature: string; description: string }[]
  operators: string[]
  examples: { label: string; text: string }[]
  goals: { name: keyof Goals; description: string }[]
  presets: { key: string; settings: RankingProfile }[]
  defaults: RankingProfile
  defaultProfile: string
}
