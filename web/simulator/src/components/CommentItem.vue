<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api'
import { usePolling } from '../composables/usePolling'
import { ago } from '../format'
import { changed, session } from '../session'
import type { Comment } from '../types'
import ComposeBox from './ComposeBox.vue'
import RichText from './RichText.vue'

const props = defineProps<{ comment: Comment; now: number; fresh?: boolean }>()

const replying = ref(false)
const showReplies = ref(false)
const replies = ref<Comment[]>([])
const justAdded = ref(new Set<string>())

// Only polls while the replies are open
usePolling(async () => {
  replies.value = (await api.replies(props.comment.id)).comments
}, { intervalMs: 3000, enabled: showReplies })

// The parent's count can lag a poll behind a reply just posted here
const replyCount = computed(() => Math.max(props.comment.replyCount, replies.value.filter(r => !r.isDeleted).length))
const mine = computed(() => props.comment.authorExternalUserId === session.actingAs)

function posted(reply: Comment) {
  replies.value = [...replies.value, reply]
  justAdded.value.add(reply.id)
  replying.value = false
  showReplies.value = true
  changed()
}

async function remove() {
  await api.deleteComment(props.comment.id)
  changed()
}
</script>

<template>
  <div class="comment" :class="{ fresh }">
    <RouterLink class="who" :to="{ name: 'profile', params: { externalUserId: comment.authorExternalUserId } }">{{ comment.authorUsername }}</RouterLink>
    <span class="muted"> · {{ ago(comment.createdAt, now) }}</span>
    <div class="body">
      <span v-if="comment.isDeleted" class="deleted">Comment deleted</span>
      <RichText v-else :text="comment.content ?? ''" />
    </div>
    <div class="actions">
      <button v-if="!comment.isDeleted" class="plain link" @click="replying = !replying">Reply</button>
      <button v-if="replyCount" class="plain link" @click="showReplies = !showReplies">
        {{ showReplies ? 'Hide' : 'View' }} {{ replyCount }} {{ replyCount === 1 ? 'reply' : 'replies' }}
      </button>
      <button v-if="mine && !comment.isDeleted" class="plain link" @click="remove">Delete</button>
    </div>
    <ComposeBox v-if="replying" :post-id="comment.postId" :parent-id="comment.id" autofocus @posted="posted" />
    <div v-if="showReplies && replies.length" class="replies">
      <CommentItem v-for="reply in replies" :key="reply.id" :comment="reply" :now="now" :fresh="justAdded.has(reply.id)" />
    </div>
  </div>
</template>

<style scoped>
.comment { padding: 6px 0; font-size: 14px; }
.comment.fresh { animation: flash 1.6s ease-out; }
.who { font-weight: 600; }
.body { margin: 1px 0 2px; }
.deleted { color: var(--muted); font-style: italic; }
.actions { display: flex; gap: 10px; }
.link { color: var(--muted); font-size: 12.5px; }
.link:hover { color: var(--accent); }
.replies { margin-left: 4px; padding-left: 12px; border-left: 2px solid var(--line); }
</style>
