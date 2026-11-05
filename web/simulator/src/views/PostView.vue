<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '../api'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import type { PostDetail } from '../types'
import PostCard from '../components/PostCard.vue'

const props = defineProps<{ postId: string }>()
const post = ref<PostDetail | null>(null)
const card = computed(() => post.value && { ...post.value, postId: post.value.id })
const missing = ref(false)
const now = useNow()
const router = useRouter()

// Opening a post's page is an open, however you got here (a click, a link, Back); the API counts each viewer once per half hour
watch(() => props.postId, postId => api.openPost(postId).catch(() => {}), { immediate: true })

// Back to wherever the post was opened from (the feed, at the same place), or the home feed for a shared link
function back() {
  if (window.history.state?.back) router.back()
  else router.push({ name: 'home' })
}

const { error } = usePolling(async () => {
  try {
    post.value = await api.post(props.postId)
    missing.value = false
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) {
      missing.value = true
      return
    }
    throw e
  }
}, { watch: [toRef(props, 'postId')] })
</script>

<template>
  <div class="stack">
    <div class="page-head">
      <button class="plain" @click="back">← Back</button>
    </div>
    <p v-if="error" class="error">{{ error }}</p>
    <div class="feed">
      <p v-if="missing" class="feed-note">This post doesn't exist or was deleted.</p>
      <PostCard v-else-if="card && card.id === postId" :key="card.id" :post="card" :now="now" show-author thread-open />
      <p v-else class="feed-note"><span class="skeleton" /></p>
    </div>
  </div>
</template>
