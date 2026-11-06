<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import { ago, fmt, label } from '../format'
import type { SystemInfo, SystemModel } from '../types'

// How this server is set up, read-only: sign-in, moderation limits, the models it's running and how they tested,
// and whether events are flowing. Secrets never leave the server; a signing key only shows as set or not.
const info = ref<SystemInfo | null>(null)
const now = useNow()
const { error } = usePolling(async () => {
  info.value = await api.system()
}, { intervalMs: 20000 })

const modelName: Record<string, string> = {
  'models/safety/toxicity': 'Offensive language',
  'models/safety/nsfw': 'Nudity',
  'models/embeddings/minilm': 'Text meaning',
}

// The tuning script's test summary, when the model was fine-tuned here
function testSummary(m: SystemModel): string[] {
  const tests = m.tests as {
    checks?: { passed: number; total: number }
    heldOut?: { recall?: { tuned: number; original: number }; falseFlagRate?: { tuned: number; original: number } }
  } | null | undefined
  if (!tests) return []
  const lines: string[] = []
  if (tests.checks) lines.push(`${tests.checks.passed} of ${tests.checks.total} check sentences right`)
  if (tests.heldOut?.recall) lines.push(`caught ${pct(tests.heldOut.recall.tuned)} of unseen offensive sentences (original ${pct(tests.heldOut.recall.original)})`)
  if (tests.heldOut?.falseFlagRate) lines.push(`wrongly stopped ${pct(tests.heldOut.falseFlagRate.tuned)} of unseen fine ones (original ${pct(tests.heldOut.falseFlagRate.original)})`)
  return lines
}
const pct = (n: number) => `${Math.round(n * 1000) / 10}%`
const limits = (values: Record<string, number>) => Object.entries(values).map(([k, v]) => `${label(k === 'identity_attack' ? 'hate' : k)} ≥ ${v}`)
</script>

