import { reactive, ref, watch } from 'vue'

const ActingAsKey = 'feedfabric.actingAs'
const ProfileKey = 'feedfabric.forYouProfile'

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
    // Private windows can refuse storage; the choice just won't survive a reload
  }
}

// Who the app acts as: sent as X-Dev-User on every request. Dev user 1 by default.
// forYouProfile: the ranking profile For You uses here; empty for the server's default.
export const session = reactive({
  actingAs: load(ActingAsKey, '1'),
  actingAsName: '',
  forYouProfile: load(ProfileKey, ''),
})

watch(() => session.actingAs, value => save(ActingAsKey, value))
watch(() => session.forYouProfile, value => save(ProfileKey, value))

// Bumped after every write, so every polling view refetches straight away instead of on its next tick
export const changes = ref(0)

export function changed() {
  changes.value++
}
