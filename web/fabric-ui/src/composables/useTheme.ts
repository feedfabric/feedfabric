import { computed, ref, watch } from 'vue'

// Light, dark, or whatever the system says. The choice is kept per browser and applied as <html data-theme>,
// which the tokens in fabric.css follow; "system" removes the attribute so the media query decides. isDark says what
// is actually showing, for things drawn outside CSS (charts, canvases) that need to redraw when it changes.
export type ThemeChoice = 'light' | 'dark' | 'system'

const StorageKey = 'fabric.theme'

function load(): ThemeChoice {
  try {
    const saved = localStorage.getItem(StorageKey)
    return saved === 'light' || saved === 'dark' ? saved : 'system'
  } catch {
    return 'system'
  }
}

const theme = ref<ThemeChoice>(load())

const media = typeof window !== 'undefined' && window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null
const systemDark = ref(media?.matches ?? false)
media?.addEventListener('change', e => (systemDark.value = e.matches))
const isDark = computed(() => theme.value === 'dark' || (theme.value === 'system' && systemDark.value))

function apply(choice: ThemeChoice) {
  if (typeof document === 'undefined') return
  if (choice === 'system') document.documentElement.removeAttribute('data-theme')
  else document.documentElement.setAttribute('data-theme', choice)
}

apply(theme.value)
watch(theme, choice => {
  apply(choice)
  try {
    if (choice === 'system') localStorage.removeItem(StorageKey)
    else localStorage.setItem(StorageKey, choice)
  } catch {
    // Private windows can refuse storage; the choice just won't survive a reload
  }
})

export function useTheme() {
  return { theme, isDark }
}
