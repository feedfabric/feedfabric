---
layout: ../../layouts/Doc.astro
title: A formula editor
description: Build your own ranking editor on the API, as the admin panel's Feed studio does. Load the vocabulary, write a formula with completions, show errors where they are, preview the feed it gives a real member as you type, and save it as a new version.
---

The admin panel's **Feed studio** (`web/admin/src/views/StudioView.vue`) is a complete editor for
[ranking profiles](/how-it-works/profiles/). This page builds the core of one, so you can put ranking controls in
your own staff tools, or give a community's moderators a say in their feed. Everything here is a public API call.

<div class="warn">

**This is a staff tool.** Everything under `/ranking` needs the `ranking-admin` role outside Development
(`Auth:Roles:RankingAdmin`), because saving a profile changes the feed for everyone who uses it. Members can choose
between profiles (`/userfeed/foryou?profile=`), but not edit them.

</div>

## The endpoints

| | Endpoint | What for |
|---|---|---|
| <span class="m">GET</span> | `/ranking/vocabulary` | Everything a profile can use, in words: variables, functions, operators, examples, goals, presets, defaults |
| <span class="m">GET</span> | `/ranking/profiles` | Presets, then saved profiles at their latest version |
| <span class="m">GET</span> | `/ranking/profiles/{key}` | One profile's settings |
| <span class="m post">POST</span> | `/ranking/preview?viewer=<externalUserId>&limit=30` | The feed some settings would give a member, without saving or logging anything (limit up to 50) |
| <span class="m put">PUT</span> | `/ranking/profiles/{key}` | Create a profile, or save a new version |
| <span class="m">GET</span> | `/ranking/profiles/{key}/versions`, `/versions/{n}` | History |
| <span class="m delete">DELETE</span> | `/ranking/profiles/{key}` | Remove a saved profile |

## Types and calls

Add these to the [API client](/build-a-ui/api-client/). They match the admin panel's `web/admin/src/types.ts`.

```ts
// src/types.ts (additions)
export type ScoringMode = 'model' | 'formula' | 'expression' | 'newest'
export interface Goals { open: number; like: number; comment: number; longRead: number; skip: number }
export interface SourceSetting { enabled: boolean; limit: number }
export interface BoostRule {
  match: 'tag' | 'author' | 'hasMedia' | 'following' | 'notFollowing' | 'favourite'
  value?: string | null
  multiplier: number
}

export interface RankingProfile {
  name: string
  description: string
  sources: { following: SourceSetting; topics: SourceSetting; similarReaders: SourceSetting; similarMeaning: SourceSetting
             topTopics: number; lookbackHours: number }
  scoring: { mode: ScoringMode; expression: string | null; goals: Goals }
  freshness: { halfLifeHours: number | null; maxAgeHours: number | null }
  variety: { authorCap: number; topicRun: number; outOfNetwork: number; seenPenalty: number }
  exploration: { slots: number; fromRank: number }
  boosts: BoostRule[]
  filters: { excludeTags: string[]; onlyTags: string[]; mediaOnly: boolean; minLikes: number }
}

export interface ProfileDetail { key: string; version: number; preset: boolean; label: string; isDefault: boolean; settings: RankingProfile }

// One entry in a profile.invalid error's "errors"; position is where in a formula the problem is
export interface ProfileError { field: string; message: string; position?: number | null }

export interface RankingVocabulary {
  variables: { name: string; description: string }[]
  functions: { name: string; signature: string; description: string }[]
  operators: string[]
  examples: { label: string; text: string }[]
  goals: { name: keyof Goals; description: string }[]
  presets: { key: string; settings: RankingProfile }[]
  defaults: RankingProfile
  defaultProfile: string
}
```

```ts
// src/api/ranking.ts
import { request } from './request'
import type { ForYouFeed, ProfileDetail, RankingProfile, RankingVocabulary } from '../types'

const id = encodeURIComponent

export const ranking = {
  vocabulary: () => request<RankingVocabulary>('/ranking/vocabulary'),
  profile: (key: string) => request<ProfileDetail>(`/ranking/profiles/${id(key)}`),
  save: (key: string, settings: RankingProfile) => request<ProfileDetail>(`/ranking/profiles/${id(key)}`, { method: 'PUT', body: settings }),
  preview: (settings: RankingProfile, viewer: string, limit = 30) =>
    request<ForYouFeed>(`/ranking/preview?viewer=${id(viewer)}&limit=${limit}`, { method: 'POST', body: settings }),
}
```

Add `/ranking` to your dev server's proxy, next to `/userfeed`.

## Start from something that works

