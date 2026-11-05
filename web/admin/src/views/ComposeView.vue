<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import {
  FabricBadge, FabricButton, FabricCard, FabricFileUpload, FabricMeter, FabricNotice, FabricPage, FabricProgress, FabricStack,
  FabricSwitch, FabricTextarea, useToast, type FileRejection,
} from '@feedfabric/fabric-ui'
import { ApiError, api, describeError, uploadFile } from '../api'
import { me } from '../me'
import type { AdminMedia, CreatedPost } from '../types'

// Post as the signed-in staff account, through the same endpoints and checks members go through: each photo is
// uploaded to quarantine, scanned for malware, sniffed for its real type, rebuilt without its metadata and scored for
// nudity by the worker; the text goes through the offensive-language check when posting. Staff also see the nudity
// score and the line it's rejected at, which members never do, so this is the place to try the photo checks out.
// The server's limits (GET /admin/system), with its defaults until they arrive
const MaxPhotos = ref(4)
const MaxBytes = ref(20 * 1024 * 1024)
const Accept = ref('image/jpeg,image/png,image/webp')
// How long to wait for the worker to decide, and how many failed polls in a row before giving up
const CheckTimeoutMs = 2 * 60 * 1000
const MaxPollFailures = 5

type PhotoState = 'uploading' | 'scanning' | 'ready' | 'rejected' | 'failed'

interface Photo {
  key: number
  file: File
  preview: string
  state: PhotoState
  progress: number
  mediaId?: string
  check?: AdminMedia
  error?: string
}

const Reasons: Record<string, string> = {
  malware_detected: 'It failed the malware scan.',
  type_not_allowed: "Its contents aren't a JPEG, PNG or WebP image, whatever its name says.",
  too_large: "It's over 20 MB.",
  image_dimensions_too_large: 'Its dimensions are too large.',
  image_unreadable: "It couldn't be read as an image.",
  upload_missing: "The upload didn't arrive.",
  nudity_detected: 'The nudity classifier scored it at or over the line.',
}
const turnedAwayBecause = (reason: FileRejection['reason']) => ({
  type: 'not a JPEG, PNG or WebP', size: `over ${Math.round(MaxBytes.value / 1024 / 1024)} MB`, count: `only ${MaxPhotos.value} photos per post`,
})[reason]
const StateLabels: Record<PhotoState, string> = { uploading: 'Uploading', scanning: 'Checking', ready: 'Passed', rejected: 'Rejected', failed: 'Upload failed' }
const StateTones = { uploading: 'info', scanning: 'warn', ready: 'good', rejected: 'bad', failed: 'bad' } as const

const content = ref('')
const photos = ref<Photo[]>([])
const reveal = ref(false)
const posting = ref(false)
const result = ref<{ post: CreatedPost } | { blocked: string } | null>(null)
const error = ref<string | null>(null)
const { toast } = useToast()
let nextKey = 1
let stopped = false

const busy = computed(() => photos.value.some(p => p.state === 'uploading' || p.state === 'scanning'))
const ready = computed(() => photos.value.filter(p => p.state === 'ready'))
const canPost = computed(() => !!content.value.trim() && !busy.value && !posting.value)

function update(key: number, changes: Partial<Photo>) {
  photos.value = photos.value.map(p => (p.key === key ? { ...p, ...changes } : p))
}

async function add(files: File[]) {
  for (const file of files.slice(0, MaxPhotos.value - photos.value.length)) {
    const photo: Photo = { key: nextKey++, file, preview: URL.createObjectURL(file), state: 'uploading', progress: 0 }
    photos.value = [...photos.value, photo]
    check(photo)
  }
}

function turnedAway(rejections: FileRejection[]) {
  toast({ title: 'Not added', message: rejections.map(r => `${r.file.name}: ${turnedAwayBecause(r.reason)}`).join('. '), tone: 'warn' })
}

const present = (key: number) => !stopped && photos.value.some(p => p.key === key)

