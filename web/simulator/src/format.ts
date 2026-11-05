const Personas: Record<string, string> = {
  runs: 'Runner', walks: 'Walker', travels: 'Globetrotter', flows: 'Yogi', lifts: 'Lifter', rides: 'Water & wheels',
  habits: 'Habit builder', recovers: 'Recovering', parenting: 'New parent', desk: 'Desk worker', stride: 'Silver stride',
  beginner: 'Beginner',
}

// Simulated usernames start with their persona's handle, e.g. runs_14
export function persona(username: string): string | undefined {
  return Personas[username.split('_')[0]]
}

export function ago(iso: string, now = Date.now()): string {
  const seconds = Math.max(0, (now - new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`).getTime()) / 1000)
  if (seconds < 60) return `${Math.round(seconds)}s ago`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m ago`
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h ago`
  return `${Math.round(seconds / 86400)}d ago`
}
