<script setup lang="ts">
import type { PostMedia } from '../types'

// One photo full width; several as square thumbnails, two to a row. Each opens the full image.
defineProps<{ media: PostMedia[] }>()
</script>

<template>
  <div class="images" :class="{ single: media.length === 1 }">
    <a v-for="(m, i) in media" :key="m.id" :href="m.url" target="_blank" rel="noopener">
      <img :src="media.length === 1 ? m.url : m.thumbnailUrl" :alt="media.length === 1 ? 'Photo shared with the post' : `Photo ${i + 1} of ${media.length}, opens full size`"
        :width="m.width" :height="m.height" loading="lazy">
    </a>
  </div>
</template>

<style scoped>
.images { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
.images.single { grid-template-columns: 1fr; }
img { display: block; width: 100%; height: auto; border-radius: 10px; border: 1px solid var(--line); }
.images:not(.single) img { aspect-ratio: 1; object-fit: cover; }
.single img { max-height: 520px; object-fit: contain; background: var(--bg); }
</style>
