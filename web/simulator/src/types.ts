// Shapes returned by the API and its Development-only /dev endpoints

export interface UserRef {
  externalUserId: string
  username: string
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

export interface MediaStatus {
  id: string
  status: 'awaitingUpload' | 'processing' | 'ready' | 'rejected'
  rejectionReason: string | null
  width: number | null
  height: number | null
  url: string | null
  thumbnailUrl: string | null
}

export interface Upload {
  mediaId: string
  uploadUrl: string
  contentType: string
  expiresAt: string
}

// One entry on a profile: a post of theirs, or a post they reposted (repostedBy set; id is the repost's, postId the post's)
export interface ProfilePost {
  id: string
  postId: string
  content: string
  createdAt: string
  tags: Tag[]
  media?: PostMedia[]
  author: UserRef
  likes: number
  likedByViewer: boolean
  comments: number
  reposts: number
  repostedByViewer: boolean
  repostedBy: UserRef | null
  repostedAt: string | null
}

export interface Profile extends UserRef {
  followerCount: number
  followingCount: number
  postCount: number
  viewerFollows: boolean
  followers: UserRef[]
  following: UserRef[]
  posts: ProfilePost[]
}

export interface PostStats {
  postId: string
  likes: number
  likedByViewer: boolean
  comments: number
  reposts: number
  repostedByViewer: boolean
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
}

export interface HomeFeed {
  posts: HomeFeedPost[]
  nextCursor: string | null
  source: string
}

export interface PostDetail {
  id: string
  content: string
  createdAt: string
  tags: Tag[]
  media?: PostMedia[]
  author: UserRef
  likes: number
  likedByViewer: boolean
  comments: number
  reposts: number
  repostedByViewer: boolean
}

export interface Comment {
  id: string
  postId: string
  parentCommentId: string | null
  authorExternalUserId: string
  authorUsername: string
  content: string | null
  isDeleted: boolean
  createdAt: string
  replyCount: number
}

export interface CommentPage {
  comments: Comment[]
  nextCursor: string | null
}

export interface Activity {
  kind: 'like' | 'comment' | 'reply' | 'repost'
  createdAt: string
  actorExternalUserId: string
  actorUsername: string
  authorExternalUserId: string
  authorUsername: string
  postId: string
  postContent: string
  commentContent: string | null
}

export interface Engagement {
  recent: Activity[]
  affinity: { author: UserRef; score: number }[]
  topics: { tag: string; score: number }[]
}

export interface TrendingTag {
  tag: string
  posts: number
}

export interface Mention {
  id: string
  createdAt: string
  authorExternalUserId: string
  authorUsername: string
  postId: string
  commentId: string | null
  content: string
}

export interface MentionPage {
  mentions: Mention[]
  nextCursor: string | null
}

// What a post card shows, whichever endpoint it came from. id is the feed entry (a repost's own ID for a repost),
// postId the post shown, liked and commented on.
export interface CardPost {
  id: string
  postId: string
  content: string
  createdAt: string
  tags: Tag[]
  media?: PostMedia[]
  ranking?: PostRanking | null
  author?: UserRef
  likes: number
  likedByViewer: boolean
  comments: number
  reposts: number
  repostedByViewer: boolean
  repostedBy?: UserRef | null
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
    blocked: { kind: string; category: string; count: number }[]
    latest: { kind: string; category: string; score: number; excerpt: string | null; createdAt: string; username: string; externalUserId: string }[]
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
