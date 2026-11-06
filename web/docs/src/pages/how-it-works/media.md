---
layout: ../../layouts/Doc.astro
title: Photos
description: Photos go straight from the browser to blob storage through a signed URL, wait in quarantine, and are scanned, sniffed, rebuilt and scored by the worker before they can go on a post.
---

## The flow

<ol class="flow">
  <li><strong>Ask</strong><code>POST /media/uploads</code> with the type and size. The API checks both and returns a media ID and a signed upload URL.</li>
  <li><strong>Upload</strong>The browser <code>PUT</code>s the file to that URL with exactly the signed <code>Content-Type</code>. It lands in quarantine.</li>
  <li><strong>Complete</strong><code>POST /media/{id}/complete</code> marks it <code>processing</code> and writes a <code>MediaUploaded</code> event.</li>
  <li><strong>Check</strong>The worker scans, sniffs, rebuilds and scores it, then marks it <code>ready</code> or <code>rejected</code>.</li>
  <li><strong>Poll</strong><code>GET /media/{id}</code> until it's one or the other.</li>
  <li><strong>Post</strong><code>POST /userfeed/posts</code> with the ready IDs in <code>mediaIds</code>.</li>
</ol>

The API never handles the file's bytes on the way in (except the local development stand-in for S3, below). The
upload URL is short-lived (`Media:UploadUrlLifetime`, 15 minutes) and only accepts the content type it was signed
for.

## What the worker checks

`MediaProcessor`, in the `worker-media` consumer group, in this order:

1. **Size**: reads at most one byte past `Media:MaxUploadBytes` (20 MB), whatever the upload claimed.
2. **Malware**: streams it to ClamAV before any image decoder touches it. Every upload is scanned, even ones about to
   fail the type check, so attacks are always logged. `MalwareScanning:Enabled=false` skips this step.
3. **Real type**: sniffs the bytes; only JPEG, PNG and WebP pass, whatever the name or declared type said.
4. **Rebuild**: decodes (refusing images over `Media:MaxDimension` 12,000 px or `Media:MaxPixels` 50 million from the
   header alone), applies the camera's rotation, and encodes brand-new files: a large one (longest edge
   `Media:LargeEdge`, 2048) and a thumbnail (`Media:ThumbnailEdge`, 400). Nothing but pixels survives, so EXIF and GPS
   data, comments and anything appended are gone. PNG is kept only where transparency matters; the rest become JPEG.
5. **Nudity**: scores the rebuilt image; at or above `Moderation:NudityRejectAt` (0.8) it's rejected and logged.

Like the text checks, it **fails closed**: if ClamAV or the nudity model can't answer, the handler throws, Kafka
redelivers, and the photo stays in quarantine (`processing`) until it can. Rejected uploads are deleted; the reason
on the row is the record.

## Statuses and reasons

`GET /media/{id}` (the owner only; anyone else gets `404 media.not_found`):

```json
{ "id": "0193…", "status": "ready", "rejectionReason": null, "width": 2048, "height": 1365,
  "url": "/blobs/media/…?expires=…&sig=…", "thumbnailUrl": "/blobs/media/…?expires=…&sig=…" }
```

| `status` | Meaning |
|---|---|
| `awaitingUpload` | Upload URL issued; waiting for the file and `complete` |
| `processing` | In quarantine being checked |
| `ready` | Passed; `url` and `thumbnailUrl` are set; can go on one post |
| `rejected` | Failed a check; see `rejectionReason` |

| `rejectionReason` | Why |
|---|---|
| `malware_detected` | ClamAV found something |
| `type_not_allowed` | The bytes aren't a JPEG, PNG or WebP |
| `too_large` | Over the size limit |
| `image_dimensions_too_large` | Too many pixels or too long an edge |
| `image_unreadable` | Couldn't be decoded |
| `upload_missing` | `complete` was called but no file arrived |
| `nudity_detected` | The nudity score was at or over the line |

Staff can see the nudity score and the line it's judged against with `GET /admin/media/{id}`; members never see
scores.

## On posts

A post takes up to `Media:MaxPerPost` (4) photos, each the author's own, `ready`, and not already on another post.
Feeds return them on each post as `media`:

```json
"media": [ { "id": "0193…", "url": "…", "thumbnailUrl": "…", "width": 2048, "height": 1365 } ]
```

**The URLs are signed and short-lived** (`Media:ReadUrlLifetime`, one hour). Use them as they come, don't store them,
and fetch the page again for fresh ones. `width` and `height` are the large image's, so you can reserve the space
before it loads.

## Where blobs live

| `BlobStorage:Provider` | Upload and read URLs |
|---|---|
| `Local` (development) | Signed links to the API's own `/blobs/{key}?expires=&sig=` endpoints. `PUT` checks the signature, the content type and the size limit (`BlobStorage:Local:MaxUploadBytes`); `GET` serves the file with `X-Content-Type-Options: nosniff` and `Content-Security-Policy: sandbox`. In Development `PublicBaseUrl` is empty, so the URLs are relative (`/blobs/…`) and **your dev server must proxy `/blobs` to the API**. |
| `S3` | Presigned S3 URLs. Browsers talk to the bucket directly, so it needs a CORS rule for your app's origin (`BlobStorage:S3:CreateBucket` adds an any-origin one for local emulators). `PublicServiceUrl` sets the host clients see when it differs from the one the API uses. |

Use `S3` outside Development. Models live in the same store, under `models/`.
