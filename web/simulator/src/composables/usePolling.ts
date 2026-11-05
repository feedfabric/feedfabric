import { onActivated, onDeactivated, onMounted, onUnmounted, ref, watch, type Ref, type WatchSource } from 'vue'
import { changes } from '../session'

interface Options {
  intervalMs?: number
  // Refetch straight away when any of these change, e.g. a route param
  watch?: WatchSource[]
  // Only poll while this is true
  enabled?: Ref<boolean>
}

// Runs load now, every intervalMs while the tab is visible, after any write anywhere in the app,
// and whenever a watched source changes. In a KeepAlive page it pauses while the page is hidden. Never runs two loads at once; a request that arrives
// mid-load runs once more afterwards so the result is never stale.
export function usePolling(load: () => Promise<void>, options: Options = {}) {
  const { intervalMs = 2000, enabled } = options
  const error = ref<string | null>(null)
  const loading = ref(true)
  let running = false
  let again = false
  let timer: ReturnType<typeof setInterval> | undefined
  let hidden = false

  async function run() {
    if (hidden || (enabled && !enabled.value)) return
    if (running) {
      again = true
      return
    }
    running = true
    try {
      await load()
      error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      running = false
      loading.value = false
    }
    if (again) {
      again = false
      await run()
    }
  }

  onMounted(() => {
    run()
    timer = setInterval(() => {
      if (!document.hidden) run()
    }, intervalMs)
  })
  onUnmounted(() => clearInterval(timer))
  onDeactivated(() => (hidden = true))
  onActivated(() => {
    hidden = false
    run()
  })
  watch([changes, ...(options.watch ?? []), ...(enabled ? [enabled] : [])], () => run())

  return { error, loading, refresh: run }
}