// Upload, complete, then poll until the worker has decided, and fetch the staff view of its checks. Polling stops if
// the photo is removed or the page left; a few failed polls in a row are a blip, more are a failure; and after
// CheckTimeoutMs it gives up, since the worker is probably down.
async function check(photo: Photo) {
  let slot
  try {
    slot = await api.createUpload(photo.file.type, photo.file.size)
    update(photo.key, { mediaId: slot.mediaId })
    await uploadFile(slot.uploadUrl, photo.file, slot.contentType, progress => update(photo.key, { progress }))
    await api.completeUpload(slot.mediaId)
  } catch (e) {
    update(photo.key, { state: 'failed', error: describeError(e) })
    return
  }
  update(photo.key, { state: 'scanning' })
  const giveUpAt = Date.now() + CheckTimeoutMs
  let failures = 0
  for (let wait = 600; present(photo.key); wait = Math.min(wait * 1.3, 3000)) {
    if (Date.now() > giveUpAt) {
      update(photo.key, { state: 'failed', error: 'Still being checked after two minutes. Is the worker running? Remove it and try again.' })
      return
    }
    try {
      const media = await api.media(slot.mediaId)
      if (media.status === 'ready' || media.status === 'rejected') {
        update(photo.key, { state: media.status, check: await api.adminMedia(slot.mediaId) })
        return
      }
      failures = 0
    } catch (e) {
      if (++failures >= MaxPollFailures) {
        update(photo.key, { state: 'failed', error: describeError(e) })
        return
      }
    }
    await new Promise(resolve => setTimeout(resolve, wait))
  }
}

function remove(photo: Photo) {
  URL.revokeObjectURL(photo.preview)
  photos.value = photos.value.filter(p => p.key !== photo.key)
}

async function post() {
  posting.value = true
  error.value = null
  result.value = null
  try {
    const created = await api.createPost(content.value.trim(), ready.value.map(p => p.mediaId!))
    result.value = { post: created }
    content.value = ''
    photos.value.forEach(p => URL.revokeObjectURL(p.preview))
    photos.value = []
  } catch (e) {
    if (e instanceof ApiError && e.code === 'content.violation') result.value = { blocked: String(e.params.category ?? 'a community rule') }
    else error.value = describeError(e)
  } finally {
    posting.value = false
  }
}

// Small scores keep two decimals, so 0.05% doesn't read as 0.1%
onMounted(async () => {
  try {
    const { media } = await api.system()
    MaxPhotos.value = media.maxPerPost
    MaxBytes.value = media.maxUploadBytes
    Accept.value = media.allowedTypes.join(',')
  } catch {
    // The defaults match the server's own
  }
})

const percent = (score: number) => `${Number((score * 100).toFixed(score < 0.01 ? 2 : 1))}%`

onBeforeUnmount(() => {
  stopped = true
  photos.value.forEach(p => URL.revokeObjectURL(p.preview))
})
</script>

