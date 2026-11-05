<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '../api'

// Resolves "whoever posted last" once and replaces this URL with theirs, so the page never moves on its own
const router = useRouter()
const message = ref('Finding someone who is posting…')

onMounted(async () => {
  try {
    const { externalUserId } = await api.activeUser()
    await router.replace({ name: 'profile', params: { externalUserId } })
  } catch (e) {
    message.value = e instanceof ApiError && e.status === 404
      ? 'Nobody has posted yet. Start the simulator, then try again.'
      : `Couldn't reach the API: ${e instanceof Error ? e.message : e}`
  }
})
</script>

<template>
  <div class="card"><p class="muted">{{ message }}</p></div>
</template>
