import { fileURLToPath } from 'node:url'
import { defineConfig } from 'astro/config'

// FabricUI's tokens are imported straight from web/fabric-ui, outside this project, so the dev server is allowed to
// read that folder as well as this one
const here = fileURLToPath(new URL('.', import.meta.url))
const fabricStyles = fileURLToPath(new URL('../fabric-ui/src/styles', import.meta.url))

export default defineConfig({
  // A static site: npm run build writes it to dist/, to serve from anywhere
  output: 'static',
  outDir: './dist',
  trailingSlash: 'ignore',
  // Its own port, clear of the API (5161), the old simulator UIs (5173/5174), the admin panel (5185) and the FabricUI
  // gallery (5190), and strict, so a busy port is an error rather than the site turning up somewhere else
  server: { port: 5186, host: 'localhost' },
  markdown: {
    shikiConfig: {
      // Light by default; docs.css switches to the dark colours when the page is dark
      themes: { light: 'github-light', dark: 'github-dark' },
      wrap: false,
    },
  },
  vite: {
    server: {
      strictPort: true,
      fs: { allow: [here, fabricStyles] },
    },
  },
  devToolbar: { enabled: false },
})
