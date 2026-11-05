<script setup lang="ts">
import { computed, ref } from 'vue'
import { api, describeError } from '../api'
import { changed, session } from '../session'
import { PhotoRejectedError, uploadPhoto } from '../uploads'

// A new post as the acting user, with up to four photos. Each photo uploads to quarantine as soon as it's
// chosen, then waits while the worker scans it for malware and rebuilds it; only ready photos can be posted.
const MaxPhotos = 4
const emit = defineEmits<{ posted: [] }>()

interface Photo { file: File; preview: string; state: 'uploading' | 'scanning' | 'ready' | 'rejected'; progress: number; mediaId?: string; reason?: string }

const content = ref('')
const photos = ref<Photo[]>([])
const posting = ref(false)
const error = ref<string | null>(null)
const input = ref<HTMLInputElement>()

const busy = computed(() => photos.value.some(p => p.state === 'uploading' || p.state === 'scanning'))
const canPost = computed(() => !!content.value.trim() && !busy.value && !posting.value)
const labels = { uploading: 'Uploading', scanning: 'Scanning for malware', ready: 'Ready', rejected: 'Rejected' }

function choose(event: Event) {
  const files = [...((event.target as HTMLInputElement).files ?? [])].slice(0, MaxPhotos - photos.value.length)
  ;(event.target as HTMLInputElement).value = ''
  for (const file of files) {
    const photo = ref<Photo>({ file, preview: URL.createObjectURL(file), state: 'uploading', progress: 0 }).value
    photos.value = [...photos.value, photo]
    const update = (changes: Partial<Photo>) => {
      photos.value = photos.value.map(p => (p.file === file ? { ...p, ...changes } : p))
    }
    uploadPhoto(file, { onProgress: progress => update({ progress }), onScanning: () => update({ state: 'scanning' }) })
      .then(media => update({ state: 'ready', mediaId: media.id }))
      .catch(e => update({ state: 'rejected', reason: e instanceof PhotoRejectedError ? e.message : 'the upload failed' }))
  }
}

function remove(photo: Photo) {
  URL.revokeObjectURL(photo.preview)
  photos.value = photos.value.filter(p => p !== photo)
}

async function post() {
  posting.value = true
  error.value = null
  try {
    const mediaIds = photos.value.filter(p => p.state === 'ready').map(p => p.mediaId!)
    await api.createPost(content.value.trim(), mediaIds)
    content.value = ''
    photos.value.forEach(p => URL.revokeObjectURL(p.preview))
    photos.value = []
    changed()
    emit('posted')
  } catch (e) {
    error.value = describeError(e)
  } finally {
    posting.value = false
  }
}
</script>

<template>
  <form class="card composer" @submit.prevent="post">
    <label for="new-post" class="muted">Post as {{ session.actingAsName }}</label>
    <textarea id="new-post" v-model="content" rows="2" maxlength="500" placeholder="e.g. 5k along the canal before work #running" />
    <ul v-if="photos.length" class="photos">
      <li v-for="photo in photos" :key="photo.preview">
        <img :src="photo.preview" :alt="photo.file.name">
        <span class="state" :class="photo.state">{{ labels[photo.state] }}{{ photo.state === 'uploading' ? ` ${photo.progress}%` : '' }}</span>
        <button type="button" class="plain remove" :aria-label="`Remove ${photo.file.name}`" @click="remove(photo)">×</button>
        <span v-if="photo.reason" class="error">Not added: {{ photo.reason }}.</span>
      </li>
    </ul>
    <div class="bar">
      <button type="button" class="plain add" :disabled="photos.length >= MaxPhotos" @click="input?.click()">🖼 Add photos</button>
      <input ref="input" type="file" accept="image/jpeg,image/png,image/webp" multiple hidden @change="choose">
      <span class="muted count">{{ content.length }} / 500</span>
      <button type="submit" class="primary" :disabled="!canPost">{{ posting ? 'Posting…' : 'Post' }}</button>
    </div>
    <p class="muted hint">Photos are scanned for malware and have their location data removed before anyone sees them.</p>
    <p v-if="error" class="error">{{ error }}</p>
  </form>
</template>

<style scoped>
.composer { display: flex; flex-direction: column; gap: 8px; }
textarea { font: inherit; padding: 8px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--bg); color: var(--ink); resize: vertical; }
.bar { display: flex; align-items: center; gap: 10px; }
.count { margin-left: auto; }
.add { color: var(--accent); font-size: 13px; }
.photos { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(110px, 1fr)); gap: 8px; }
.photos li { position: relative; display: flex; flex-direction: column; gap: 4px; font-size: 12px; }
.photos img { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: 8px; border: 1px solid var(--line); }
.state { color: var(--muted); }
.state.ready { color: var(--accent); }
.state.rejected { color: var(--like); }
.remove { position: absolute; top: 4px; right: 4px; width: 22px; height: 22px; border-radius: 50%; background: rgba(0,0,0,.6); color: #fff; line-height: 1; }
.hint, .error { margin: 0; font-size: 12px; }
</style>
