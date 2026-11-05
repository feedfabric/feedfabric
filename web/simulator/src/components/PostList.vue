<script setup lang="ts">
import { ref, watch } from 'vue'
import { useNow } from '../composables/useNow'
import type { CardPost } from '../types'
import PostCard from './PostCard.vue'
import ScrollSentinel from './ScrollSentinel.vue'

// The posts as one list in one card, scrolling on forever where the feed has more (`hasMore` + `more`).
// null while the first page loads: placeholder rows, so nothing jumps.
const props = defineProps<{ posts: CardPost[] | null; showAuthor?: boolean; empty: string; hasMore?: boolean; loadingMore?: boolean }>()
defineEmits<{ more: [] }>()
const now = useNow()

// Posts that turn up on a refresh flash once, so new activity is easy to spot
const seen = new Set<string>()
const fresh = ref(new Set<string>())
let first = true
watch(() => props.posts, posts => {
  if (!posts) return
  fresh.value = first ? new Set() : new Set(posts.filter(p => !seen.has(p.id)).map(p => p.id))
  posts.forEach(p => seen.add(p.id))
  first = false
}, { immediate: true })
</script>

<template>
  <div class="feed" :aria-busy="posts === null || loadingMore">
    <template v-if="posts === null">
      <div v-for="n in 4" :key="n" class="feed-note"><span class="skeleton" /></div>
    </template>
    <p v-else-if="!posts.length" class="feed-note">{{ empty }}</p>
    <template v-else>
      <PostCard v-for="post in posts" :key="post.id" :post="post" :now="now" :show-author="showAuthor" :fresh="fresh.has(post.id)" />
      <p v-if="loadingMore" class="feed-note">Loading more…</p>
      <p v-else-if="!hasMore" class="feed-note">You're all caught up</p>
    </template>
  </div>
  <ScrollSentinel v-if="posts?.length" :active="!!hasMore" :loading="!!loadingMore" @more="$emit('more')" />
</template>
