import { shallowRef } from 'vue'

// A feed that holds still, as on X: posts that arrive on a refresh wait behind a "Show N new posts" button
// instead of pushing the list down. Posts already on screen are swapped for their fresh copies, so like and
// comment counts stay live.
export function useLiveFeed<T extends { id: string }>() {
  // Lists are only ever replaced, never changed in place, so shallow refs are enough
  const shown = shallowRef<T[] | null>(null)
  const waiting = shallowRef<T[]>([])

  function update(latest: T[]) {
    if (shown.value === null) {
      shown.value = latest
      return
    }
    const fresh = new Map(latest.map(p => [p.id, p]))
    const onScreen = new Set(shown.value.map(p => p.id))
    shown.value = shown.value.map(p => fresh.get(p.id) ?? p)
    waiting.value = latest.filter(p => !onScreen.has(p.id))
  }

  function showNew() {
    if (!shown.value || !waiting.value.length) return
    shown.value = [...waiting.value, ...shown.value]
    waiting.value = []
  }

  function reset() {
    shown.value = null
    waiting.value = []
  }

  return { shown, waiting, update, showNew, reset }
}
