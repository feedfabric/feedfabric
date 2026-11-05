import { ref, watch } from 'vue'
import { api } from './api'
import { session } from './session'
import type { Me } from './types'

// Who's signed in and what they can do; refetched whenever the sign-in changes
export const me = ref<Me | null>(null)
export const meError = ref<string | null>(null)

export async function loadMe() {
  try {
    me.value = await api.me()
    meError.value = null
  } catch (e) {
    me.value = null
    meError.value = e instanceof Error ? e.message : String(e)
  }
}

watch(() => [session.token, session.devUser], () => loadMe())
