import { computed, ref, shallowRef, watch } from 'vue'
import { api, describeError, profileErrors } from '../api'
import { session } from '../session'
import type { ForYouFeed, ProfileDetail, ProfileError, ProfileSummary, ProfileVersion, RankingProfile, RankingVocabulary } from '../types'

const PreviewDelayMs = 400

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value))

// The feed studio: pick a profile, change its settings, and watch the feed they'd give change as you go, next to
// what the saved version gives. Nothing reaches anyone's feed until you save. Shared by both simulator apps.
export function useProfileStudio() {
  const profiles = ref<ProfileSummary[]>([])
  const vocabulary = shallowRef<RankingVocabulary | null>(null)
  // The saved version being edited, and the edits
  const selected = shallowRef<ProfileDetail | null>(null)
  const draft = ref<RankingProfile | null>(null)
  const versions = ref<ProfileVersion[]>([])
  // Whose feed the preview shows
  const viewer = ref(session.actingAs)

  const errors = ref<ProfileError[]>([])
  const error = ref<string | null>(null)
  const notice = ref<string | null>(null)
  const saving = ref(false)

  const preview = shallowRef<ForYouFeed | null>(null)
  const baseline = shallowRef<ForYouFeed | null>(null)
  const previewing = ref(false)

  const dirty = computed(() => !!selected.value && !!draft.value && JSON.stringify(draft.value) !== JSON.stringify(selected.value.settings))
  const fieldError = (field: string) => errors.value.find(e => e.field === field || e.field.startsWith(`${field}.`))?.message ?? null

  async function load() {
    try {
      const [list, vocab] = await Promise.all([api.profiles(), vocabulary.value ? Promise.resolve(vocabulary.value) : api.vocabulary()])
      profiles.value = list
      vocabulary.value = vocab
      error.value = null
    } catch (e) {
      error.value = describeError(e)
    }
  }

  async function select(key: string) {
    try {
      const detail = await api.rankingProfile(key)
      selected.value = detail
      draft.value = clone(detail.settings)
      errors.value = []
      notice.value = null
      versions.value = detail.preset ? [] : await api.profileVersions(key)
      error.value = null
      void refreshBaseline()
    } catch (e) {
      error.value = describeError(e)
    }
  }

  function reset() {
    if (selected.value) draft.value = clone(selected.value.settings)
  }

  // An earlier version's settings as the draft; saving makes it the newest version again
  async function restore(version: number) {
    if (!selected.value) return
    try {
      draft.value = clone((await api.profileVersion(selected.value.key, version)).settings)
      notice.value = `Loaded version ${version}. Save to make it live again.`
    } catch (e) {
      error.value = describeError(e)
    }
  }

  async function saveAs(key: string, name?: string) {
    if (!draft.value) return false
    saving.value = true
    try {
      const settings = { ...clone(draft.value), name: name?.trim() || draft.value.name }
      const saved = await api.saveProfile(key, settings)
      await load()
      await select(saved.key)
      notice.value = saved.version === 1 ? `Saved as “${saved.settings.name}”.` : `Saved version ${saved.version}.`
      return true
    } catch (e) {
      errors.value = profileErrors(e)
      error.value = describeError(e)
      return false
    } finally {
      saving.value = false
    }
  }

  const save = () => (selected.value && !selected.value.preset ? saveAs(selected.value.key) : Promise.resolve(false))

  async function remove() {
    if (!selected.value || selected.value.preset) return
    try {
      const key = selected.value.key
      await api.deleteProfile(key)
      if (session.forYouProfile === key) session.forYouProfile = ''
      await load()
      await select(vocabulary.value?.defaultProfile ?? 'balanced')
      notice.value = `Deleted “${key}”.`
    } catch (e) {
      error.value = describeError(e)
    }
  }

  // This app's For You uses the profile from now on
  function useInMyFeed() {
    if (selected.value) session.forYouProfile = selected.value.key
  }

  // Previews are numbered so a slow one can't overwrite a newer one
  let previewRun = 0
  let timer: ReturnType<typeof setTimeout> | undefined

  async function refreshPreview() {
    if (!draft.value) return
    const run = ++previewRun
    previewing.value = true
    try {
      const feed = await api.previewProfile(draft.value, viewer.value)
      if (run !== previewRun) return
      preview.value = feed
      errors.value = []
    } catch (e) {
      if (run !== previewRun) return
      errors.value = profileErrors(e)
      if (!errors.value.length) error.value = describeError(e)
    } finally {
      if (run === previewRun) previewing.value = false
    }
  }

  async function refreshBaseline() {
    if (!selected.value) return
    try {
      baseline.value = await api.previewProfile(selected.value.settings, viewer.value)
    } catch {
      baseline.value = null
    }
  }

  watch(draft, () => {
    clearTimeout(timer)
    timer = setTimeout(refreshPreview, PreviewDelayMs)
  }, { deep: true })

  watch(viewer, () => {
    void refreshBaseline()
    void refreshPreview()
  })

  // How far each post moved compared with the saved version: positive is up, null is new to the top
  const baselinePositions = computed(() => new Map(baseline.value?.posts.map((p, i) => [p.id, i]) ?? []))
  const movement = (postId: string, position: number): number | null => {
    const before = baselinePositions.value.get(postId)
    return before === undefined ? null : before - position
  }

  // The preview in numbers, against the saved version
  const summary = computed(() => describe(preview.value))
  const baselineSummary = computed(() => describe(baseline.value))

  return {
    profiles, vocabulary, selected, draft, versions, viewer, errors, error, notice, saving, preview, baseline, previewing,
    dirty, summary, baselineSummary, fieldError, movement,
    load, select, reset, restore, save, saveAs, remove, useInMyFeed, refreshPreview,
  }
}

