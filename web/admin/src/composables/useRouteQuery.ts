import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

// A filter kept in the URL, so a filtered list can be bookmarked, shared and returned to with Back
export function useRouteQuery(name: string, fallback = '') {
  const route = useRoute()
  const router = useRouter()
  return computed({
    get: () => (typeof route.query[name] === 'string' ? (route.query[name] as string) : fallback),
    set: value => {
      const query = { ...route.query, [name]: value === fallback ? undefined : value }
      if (name !== 'page') delete query.page
      router.replace({ query })
    },
  })
}
