---
layout: ../../layouts/Doc.astro
title: Composing with photos
description: Upload each photo straight to storage, wait for the worker to pass it, then post; and handle every way that can go wrong. Modelled on the admin panel's ComposeView.vue.
---

## The sequence

For each photo, as soon as it's picked (not when Post is pressed), so the checks run while the member types:

1. `POST /media/uploads` `{ contentType, size }` → `201 { mediaId, uploadUrl, contentType, expiresAt }`.
   Refused at once with `415 media.type_not_allowed` (`params.allowed` lists the types) or `413 media.too_large`.
2. `PUT uploadUrl` with the file as the body and **exactly `contentType`** as `Content-Type`. No `Authorization`
   header: the signature in the URL is the permission. The URL expires after 15 minutes.
3. `POST /media/{mediaId}/complete` → `202` with the media, now `processing`. Calling it twice is
   `409 media.wrong_state`.
4. Poll `GET /media/{mediaId}` until `status` is `ready` or `rejected`.

Then `POST /userfeed/posts` `{ content, mediaIds }` with the ready IDs in display order. `content` is required; a post
can't be photos alone.

## Uploading with progress

`fetch` can't report upload progress yet, so use `XMLHttpRequest` for the `PUT`, as the admin panel does:

```ts
// src/api/upload.ts
import { ApiError } from './errors'

export function uploadFile(url: string, file: Blob, contentType: string, onProgress: (percent: number) => void,
  signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    xhr.setRequestHeader('Content-Type', contentType)          // must match the signed type exactly
    xhr.upload.onprogress = e => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100))
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new ApiError(xhr.status, `upload_${xhr.status}`)))
    xhr.onerror = () => reject(new TypeError('Upload failed: network error'))
    xhr.onabort = () => reject(new DOMException('Upload cancelled', 'AbortError'))
    signal?.addEventListener('abort', () => xhr.abort())
    xhr.send(file)
  })
}
```

In development the upload URL is relative (`/blobs/…`), which is why the dev server proxies `/blobs`. The local
endpoint answers `403` for a bad signature or a different `Content-Type`, and `413` past
`BlobStorage:Local:MaxUploadBytes`. With S3, the URL points at the bucket.

## Waiting for the checks

The worker usually decides in a second or two, but it can take longer (ClamAV busy, the worker restarting), and it
waits indefinitely if a check can't run. Poll with backoff, stop when the photo is removed or the page left, and give up
after a couple of minutes:

```ts
// src/composables/usePhotoUpload.ts
import { api } from '../api'
import { uploadFile } from '../api/upload'
import type { Media } from '../types'

const CheckTimeoutMs = 2 * 60 * 1000
const MaxPollFailures = 5

export async function uploadAndCheck(file: File, onProgress: (p: number) => void, signal: AbortSignal): Promise<Media> {
  const slot = await api.createUpload(file.type, file.size)
  await uploadFile(slot.uploadUrl, file, slot.contentType, onProgress, signal)
  await api.completeUpload(slot.mediaId)

  const giveUpAt = Date.now() + CheckTimeoutMs
  let failures = 0
  for (let wait = 600; !signal.aborted; wait = Math.min(wait * 1.3, 3000)) {
    if (Date.now() > giveUpAt) throw new Error('Still being checked after two minutes. Try again in a while.')
    try {
      const media = await api.media(slot.mediaId)
      if (media.status === 'ready' || media.status === 'rejected') return media
      failures = 0
    } catch (e) {
      if (++failures >= MaxPollFailures) throw e     // a blip is fine; five in a row isn't
    }
    await new Promise(resolve => setTimeout(resolve, wait))
  }
  throw new DOMException('Upload cancelled', 'AbortError')
}
```

## Telling the member why

| `rejectionReason` | Say |
|---|---|
| `type_not_allowed` | Its contents aren't a JPEG, PNG or WebP image, whatever its name says. |
| `too_large` | It's over the size limit. |
| `image_dimensions_too_large` | Its dimensions are too large. |
| `image_unreadable` | It couldn't be read as an image. |
| `upload_missing` | The upload didn't arrive. Try again. |
| `malware_detected` | It can't be posted. |
| `nudity_detected` | It can't be posted because of what it shows. |

