<script setup lang="ts">
import { computed, ref } from 'vue'
import type { EChartsOption } from 'echarts'
import { api } from '../api'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import { ago } from '../format'
import type { Dashboard, DashboardPost } from '../types'
import EChart from '../components/EChart.vue'
import { FeatureLabels, HeadLabels } from '../ranking'

// The whole platform at a glance, refreshed every 10 seconds: activity, what's seen, opened and engaged with
// most, how long posts hold attention, and whether the pipeline (outbox, consumers, photo scanning) keeps up
const windows = [{ value: 15, label: '15 min' }, { value: 60, label: '1 hour' }, { value: 360, label: '6 hours' }, { value: 1440, label: '24 hours' }]
const minutes = ref(60)
const data = ref<Dashboard | null>(null)
const now = useNow()
const number = new Intl.NumberFormat('en-GB')
const fmt = (n: number | null | undefined) => (n == null ? '–' : number.format(n))

const { error } = usePolling(async () => {
  data.value = await api.dashboard(minutes.value)
}, { intervalMs: 10000, watch: [minutes] })

const windowLabel = computed(() => windows.find(w => w.value === minutes.value)?.label ?? `${minutes.value} min`)
const w = computed(() => data.value?.window)
// A rate over a handful of impressions means nothing, so wait for enough to count
const enough = computed(() => (w.value?.impressions ?? 0) >= 200)

const stats = computed(() => [
  { label: 'Posts', value: fmt(w.value?.posts), caption: `last ${windowLabel.value}` },
  { label: 'Likes', value: fmt(w.value?.likes), caption: `and ${fmt(w.value?.comments)} comments` },
  { label: 'Active users', value: fmt(w.value?.activeUsers), caption: 'posted, liked or commented' },
  { label: 'Impressions', value: fmt(w.value?.impressions), caption: 'seen for a second or more' },
  { label: 'Opens', value: fmt(w.value?.opens), caption: 'opened to read in full' },
  { label: 'Click-through', value: enough.value ? `${w.value?.clickThroughRate}%` : '–', caption: enough.value ? 'opens per impression' : 'collecting impressions…' },
  { label: 'Engagement rate', value: enough.value ? `${w.value?.engagementRate}%` : '–', caption: enough.value ? 'likes and comments per impression' : 'collecting impressions…' },
  { label: 'Average dwell', value: w.value?.averageDwellSeconds == null ? '–' : `${w.value.averageDwellSeconds}s`, caption: 'time a post stays on screen' },
])

const labels = computed(() => (data.value?.series ?? []).map(s => new Date(s.minute).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })))
const line = (name: string, values: number[]) => ({ type: 'line' as const, name, showSymbol: false, smooth: true, data: values })
const bar = (name: string, values: number[]) => ({ type: 'bar' as const, name, data: values, barMaxWidth: 12 })
const base = { tooltip: { trigger: 'axis' as const }, legend: { top: 0 }, grid: { left: 44, right: 12, top: 34, bottom: 26 } }

const activityOption = computed<EChartsOption>(() => ({
  ...base,
  xAxis: { type: 'category', data: labels.value, boundaryGap: false },
  yAxis: { type: 'value' },
  series: [
    line('Posts', (data.value?.series ?? []).map(s => s.posts)),
    line('Likes', (data.value?.series ?? []).map(s => s.likes)),
    line('Comments', (data.value?.series ?? []).map(s => s.comments)),
  ],
}))

const attentionOption = computed<EChartsOption>(() => ({
  ...base,
  xAxis: { type: 'category', data: labels.value },
  yAxis: { type: 'value' },
  series: [bar('Impressions', (data.value?.series ?? []).map(s => s.impressions)), bar('Opens', (data.value?.series ?? []).map(s => s.opens))],
}))

const dwellOption = computed<EChartsOption>(() => ({
  tooltip: { trigger: 'axis' },
  grid: { left: 44, right: 12, top: 12, bottom: 26 },
  xAxis: { type: 'category', data: (data.value?.dwellDistribution ?? []).map(d => d.label) },
  yAxis: { type: 'value' },
  series: [{ type: 'bar', name: 'Views', data: (data.value?.dwellDistribution ?? []).map(d => d.views), barMaxWidth: 48 }],
}))

