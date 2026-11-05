import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// API_URL: where the dev server sends API calls; defaults to the API's http launch profile
const api = process.env.API_URL ?? 'http://localhost:5161'

export default defineConfig({
  plugins: [vue()],
  // FabricUI (web/fabric-ui) from source. One copy of Vue, this app's, however FabricUI's own folder is set up.
  resolve: {
    alias: {
      '@feedfabric/fabric-ui/styles.css': fileURLToPath(new URL('../fabric-ui/src/styles/fabric.css', import.meta.url)),
      '@feedfabric/fabric-ui': fileURLToPath(new URL('../fabric-ui/src/index.ts', import.meta.url)),
    },
    dedupe: ['vue'],
  },
  // Its own port, well clear of the simulator's 5173 (and the 5174, 5175… Vite falls back to when that's busy), and
  // strict, so a busy port is an error rather than the admin app quietly turning up somewhere else
  server: {
    port: 5180,
    strictPort: true,
    proxy: {
      '/admin': api,
      '/ranking': api,
      // Images on posts: in development, LocalBlobStorage's signed links on the API
      '/blobs': api,
      // Posting from the panel uses the member endpoints
      '/media': api,
      '/userfeed': api,
    },
  },
  // A static site of its own, deployed apart from the member-facing app and served behind the same API
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
})
