import { ref, watch } from 'vue'
import { api } from './api'
import { me } from './me'

// The signed-in user's favourites, so a post's menu can say whether its author is one; reloaded whenever the
// sign-in changes. Empty until loaded, and when it can't be (the account isn't a member yet).
export const favourites = ref(new Set<string>())

export async function setFavourite(externalUserId: string, on: boolean) {
  await api.favourite(externalUserId, on)
  const next = new Set(favourites.value)
  if (on) next.add(externalUserId)
  else next.delete(externalUserId)
  favourites.value = next
}

watch(() => me.value?.externalUserId, async id => {
  favourites.value = new Set()
  if (!id) return
  try {
    const list = await api.favourites()
    // Someone else may have signed in while this was loading
    if (id === me.value?.externalUserId) favourites.value = new Set(list.map(f => f.externalUserId))
  } catch {
    // Shown as not favourites
  }
}, { immediate: true })