A profile only needs the parts it changes, but an editor is easier to build on a complete one. The vocabulary
carries every preset's full settings and the defaults, so start a new profile from one of them:

```ts
const vocabulary = await ranking.vocabulary()
const base = vocabulary.presets.find(p => p.key === 'balanced')!.settings
const draft: RankingProfile = structuredClone({
  ...base,
  name: 'My feed',
  scoring: { ...base.scoring, mode: 'expression', expression: vocabulary.examples[0].text },
})
```

## Previewing as you type

The server is the only judge of whether a formula is valid, and `POST /ranking/preview` both checks it and ranks
with it, so one call per pause in typing gives you errors **and** results. Debounce it, and cancel a preview that a
newer one has overtaken.

```ts
// src/core/livePreview.ts
import { ApiError } from '../api/errors'
import { ranking } from '../api/ranking'
import type { ForYouFeed, ProfileError, RankingProfile } from '../types'

export type PreviewResult =
  | { kind: 'feed'; feed: ForYouFeed }
  | { kind: 'invalid'; errors: ProfileError[] }
  | { kind: 'failed'; error: unknown }

export function livePreview(onResult: (result: PreviewResult) => void, waitMs = 400) {
  let timer: ReturnType<typeof setTimeout> | undefined
  let latest = 0

  return (settings: RankingProfile, viewer: string) => {
    clearTimeout(timer)
    timer = setTimeout(async () => {
      const mine = ++latest
      try {
        const feed = await ranking.preview(settings, viewer)
        if (mine === latest) onResult({ kind: 'feed', feed })
      } catch (e) {
        if (mine !== latest) return
        if (e instanceof ApiError && e.code === 'profile.invalid') {
          onResult({ kind: 'invalid', errors: e.errors as unknown as ProfileError[] })
        } else {
          onResult({ kind: 'failed', error: e })
        }
      }
    }, waitMs)
  }
}
```

A preview ranks a few hundred posts for real, so keep the debounce at a few hundred milliseconds and don't preview
on every keystroke.

## Showing an error where it is

A formula error is the entry with `field` `scoring.expression`. Its `position` is a 0-based character offset into
the formula as sent; put the cursor there and underline from there to the end of the word.

```ts
// src/core/livePreview.ts (continued)
export function formulaError(errors: ProfileError[]) {
  return errors.find(e => e.field === 'scoring.expression') ?? null
}

// Select the problem in a <textarea> so the member sees it and can type over it
export function showAt(textarea: HTMLTextAreaElement, position: number) {
  const text = textarea.value
  let end = position
  while (end < text.length && /[\w.]/.test(text[end])) end++
  textarea.focus()
  textarea.setSelectionRange(position, Math.max(end, position + 1))
}
```

Other entries in `errors` belong to the rest of the profile (`variety.authorCap`, `boosts[2].value`), with the
setting's camelCase path as `field`, so show them next to that control. The messages are English and may change:
key your own UI off `field`, and show `message` as it is or replace it with your own.

## Completions

The vocabulary has everything a completion list needs: names, a signature for each function, and a sentence for each.