// The most seen table: sortable by any number column
type SortKey = 'impressions' | 'viewers' | 'opens' | 'clickThroughRate' | 'averageDwellSeconds' | 'likes' | 'comments' | 'engagementRate'
const sortKey = ref<SortKey>('impressions')
const mostSeen = computed(() => [...(data.value?.mostSeen ?? [])].sort((a, b) => ((b[sortKey.value] ?? 0) as number) - ((a[sortKey.value] ?? 0) as number)))
const columns: { key: SortKey; label: string }[] = [
  { key: 'impressions', label: 'Impressions' }, { key: 'viewers', label: 'Viewers' }, { key: 'opens', label: 'Opens' },
  { key: 'clickThroughRate', label: 'CTR' }, { key: 'averageDwellSeconds', label: 'Dwell' }, { key: 'likes', label: 'Likes' },
  { key: 'comments', label: 'Comments' }, { key: 'engagementRate', label: 'Engagement' },
]
function cell(p: DashboardPost, key: SortKey) {
  if (key === 'clickThroughRate' || key === 'engagementRate') return p.impressions >= 20 ? `${p[key]}%` : '–'
  if (key === 'averageDwellSeconds') return p.averageDwellSeconds == null ? '–' : `${p.averageDwellSeconds}s`
  return fmt(p[key] as number)
}

// Ranking: For You against Following on the same simulated population
const ranking = computed(() => data.value?.ranking)
const feedLabel = (feed: string) => (feed === 'foryou' ? 'For you' : 'Following')
const experimentOption = computed<EChartsOption>(() => {
  const rows = ranking.value?.experiment ?? []
  const metrics = [['Engaged', 'engagementRate'], ['Opened', 'openRate'], ['Liked', 'likeRate'], ['Commented', 'commentRate']] as const
  return {
    tooltip: { trigger: 'axis', valueFormatter: v => `${v}%` },
    legend: { top: 0 },
    grid: { left: 44, right: 12, top: 34, bottom: 26 },
    xAxis: { type: 'category', data: metrics.map(m => m[0]) },
    yAxis: { type: 'value', axisLabel: { formatter: '{value}%' } },
    series: rows.map(r => ({ type: 'bar', name: feedLabel(r.feed), data: metrics.map(m => r[m[1]]), barMaxWidth: 28 })),
  }
})
const experimentSummary = computed(() => (ranking.value?.experiment ?? [])
  .map(r => `${feedLabel(r.feed)}: ${r.engagementRate}% of posts seen engaged with`).join('; '))
const importanceOption = computed<EChartsOption>(() => {
  const entries = Object.entries(ranking.value?.model?.importance ?? {}).reverse()
  return {
    tooltip: { trigger: 'axis', valueFormatter: v => `${v}%` },
    grid: { left: 130, right: 24, top: 8, bottom: 20 },
    xAxis: { type: 'value', axisLabel: { formatter: '{value}%' } },
    yAxis: { type: 'category', data: entries.map(([k]) => FeatureLabels[k] ?? k), axisLabel: { interval: 0 } },
    series: [{ type: 'bar', name: 'Importance', data: entries.map(([, v]) => Math.round(v * 1000) / 10), barMaxWidth: 14 }],
  }
})
const exploredRate = computed(() => ranking.value?.exploration.find(e => e.explored)?.engagementRate)
const rankedRate = computed(() => ranking.value?.exploration.find(e => !e.explored)?.engagementRate)
const kindLabel: Record<string, string> = { post: 'Post', comment: 'Comment', photo: 'Photo' }
const categoryLabel = (c: string) => c.replaceAll('_', ' ')
const lift = (model?: number, baseline?: number) =>
  model == null || baseline == null ? '' : `${model >= baseline ? '+' : ''}${(model - baseline).toFixed(3)} vs the formula`