<template>
  <div class="page-head">
    <h1>Settings and models</h1>
    <span v-if="info" class="chip" :class="info.environment === 'Production' ? 'good' : 'warn'">{{ info.environment }}</span>
  </div>
  <p v-if="error" class="error card">{{ error }}</p>
  <p v-if="!info && !error" class="card"><span class="skeleton" /></p>

  <div v-if="info" class="grid two">
    <section class="card">
      <h2>Sign-in</h2>
      <dl>
        <dt>Mode</dt><dd><code>{{ info.auth.mode }}</code><span v-if="info.auth.mode === 'Dev'" class="muted"> · any token or X-Dev-User is trusted</span></dd>
        <template v-if="info.auth.mode === 'Jwt'">
          <dt>Identity provider</dt><dd>{{ info.auth.authority || '–' }}</dd>
          <dt>Audience</dt><dd>{{ info.auth.audience || '–' }}</dd>
          <dt>Shared secret</dt><dd>{{ info.auth.signingKeySet ? 'Set' : 'Not used' }}</dd>
        </template>
        <dt>User ID claim</dt><dd><code>{{ info.auth.claims.userId }}</code></dd>
        <dt>Role claims</dt><dd><code v-for="r in info.auth.claims.roles" :key="r">{{ r }}</code></dd>
        <dt>Moderator roles</dt><dd><code v-for="r in info.auth.roles.moderator" :key="r">{{ r }}</code></dd>
        <dt>Ranking roles</dt><dd><code v-for="r in info.auth.roles.rankingAdmin" :key="r">{{ r }}</code></dd>
        <dt>New users</dt><dd>{{ info.auth.provisionUsers ? 'Created on their first request' : 'Created by your system' }}</dd>
      </dl>
    </section>

    <section class="card">
      <h2>Moderation</h2>
      <dl>
        <dt>Blocked when posted</dt><dd>{{ limits(info.moderation.blockAt).join(', ') || '–' }}, plus blocked words, link spam and flooding</dd>
        <dt>Held for review</dt><dd>{{ limits(info.moderation.holdAt).join(', ') || '–' }}</dd>
        <dt>Held after</dt><dd>{{ info.moderation.reportsToHold }} reports from different people</dd>
        <dt>Photos refused at</dt><dd>nudity ≥ {{ info.moderation.nudityRejectAt }}</dd>
        <dt>Malware scan</dt>
        <dd>
          <span class="chip" :class="info.media.malwareScanning ? 'good' : 'warn'">{{ info.media.malwareScanning ? 'ClamAV' : 'off' }}</span>
          <span v-if="!info.media.malwareScanning" class="muted">photos are still checked and rebuilt</span>
        </dd>
        <dt>Spam rules</dt><dd>more than {{ info.moderation.maxLinks }} links, or a character {{ info.moderation.maxRepeatedCharacters }}+ times in a row</dd>
        <dt>Blocked words</dt><dd>{{ fmt(info.moderation.blockedTerms) }} in the list</dd>
      </dl>
    </section>

    <section class="card wide">
      <h2>Models</h2>
      <div class="models">
        <div v-for="m in info.models" :key="m.key" class="model">
          <div class="mhead">
            <strong>{{ modelName[m.key] ?? m.key }}</strong>
            <span class="chip" :class="m.present ? 'good' : 'bad'">{{ m.present ? 'loaded' : 'missing: run make models' }}</span>
            <span v-if="m.tunedAt" class="chip">fine-tuned {{ ago(m.tunedAt, now) }}</span>
          </div>
          <p v-if="m.source" class="muted src">{{ m.source }}</p>
          <ul v-if="testSummary(m).length" class="tests">
            <li v-for="line in testSummary(m)" :key="line">{{ line }}</li>
          </ul>
        </div>
        <div class="model">
          <div class="mhead">
            <strong>For You ranker</strong>
            <span class="chip" :class="info.ranker.loaded ? 'good' : 'warn'">{{ info.ranker.loaded ? `version ${info.ranker.version}` : 'not trained: using the formula' }}</span>
          </div>
          <p v-if="info.ranker.loaded" class="muted src">Trained {{ ago(info.ranker.trainedAt!, now) }} on {{ fmt(info.ranker.trainingRows) }} served posts</p>
        </div>
      </div>
    </section>

    <section class="card">
      <h2>Events</h2>
      <dl>
        <dt>Waiting to publish</dt>
        <dd>
          {{ fmt(info.pipeline.outboxWaiting) }}
          <span v-if="info.pipeline.oldestWaitingSeconds" class="muted"> · oldest {{ Math.round(info.pipeline.oldestWaitingSeconds) }}s</span>
        </dd>
        <template v-for="c in info.pipeline.consumers" :key="c.consumer">
          <dt>{{ c.consumer }}</dt><dd>last event {{ ago(c.lastProcessedAt, now) }}</dd>
        </template>
      </dl>
    </section>

    <section class="card">
      <h2>Accounts</h2>
      <dl>
        <template v-for="(count, status) in info.users" :key="status">
          <dt class="cap">{{ status }}</dt><dd>{{ fmt(count) }}</dd>
        </template>
      </dl>
    </section>
  </div>
</template>

<style scoped>
.wide { grid-column: 1 / -1; }
dl { display: grid; grid-template-columns: minmax(120px, max-content) 1fr; gap: 6px 16px; margin: 0; font-size: 14px; }
dt { color: var(--muted); }
dd { margin: 0; min-width: 0; overflow-wrap: anywhere; display: flex; flex-wrap: wrap; gap: 4px; align-items: baseline; }
.cap { text-transform: capitalize; }
.models { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 260px), 1fr)); gap: 12px; }
.model { border: 1px solid var(--line); border-radius: 10px; padding: 10px 12px; }
.mhead { display: flex; flex-wrap: wrap; gap: 6px; align-items: baseline; }
.src { margin: 4px 0 0; overflow-wrap: anywhere; }
.tests { margin: 6px 0 0; padding-left: 18px; font-size: 13px; }
@media (max-width: 520px) { dl { grid-template-columns: 1fr; gap: 0 16px; } dd { margin-bottom: 6px; } }
</style>
