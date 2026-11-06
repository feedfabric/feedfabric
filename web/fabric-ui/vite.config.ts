/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// npm run dev serves the gallery (gallery/), every component in every state, at http://localhost:5190.
// Apps use FabricUI from source, so there's no library build: npm run build only checks the gallery builds.
export default defineConfig({
  plugins: [vue()],
  root: 'gallery',
  server: { port: 5190, strictPort: true },
  build: { outDir: '../dist', emptyOutDir: true },
  test: {
    root: '.',
    environment: 'jsdom',
    include: ['tests/**/*.spec.ts'],
  },
})