export interface FeedSummary {
  candidates: number
  following: number
  authors: number
  explored: number
  medianAgeHours: number
  withPhotos: number
}

// What a page of feed looks like: how much is from people you follow, how varied, how fresh
function describe(feed: ForYouFeed | null): FeedSummary | null {
  if (!feed) return null
  const posts = feed.posts
  const share = (count: number) => (posts.length ? Math.round((count * 100) / posts.length) : 0)
  const ages = posts.map(p => (p.ranking?.features.age_minutes ?? 0) / 60).sort((a, b) => a - b)
  return {
    candidates: feed.candidates,
    following: share(posts.filter(p => (p.ranking?.features.follows_author ?? 0) > 0).length),
    authors: new Set(posts.map(p => p.authorExternalUserId)).size,
    explored: posts.filter(p => p.ranking?.explored).length,
    medianAgeHours: ages.length ? Math.round(ages[Math.floor(ages.length / 2)] * 10) / 10 : 0,
    withPhotos: share(posts.filter(p => (p.ranking?.features.has_media ?? 0) > 0).length),
  }
}

// Settings in words, for the editors in both apps
export const SourceLabels = {
  following: { label: 'People you follow', hint: 'Their latest posts' },
  topics: { label: 'Your topics', hint: "Doing well right now on the topics you engage with most" },
  similarReaders: { label: 'People like you', hint: 'What people with the same taste liked lately' },
  similarMeaning: { label: 'Similar meaning', hint: 'Close in meaning to what you liked, from anyone' },
} as const

export const ModeLabels = {
  model: { label: 'Trained model', hint: 'Predicts what each person will do, weighted by your goals. The formula until a model is trained.' },
  formula: { label: 'Built-in formula', hint: 'Recency × interest × quality. Simple, explainable, needs no training data.' },
  expression: { label: 'Your formula', hint: 'Write your own score from the signals below.' },
  newest: { label: 'Newest first', hint: 'No ranking: everything from your sources, newest at the top.' },
} as const

export const GoalLabels = {
  open: 'Opens it',
  like: 'Likes it',
  comment: 'Comments',
  longRead: 'Reads for 10s+',
  skip: 'Scrolls past',
} as const

export const BoostLabels = {
  tag: 'Tagged',
  author: 'By',
  hasMedia: 'Has photos',
  following: 'From people you follow',
  notFollowing: "From people you don't follow",
} as const
