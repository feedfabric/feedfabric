import { onUnmounted, ref } from 'vue'

// A clock that ticks every few seconds, so "12s ago" labels stay current without refetching
export function useNow(intervalMs = 5000) {
  const now = ref(Date.now())
  const timer = setInterval(() => (now.value = Date.now()), intervalMs)
  onUnmounted(() => clearInterval(timer))
  return now
}
