import { computed, ref, shallowRef } from 'vue'
import { api } from '../api'
import { session } from '../session'
import { withStats } from '../toCardPost'
import type { CardPost } from '../types'

// The For You feed: ranked once when it opens (and on refresh), then paged through that same order as you scroll.
// It doesn't re-rank on a timer, as that would reshuffle what you're reading.
export function useForYou() {
  const posts = shallowRef<CardPost[] | null>(null)
  const ranker = ref('')
  const profile = ref('')
  const candidates = ref(0)
  const nextCursor = ref<string | null>(null)
  const loadingMore = ref(false)
  const refreshing = ref(false)
  const error = ref<string | null>(null)

  async function refresh() {
    refreshing.value = true
    try {
      const page = await api.forYou(null, session.forYouProfile)
      posts.value = await withStats(page.posts, api.postStats)
      ranker.value = page.ranker
      profile.value = page.profile
      candidates.value = page.candidates
      nextCursor.value = page.nextCursor
      error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      refreshing.value = false
    }
  }

  async function loadMore() {
    if (!nextCursor.value || loadingMore.value) return
    loadingMore.value = true
    try {
      const page = await api.forYou(nextCursor.value, session.forYouProfile)
      const known = new Set(posts.value?.map(p => p.id))
      posts.value = [...(posts.value ?? []), ...(await withStats(page.posts, api.postStats)).filter(p => !known.has(p.id))]
      nextCursor.value = page.nextCursor
    } finally {
      loadingMore.value = false
    }
  }

  const rankerLabel = computed(() => describeRanker(ranker.value))

  return { posts, ranker, rankerLabel, profile, candidates, nextCursor, loadingMore, refreshing, error, refresh, loadMore }
}

// "model:20261105-041807", "heuristic", "expression+model:…" or "newest", in words
export function describeRanker(ranker: string): string {
  if (!ranker) return ''
  if (ranker.startsWith('model:')) return `Trained model ${ranker.slice(6)}`
  if (ranker.startsWith('expression')) return ranker.includes('model:') ? 'Custom formula using the trained model' : 'Custom formula'
  if (ranker === 'newest') return 'Newest first'
  return 'Scoring formula'
}

// Signal names from the API, in words
export const FeatureLabels: Record<string, string> = {
  age_minutes: 'Age',
  follows_author: 'Follows the author',
  author_affinity: 'Author you like',
  topic_match: 'Topic match',
  likes: 'Likes',
  comments: 'Comments',
  impressions: 'Impressions',
  opens: 'Opens',
  ctr: 'Click-through',
  engagement_rate: 'Engagement rate',
  avg_dwell_s: 'Time on screen',
  author_followers_log: 'Author followers',
  has_media: 'Has photos',
  content_length: 'Length',
  velocity_30m: 'Trending now',
  viewer_author_ctr: 'Your opens of the author',
  viewer_author_seen: "Author's posts you've seen",
  session_topic_match: 'This session',
  semantic_match: 'Similar meaning',
  position: 'Position',
}

// The model's predictions, in words
export const HeadLabels: Record<string, string> = {
  open: 'Opened', like: 'Liked', comment: 'Commented', long_dwell: 'Read 10s+', skip: 'Skipped', engaged: 'Engaged',
}
