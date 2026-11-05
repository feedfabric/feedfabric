// "3m ago", "2h ago", "4d ago". Timestamps without a zone are UTC.
export function ago(iso: string, now = Date.now()): string {
  const seconds = Math.max(0, (now - new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`).getTime()) / 1000)
  if (seconds < 60) return `${Math.round(seconds)}s ago`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m ago`
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h ago`
  return `${Math.round(seconds / 86400)}d ago`
}

const number = new Intl.NumberFormat('en-GB')
export const fmt = (n: number | null | undefined) => (n == null ? '–' : number.format(n))

const labels: Record<string, string> = {
  severe_toxicity: 'severe abuse', threat: 'threat', hate: 'hate', insult: 'insult', obscene: 'obscene',
  sexual_explicit: 'sexually explicit', blocked_term: 'blocked word', link_spam: 'link spam', flooding: 'flooding',
  nudity: 'nudity', reported: 'reported', manual: 'found by a reviewer', self_harm: 'self-harm', harassment: 'harassment',
  sexual: 'sexual', spam: 'spam', other: 'something else', suspended: 'account suspended', reinstated: 'account reinstated',
}

export const label = (category: string) => labels[category] ?? category.replaceAll('_', ' ')

// The chip colour for a post, comment or account status
export function statusTone(status: string): string {
  if (status === 'visible' || status === 'active' || status === 'approved' || status === 'reinstated') return 'good'
  if (status === 'held') return 'warn'
  if (status === 'deleted') return 'plain'
  return 'bad'
}
