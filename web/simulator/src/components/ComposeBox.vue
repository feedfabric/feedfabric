<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, describeError } from '../api'
import type { Comment } from '../types'

const props = defineProps<{ postId: string; parentId?: string | null; autofocus?: boolean }>()
const emit = defineEmits<{ posted: [comment: Comment] }>()

const content = ref('')
const sending = ref(false)
const error = ref<string | null>(null)
const input = ref<HTMLInputElement>()

onMounted(() => props.autofocus && input.value?.focus())

async function send() {
  const text = content.value.trim()
  if (!text || sending.value) return
  sending.value = true
  error.value = null
  try {
    emit('posted', await api.addComment(props.postId, text, props.parentId ?? null))
    content.value = ''
  } catch (e) {
    error.value = describeError(e)
  } finally {
    sending.value = false
  }
}
</script>

<template>
  <form class="compose" @submit.prevent="send">
    <input ref="input" v-model="content" :placeholder="parentId ? 'Write a reply…' : 'Add a comment…'" autocomplete="off">
    <button class="primary" :disabled="sending || !content.trim()">{{ sending ? '…' : parentId ? 'Reply' : 'Comment' }}</button>
  </form>
  <p v-if="error" class="error">{{ error }}</p>
</template>

<style scoped>
.compose { display: flex; gap: 6px; margin: 6px 0; }
.compose input { flex: 1; font-size: 14px; padding: 6px 10px; background: var(--bg); }
.compose button { padding: 4px 12px; }
.error { margin: 0 0 6px; }
</style>
