import type { CardPost, HomeFeedPost, PostStats } from './types'

// A feed post plus its like, comment and repost counts, as PostCard shows it. A repost shows the post it shares.
export function toCardPost(p: HomeFeedPost, stats: Map<string, PostStats>): CardPost {
  const shown = p.repostOf ?? p
  const s = stats.get(shown.id)
  return {
    id: p.id,
    postId: shown.id,
    content: shown.content,
    createdAt: shown.createdAt,
    tags: shown.tags,
    media: shown.media,
    ranking: p.ranking,
    author: { externalUserId: shown.authorExternalUserId, username: shown.authorUsername },
    likes: s?.likes ?? 0,
    likedByViewer: s?.likedByViewer ?? false,
    comments: s?.comments ?? 0,
    reposts: s?.reposts ?? 0,
    repostedByViewer: s?.repostedByViewer ?? false,
    repostedBy: p.repostOf ? { externalUserId: p.authorExternalUserId, username: p.authorUsername } : null,
  }
}

export async function withStats(posts: HomeFeedPost[], load: (ids: string[]) => Promise<PostStats[]>): Promise<CardPost[]> {
  const ids = [...new Set(posts.map(p => (p.repostOf ?? p).id))]
  const stats = new Map((await load(ids)).map(s => [s.postId, s]))
  return posts.map(p => toCardPost(p, stats))
}
