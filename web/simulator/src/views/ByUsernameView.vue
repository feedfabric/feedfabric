<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api'

const props = defineProps<{ username: string }>()
const router = useRouter()
const missing = ref(false)

onMounted(async () => {
  try {
    const user = await api.userByUsername(props.username)
    await router.replace({ name: 'profile', params: { externalUserId: user.externalUserId } })
  } catch {
    missing.value = true
  }
})
</script>

<template>
  <div class="card">
    <p class="muted">{{ missing ? `No user called @${username}.` : 'Opening profile…' }}</p>
  </div>
</template>
