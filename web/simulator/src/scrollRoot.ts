// The element a page scrolls in: null for the window (this app), or an app shell's <main> for a front end whose
// shell scrolls its page area rather than the window. Impressions, dwell and infinite scroll watch it.
let find: () => Element | null = () => null

export function setScrollRoot(finder: () => Element | null) {
  find = finder
}

export function scrollRoot(): Element | null {
  return find()
}
