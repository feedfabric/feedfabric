<script setup lang="ts">
import { computed, toRef, watch } from 'vue'
import { api } from '../api'
import { useLiveFeed } from '../composables/useLiveFeed'
import { useOlderPages } from '../composables/useOlderPages'
import { usePolling } from '../composables/usePolling'
import type { CardPost } from '../types'
import { withStats } from '../toCardPost'
import NewPostsButton from '../components/NewPostsButton.vue'
import PostList from '../components/PostList.vue'

const props = defineProps<{ tag: string }>()
const feed = useLiveFeed<CardPost>()
const older = useOlderPages<CardPost>(async cursor => {
  const page = await api.tagPosts(props.tag, cursor)
  return { items: await withStats(page.posts, api.postStats), nextCursor: page.nextCursor }
})
watch(() => props.tag, () => {
  feed.reset()
  older.reset()
})

const { error } = usePolling(async () => {
  const tag = props.tag
  const page = await api.tagPosts(tag)
  const cards = await withStats(page.posts, api.postStats)
  if (tag !== props.tag) return
  feed.update(cards)
  older.follow(page.nextCursor)
}, { intervalMs: 5000, watch: [toRef(props, 'tag')] })

const all = computed(() => feed.shown.value && [...feed.shown.value, ...older.items.value.filter(o => !feed.shown.value!.some(p => p.id === o.id))])
</script>

<template>
  <div class="stack">
    <div class="page-head">
      <h1>#{{ tag }}</h1>
      <span class="muted">Tagged by their author, or matched from their wording</span>
    </div>
    <p v-if="error" class="error">{{ error }}</p>
    <NewPostsButton :count="feed.waiting.value.length" @show="feed.showNew" />
    <PostList :posts="all" show-author :empty="`No posts tagged #${tag} yet.`"
      :has-more="!!older.nextCursor.value" :loading-more="older.loading.value" @more="older.loadMore" />
  </div>
</template>
