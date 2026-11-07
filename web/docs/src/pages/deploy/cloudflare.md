---
layout: ../../layouts/Doc.astro
title: Cloudflare
description: What of FeedFabric runs on Cloudflare and how. Your front ends as static assets on Workers, R2 for photos and models, Cloudflare Access for the admin panel, and Cloudflare in front of the API.
---

FeedFabric's API and worker are .NET services that talk to Postgres, Redis and Kafka, so they run on your own
server ([Deploying FeedFabric](/deploy/)). Everything around them can live on Cloudflare:

| Part | On Cloudflare |
|---|---|
| Your front end, the admin panel | Static assets on Workers, with a few lines of Worker code passing API calls to your server |
| Photos and models | R2, through FeedFabric's S3 storage |
| The admin panel's door | Cloudflare Access, so only staff reach it |
| The API | Behind Cloudflare's proxy, for TLS, caching rules and rate limiting |

The Worker parts use [Wrangler](https://developers.cloudflare.com/workers/wrangler/), Cloudflare's CLI
(`npm install -D wrangler` in your app, then `npx wrangler login` once).

## R2 for photos and models

R2 speaks the S3 API, so FeedFabric uses it through `BlobStorage:Provider` `S3`.

1. **Create a bucket** under *R2 → Create bucket*, say `feedfabric-prod`. Leave public access off: members' photos
   are only read through signed URLs.
2. **Create an API token** under *R2 → Manage API tokens*, with **Object Read & Write** on that bucket only. Note the
   access key ID and secret; the secret is shown once.
3. **Add a CORS policy** under the bucket's *Settings → CORS policy*, so browsers can upload with the signed URLs:

   ```json
   [
     {
       "AllowedOrigins": ["https://social.example.org"],
       "AllowedMethods": ["PUT", "GET"],
       "AllowedHeaders": ["Content-Type"],
       "MaxAgeSeconds": 3600
     }
   ]
   ```

4. **Point the API and worker at it**, both with the same values:

   ```bash
   BlobStorage__Provider=S3
   BlobStorage__S3__Bucket=feedfabric-prod
   BlobStorage__S3__Region=auto
   BlobStorage__S3__ServiceUrl=https://<account-id>.r2.cloudflarestorage.com
   BlobStorage__S3__ForcePathStyle=true
   BlobStorage__S3__AccessKey=<access key id>
   BlobStorage__S3__SecretKey=<secret access key>
   ```

5. **Publish the models to it** from a checkout:

   ```bash
   cd ml
   export AWS_ACCESS_KEY_ID=<access key id> AWS_SECRET_ACCESS_KEY=<secret> AWS_DEFAULT_REGION=auto
   R2=https://<account-id>.r2.cloudflarestorage.com
   uv run fetch_models.py --s3-bucket feedfabric-prod --s3-endpoint $R2
   uv run --group tune tune_toxicity.py --s3-bucket feedfabric-prod --s3-endpoint $R2
   # Later, once there's real reading history:
   DATABASE_URL=postgresql://… uv run train_ranker.py --source real --s3-bucket feedfabric-prod --s3-endpoint $R2
   ```

Then post a photo end to end (upload, `processing`, `ready`, shown in a feed) before telling anyone it works. CI
tests the S3 storage against LocalStack, not R2.

R2 has no egress fees, which suits photos: members download every photo they scroll past, through signed URLs
pointing straight at the bucket.

## The admin panel and your front end

Both are static sites that must reach the API **on their own origin**, because the API sends no CORS headers. On
Cloudflare, a Worker serves the files and passes the API's paths through to your server:

```js
// worker.js: static files from ./dist, API paths to the FeedFabric server
const ApiPaths = ['/userfeed', '/follows', '/favourites', '/media', '/me', '/ranking', '/admin/']

export default {
  async fetch(request, env) {
    const url = new URL(request.url)
    if (ApiPaths.some(p => url.pathname === p || url.pathname.startsWith(p.endsWith('/') ? p : `${p}/`))) {
      const target = new URL(url.pathname + url.search, env.API_ORIGIN)
      return fetch(new Request(target, request))
    }
    return env.ASSETS.fetch(request)
  },
}
```

```jsonc
// wrangler.jsonc, next to the app's package.json
{
  "name": "feedfabric-app",
  "main": "./worker.js",
  "compatibility_date": "2026-11-01",
  "assets": {
    "directory": "./dist",
    "binding": "ASSETS",
    // Unknown paths are the app's own routes: serve index.html and let its router decide
    "not_found_handling": "single-page-application",
    // Only these reach the Worker; everything else is served straight from the assets
    "run_worker_first": ["/userfeed", "/userfeed/*", "/follows/*", "/favourites", "/favourites/*", "/media/*", "/me", "/ranking/*", "/admin/*"]
  },
  "vars": { "API_ORIGIN": "https://api-origin.social.example.org" },
  "routes": [{ "pattern": "social.example.org", "custom_domain": true }]
}
```

- `API_ORIGIN` is where your server answers, ideally a host name only Cloudflare uses. Add it to the API's
  `AllowedHosts`.
- Build the app first (`npm run build`), then `npx wrangler deploy`.
- The admin panel is the same with its own `dist/` (`make admin-build`) and its own host name, such as
  `staff.social.example.org`. Its routes live at the root, so give it a whole host name rather than a path.

### Keeping the admin panel to staff

The admin panel is harmless without a staff token (the API refuses every staff call), but there's no reason to let
the world load it. Put it behind **Cloudflare Access**: *Zero Trust → Access → Applications → Add an application →
Self-hosted*, with the panel's host name and a policy allowing your staff's emails or your identity provider's group.
Staff then sign in to Cloudflare to reach the panel, and paste their FeedFabric token into it as usual.

## Cloudflare in front of the API

Whether or not the front ends are on Cloudflare, proxying the API's host through Cloudflare (the orange cloud on its
DNS record) gives you:

- **TLS** at the edge. Set *SSL/TLS* to **Full (strict)** and keep a real certificate on the server (Caddy's is fine).
- **Rate limiting**, which FeedFabric doesn't do itself yet. A rule under *Security → WAF → Rate limiting rules*
  such as "requests to `/userfeed` with method `POST`, `PUT` or `DELETE`, more than 60 per minute from one IP: block
  for 1 minute" protects writes; give `/userfeed/impressions` and `/userfeed/dwell` a much higher limit, since every
  reader sends them every few seconds.
- **No caching** of API responses. Cloudflare doesn't cache JSON by default; don't add a cache rule that does, since
  every feed is per member.
- **The client's address.** Behind Cloudflare, the server sees Cloudflare's address; the member's is in
  `CF-Connecting-IP`. FeedFabric doesn't log client addresses yet, so nothing needs changing, but your proxy's rate
  limits should use that header if you add any there.