<template>
  <FabricPage title="New post" :description="`Posts as ${me?.username ?? 'you'}, through the same checks as everyone else. Photos show what the checks found.`">
    <FabricNotice v-if="result && 'post' in result" :tone="result.post.isHeld ? 'warn' : 'good'"
      :title="result.post.isHeld ? 'Held for review' : 'Published'" dismissible @dismiss="result = null">
      {{ result.post.isHeld
        ? 'The offensive-language check held it: only you can see it until a moderator decides.'
        : 'It passed every check and is in your followers’ feeds.' }}
      <template #actions>
        <FabricButton size="sm" :to="{ name: 'post', params: { postId: result.post.id } }" icon-after="chevron-right">Open the post</FabricButton>
      </template>
    </FabricNotice>
    <FabricNotice v-if="result && 'blocked' in result" tone="bad" title="Blocked" dismissible @dismiss="result = null">
      The text broke a rule ({{ result.blocked.replaceAll('_', ' ') }}), so nothing was saved. It's in the moderation log.
    </FabricNotice>
    <FabricNotice v-if="error" tone="bad" dismissible @dismiss="error = null">{{ error }}</FabricNotice>

    <FabricCard>
      <form class="compose" @submit.prevent="post">
        <FabricTextarea v-model="content" label="Post" :rows="3" :maxlength="500" placeholder="e.g. 5k along the canal before work #running"
          hint="Hashtags become topics; @mentions notify people." />
        <FabricFileUpload v-if="photos.length < MaxPhotos" label="Add photos" :accept="Accept" :max-bytes="MaxBytes"
          :max-files="MaxPhotos - photos.length" hint="JPEG, PNG or WebP" compact @files="add" @rejected="turnedAway" />
        <FabricStack direction="row" align="center" justify="between" wrap :gap="3">
          <FabricSwitch v-if="photos.length" v-model="reveal" label="Show photos" description="Previews stay blurred until you choose to see them." />
          <span v-else />
          <FabricButton type="submit" variant="primary" icon="check" :loading="posting" :disabled="!canPost">
            {{ busy ? 'Waiting for checks…' : ready.length ? `Post with ${ready.length} photo${ready.length > 1 ? 's' : ''}` : 'Post' }}
          </FabricButton>
        </FabricStack>
      </form>
    </FabricCard>

    <div v-if="photos.length" class="photos">
      <FabricCard v-for="p in photos" :key="p.key" padding="sm">
        <div class="photo">
          <div class="frame" :class="{ hidden: !reveal }">
            <img :src="p.preview" :alt="p.file.name">
          </div>
          <div class="details">
            <FabricStack direction="row" align="center" justify="between" :gap="2">
              <span class="name" :title="p.file.name">{{ p.file.name }}</span>
              <FabricButton size="sm" variant="ghost" icon="x" :label="`Remove ${p.file.name}`" @click="remove(p)" />
            </FabricStack>
            <FabricBadge :tone="StateTones[p.state]" dot>{{ StateLabels[p.state] }}</FabricBadge>
            <FabricProgress v-if="p.state === 'uploading'" :value="p.progress" :label="`Uploading ${p.file.name}`" show-value />
            <FabricProgress v-else-if="p.state === 'scanning'" :value="null" label="Malware scan, type check, rebuild and nudity check" show-value tone="warn" />
            <template v-else-if="p.check">
              <FabricMeter v-if="p.check.nudityScore != null" label="Nudity" :value="p.check.nudityScore" :threshold="p.check.nudityRejectAt"
                :threshold-label="`rejects at ${percent(p.check.nudityRejectAt)}`" :format="percent" />
              <p v-if="p.check.rejectionReason" class="reason">{{ Reasons[p.check.rejectionReason] ?? p.check.rejectionReason }}</p>
              <p v-else class="muted">Malware scan clean · {{ p.check.width }}×{{ p.check.height }} rebuilt without location data</p>
            </template>
            <p v-if="p.error" class="reason">{{ p.error }}</p>
          </div>
        </div>
      </FabricCard>
    </div>
  </FabricPage>
</template>

<style scoped>
.compose { display: flex; flex-direction: column; gap: 14px; }
.photos { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(min(100%, 340px), 1fr)); }
.photo { display: flex; gap: 12px; min-width: 0; }
.frame { flex: none; width: 96px; height: 96px; border-radius: var(--fabric-radius-md); overflow: hidden; background: var(--fabric-sunken); }
.frame img { width: 100%; height: 100%; object-fit: cover; transition: filter var(--fabric-slow); }
.frame.hidden img { filter: blur(18px) saturate(.6); }
.details { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 8px; align-items: flex-start; }
.details > :deep(.fabric-meter), .details > :deep(.fabric-progress) { align-self: stretch; }
.name { font-size: var(--fabric-text-sm); font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
.reason { margin: 0; color: var(--fabric-bad); font-size: var(--fabric-text-sm); }
.muted { margin: 0; color: var(--fabric-muted); font-size: var(--fabric-text-xs); }
</style>
