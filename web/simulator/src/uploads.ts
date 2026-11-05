import { api, uploadFile } from './api'
import type { MediaStatus } from './types'

// One photo through the upload flow: a quarantine URL, the PUT, "complete", then waiting while the worker scans it
// for malware and rebuilds it. Resolves with the ready media, or rejects with the reason in words.
export const RejectionReasons: Record<string, string> = {
  malware_detected: 'it failed the malware scan',
  type_not_allowed: "it isn't a JPEG, PNG or WebP image",
  too_large: "it's over 20 MB",
  image_dimensions_too_large: 'its dimensions are too large',
  image_unreadable: "it couldn't be read as an image",
  upload_missing: "the upload didn't arrive",
  nudity_detected: "it appears to contain nudity, which the community rules don't allow",
}

export class PhotoRejectedError extends Error {}

export async function uploadPhoto(
  file: File,
  { onProgress, onScanning, signal }: { onProgress?: (percent: number) => void; onScanning?: () => void; signal?: AbortSignal } = {},
): Promise<MediaStatus> {
  const slot = await api.createUpload(file.type, file.size)
  await uploadFile(slot.uploadUrl, file, slot.contentType, onProgress ?? (() => {}), signal)
  await api.completeUpload(slot.mediaId)
  onScanning?.()

  for (;;) {
    const media = await api.media(slot.mediaId)
    if (media.status === 'ready') return media
    if (media.status === 'rejected') {
      throw new PhotoRejectedError(RejectionReasons[media.rejectionReason ?? ''] ?? media.rejectionReason ?? 'it was rejected')
    }
    await new Promise(resolve => setTimeout(resolve, 800))
    if (signal?.aborted) throw new DOMException('Upload cancelled', 'AbortError')
  }
}
