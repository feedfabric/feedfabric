<script setup lang="ts">
import { computed } from 'vue'
import type { PostRanking } from '../types'

// What put a For You post where it is: each part of the formula as a bar (1 = no effect, longer = pushed it up),
// and the model's score when a trained model ranked the feed
const props = defineProps<{ ranking: PostRanking }>()
const byModel = computed(() => props.ranking.ranker.includes('model:'))
// A custom formula comes first, as its score rather than a multiplier
const formula = computed(() => props.ranking.factors.find(f => f.label === 'Profile formula'))
const factors = computed(() => props.ranking.factors.filter(f => f.label !== 'Profile formula'))
const intro = computed(() => {
  const ranker = props.ranking.ranker
  if (formula.value) return "Ranked by the profile's own formula. The built-in formula's view of it:"
  if (ranker === 'newest') return 'Newest first: no ranking.'
  return byModel.value ? "Ranked by the trained model. The formula's view of it:" : 'Ranked by the scoring formula: recency × interest × quality.'
})
const widest = computed(() => Math.max(0.01, ...factors.value.map(f => f.value - 1)))
const width = (value: number) => `${Math.max(2, ((value - 1) / widest.value) * 100)}%`
</script>

<template>
  <details class="why" @click.stop>
    <summary>Why this post? <span class="muted">#{{ ranking.position + 1 }} · {{ byModel ? `model score ${ranking.score.toFixed(3)}` : `score ${ranking.score.toFixed(2)}` }}</span></summary>
    <p v-if="ranking.explored" class="explore">Exploring: shown in a slot kept for posts the ranker wouldn't have picked, so it can learn what else you like.</p>
    <p v-if="formula" class="muted"><code>{{ formula.detail }}</code> = {{ formula.value.toFixed(3) }}</p>
    <p class="muted">{{ intro }}</p>
    <ul>
      <li v-for="f in factors" :key="f.label + f.detail">
        <span class="label">{{ f.label }}</span>
        <span class="track"><span class="fill" :class="{ down: f.value < 1 && f.label !== 'Recent' }" :style="{ width: f.label === 'Recent' || f.value < 1 ? `${Math.max(2, f.value * 100)}%` : width(f.value) }" /></span>
        <span class="value">×{{ f.value.toFixed(2) }}</span>
        <span class="detail muted">{{ f.detail }}</span>
      </li>
    </ul>
  </details>
</template>

<style scoped>
.why { font-size: 12.5px; border: 1px solid var(--line); border-radius: 8px; padding: 6px 10px; cursor: default; }
summary { cursor: pointer; color: var(--accent); }
.explore { color: var(--me); }
p { margin: 6px 0; }
ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
li { display: grid; grid-template-columns: 120px 1fr 48px; gap: 2px 8px; align-items: center; }
.track { height: 6px; border-radius: 99px; background: var(--line); overflow: hidden; }
.fill { display: block; height: 100%; background: var(--accent); border-radius: 99px; }
.fill.down { background: var(--like); }
.value { text-align: right; font-variant-numeric: tabular-nums; }
.detail { grid-column: 1 / -1; font-size: 11.5px; margin-bottom: 2px; }
</style>
