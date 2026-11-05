import type { CardPost, HomeFeedPost, PostStats } from './types'

// A feed post plus its like and comment counts, as PostCard shows it
export function toCardPost(p: HomeFeedPost, stats: Map<string, PostStats>): CardPost {
  const s = stats.get(p.id)
  return {
    id: p.id,
    content: p.content,
    createdAt: p.createdAt,
    tags: p.tags,
    media: p.media,
    ranking: p.ranking,
    author: { externalUserId: p.authorExternalUserId, username: p.authorUsername },
    likes: s?.likes ?? 0,
    likedByViewer: s?.likedByViewer ?? false,
    comments: s?.comments ?? 0,
  }
}

export async function withStats(posts: HomeFeedPost[], load: (ids: string[]) => Promise<PostStats[]>): Promise<CardPost[]> {
  const stats = new Map((await load(posts.map(p => p.id))).map(s => [s.postId, s]))
  return posts.map(p => toCardPost(p, stats))
}
