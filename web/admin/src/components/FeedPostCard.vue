<script setup lang="ts">
import { computed } from 'vue'
import { FabricAvatar, FabricBadge, FabricCard, FabricDisclosure, FabricIcon, FabricStack } from '@feedfabric/fabric-ui'
import { ago } from '../format'
import type { HomeFeedPost } from '../types'

// One post as the member sees it in their feed: a repost shows who shared it above the post it shares, and a For You
// post shows where it ranked and why
const props = defineProps<{ post: HomeFeedPost; now: number }>()

const shown = computed(() => props.post.repostOf ?? props.post)
const ranking = computed(() => props.post.ranking)
// Text with its #hashtags and @mentions picked out, as members see them
const parts = computed(() => shown.value.content.split(/([#@][\p{L}\p{N}_]+)/u).map(text => ({ text, tag: /^[#@]/.test(text) })))
const signed = (n: number) => `${n > 0 ? '+' : ''}${Number(n.toFixed(3))}`
</script>

<template>
  <FabricCard as="article" padding="md">
    <FabricStack :gap="3">
      <RouterLink v-if="post.repostOf" :to="{ name: 'user', params: { externalUserId: post.authorExternalUserId } }" class="reposted">
        <FabricIcon name="repeat" :size="14" /> @{{ post.authorUsername }} reposted · {{ ago(post.createdAt, now) }}
      </RouterLink>

      <FabricStack direction="row" align="center" :gap="3">
        <FabricAvatar :name="shown.authorUsername" decorative />
        <div class="who">
          <RouterLink :to="{ name: 'user', params: { externalUserId: shown.authorExternalUserId } }" class="author">@{{ shown.authorUsername }}</RouterLink>
          <span class="when">
            <RouterLink :to="{ name: 'post', params: { postId: shown.id } }">{{ ago(shown.createdAt, now) }}</RouterLink>
            <template v-if="shown.editedAt"> · edited</template>
          </span>
        </div>
        <FabricStack v-if="ranking || shown.isHeld" direction="row" align="center" wrap :gap="1" class="badges">
          <FabricBadge v-if="shown.isHeld" tone="warn" size="sm" dot>held for review</FabricBadge>
          <FabricBadge v-if="ranking?.explored" tone="info" size="sm">exploring</FabricBadge>
          <FabricBadge v-if="ranking" tone="accent" size="sm">#{{ ranking.position + 1 }} · {{ ranking.score.toFixed(3) }}</FabricBadge>
        </FabricStack>
      </FabricStack>

      <p class="content"><template v-for="(part, i) in parts" :key="i"><span v-if="part.tag" class="tag">{{ part.text }}</span><template v-else>{{ part.text }}</template></template></p>

      <div v-if="shown.media?.length" class="media" :class="`n${Math.min(shown.media.length, 4)}`">
        <a v-for="m in shown.media" :key="m.id" :href="m.url" target="_blank" rel="noopener">
          <img :src="m.thumbnailUrl" :width="m.width" :height="m.height" alt="" loading="lazy">
        </a>
      </div>

      <FabricStack v-if="shown.tags.length" direction="row" wrap :gap="1">
        <FabricBadge v-for="t in shown.tags" :key="t.tag" :tone="t.source === 'hashtag' ? 'accent' : 'neutral'" size="sm">
          {{ t.source === 'hashtag' ? '#' : '' }}{{ t.tag }}
        </FabricBadge>
      </FabricStack>

      <FabricDisclosure v-if="ranking?.factors.length" summary="Why this post" :hint="`${ranking.factors.length} factors`">
        <ul class="factors">
          <li v-for="f in ranking.factors" :key="f.label">
            <span class="factor">{{ f.label }}</span>
            <span class="detail">{{ f.detail }}</span>
            <span class="value" :class="{ up: f.value > 0, down: f.value < 0 }">{{ signed(f.value) }}</span>
          </li>
        </ul>
      </FabricDisclosure>
    </FabricStack>
  </FabricCard>
</template>

<style scoped>
.reposted { display: inline-flex; align-items: center; gap: 6px; font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
.who { display: flex; flex-direction: column; min-width: 0; flex: 1; line-height: 1.3; }
.author { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.when { font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
.badges { flex: none; }
.content { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
.tag { color: var(--fabric-accent); }
.media { display: grid; gap: 4px; border-radius: var(--fabric-radius-md); overflow: hidden; }
.media.n2, .media.n4 { grid-template-columns: 1fr 1fr; }
.media.n3 { grid-template-columns: 2fr 1fr; }
.media.n3 a:first-child { grid-row: span 2; }
.media img { display: block; width: 100%; height: 100%; max-height: 360px; object-fit: cover; background: var(--fabric-sunken); }
.media.n1 img { height: auto; }
.factors { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; font-size: var(--fabric-text-sm); }
.factors li { display: grid; grid-template-columns: minmax(0, 10em) minmax(0, 1fr) auto; gap: 8px; align-items: baseline; }
.factor { font-weight: 600; }
.detail { color: var(--fabric-muted); }
.value { font-variant-numeric: tabular-nums; color: var(--fabric-muted); }
.value.up { color: var(--fabric-good); }
.value.down { color: var(--fabric-bad); }
@media (max-width: 520px) { .factors li { grid-template-columns: minmax(0, 1fr) auto; } .detail { grid-column: 1 / -1; grid-row: 2; } }
</style>
