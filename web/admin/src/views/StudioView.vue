<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { BoostLabels, GoalLabels, ModeLabels, SourceLabels, useProfileStudio } from '../composables/useProfileStudio'
import { describeRanker } from '../ranking'
import { me } from '../me'
import type { BoostMatch, Goals, ScoringMode } from '../types'
import SettingSlider from '../components/SettingSlider.vue'
import UserPicker from '../components/UserPicker.vue'

// The feed studio (/studio/<key>): change how For You is put together and see the result straight away
const route = useRoute()
const router = useRouter()
const studio = useProfileStudio()
const { draft, selected } = studio

const presets = computed(() => studio.profiles.value.filter(p => p.preset))
const custom = computed(() => studio.profiles.value.filter(p => !p.preset))

onMounted(async () => {
  await studio.load()
  const key = (route.params.key as string | undefined) || studio.vocabulary.value?.defaultProfile || 'balanced'
  await studio.select(key)
})

watch(() => route.params.key, key => key && key !== selected.value?.key && studio.select(key as string))
const open = (key: string) => router.push({ name: 'studio', params: { key } })

// Save as: a URL-safe key from the name, editable
const savingAs = ref(false)
const newName = ref('')
const newKey = ref('')
const keyEdited = ref(false)
const slug = (name: string) => name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^[^a-z]+|-+$/g, '').slice(0, 40)
watch(newName, name => { if (!keyEdited.value) newKey.value = slug(name) })
function startSaveAs() {
  newName.value = selected.value?.preset ? `My ${draft.value?.name ?? 'profile'}` : `${draft.value?.name ?? ''} copy`
  keyEdited.value = false
  savingAs.value = true
}
async function confirmSaveAs() {
  if (await studio.saveAs(newKey.value, newName.value)) {
    savingAs.value = false
    await router.replace({ name: 'studio', params: { key: newKey.value } })
  }
}
async function confirmDelete() {
  if (selected.value && confirm(`Delete “${selected.value.settings.name}” and all its versions?`)) {
    await studio.remove()
    await router.replace({ name: 'studio', params: { key: selected.value?.key ?? 'balanced' } })
  }
}

const modes = Object.keys(ModeLabels) as ScoringMode[]
const goals = Object.keys(GoalLabels) as (keyof Goals)[]
const sources = Object.keys(SourceLabels) as (keyof typeof SourceLabels)[]
const boostKinds = Object.keys(BoostLabels) as BoostMatch[]

// The formula box: insert a signal or function where the cursor is
const formulaBox = ref<HTMLTextAreaElement | null>(null)
async function insert(text: string) {
  if (!draft.value) return
  const box = formulaBox.value
  const current = draft.value.scoring.expression ?? ''
  const at = box?.selectionStart ?? current.length
  draft.value.scoring.expression = current.slice(0, at) + text + current.slice(box?.selectionEnd ?? at)
  await nextTick()
  box?.focus()
  box?.setSelectionRange(at + text.length, at + text.length)
}
const sourcesError = computed(() => studio.errors.value.find(e => e.field === 'sources')?.message)
const formulaError = computed(() => studio.errors.value.find(e => e.field === 'scoring.expression'))

// Tags as comma-separated text
const tagList = (field: 'excludeTags' | 'onlyTags') => computed({
  get: () => draft.value?.filters[field].join(', ') ?? '',
  set: (text: string) => { if (draft.value) draft.value.filters[field] = text.split(',').map(t => t.trim()).filter(Boolean) },
})
const excludeTags = tagList('excludeTags')
const onlyTags = tagList('onlyTags')

// Optional numbers: a checkbox turns them on with a sensible value
function toggle(field: 'halfLifeHours' | 'maxAgeHours', on: boolean) {
  if (draft.value) draft.value.freshness[field] = on ? (field === 'halfLifeHours' ? 12 : 72) : null
}

