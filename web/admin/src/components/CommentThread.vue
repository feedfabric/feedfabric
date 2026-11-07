<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { FabricBadge, FabricButton, FabricConfirmDialog, FabricNotice, FabricSpinner, FabricStack, FabricTextarea } from '@feedfabric/fabric-ui'
import { api, describeError } from '../api'
import { ago } from '../format'
import { me } from '../me'
import type { FeedComment } from '../types'
import ReportDialog from './ReportDialog.vue'

// A post's comments, or one comment's replies, as members see them, and commenting, replying, deleting and
// reporting as the signed-in user. Each comment's replies are this again, one level in.
const MaxLength = 2000
const PageSize = 20

const props = withDefaults(defineProps<{ postId: string; parentId?: string | null; now: number; depth?: number }>(), { parentId: null, depth: 0 })
// +1 when a comment is added here or below, -1 when one is deleted, so the post can keep its count right
const emit = defineEmits<{ count: [delta: number] }>()

const comments = ref<FeedComment[]>([])
const cursor = ref<string | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

// The composer: the top level's is always open, a reply's opens under the comment it answers
const draft = ref('')
const replyTo = ref<string | null>(null)
const replyDraft = ref('')
const sending = ref(false)
const sendError = ref<string | null>(null)
const openReplies = ref(new Set<string>())
const deleting = ref<FeedComment | null>(null)
const reporting = ref<string | null>(null)

async function load(more = false) {
  loading.value = true
  try {
    const page = props.parentId
      ? await api.replies(props.parentId, more ? cursor.value : null, PageSize)
      : await api.comments(props.postId, more ? cursor.value : null, PageSize)
    comments.value = more ? [...comments.value, ...page.comments] : page.comments
    cursor.value = page.nextCursor
    error.value = null
  } catch (e) {
    error.value = describeError(e)
  } finally {
    loading.value = false
  }
}
onMounted(() => load())

async function send(parentCommentId: string | null) {
  const content = (parentCommentId ? replyDraft.value : draft.value).trim()
  if (!content) return
  sending.value = true
  sendError.value = null
  try {
    const created = await api.comment(props.postId, content, parentCommentId ?? props.parentId)
    if (parentCommentId) {
      // Shown by opening that comment's replies, which load it
      replyDraft.value = ''
      replyTo.value = null
      comments.value = comments.value.map(c => (c.id === parentCommentId ? { ...c, replyCount: c.replyCount + 1 } : c))
      openReplies.value = new Set(openReplies.value).add(parentCommentId)
    } else {
      draft.value = ''
      comments.value = [...comments.value, created]
    }
    emit('count', 1)
  } catch (e) {
    sendError.value = describeError(e)
  } finally {
    sending.value = false
  }
}

async function remove() {
  const comment = deleting.value
  if (!comment) return
  try {
    await api.deleteComment(comment.id)
    comments.value = comments.value.map(c => (c.id === comment.id ? { ...c, isDeleted: true, content: null } : c))
    emit('count', -1)
  } catch (e) {
    error.value = describeError(e)
  } finally {
    deleting.value = null
  }
}

function toggleReplies(id: string) {
  const next = new Set(openReplies.value)
  if (!next.delete(id)) next.add(id)
  openReplies.value = next
}

function startReply(id: string) {
  replyTo.value = replyTo.value === id ? null : id
  replyDraft.value = ''
  sendError.value = null
}

const mine = (c: FeedComment) => c.authorExternalUserId === me.value?.externalUserId
</script>

