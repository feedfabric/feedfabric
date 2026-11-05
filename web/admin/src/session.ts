import { reactive, ref, watch } from 'vue'

const TokenKey = 'feedfabric.admin.token'
const DevUserKey = 'feedfabric.admin.devUser'

function load(key: string, fallback: string): string {
  try {
    return localStorage.getItem(key) ?? fallback
  } catch {
    return fallback
  }
}

function save(key: string, value: string) {
  try {
    if (value) localStorage.setItem(key, value)
    else localStorage.removeItem(key)
  } catch {
    // Private windows can refuse storage; it just won't survive a reload
  }
}

// How the panel signs in. token: a bearer token from your identity provider, carrying a staff role; needed
// everywhere but Development. devUser: in Development without a token, the user to act as (sent as X-Dev-User), so
// decisions are recorded against someone.
export const session = reactive({
  token: load(TokenKey, ''),
  devUser: load(DevUserKey, '1'),
})

watch(() => session.token, token => save(TokenKey, token))
watch(() => session.devUser, user => save(DevUserKey, user))

// Bumped after every change the panel makes, so open pages refetch at once rather than on their next poll
export const changes = ref(0)
export const changed = () => changes.value++