// Empty until you pick someone else: the preview is of you
const viewerName = ref('')
function pickViewer(user: { externalUserId: string; username: string }) {
  studio.viewer.value = user.externalUserId
  viewerName.value = user.username
}

const compare = computed(() => {
  const now = studio.summary.value
  const before = studio.baselineSummary.value
  if (!now) return []
  return [
    { label: 'Candidates', now: now.candidates, before: before?.candidates },
    { label: 'From people you follow', now: now.following, before: before?.following, unit: '%' },
    { label: 'Different authors', now: now.authors, before: before?.authors },
    { label: 'Exploring', now: now.explored, before: before?.explored },
    { label: 'Median age', now: now.medianAgeHours, before: before?.medianAgeHours, unit: 'h' },
    { label: 'With photos', now: now.withPhotos, before: before?.withPhotos, unit: '%' },
  ]
})
const adjustments = (factors: { label: string }[]) =>
  factors.filter(f => ['Boosted', 'Lowered', 'Freshness', 'Seen already'].includes(f.label)).map(f => f.label)
</script>

<template>
  <div class="studio">
    <div class="page-head">
      <div>
        <h1>Feed studio</h1>
        <p class="muted">Change how For You is put together. The preview updates as you go; nothing reaches anyone's feed until you save.</p>
      </div>
    </div>

    <p v-if="studio.error.value" class="error">{{ studio.error.value }}</p>

    <div class="picker card">
      <label class="field">
        <span class="muted">Profile</span>
        <select :value="selected?.key" @change="open(($event.target as HTMLSelectElement).value)">
          <optgroup label="Presets">
            <option v-for="p in presets" :key="p.key" :value="p.key">{{ p.name }}{{ p.isDefault ? ' (default)' : '' }}</option>
          </optgroup>
          <optgroup v-if="custom.length" label="Yours">
            <option v-for="p in custom" :key="p.key" :value="p.key">{{ p.name }} · v{{ p.version }}{{ p.isDefault ? ' (default)' : '' }}</option>
          </optgroup>
        </select>
      </label>
      <div v-if="selected" class="badges">
        <span class="chip">{{ selected.preset ? 'Preset' : `Version ${selected.version}` }}</span>
        <span v-if="selected.isDefault" class="chip you">Server default</span>
        <span v-if="studio.dirty.value" class="chip changed">Unsaved changes</span>
      </div>
      <div class="grow" />
      <div class="actions">
        <button :disabled="!studio.dirty.value" @click="studio.reset">Undo changes</button>
        <button v-if="selected && !selected.preset" class="primary" :disabled="!studio.dirty.value || studio.saving.value" @click="studio.save">
          {{ studio.saving.value ? 'Saving…' : 'Save' }}
        </button>
        <button :class="{ primary: selected?.preset }" @click="startSaveAs">Save as…</button>
        <button v-if="selected && !selected.preset" class="danger" @click="confirmDelete">Delete</button>
      </div>
      <form v-if="savingAs" class="save-as" @submit.prevent="confirmSaveAs">
        <label class="field"><span class="muted">Name</span><input v-model="newName" maxlength="60" required /></label>
        <label class="field"><span class="muted">Key, for the URL and API</span>
          <input v-model="newKey" pattern="[a-z][a-z0-9-]{1,39}" required @input="keyEdited = true" />
        </label>
        <button class="primary" :disabled="studio.saving.value">Save</button>
        <button type="button" @click="savingAs = false">Cancel</button>
      </form>
      <p v-if="studio.notice.value" class="notice">{{ studio.notice.value }}</p>
    </div>

    <div v-if="draft" class="columns">
      <div class="stack">
        <section class="card">
          <label class="field"><span class="muted">Name</span><input v-model="draft.name" maxlength="60" /></label>
          <label class="field"><span class="muted">Description</span><input v-model="draft.description" maxlength="300" /></label>
        </section>

        <section class="card">
          <h2>1 · What it optimises for</h2>
          <div class="modes" role="radiogroup" aria-label="Scoring">
            <button v-for="m in modes" :key="m" role="radio" :aria-checked="draft.scoring.mode === m" :class="{ on: draft.scoring.mode === m }"
              @click="draft.scoring.mode = m">{{ ModeLabels[m].label }}</button>
          </div>
          <p class="muted">{{ ModeLabels[draft.scoring.mode].hint }}</p>

          <template v-if="draft.scoring.mode === 'model' || (draft.scoring.mode === 'expression' && draft.scoring.expression?.includes('model'))">
            <p class="muted">What each thing a reader might do is worth. Negative means avoid it.</p>
            <SettingSlider v-for="g in goals" :key="g" v-model="draft.scoring.goals[g]" :label="GoalLabels[g]" :min="-5" :max="10" :step="0.1"
              :error="studio.fieldError(`scoring.goals.${g === 'longRead' ? 'long_dwell' : g}`)" />
          </template>

          <template v-if="draft.scoring.mode === 'expression'">
            <textarea ref="formulaBox" v-model="draft.scoring.expression" class="formula" rows="3" spellcheck="false"
              placeholder="e.g. model * decay_exp(age_hours, 12)" />
            <p v-if="formulaError" class="error">
              {{ formulaError.message }}
              <code v-if="formulaError.position != null">{{ (draft.scoring.expression ?? '').slice(Math.max(0, formulaError.position - 12), formulaError.position) }}<mark>{{ (draft.scoring.expression ?? '').slice(formulaError.position, formulaError.position + 8) || '␣' }}</mark></code>
            </p>
            <div class="examples">
              <span class="muted">Try:</span>
              <button v-for="x in studio.vocabulary.value?.examples ?? []" :key="x.text" class="link" @click="draft.scoring.expression = x.text">{{ x.label }}</button>
            </div>
            <details v-if="studio.vocabulary.value" class="reference">
              <summary>Signals and functions you can use</summary>
              <div class="ref-grid">
                <button v-for="v in studio.vocabulary.value.variables" :key="v.name" class="token" :title="v.description" @click="insert(v.name)">
                  <code>{{ v.name }}</code><span class="muted">{{ v.description }}</span>
                </button>
                <button v-for="f in studio.vocabulary.value.functions" :key="f.name" class="token" :title="f.description" @click="insert(`${f.name}(`)">
                  <code>{{ f.signature }}</code><span class="muted">{{ f.description }}</span>
                </button>
              </div>
              <p class="muted">Operators: {{ studio.vocabulary.value.operators.join(' · ') }}</p>
            </details>
          </template>
        </section>

        <section class="card">
          <h2>2 · Where posts come from</h2>
          <div v-for="s in sources" :key="s" class="source">
            <label class="toggle"><input v-model="draft.sources[s].enabled" type="checkbox" /> {{ SourceLabels[s].label }}</label>
            <SettingSlider v-model="draft.sources[s].limit" label="Up to" :hint="SourceLabels[s].hint" :min="0" :max="s === 'following' ? 1000 : 500" :step="10"
              unit="posts" :disabled="!draft.sources[s].enabled" :error="studio.fieldError(`sources.${s}`)" />
          </div>
          <SettingSlider v-model="draft.sources.topTopics" label="Topics to draw on" hint="How many of your top topics" :min="1" :max="10" />
          <SettingSlider v-model="draft.sources.lookbackHours" label="Look back" hint="For your topics and people like you" :min="1" :max="168" unit="hours" />
          <p v-if="sourcesError" class="error">{{ sourcesError }}</p>
        </section>

        <section class="card">
          <h2>3 · Freshness</h2>
          <label class="toggle"><input type="checkbox" :checked="draft.freshness.halfLifeHours != null" @change="toggle('halfLifeHours', ($event.target as HTMLInputElement).checked)" />
            Fade older posts</label>
          <SettingSlider v-if="draft.freshness.halfLifeHours != null" v-model="draft.freshness.halfLifeHours" label="Halve the score every"
            :min="0.25" :max="168" :step="0.25" unit="hours" :error="studio.fieldError('freshness.halfLifeHours')" />
          <label class="toggle"><input type="checkbox" :checked="draft.freshness.maxAgeHours != null" @change="toggle('maxAgeHours', ($event.target as HTMLInputElement).checked)" />
            Leave out old posts</label>
          <SettingSlider v-if="draft.freshness.maxAgeHours != null" v-model="draft.freshness.maxAgeHours" label="Older than"
            :min="1" :max="720" unit="hours" :error="studio.fieldError('freshness.maxAgeHours')" />
        </section>

        <section class="card">
          <h2>4 · Variety</h2>
          <SettingSlider v-model="draft.variety.authorCap" label="Posts per author" hint="At most, in any ten" :min="1" :max="10" />
          <SettingSlider v-model="draft.variety.topicRun" label="Same topic in a row" hint="At most" :min="1" :max="10" />
          <SettingSlider v-model="draft.variety.outOfNetwork" label="From outside your follows" hint="At least, in each ten, when there are any" :min="0" :max="10" />
          <SettingSlider v-model="draft.variety.seenPenalty" label="Already seen" hint="Keeps this share of its score (1 = no penalty)" :min="0" :max="1" :step="0.05" />
        </section>

        <section class="card">
          <h2>5 · Exploration</h2>
          <SettingSlider v-model="draft.exploration.slots" label="Exploring slots" hint="In each ten, a post the ranker wouldn't have picked" :min="0" :max="3" />
          <SettingSlider v-model="draft.exploration.fromRank" label="Picked from below rank" :min="10" :max="500" :step="5" :disabled="draft.exploration.slots === 0" />
          <p v-if="draft.exploration.slots === 0 && draft.scoring.mode === 'model'" class="warn">
            With no exploring, the model only learns from posts it already chose, so its training data narrows over time.
          </p>
        </section>

        <section class="card">
          <h2>6 · Boosts</h2>
          <p class="muted">Multiply the score of matching posts: 2 doubles it, 0.5 halves it, 0 sinks it to the bottom.</p>
          <div v-for="(b, i) in draft.boosts" :key="i" class="boost">
            <select v-model="b.match">
              <option v-for="k in boostKinds" :key="k" :value="k">{{ BoostLabels[k] }}</option>
            </select>
            <input v-if="b.match === 'tag' || b.match === 'author'" v-model="b.value" :placeholder="b.match === 'tag' ? 'tag, e.g. running' : 'username'" />
            <span v-else />
            <SettingSlider v-model="b.multiplier" label="×" :min="0" :max="5" :step="0.1" :error="studio.fieldError(`boosts[${i}]`)" />
            <button class="plain remove" :aria-label="`Remove boost ${i + 1}`" @click="draft.boosts.splice(i, 1)">✕</button>
          </div>
          <button :disabled="draft.boosts.length >= 20" @click="draft.boosts.push({ match: 'tag', value: '', multiplier: 1.5 })">Add a boost</button>
        </section>

        <section class="card">
          <h2>7 · Filters</h2>
          <label class="field"><span class="muted">Never show posts tagged (comma separated)</span><input v-model.lazy="excludeTags" placeholder="e.g. politics, spoilers" /></label>
          <label class="field"><span class="muted">Only show posts tagged (empty for any)</span><input v-model.lazy="onlyTags" placeholder="e.g. running, cycling" /></label>
          <label class="toggle"><input v-model="draft.filters.mediaOnly" type="checkbox" /> Only posts with photos</label>
          <SettingSlider v-model="draft.filters.minLikes" label="At least this many likes" :min="0" :max="50" />
        </section>

        <details v-if="studio.versions.value.length > 1" class="card">
          <summary>History ({{ studio.versions.value.length }} versions)</summary>
          <ul class="versions">
            <li v-for="v in studio.versions.value" :key="v.version">
              <span>Version {{ v.version }}</span>
              <span class="muted">{{ new Date(v.savedAt).toLocaleString() }}{{ v.savedBy ? ` by ${v.savedBy}` : '' }}</span>
              <button v-if="v.version !== selected?.version" class="link" @click="studio.restore(v.version)">Load</button>
              <span v-else class="muted">live</span>
            </li>
          </ul>
        </details>
      </div>

      <aside class="preview card">
        <div class="preview-head">
          <h2>Preview</h2>
          <span class="muted">{{ studio.previewing.value ? 'Updating…' : studio.preview.value ? describeRanker(studio.preview.value.ranker) : '' }}</span>
        </div>
        <div class="viewer">
          <span class="muted">As</span> <b>{{ viewerName || (studio.viewer.value === me?.externalUserId ? me?.username ?? 'you' : studio.viewer.value) }}</b>
          <UserPicker placeholder="Preview as…" @pick="pickViewer" />
        </div>

        <table v-if="compare.length" class="compare">
          <thead><tr><th /><th>Now</th><th>Saved</th></tr></thead>
          <tbody>
            <tr v-for="row in compare" :key="row.label">
              <td>{{ row.label }}</td>
              <td :class="{ diff: row.before !== undefined && row.now !== row.before }">{{ row.now }}{{ row.unit ?? '' }}</td>
              <td class="muted">{{ row.before ?? '–' }}{{ row.before !== undefined ? row.unit ?? '' : '' }}</td>
            </tr>
          </tbody>
        </table>

        <ol v-if="studio.preview.value" class="posts" :class="{ stale: studio.previewing.value }">
          <li v-for="(p, i) in studio.preview.value.posts" :key="p.id">
            <span class="rank">{{ i + 1 }}</span>
            <span class="move" :class="{ up: (studio.movement(p.id, i) ?? 0) > 0, down: (studio.movement(p.id, i) ?? 0) < 0, new: studio.movement(p.id, i) === null }">
              {{ studio.movement(p.id, i) === null ? 'new' : studio.movement(p.id, i)! > 0 ? `▲${studio.movement(p.id, i)}` : studio.movement(p.id, i)! < 0 ? `▼${-studio.movement(p.id, i)!}` : '–' }}
            </span>
            <div class="body">
              <div class="meta">
                <RouterLink :to="{ name: 'user', params: { externalUserId: p.authorExternalUserId } }">@{{ p.authorUsername }}</RouterLink>
                <span v-if="p.ranking?.explored" class="chip you">exploring</span>
                <span v-for="a in adjustments(p.ranking?.factors ?? [])" :key="a" class="chip">{{ a.toLowerCase() }}</span>
                <span class="muted score">{{ p.ranking?.score.toFixed(3) }}</span>
              </div>
              <RouterLink :to="{ name: 'post', params: { postId: p.id } }" class="text">{{ p.content }}</RouterLink>
            </div>
          </li>
        </ol>
        <p v-if="studio.preview.value && !studio.preview.value.posts.length" class="muted">Nothing gets through these settings for this person.</p>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.studio { display: flex; flex-direction: column; gap: 16px; }
