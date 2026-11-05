<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api'
import { useNow } from '../composables/useNow'
import { usePolling } from '../composables/usePolling'
import { changed } from '../session'
import type { Comment } from '../types'
import CommentItem from './CommentItem.vue'
import ComposeBox from './ComposeBox.vue'

const props = defineProps<{ postId: string; autofocus?: boolean }>()
const comments = ref<Comment[]>([])
const justAdded = ref(new Set<string>())
const now = useNow()

const { error, loading } = usePolling(async () => {
  comments.value = (await api.comments(props.postId)).comments
}, { intervalMs: 3000 })

// Shown straight away from the API's response; the next poll confirms it
function posted(comment: Comment) {
  comments.value = [...comments.value, comment]
  justAdded.value.add(comment.id)
  changed()
}
</script>

<template>
  <div class="thread">
    <ComposeBox :post-id="postId" :autofocus="autofocus" @posted="posted" />
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="muted">Loading comments…</p>
    <p v-else-if="!comments.length" class="muted">No comments yet.</p>
    <CommentItem v-for="comment in comments" :key="comment.id" :comment="comment" :now="now" :fresh="justAdded.has(comment.id)" />
  </div>
</template>

<style scoped>
.thread { margin: 10px 0 0 4px; padding-left: 12px; border-left: 2px solid var(--line); }
p { margin: 4px 0; }
</style>
