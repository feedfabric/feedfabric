import { reactive } from 'vue'

// Short-lived messages about something that just happened ("Saved", "Couldn't reach the server"). One queue for
// the whole app, shown by a single <FabricToaster /> near the root. Problems stay until dismissed; everything else
// goes after timeout ms, paused while the pointer or focus is on it.
export interface Toast {
  id: number
  title: string
  message?: string
  tone: 'info' | 'good' | 'warn' | 'bad'
  timeout: number
  action?: { label: string; onSelect: () => void }
}

export type ToastInput = Partial<Omit<Toast, 'id' | 'title'>> & { title: string }

const MaxShown = 4
let nextId = 1

export const toasts = reactive<Toast[]>([])

export function dismissToast(id: number) {
  const index = toasts.findIndex(t => t.id === id)
  if (index >= 0) toasts.splice(index, 1)
}

export function toast(input: ToastInput): number {
  const tone = input.tone ?? 'info'
  const item: Toast = { timeout: tone === 'bad' ? 0 : 5000, ...input, tone, id: nextId++ }
  toasts.push(item)
  // Once there are too many to read the oldest goes, but never a problem while anything else could go instead:
  // problems stay until dismissed
  while (toasts.length > MaxShown) {
    const oldest = toasts.findIndex(t => t.tone !== 'bad')
    toasts.splice(oldest >= 0 ? oldest : 0, 1)
  }
  return item.id
}

export function useToast() {
  return {
    toast,
    dismiss: dismissToast,
    success: (title: string, message?: string) => toast({ title, message, tone: 'good' }),
    error: (title: string, message?: string) => toast({ title, message, tone: 'bad' }),
  }
}
