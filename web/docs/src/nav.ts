// The sidebar, in reading order. Prev/next links at the foot of each page follow the same order.
export interface NavLink { label: string; href: string }
export interface NavSection { title: string; links: NavLink[] }

export const nav: NavSection[] = [
  {
    title: 'Start here',
    links: [
      { label: 'Overview', href: '/' },
      { label: 'Running it locally', href: '/running-locally/' },
    ],
  },
  {
    title: 'How it works',
    links: [
      { label: 'Posting and safety', href: '/how-it-works/posting/' },
      { label: 'Following and fan-out', href: '/how-it-works/following/' },
      { label: 'For You ranking', href: '/how-it-works/for-you/' },
      { label: 'Ranking profiles', href: '/how-it-works/profiles/' },
      { label: 'Feed log and training', href: '/how-it-works/training/' },
      { label: 'Moderation and reports', href: '/how-it-works/moderation/' },
      { label: 'Photos', href: '/how-it-works/media/' },
    ],
  },
  {
    title: 'Formulas',
    links: [
      { label: 'How a score is made', href: '/formulas/' },
      { label: 'Formula language', href: '/formulas/language/' },
      { label: 'Formula cookbook', href: '/formulas/cookbook/' },
    ],
  },
  {
    title: 'Build a UI',
    links: [
      { label: 'Sign-in and users', href: '/auth/' },
      { label: 'Building a UI', href: '/build-a-ui/' },
      { label: 'API client', href: '/build-a-ui/api-client/' },
      { label: 'Feeds and paging', href: '/build-a-ui/feeds/' },
      { label: 'Post cards and actions', href: '/build-a-ui/post-cards/' },
      { label: 'Composing with photos', href: '/build-a-ui/compose/' },
      { label: 'Reading signals', href: '/build-a-ui/signals/' },
      { label: 'React and plain JavaScript', href: '/build-a-ui/react/' },
      { label: 'A formula editor', href: '/build-a-ui/formula-editor/' },
    ],
  },
  {
    title: 'Deploy',
    links: [
      { label: 'Deploying FeedFabric', href: '/deploy/' },
      { label: 'Cloudflare', href: '/deploy/cloudflare/' },
      { label: 'Operating it', href: '/operating/' },
    ],
  },
  {
    title: 'Reference',
    links: [
      { label: 'API reference', href: '/api/' },
      { label: 'Errors', href: '/api/errors/' },
    ],
  },
  {
    title: 'Project',
    links: [
      { label: 'Contributing to the docs', href: '/contributing/' },
    ],
  },
]

export const pages: NavLink[] = nav.flatMap(section => section.links)

// "/auth" and "/auth/" are the same page
export const normalise = (path: string) => (path.endsWith('/') ? path : `${path}/`)