<template>
  <div class="thread" :class="{ nested: depth > 0 }">
    <FabricNotice v-if="error" tone="bad">
      {{ error }}
      <template #actions><FabricButton size="sm" @click="load()">Try again</FabricButton></template>
    </FabricNotice>

    <ul v-if="comments.length" class="comments">
      <li v-for="c in comments" :key="c.id" class="comment">
        <div class="meta">
          <RouterLink :to="{ name: 'user', params: { externalUserId: c.authorExternalUserId } }" class="author">@{{ c.authorUsername }}</RouterLink>
          <span class="when">{{ ago(c.createdAt, now) }}</span>
          <FabricBadge v-if="c.isHeld" tone="warn" size="sm" dot>held for review</FabricBadge>
        </div>
        <p v-if="c.isDeleted" class="text deleted">Deleted</p>
        <p v-else class="text">{{ c.content }}</p>

        <FabricStack v-if="!c.isDeleted || c.replyCount" direction="row" wrap :gap="1" class="actions">
          <FabricButton v-if="!c.isDeleted && depth < 4" variant="ghost" size="sm" icon="message" @click="startReply(c.id)">Reply</FabricButton>
          <FabricButton v-if="c.replyCount" variant="ghost" size="sm" :aria-expanded="openReplies.has(c.id)" @click="toggleReplies(c.id)">
            {{ openReplies.has(c.id) ? 'Hide replies' : `${c.replyCount} ${c.replyCount === 1 ? 'reply' : 'replies'}` }}
          </FabricButton>
          <FabricButton v-if="!c.isDeleted && mine(c)" variant="ghost" size="sm" icon="trash" @click="deleting = c">Delete</FabricButton>
          <FabricButton v-else-if="!c.isDeleted" variant="ghost" size="sm" icon="flag" @click="reporting = c.id">Report</FabricButton>
        </FabricStack>

        <form v-if="replyTo === c.id" class="composer" @submit.prevent="send(c.id)">
          <FabricTextarea v-model="replyDraft" :label="`Reply to @${c.authorUsername}`" hide-label :rows="2" :maxlength="MaxLength"
            :placeholder="`Reply to @${c.authorUsername}`" />
          <FabricNotice v-if="sendError" tone="bad">{{ sendError }}</FabricNotice>
          <FabricStack direction="row" justify="end" :gap="2">
            <FabricButton variant="ghost" size="sm" @click="replyTo = null">Cancel</FabricButton>
            <FabricButton type="submit" size="sm" :loading="sending" :disabled="!replyDraft.trim()">Reply</FabricButton>
          </FabricStack>
        </form>

        <CommentThread v-if="openReplies.has(c.id)" :post-id="postId" :parent-id="c.id" :now="now" :depth="depth + 1" @count="emit('count', $event)" />
      </li>
    </ul>
    <p v-else-if="!loading && !error && depth === 0" class="none">No comments yet.</p>

    <FabricSpinner v-if="loading" label="Loading comments" />
    <FabricButton v-else-if="cursor" variant="ghost" size="sm" @click="load(true)">Show more {{ depth ? 'replies' : 'comments' }}</FabricButton>

    <form v-if="depth === 0" class="composer" @submit.prevent="send(null)">
      <FabricTextarea v-model="draft" label="Add a comment" hide-label :rows="2" :maxlength="MaxLength" placeholder="Add a comment as you" />
      <FabricNotice v-if="sendError && !replyTo" tone="bad">{{ sendError }}</FabricNotice>
      <FabricStack direction="row" justify="end">
        <FabricButton type="submit" size="sm" icon="message" :loading="sending && !replyTo" :disabled="!draft.trim()">Comment</FabricButton>
      </FabricStack>
    </form>

    <FabricConfirmDialog :open="!!deleting" title="Delete this comment?" message="Members will see it as deleted. Its replies stay." confirm-label="Delete"
      tone="danger" @update:open="v => !v && (deleting = null)" @confirm="remove" @cancel="deleting = null" />
    <ReportDialog v-if="reporting" :open="true" :target="{ commentId: reporting }" @update:open="v => !v && (reporting = null)" />
  </div>
</template>

<style scoped>
.thread { display: flex; flex-direction: column; gap: var(--fabric-space-2); }
.thread.nested { padding-left: var(--fabric-space-3); border-left: 2px solid var(--fabric-border); margin-top: var(--fabric-space-2); }
.comments { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--fabric-space-3); }
.meta { display: flex; align-items: center; gap: var(--fabric-space-2); flex-wrap: wrap; font-size: var(--fabric-text-sm); }
.author { font-weight: 600; }
.when { color: var(--fabric-muted); font-size: var(--fabric-text-xs); }
.text { margin: 2px 0 0; white-space: pre-wrap; overflow-wrap: anywhere; font-size: var(--fabric-text-sm); }
.text.deleted { color: var(--fabric-muted); font-style: italic; }
.actions { margin-top: 2px; }
.composer { display: flex; flex-direction: column; gap: var(--fabric-space-2); }
.none { margin: 0; color: var(--fabric-muted); font-size: var(--fabric-text-sm); }
</style>