// A consumer that hasn't finished an event for a while is falling behind, or stopped
function freshness(iso: string | null | undefined) {
  if (!iso) return { tone: 'idle', text: 'Nothing yet' }
  const seconds = (now.value - new Date(iso).getTime()) / 1000
  return seconds < 30 ? { tone: 'ok', text: `Live · ${ago(iso, now.value)}` }
    : seconds < 300 ? { tone: 'warn', text: `Slow · ${ago(iso, now.value)}` }
      : { tone: 'bad', text: `Stopped? · ${ago(iso, now.value)}` }
}
const consumerNames: Record<string, string> = { 'feed-fan-out': 'Feed fan-out', 'like-counts': 'Like counts', 'comment-affinity': 'Comment scores' }
</script>

<template>
  <div class="stack dashboard">
    <div class="page-head">
      <div>
        <h1>Platform dashboard</h1>
        <span class="muted">{{ data ? `Last ${windowLabel} · updated ${ago(data.generatedAt, now)}` : 'Loading…' }}</span>
      </div>
      <div class="windows" role="radiogroup" aria-label="Time window">
        <button v-for="o in windows" :key="o.value" role="radio" :aria-checked="minutes === o.value" :class="{ on: minutes === o.value }"
          @click="minutes = o.value">{{ o.label }}</button>
      </div>
    </div>
    <p v-if="error" class="error">{{ error }}</p>

    <div class="stats">
      <div v-for="s in stats" :key="s.label" class="card stat">
        <span class="label">{{ s.label }}</span>
        <strong>{{ data ? s.value : '…' }}</strong>
        <span class="muted">{{ s.caption }}</span>
      </div>
    </div>

    <div class="grid two">
      <section class="card">
        <h2>Activity per minute</h2>
        <EChart :option="activityOption" :height="240" label="Posts, likes and comments per minute"
          :summary="w ? `${fmt(w.posts)} posts, ${fmt(w.likes)} likes and ${fmt(w.comments)} comments in the last ${windowLabel}.` : undefined" />
      </section>
      <section class="card">
        <h2>Impressions and opens per minute</h2>
        <EChart :option="attentionOption" :height="240" label="Impressions and opens per minute"
          :summary="w ? `${fmt(w.impressions)} impressions and ${fmt(w.opens)} opens in the last ${windowLabel}.` : undefined" />
      </section>
    </div>

    <section class="card">
      <h2>How long posts stay on screen · {{ w?.averageDwellSeconds == null ? 'no views yet' : `average ${w.averageDwellSeconds}s` }}</h2>
      <EChart :option="dwellOption" :height="180" label="Views by time on screen" />
    </section>

    <section class="card">
      <h2>Ranking · For you vs Following · last {{ windowLabel }}</h2>
      <div class="grid two">
        <EChart :option="experimentOption" :height="220" label="For you against Following: share of posts seen that were engaged with, opened, liked and commented on"
          :summary="experimentSummary" />
        <table class="compare">
          <thead><tr><th /><th v-for="r in ranking?.experiment ?? []" :key="r.feed" class="num">{{ feedLabel(r.feed) }}</th></tr></thead>
          <tbody>
            <tr><td>Posts served</td><td v-for="r in ranking?.experiment ?? []" :key="r.feed" class="num">{{ fmt(r.served) }}</td></tr>
            <tr><td>Seen</td><td v-for="r in ranking?.experiment ?? []" :key="r.feed" class="num">{{ fmt(r.seen) }}</td></tr>
            <tr><td>Engaged per seen</td><td v-for="r in ranking?.experiment ?? []" :key="r.feed" class="num"><strong>{{ r.engagementRate }}%</strong></td></tr>
            <tr><td>Average dwell</td><td v-for="r in ranking?.experiment ?? []" :key="r.feed" class="num">{{ r.averageDwellSeconds == null ? '–' : `${r.averageDwellSeconds}s` }}</td></tr>
            <tr><td>Shown from same persona</td><td v-for="r in ranking?.experiment ?? []" :key="r.feed" class="num">{{ r.samePersonaShownShare == null ? '–' : `${r.samePersonaShownShare}%` }}</td></tr>
          </tbody>
        </table>
      </div>
      <p v-if="data && !ranking?.experiment.length" class="muted">No feeds served in this window yet.</p>
    </section>

    <section v-if="ranking?.profiles.length" class="card">
      <h2>For you by ranking profile · last {{ windowLabel }}</h2>
      <p class="muted">
        The same population, split between profiles: list them in <code>Simulator:ForYouProfiles</code>. Change one in the
        <RouterLink to="/studio" class="link">Feed studio</RouterLink>; each saved version is counted separately.
      </p>
      <table class="compare">
        <thead><tr><th>Profile</th><th class="num">Seen</th><th class="num">Engaged</th><th class="num">Opened</th><th class="num">Liked</th>
          <th class="num">Commented</th><th class="num">Not interested</th><th class="num">Dwell</th></tr></thead>
        <tbody>
          <tr v-for="p in ranking.profiles" :key="p.profile">
            <td><RouterLink :to="{ name: 'studio', params: { key: p.profile.split('@')[0] } }" class="link">{{ p.profile }}</RouterLink></td>
            <td class="num">{{ fmt(p.seen) }}</td>
            <td class="num"><strong>{{ p.engagementRate }}%</strong></td>
            <td class="num">{{ p.openRate }}%</td>
            <td class="num">{{ p.likeRate }}%</td>
            <td class="num">{{ p.commentRate }}%</td>
            <td class="num">{{ p.notInterestedRate }}%</td>
            <td class="num">{{ p.averageDwellSeconds == null ? '–' : `${p.averageDwellSeconds}s` }}</td>
          </tr>
        </tbody>
      </table>
    </section>

    <div class="grid two">
      <section class="card">
        <h2>Ranker</h2>
        <template v-if="ranking?.model">
          <strong class="big">Model {{ ranking.model.version }}</strong>
          <ul class="rows plain">
            <li><span>Trained</span><span>{{ ago(ranking.model.trainedAt, now) }} on {{ fmt(ranking.model.trainingRows) }} posts seen</span></li>
            <li><span>Learned from</span><span>{{ ranking.model.source === 'real' ? 'Real members' : ranking.model.source === 'synthetic' ? 'Simulated members only' : 'Real and simulated members' }}</span></li>
            <li><span>AUC (newest 20%)</span><span>{{ ranking.model.metrics.auc }} <span class="muted">{{ lift(ranking.model.metrics.auc, ranking.model.metrics.heuristic_auc) }}</span></span></li>
            <li v-if="ranking.model.metrics.ndcg_at_10 != null"><span>NDCG@10</span><span>{{ ranking.model.metrics.ndcg_at_10 }} <span class="muted">{{ lift(ranking.model.metrics.ndcg_at_10, ranking.model.metrics.heuristic_ndcg_at_10) }}</span></span></li>
            <li v-if="ranking.model.metrics.value_ndcg_at_10 != null"><span>Value NDCG@10</span><span>{{ ranking.model.metrics.value_ndcg_at_10 }} <span class="muted">{{ lift(ranking.model.metrics.value_ndcg_at_10, ranking.model.metrics.heuristic_value_ndcg_at_10) }}</span></span></li>
            <li v-for="(w, head) in ranking.model.weights ?? {}" :key="head">
              <span>{{ HeadLabels[head] ?? head }} <span class="muted">× {{ w }}</span></span>
              <span>AUC {{ ranking.model.metrics[`auc_${head}`] ?? '–' }}</span>
            </li>
            <li><span>Exploration slots</span><span>{{ exploredRate ?? '–' }}% engaged <span class="muted">vs {{ rankedRate ?? '–' }}% ranked</span></span></li>
            <li><span>Not interested</span><span>{{ fmt(ranking.notInterested) }} in the last {{ windowLabel }}</span></li>
            <li><span>Training data now</span><span>{{ fmt(ranking.trainingRows) }} seen · {{ fmt(ranking.trainingPositives) }} engaged</span></li>
          </ul>
        </template>
        <template v-else>
          <strong class="big">Scoring formula</strong>
          <p class="muted">No trained model yet: {{ fmt(ranking?.trainingRows) }} posts seen so far. Run <code>make train</code> once there are a few thousand.</p>
        </template>
      </section>
      <section class="card">
        <h2>What the model relies on</h2>
        <EChart v-if="ranking?.model" :option="importanceOption" :height="Math.max(260, Object.keys(ranking.model.importance).length * 20)" label="Share of the model's decisions each signal accounts for" />
        <p v-else class="muted">Shown once a model is trained.</p>
      </section>
    </div>

    <div class="grid two">
      <section class="card flush">
        <h2>Safety · stopped in the last {{ windowLabel }} <span class="badge" :class="data?.safety.total ? 'warn' : 'ok'">{{ fmt(data?.safety.total ?? 0) }}</span></h2>
        <ul class="rows">
          <li v-for="b in data?.safety.blocked ?? []" :key="b.kind + b.category + b.action">
            <span>{{ kindLabel[b.kind] ?? b.kind }} {{ b.action }}: <span class="cap">{{ categoryLabel(b.category) }}</span></span><span class="badge bad">{{ fmt(b.count) }}</span>
          </li>
          <li v-if="data && !data.safety.blocked.length" class="muted">Nothing stopped: no nudity, offensive language or spam got through to be blocked or held.</li>
          <li v-if="data?.safety.awaitingReview"><RouterLink to="/review" class="link">{{ fmt(data.safety.awaitingReview) }} waiting for review</RouterLink></li>
        </ul>
      </section>
      <section class="card flush">
        <h2>Latest blocked or held</h2>
        <ul class="rows">
          <li v-for="(b, i) in data?.safety.latest ?? []" :key="i">
            <span class="text">
              <RouterLink :to="{ name: 'user', params: { externalUserId: b.externalUserId } }">{{ b.username }}</RouterLink>
              · {{ kindLabel[b.kind] ?? b.kind }} {{ b.action }} · <span class="cap">{{ categoryLabel(b.category) }}</span>
              <span v-if="b.excerpt" class="muted"> · “{{ b.excerpt }}”</span>
            </span>
            <span class="muted">{{ ago(b.createdAt, now) }}</span>
          </li>
          <li v-if="data && !data.safety.latest.length" class="muted">Nothing blocked yet.</li>
        </ul>
      </section>
    </div>

    <section class="card flush">
      <h2>Most seen posts <span class="muted">· all time</span></h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Post</th><th>Author</th>
              <th v-for="c in columns" :key="c.key" class="num" :aria-sort="sortKey === c.key ? 'descending' : 'none'">
                <button class="plain" @click="sortKey = c.key">{{ c.label }}{{ sortKey === c.key ? ' ↓' : '' }}</button>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in mostSeen" :key="p.id">
              <td class="text"><RouterLink :to="{ name: 'post', params: { postId: p.id } }">{{ p.content }}</RouterLink></td>
              <td><RouterLink :to="{ name: 'user', params: { externalUserId: p.authorExternalUserId } }">{{ p.authorUsername }}</RouterLink></td>
              <td v-for="c in columns" :key="c.key" class="num">{{ cell(p, c.key) }}</td>
            </tr>
            <tr v-if="data && !mostSeen.length"><td colspan="10" class="muted">No impressions yet. Scroll a feed, or let the simulator read for a minute.</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <div class="grid two">
      <section class="card flush">
        <h2>Most liked · last {{ windowLabel }}</h2>
        <ul class="rows">
          <li v-for="p in data?.mostLiked ?? []" :key="p.id">
            <RouterLink class="text" :to="{ name: 'post', params: { postId: p.id } }">{{ p.content }}</RouterLink>
            <span class="muted">{{ p.authorUsername }} · {{ fmt(p.likes) }} likes</span>
          </li>
        </ul>
      </section>
      <section class="card flush">
        <h2>Top authors · last {{ windowLabel }}</h2>
        <ul class="rows">
          <li v-for="a in data?.topAuthors ?? []" :key="a.externalUserId">
            <RouterLink :to="{ name: 'user', params: { externalUserId: a.externalUserId } }">{{ a.username }}</RouterLink>
            <span class="muted">{{ fmt(a.likes) }} likes · {{ fmt(a.comments) }} comments</span>
          </li>
        </ul>
      </section>
    </div>

    <div class="grid four">
      <section class="card">
        <h2>Interest matching</h2>
        <strong class="big">{{ data?.window.samePersonaLikeShare == null ? '–' : `${data.window.samePersonaLikeShare}%` }}</strong>
        <p class="muted">of likes go to someone with the same persona; about 8% would be chance.</p>
      </section>
      <section class="card flush">
        <h2>Totals</h2>
        <ul v-if="data" class="rows">
          <li v-for="(value, name) in data.totals" :key="name"><span class="cap">{{ name }}</span><span>{{ fmt(value) }}</span></li>
        </ul>
      </section>
      <section class="card flush">
        <h2>Photo scanning</h2>
        <ul class="rows">
          <li v-for="m in data?.media.byStatus ?? []" :key="m.status"><span>{{ m.status }}</span><span class="badge" :class="m.status === 'Rejected' ? 'bad' : 'ok'">{{ fmt(m.count) }}</span></li>
          <li v-for="r in data?.media.rejections ?? []" :key="r.reason ?? '?'"><span class="muted">Rejected: {{ (r.reason ?? 'unknown').replaceAll('_', ' ') }}</span><span>{{ fmt(r.count) }}</span></li>
          <li v-if="data && !data.media.byStatus.length" class="muted">No photos yet</li>
        </ul>
      </section>
      <section class="card flush">
        <h2>Pipeline health</h2>
        <ul v-if="data" class="rows">
          <li>
            <span>Outbox</span>
            <span class="badge" :class="data.pipeline.outboxWaiting < 100 ? 'ok' : data.pipeline.outboxWaiting < 1000 ? 'warn' : 'bad'">{{ fmt(data.pipeline.outboxWaiting) }} waiting</span>
          </li>
          <li v-for="c in data.pipeline.consumers" :key="c.consumer">
            <span>{{ consumerNames[c.consumer] ?? c.consumer }}</span>
            <span class="badge" :class="freshness(c.lastProcessedAt).tone">{{ freshness(c.lastProcessedAt).text }}</span>
          </li>
        </ul>
      </section>
    </div>
  </div>