```ts
// src/core/completions.ts
import type { RankingVocabulary } from '../types'

export interface Completion { label: string; insert: string; detail: string }

export function completions(vocabulary: RankingVocabulary): Completion[] {
  return [
    ...vocabulary.variables.map(v => ({ label: v.name, insert: v.name, detail: v.description })),
    ...vocabulary.functions.map(f => ({ label: f.signature, insert: `${f.name}(`, detail: f.description })),
  ]
}

// The word being typed just before the cursor, to filter the list with
export function wordBefore(text: string, cursor: number) {
  const match = /[A-Za-z_][\w]*$/.exec(text.slice(0, cursor))
  return match ? { word: match[0], start: cursor - match[0].length } : null
}
```

Show the list when `wordBefore` returns at least two characters, filter by prefix, and on Enter or Tab replace from
`start` to the cursor with `insert`. Libraries such as CodeMirror's autocomplete take exactly this list if you'd
rather not build the popup yourself. For syntax highlighting, the [grammar](/formulas/language/#grammar) is small
enough to tokenise with one regular expression for numbers, names and operators.

## The editor

Put together in Vue (the React version follows the same shape with `useState` and `useEffect`):

```vue
<!-- src/components/FormulaEditor.vue -->
<script setup lang="ts">
import { computed, onMounted, ref, shallowRef, watch } from 'vue'
import { ranking } from '../api/ranking'
import { describe } from '../api/errors'
import { formulaError, livePreview, showAt, type PreviewResult } from '../core/livePreview'
import type { RankingProfile, RankingVocabulary } from '../types'

const props = defineProps<{ profileKey: string; viewer: string }>()

const vocabulary = shallowRef<RankingVocabulary>()
const draft = ref<RankingProfile>()
const result = shallowRef<PreviewResult>()
const textarea = ref<HTMLTextAreaElement>()
const saving = ref(false)
const saved = ref<string | null>(null)

const preview = livePreview(r => (result.value = r))
const error = computed(() => (result.value?.kind === 'invalid' ? formulaError(result.value.errors) : null))

onMounted(async () => {
  vocabulary.value = await ranking.vocabulary()
  // An existing profile, or a new one from the balanced preset
  const existing = await ranking.profile(props.profileKey).catch(() => null)
  const base = existing?.settings ?? vocabulary.value.presets.find(p => p.key === 'balanced')!.settings
  draft.value = structuredClone({ ...base, scoring: { ...base.scoring, mode: 'expression', expression: base.scoring.expression ?? 'formula' } })
})

watch([draft, () => props.viewer], () => draft.value && preview(draft.value, props.viewer), { deep: true })

async function save() {
  saving.value = true
  try {
    const detail = await ranking.save(props.profileKey, draft.value!)
    saved.value = `Saved as ${detail.key}@${detail.version}. Feeds use it from their next first page.`
  } catch (e) {
    saved.value = describe(e)   // profile.read_only for a preset key, profile.invalid_key, profile.invalid
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div v-if="draft && vocabulary" class="editor">
    <label for="formula">Formula</label>
    <textarea id="formula" ref="textarea" v-model="draft.scoring.expression" rows="3" spellcheck="false"
      :aria-invalid="!!error" aria-describedby="formula-help formula-error" />
    <p id="formula-help">Variables, functions and operators are listed below. Only the order of scores matters.</p>
    <p v-if="error" id="formula-error" role="alert">
      {{ error.message }}
      <button v-if="error.position != null" type="button" @click="showAt(textarea!, error.position)">Show me</button>
    </p>

    <p>
      Examples:
      <button v-for="x in vocabulary.examples" :key="x.label" type="button" @click="draft.scoring.expression = x.text">{{ x.label }}</button>
    </p>

    <section v-if="result?.kind === 'feed'" aria-label="Preview">
      <p>{{ result.feed.candidates }} candidates, ranked by <code>{{ result.feed.ranker }}</code></p>
      <ol>
        <li v-for="post in result.feed.posts" :key="post.id">
          <strong>{{ post.ranking!.score.toFixed(2) }}</strong> @{{ post.authorUsername }}: {{ post.content.slice(0, 80) }}
          <small v-if="post.ranking!.explored">explored</small>
        </li>
      </ol>
    </section>
    <p v-else-if="result?.kind === 'failed'" role="alert">{{ describe(result.error) }}</p>

    <button type="button" :disabled="saving || !!error" @click="save">Save as a new version</button>
    <p v-if="saved" role="status">{{ saved }}</p>

    <details>
      <summary>Variables and functions</summary>
      <dl>
        <template v-for="v in vocabulary.variables" :key="v.name"><dt><code>{{ v.name }}</code></dt><dd>{{ v.description }}</dd></template>
        <template v-for="f in vocabulary.functions" :key="f.name"><dt><code>{{ f.signature }}</code></dt><dd>{{ f.description }}</dd></template>
      </dl>
    </details>
  </div>
</template>
```

## Comparing with what's live

A preview on its own says what the feed would be, not how it differs. The Feed studio previews both the draft and
the saved version for the same member and shows how far each post moved; you can do the same with two calls:

```ts
const [before, after] = await Promise.all([
  ranking.preview(savedSettings, viewer),
  ranking.preview(draftSettings, viewer),
])
const was = new Map(before.posts.map((p, i) => [p.id, i]))
const moves = after.posts.map((p, i) => ({ post: p, moved: was.has(p.id) ? was.get(p.id)! - i : null }))  // null: new to the top 30
```

Summaries that help people judge a change: the share of posts from accounts the member follows, how many different
authors, the median age (`ranking.features.age_minutes`), and the share with photos. Preview for a few different
members (new, heavy readers, people who follow many) before saving, because one member's feed can mislead.

## After saving

- The profile is saved as a **new version**; feeds pick it up from their next first page, and anyone mid-scroll
  finishes their session on the old one.
- Every page served is logged with `key@version`, and the dashboard compares versions on real engagement.
- Preset keys (`balanced`, `fresh`…) can't be saved over: `409 profile.read_only`. Save under a new key and point
  `Ranking:DefaultProfile` at it to make it everyone's default.
