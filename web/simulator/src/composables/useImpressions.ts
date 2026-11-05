import { onMounted, onUnmounted, type Ref } from 'vue'
import { api } from '../api'
import { scrollRoot } from '../scrollRoot'

// What the feed tells the API about each post: an impression once at least half of it has been on screen for a
// second, and its dwell (how long it stayed at least half on screen) each time it scrolls away. Both are sent in
// batches every few seconds, and when the tab is hidden; they're best effort, so a failed batch is dropped.
const VisibleFor = 1000
const MinDwell = 300
const FlushEvery = 3000
const seen = new Set<string>()
const dwell: { postId: string; ms: number }[] = []
let flushTimer: ReturnType<typeof setTimeout> | undefined

function flush() {
  clearTimeout(flushTimer)
  flushTimer = undefined
  const ids = [...seen].slice(0, 100)
  ids.forEach(id => seen.delete(id))
  const views = dwell.splice(0, 100)
  if (ids.length) api.recordImpressions(ids).catch(() => {})
  if (views.length) api.recordDwell(views).catch(() => {})
  if (seen.size || dwell.length) schedule()
}

function schedule() {
  flushTimer ??= setTimeout(flush, FlushEvery)
}

// Leaving or hiding the tab sends what's waiting, so the last few posts' dwell isn't lost
document.addEventListener('visibilitychange', () => document.visibilityState === 'hidden' && flush())

// Watches one post's element. `target` is the element, or a component whose root element it is.
export function useImpression(target: Ref<HTMLElement | { $el: HTMLElement } | undefined>, postId: () => string) {
  let impressionTimer: ReturnType<typeof setTimeout> | undefined
  let counted = false
  let visibleSince: number | null = null
  let observer: IntersectionObserver | undefined

  function startViewing() {
    visibleSince = performance.now()
    if (!counted) {
      impressionTimer = setTimeout(() => {
        counted = true
        seen.add(postId())
        schedule()
      }, VisibleFor)
    }
  }

  function stopViewing() {
    clearTimeout(impressionTimer)
    if (visibleSince === null) return
    const ms = Math.round(performance.now() - visibleSince)
    visibleSince = null
    // Scrolled straight past: not a view
    if (ms >= MinDwell) {
      dwell.push({ postId: postId(), ms })
      schedule()
    }
  }

  // A hidden tab isn't being read
  const onVisibility = () => (document.visibilityState === 'hidden' ? stopViewing() : null)

  onMounted(() => {
    const el = target.value instanceof HTMLElement ? target.value : target.value?.$el
    if (!el) return
    observer = new IntersectionObserver(([entry]) => (entry.isIntersecting ? startViewing() : stopViewing()),
      { root: scrollRoot(), threshold: 0.5 })
    observer.observe(el)
    document.addEventListener('visibilitychange', onVisibility)
  })

  onUnmounted(() => {
    stopViewing()
    observer?.disconnect()
    document.removeEventListener('visibilitychange', onVisibility)
  })
}