</template>

<style scoped>
.windows { display: flex; gap: 2px; padding: 3px; border-radius: 99px; background: var(--card); border: 1px solid var(--line); }
.windows button { border: 0; border-radius: 99px; padding: 4px 12px; font-size: 13px; color: var(--muted); background: none; }
.windows button.on { background: var(--accent); color: #fff; }
.stats { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 12px; }
.stat { display: flex; flex-direction: column; gap: 2px; }
.stat .label, h2 { font-size: 11.5px; text-transform: uppercase; letter-spacing: .06em; color: var(--muted); }
.stat strong { font-size: 24px; font-variant-numeric: tabular-nums; }
.stat .muted { font-size: 12px; }
.grid { display: grid; gap: 12px; }
.grid.two { grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); }
.grid.four { grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); }
h2 { margin: 0 0 10px; }
.card.flush { padding: 0; overflow: hidden; }
.card.flush h2 { padding: 14px 16px 0; }
.big { font-size: 32px; }
.table-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th, td { padding: 8px 12px; border-top: 1px solid var(--line); text-align: left; white-space: nowrap; }
th { color: var(--muted); font-weight: 600; }
th button { color: inherit; font-weight: inherit; }
.num { text-align: right; font-variant-numeric: tabular-nums; }
.text { max-width: 320px; overflow: hidden; text-overflow: ellipsis; }
.rows { list-style: none; margin: 0; padding: 0; }
.rows.plain li { padding: 6px 0; }
.compare td, .compare th { white-space: nowrap; }
.rows li { display: flex; justify-content: space-between; gap: 12px; padding: 8px 16px; border-top: 1px solid var(--line); font-size: 13.5px; }
.rows li .text { flex: 1; white-space: nowrap; }
.cap { text-transform: capitalize; }
.badge { font-size: 12px; padding: 1px 8px; border-radius: 99px; background: var(--line); }
.badge.ok { background: var(--accent-soft); color: var(--accent); }
.badge.warn { background: #fdf0d5; color: #8a5a00; }
.badge.bad { background: var(--like-soft); color: var(--like); }
p { margin: 0; }
</style>