.page-head p { margin: 4px 0 0; }
.picker { display: flex; flex-wrap: wrap; gap: 10px 14px; align-items: end; }
.field { display: flex; flex-direction: column; gap: 4px; font-size: 13px; margin-bottom: 8px; }
.picker .field { margin: 0; }
select, textarea { font: inherit; padding: 6px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--card); color: var(--ink); }
.badges { display: flex; gap: 4px; align-items: center; padding-bottom: 6px; }
.badges .chip { margin: 0; }
.chip.changed { background: var(--like-soft); color: var(--like); }
.grow { flex: 1; }
.actions { display: flex; gap: 6px; flex-wrap: wrap; }
button.danger { color: var(--like); }
button.link { border: 0; background: none; padding: 0 4px; color: var(--accent); }
.save-as { flex-basis: 100%; display: flex; gap: 10px; align-items: end; flex-wrap: wrap; }
.save-as .field { margin: 0; }
.notice { flex-basis: 100%; margin: 0; color: var(--accent); font-size: 13px; }
.columns { display: grid; grid-template-columns: minmax(0, 1fr) minmax(320px, 420px); gap: 16px; align-items: start; }
@media (max-width: 900px) { .columns { grid-template-columns: 1fr; } }
section p { margin: 4px 0 8px; }
.modes { display: flex; flex-wrap: wrap; gap: 2px; padding: 3px; border-radius: 10px; background: var(--bg); border: 1px solid var(--line); width: fit-content; }
.modes button { border: 0; border-radius: 8px; padding: 5px 12px; color: var(--muted); background: none; }
.modes button.on { background: var(--accent); color: #fff; }
.formula { width: 100%; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px; resize: vertical; margin-top: 8px; }
.error code { margin-left: 6px; }
mark { background: var(--like-soft); color: var(--like); }
.examples { display: flex; flex-wrap: wrap; gap: 4px; align-items: center; font-size: 13px; margin: 6px 0; }
.reference summary { cursor: pointer; color: var(--accent); font-size: 13px; }
.ref-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 4px; margin: 8px 0; }
.token { display: flex; flex-direction: column; align-items: flex-start; gap: 2px; text-align: left; padding: 6px 8px; font-size: 12px; }
.source { border-top: 1px solid var(--line); padding-top: 6px; margin-top: 6px; }
.source:first-of-type { border-top: 0; margin-top: 0; }
.toggle { display: flex; gap: 8px; align-items: center; font-size: 14px; font-weight: 600; padding: 4px 0; }
.toggle input { accent-color: var(--accent); }
.warn { color: var(--like); font-size: 13px; }
.boost { display: grid; grid-template-columns: 170px minmax(0, 1fr) minmax(0, 1.6fr) 24px; gap: 8px; align-items: center; margin-bottom: 6px; }
@media (max-width: 620px) { .boost { grid-template-columns: 1fr 1fr; } }
.remove { color: var(--muted); }
.versions { list-style: none; padding: 0; margin: 8px 0 0; display: grid; gap: 4px; font-size: 13px; }
.versions li { display: flex; gap: 10px; align-items: center; }
.preview { position: sticky; top: 72px; max-height: calc(100vh - 88px); overflow: auto; }
.preview-head { display: flex; justify-content: space-between; align-items: baseline; }
.viewer { display: flex; gap: 6px; align-items: center; font-size: 13px; margin-bottom: 10px; flex-wrap: wrap; }
.viewer :deep(input) { width: 140px; padding: 3px 8px; }
.compare { width: 100%; border-collapse: collapse; font-size: 13px; margin-bottom: 10px; }
.compare th { text-align: right; font-weight: 500; color: var(--muted); font-size: 12px; }
.compare td { padding: 2px 0; border-top: 1px solid var(--line); }
.compare td + td { text-align: right; font-variant-numeric: tabular-nums; width: 64px; }
.compare td.diff { color: var(--accent); font-weight: 600; }
.posts { list-style: none; margin: 0; padding: 0; transition: opacity .2s; }
.posts.stale { opacity: .55; }
.posts li { display: grid; grid-template-columns: 22px 38px minmax(0, 1fr); gap: 6px; padding: 8px 0; border-top: 1px solid var(--line); font-size: 13px; }
.rank { color: var(--muted); font-variant-numeric: tabular-nums; text-align: right; }
.move { font-size: 11px; font-variant-numeric: tabular-nums; color: var(--muted); padding-top: 1px; }
.move.up { color: var(--accent); }
.move.down { color: var(--like); }
.move.new { color: var(--me); }
.meta { display: flex; gap: 4px; align-items: center; flex-wrap: wrap; }
.meta .chip { margin: 0; font-size: 11px; }
.score { margin-left: auto; font-variant-numeric: tabular-nums; }
.text { display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
@media (prefers-reduced-motion: reduce) { .posts { transition: none; } }
</style>
