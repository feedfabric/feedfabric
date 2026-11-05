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
