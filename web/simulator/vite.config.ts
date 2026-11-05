import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// API_URL: where the dev server sends API calls; defaults to the API's http launch profile
const api = process.env.API_URL ?? 'http://localhost:5161'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/dev': api,
      '/userfeed': api,
      '/follows': api,
      '/ranking': api,
    },
  },
  // The API serves the built app from wwwroot in Development
  build: {
    outDir: '../../src/FeedFabric.Api/wwwroot',
    emptyOutDir: true,
  },
})
