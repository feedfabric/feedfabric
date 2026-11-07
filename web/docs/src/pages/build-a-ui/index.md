---
layout: ../../layouts/Doc.astro
title: Building a UI
description: A member-facing app on FeedFabric, step by step, in Vue 3 with FabricUI. The same steps apply to any framework; the API is plain JSON over HTTP.
---

## What you'll build

A member app with the two feeds as tabs, infinite scroll, post cards with likes, reposts, comments and a *Why this
post?* panel, a composer that uploads photos, and the reading signals the ranker learns from. The pages in this
section build it in order:

1. **Set up the project** (this page): Vite, Vue, FabricUI from source, the dev proxy.
2. **[API client](/build-a-ui/api-client/)**: types, Problem Details errors and their `code`, signing in.
3. **[Feeds and paging](/build-a-ui/feeds/)**: Following and For You with cursors and infinite scroll.
4. **[Post cards and actions](/build-a-ui/post-cards/)**: reposts, held posts, tags, photos, actions, *Why this post?*
5. **[Composing with photos](/build-a-ui/compose/)**: upload, wait for the checks, post.
6. **[Reading signals](/build-a-ui/signals/)**: impressions, dwell, opens, *Not interested* and *Show more*.

The admin panel (`web/admin`) is a complete, working app built the same way; its `ComposeView.vue` is the reference
for the photo flow. Git history also has an earlier member-facing app (`git show 788f9bd^:web/simulator/src/…`),
whose reading-signal code these pages bring up to date with today's API.

## Know the API's shape first

The member API is deliberately small. Before designing screens, note what it **doesn't** have, so you design around
it rather than discovering it later:

| Not in the API | What to do |
|---|---|
| A single-post `GET` | A post page renders the post you navigated from (keep it in a store or pass it in router state), then loads its comments with `GET /userfeed/posts/{id}/comments`. |
| Like, comment or repost counts on feed posts, or "liked by me" | Track what the member did this session yourself; `PUT`/`DELETE` are idempotent, so a wrong guess never doubles anything. For You posts carry `likes` and `comments` in `ranking.features` as they were when ranked. |
| User profiles, search, follower lists, "do I follow them?" | Follows and favourites are fire-and-forget `PUT`/`DELETE` by `externalUserId`. `GET /favourites` lists favourites; nothing lists follows. Profile pages need your own user service. |
| Notifications | `GET /userfeed/mentions` lists @mentions; poll it. |
| CORS | The API sends no CORS headers. Serve your app from the same origin as the API (a reverse proxy in production, Vite's proxy in development), or add a CORS policy to the API. |

Members' public identity is `externalUserId` (the `sub` from your identity provider) and `username`. Internal IDs
(`authorId`) are UUIDs you can ignore except as keys.

## Create the project

Next to the admin panel, so FabricUI can be used from source:

```bash
cd web
npm create vite@latest app -- --template vue-ts
cd app
npm install vue-router@4.6.4
```

Pin the same versions the admin panel uses, so one copy of Vue serves both the app and FabricUI:

```json
{
  "name": "feedfabric-app",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "vue-tsc --noEmit -p tsconfig.app.json && vite build"
  },
  "dependencies": {
    "vue": "3.5.44",
    "vue-router": "4.6.4"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "6.0.10",
    "typescript": "6.0.3",
    "vite": "8.3.5",
    "vue-tsc": "3.3.14"
  }
}
```

## Vite: FabricUI from source and the API proxy

There's no FabricUI package to publish. Alias it to the source and dedupe `vue`, exactly as
`web/admin/vite.config.ts` does, and proxy the member routes to the API:

```ts
// web/app/vite.config.ts
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// API_URL: where API calls go in development. 5161 is `make run`; the API container from `make up` is on 8080.
const api = process.env.API_URL ?? 'http://localhost:5161'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@feedfabric/fabric-ui/styles.css': fileURLToPath(new URL('../fabric-ui/src/styles/fabric.css', import.meta.url)),
      '@feedfabric/fabric-ui': fileURLToPath(new URL('../fabric-ui/src/index.ts', import.meta.url)),
    },
    // One copy of Vue, this app's, however FabricUI's own folder is set up
    dedupe: ['vue'],
  },
  server: {
    // Clear of 5161 (API), 5173/5174, 5185 (admin), 5186 (docs) and 5190 (FabricUI gallery)
    port: 5187,
    strictPort: true,
    proxy: {
      '/userfeed': api,
      '/follows': api,
      '/favourites': api,
      '/media': api,
      // Photos: in development, signed links to the API's local blob storage, as relative URLs
      '/blobs': api,
    },
  },
})
```

For TypeScript to resolve the alias, add it to `tsconfig.app.json` too:

```json
{
  "compilerOptions": {
    "paths": {
      "@feedfabric/fabric-ui": ["../fabric-ui/src/index.ts"]
    }
  }
}
```

<div class="note">

**In production** put the built app (`npm run build`, a static `dist/`) and the API behind one origin: a reverse
proxy sends `/userfeed`, `/follows`, `/favourites` and `/media` to the API and everything else to the app. With S3
storage, photos come from the bucket's own URLs, and the bucket needs a CORS rule allowing your origin to `PUT`.

</div>

## Styles and the app shell

Import FabricUI's tokens once, before your own styles, and give `body` the `fabric` class for the base font and
colours. Dark mode follows the system unless `<html data-theme="light|dark">` says otherwise; `useTheme` and
`FabricThemeToggle` set it.

```ts
// web/app/src/main.ts
import { createApp } from 'vue'
import '@feedfabric/fabric-ui/styles.css'
import './style.css'
import App from './App.vue'
import { router } from './router'

document.body.classList.add('fabric')
createApp(App).use(router).mount('#app')
```

```vue
<!-- web/app/src/App.vue -->
<script setup lang="ts">
import { useRoute } from 'vue-router'
import { FabricAppShell, FabricSidebar, FabricThemeToggle, FabricToaster, type SidebarSection } from '@feedfabric/fabric-ui'

const route = useRoute()
const sections: SidebarSection[] = [
  {
    links: [
      { label: 'Home', to: '/', icon: 'sparkle', exact: true },
      { label: 'Favourites', to: '/favourites', icon: 'star' },
      { label: 'Mentions', to: '/mentions', icon: 'message' },
    ],
  },
]
</script>

<template>
  <FabricAppShell :route-key="route.fullPath">
    <template #brand><strong>Your app</strong></template>
    <template #sidebar="{ closeMenu }">
      <FabricSidebar :sections="sections" :active-path="route.path" label="Main" @navigate="closeMenu" />
    </template>
    <template #sidebar-footer><FabricThemeToggle /></template>
    <RouterView />
  </FabricAppShell>
  <FabricToaster />
</template>
```

`FabricAppShell` folds the sidebar into a menu at phone width and has a skip link to the main content. Run
`make fabric` to see every component in every state at `http://localhost:5190`.

## Run it

```bash
make run                    # the API on 5161 (or: make up, then API_URL=http://localhost:8080 npm run dev)
make run-worker             # timelines, photo checks
cd web/app && npm run dev   # http://localhost:5187
```

In Development no sign-in is needed: requests without a token act as user `1`, and `X-Dev-User` picks anyone else.
Start the simulator first and act as `sim-2` (a For You reader) to see a feed with history in it.
