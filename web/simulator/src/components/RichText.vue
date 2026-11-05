<script setup lang="ts">
import { computed } from 'vue'

// Same patterns as TextTokens in FeedFabric.Application, so links match what the API stored
const Token = /(?<![\w#@])(#[A-Za-z][A-Za-z0-9_]{1,49}|@[A-Za-z0-9][A-Za-z0-9_-]{0,49})/g

const props = defineProps<{ text: string }>()

const parts = computed(() => {
  const out: { text: string; kind?: 'tag' | 'mention' }[] = []
  let last = 0
  for (const match of props.text.matchAll(Token)) {
    const start = match.index ?? 0
    if (start > last) out.push({ text: props.text.slice(last, start) })
    out.push({ text: match[0], kind: match[0][0] === '#' ? 'tag' : 'mention' })
    last = start + match[0].length
  }
  if (last < props.text.length) out.push({ text: props.text.slice(last) })
  return out
})
</script>

<template>
  <div class="rich">
    <template v-for="(part, i) in parts" :key="i">
      <RouterLink v-if="part.kind === 'tag'" class="token" :to="{ name: 'tag', params: { tag: part.text.slice(1).toLowerCase() } }">{{ part.text }}</RouterLink>
      <RouterLink v-else-if="part.kind === 'mention'" class="token" :to="{ name: 'by-username', params: { username: part.text.slice(1).toLowerCase() } }">{{ part.text }}</RouterLink>
      <template v-else>{{ part.text }}</template>
    </template>
  </div>
</template>

<style scoped>
.token { color: var(--me); }
.token:hover { text-decoration: underline; }
</style>