Members can only ever see their own uploads (`media.not_found` for anyone else's) and never see scores; staff can
inspect one with `GET /admin/media/{id}`.

## The composer

```vue
<!-- src/components/Composer.vue -->
<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { FabricButton, FabricCard, FabricFileUpload, FabricNotice, FabricTextarea, useToast, type FileRejection } from '@feedfabric/fabric-ui'
import { api } from '../api'
import { ApiError, describe } from '../api/errors'
import { uploadAndCheck } from '../composables/usePhotoUpload'

// The server's defaults (Media:MaxPerPost, Media:MaxUploadBytes); it checks again either way
const MaxPhotos = 4
const MaxBytes = 20 * 1024 * 1024
const Accept = 'image/jpeg,image/png,image/webp'

interface Photo { key: number; file: File; preview: string; state: 'checking' | 'ready' | 'rejected' | 'failed'; progress: number;
  mediaId?: string; reason?: string; abort: AbortController }

const emit = defineEmits<{ posted: [held: boolean] }>()
const { toast } = useToast()
const content = ref('')
const photos = ref<Photo[]>([])
const posting = ref(false)
const blocked = ref<string | null>(null)
let nextKey = 1

const checking = computed(() => photos.value.some(p => p.state === 'checking'))
const canPost = computed(() => content.value.trim().length > 0 && !checking.value && !posting.value
  && photos.value.every(p => p.state === 'ready'))

function update(key: number, changes: Partial<Photo>) {
  photos.value = photos.value.map(p => (p.key === key ? { ...p, ...changes } : p))
}

function add(files: File[]) {
  for (const file of files.slice(0, MaxPhotos - photos.value.length)) {
    const photo: Photo = { key: nextKey++, file, preview: URL.createObjectURL(file), state: 'checking', progress: 0, abort: new AbortController() }
    photos.value = [...photos.value, photo]
    uploadAndCheck(file, progress => update(photo.key, { progress }), photo.abort.signal)
      .then(media => update(photo.key, { state: media.status === 'ready' ? 'ready' : 'rejected', mediaId: media.id, reason: media.rejectionReason ?? undefined }))
      .catch(e => e.name !== 'AbortError' && update(photo.key, { state: 'failed', reason: describe(e) }))
  }
}

function remove(photo: Photo) {
  photo.abort.abort()
  URL.revokeObjectURL(photo.preview)
  photos.value = photos.value.filter(p => p.key !== photo.key)
}

function turnedAway(rejections: FileRejection[]) {
  toast({ title: 'Not added', message: rejections.map(r => `${r.file.name}: ${r.reason}`).join('. '), tone: 'warn' })
}

async function post() {
  posting.value = true
  blocked.value = null
  try {
    const created = await api.createPost(content.value.trim(), photos.value.map(p => p.mediaId!))
    photos.value.forEach(p => URL.revokeObjectURL(p.preview))
    photos.value = []
    content.value = ''
    emit('posted', created.isHeld)
  } catch (e) {
    if (e instanceof ApiError && e.code === 'content.violation') blocked.value = String(e.params.category ?? '')
    else toast({ title: describe(e), tone: 'bad' })
  } finally {
    posting.value = false
  }
}

onBeforeUnmount(() => photos.value.forEach(p => { p.abort.abort(); URL.revokeObjectURL(p.preview) }))
</script>

<template>
  <FabricCard>
    <form @submit.prevent="post">
      <FabricNotice v-if="blocked !== null" tone="bad" title="Not posted">
        This breaks the community rules ({{ blocked.replaceAll('_', ' ') }}). Change it and try again.
      </FabricNotice>
      <FabricTextarea v-model="content" label="What's happening?" hide-label :rows="3" hint="#tags become topics; @mentions tell people." />
      <!-- previews, each with its state, a FabricProgress while uploading and a remove button -->
      <FabricFileUpload v-if="photos.length < MaxPhotos" label="Add photos" :accept="Accept" :max-bytes="MaxBytes"
        :max-files="MaxPhotos - photos.length" hint="JPEG, PNG or WebP" compact @files="add" @rejected="turnedAway" />
      <FabricButton type="submit" variant="primary" :loading="posting" :disabled="!canPost">
        {{ checking ? 'Checking photos…' : 'Post' }}
      </FabricButton>
    </form>
  </FabricCard>
</template>
```

After posting:

- **`isHeld: true`**: say it's waiting for review and only they can see it; don't celebrate it as published.
- Otherwise it reaches followers' timelines within a moment (the worker fans it out) and appears at the top of the
  member's own Following feed on its next refresh. Insert it at the top yourself for instant feedback.

## Edge cases worth handling

| Case | Response | Handle by |
|---|---|---|
| A photo was rejected but left in the list | `409 media.not_ready` on post | Disable Post until every photo is `ready`, or drop rejected ones |
| The same photo ID on two posts | `404 media.not_found` | Clear the composer after a successful post |
| More than four photos | `400 post.too_many_media` | Cap the picker at four |
| A wrong `Content-Type` on the `PUT` | `403` from storage | Send `slot.contentType`, not the file's |
| The upload URL expired (15 minutes) | `403` from storage | Upload as soon as the photo is picked; start again if it fails |
| Member suspended | `403 user.suspended` on `POST /media/uploads` | Hide the composer for suspended members |
