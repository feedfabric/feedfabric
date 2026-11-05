import { ref, shallowRef } from 'vue'

// Older pages of a feed for infinite scroll: each loaded page is kept as loaded (only the newest page refreshes
// live), and the cursor moves on until the API says there are no more.
export function useOlderPages<T extends { id: string }>(fetchPage: (cursor: string) => Promise<{ items: T[]; nextCursor: string | null }>) {
  const items = shallowRef<T[]>([])
  const nextCursor = ref<string | null>(null)
  const loading = ref(false)
  let started = false

  // The newest page's cursor, until the first older page has been loaded
  function follow(cursor: string | null) {
    if (!started) nextCursor.value = cursor
  }

  async function loadMore() {
    if (!nextCursor.value || loading.value) return
    loading.value = true
    started = true
    try {
      const page = await fetchPage(nextCursor.value)
      const known = new Set(items.value.map(i => i.id))
      items.value = [...items.value, ...page.items.filter(i => !known.has(i.id))]
      nextCursor.value = page.nextCursor
    } finally {
      loading.value = false
    }
  }

  function reset() {
    items.value = []
    nextCursor.value = null
    started = false
  }

  return { items, nextCursor, loading, follow, loadMore, reset }
}
