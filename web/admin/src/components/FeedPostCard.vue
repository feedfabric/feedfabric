<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  FabricAvatar, FabricBadge, FabricButton, FabricCard, FabricConfirmDialog, FabricDialog, FabricDisclosure, FabricIcon, FabricMenu, FabricNotice,
  FabricStack, FabricTextarea, useToast, type MenuAction,
} from '@feedfabric/fabric-ui'
import { api, describeError } from '../api'
import { favourites, setFavourite } from '../favourites'
import { ago } from '../format'
import { me } from '../me'
import type { HomeFeedPost } from '../types'
import CommentThread from './CommentThread.vue'
import ReportDialog from './ReportDialog.vue'

// One post as the member sees it in their feed: a repost shows who shared it above the post it shares, and a For You
// post shows where it ranked and why. Below it, what a member can do with it, done as the signed-in user through the
// endpoints members' apps use: like, repost, comment, report, follow or favourite its author, and edit or delete
// their own. signals adds Show more and Not interested, which shape the signed-in user's For You, so only on their
// own feed.
const props = withDefaults(defineProps<{ post: HomeFeedPost; now: number; signals?: boolean }>(), { signals: false })
const emit = defineEmits<{
  // Changes to the post shown, by its ID: it can be in the feed twice, as itself and as what a repost shares
  patch: [postId: string, changes: Partial<HomeFeedPost>]
  // Take this card out, or every card showing this post
  remove: [scope: 'this' | 'everywhere']
}>()
const { toast } = useToast()

const shown = computed(() => props.post.repostOf ?? props.post)
const ranking = computed(() => props.post.ranking)
// Text with its #hashtags and @mentions picked out, as members see them
// split with one capture group alternates text and captures, so the odd parts are the tags
const parts = computed(() => shown.value.content.split(/([#@][\p{L}\p{N}_]+)/u).map((text, i) => ({ text, tag: i % 2 === 1 })))
const signed = (n: number) => `${n > 0 ? '+' : ''}${Number(n.toFixed(3))}`

const mine = computed(() => shown.value.authorExternalUserId === me.value?.externalUserId)
// This card is the signed-in user's own repost, which undoing the repost takes away
const myRepost = computed(() => !!props.post.repostOf && props.post.authorExternalUserId === me.value?.externalUserId)
const author = computed(() => `@${shown.value.authorUsername}`)

const liking = ref(false)
const reposting = ref(false)
const commentsOpen = ref(false)
const reporting = ref(false)
const deleting = ref(false)
const editing = ref(false)
const editDraft = ref('')
const editError = ref<string | null>(null)
const saving = ref(false)

const failed = (title: string, e: unknown) => toast({ title, message: describeError(e), tone: 'bad' })

// Shown at once, put back if the API says no
async function toggleLike() {
  const { id, liked, likes } = shown.value
  liking.value = true
  emit('patch', id, { liked: !liked, likes: likes + (liked ? -1 : 1) })
  try {
    await api.like(id, !liked)
  } catch (e) {
    emit('patch', id, { liked, likes })
    failed(liked ? "Couldn't unlike" : "Couldn't like", e)
  } finally {
    liking.value = false
  }
}

async function toggleRepost() {
  const { id, reposted, reposts } = shown.value
  reposting.value = true
  emit('patch', id, { reposted: !reposted, reposts: reposts + (reposted ? -1 : 1) })
  try {
    await api.repost(id, !reposted)
    if (reposted && myRepost.value) emit('remove', 'this')
    else if (!reposted) toast({ title: 'Reposted', message: `Your followers see ${author.value}'s post in their Following.`, tone: 'good' })
  } catch (e) {
    emit('patch', id, { reposted, reposts })
    failed(reposted ? "Couldn't undo the repost" : "Couldn't repost", e)
  } finally {
    reposting.value = false
  }
}

function toggleComments() {
  commentsOpen.value = !commentsOpen.value
  // Opening a post is a reading signal; only sent from your own feed, where it's about what you read
  if (commentsOpen.value && props.signals) api.opened(shown.value.id).catch(() => {})
}

async function run(title: string, action: () => Promise<unknown>, done?: string) {
  try {
    await action()
    if (done) toast({ title: done, tone: 'good' })
  } catch (e) {
    failed(title, e)
  }
}

const menu = computed<MenuAction[]>(() => {
  const { id, authorExternalUserId: authorId } = shown.value
  const items: MenuAction[] = []
  if (props.signals) {
    items.push(
      { label: 'Show more like this', icon: 'plus', onSelect: () => run("Couldn't send that", () => api.showMore(id), `More from ${author.value} and its topics`) },
      {
        label: 'Not interested', icon: 'x',
        onSelect: () => run("Couldn't send that", async () => {
          await api.notInterested(id)
          emit('remove', 'everywhere')
        }, 'Hidden from your For You for 30 days'),
      },
    )
  }
  if (mine.value) {
    items.push(
      { label: 'Edit post', icon: 'edit', onSelect: startEdit },
      { label: 'Delete post', icon: 'trash', tone: 'danger', onSelect: () => (deleting.value = true) },
    )
  } else {
    const favourite = favourites.value.has(authorId)
    items.push(
      { label: `Follow ${author.value}`, icon: 'user', onSelect: () => run("Couldn't follow", () => api.follow(authorId, true), `Following ${author.value}`) },
      { label: `Unfollow ${author.value}`, icon: 'users', onSelect: () => run("Couldn't unfollow", () => api.follow(authorId, false), `Unfollowed ${author.value}`) },
      {
        label: favourite ? 'Remove from favourites' : 'Add to favourites', icon: 'star',
        onSelect: () => run("Couldn't change favourites", () => setFavourite(authorId, !favourite),
          favourite ? `${author.value} isn't a favourite now` : `${author.value} is a favourite`),
      },
      { label: 'Report post', icon: 'flag', tone: 'danger', onSelect: () => (reporting.value = true) },
    )
  }
  return items
})

function startEdit() {
  editDraft.value = shown.value.content
  editError.value = null
  editing.value = true
}

async function saveEdit() {
  saving.value = true
  try {
    const saved = await api.editPost(shown.value.id, editDraft.value.trim())
    emit('patch', shown.value.id, { content: saved.content, editedAt: new Date().toISOString(), isHeld: saved.isHeld })
    editing.value = false
  } catch (e) {
    editError.value = describeError(e)
  } finally {
    saving.value = false
  }
}

async function remove() {
  try {
    await api.deletePost(shown.value.id)
    emit('remove', 'everywhere')
  } catch (e) {
    failed("Couldn't delete it", e)
  } finally {
    deleting.value = false
  }
}
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

      <FabricStack direction="row" align="center" :gap="1" class="actions">
        <FabricButton variant="ghost" size="sm" icon="heart" :class="{ on: shown.liked }" :aria-pressed="shown.liked" :disabled="liking"
          :title="shown.liked ? 'Unlike' : 'Like'" @click="toggleLike">
          {{ shown.likes }}<span class="sr-only"> {{ shown.likes === 1 ? 'like' : 'likes' }}</span>
        </FabricButton>
        <FabricButton variant="ghost" size="sm" icon="repeat" :class="{ on: shown.reposted }" :aria-pressed="shown.reposted" :disabled="reposting"
          :title="shown.reposted ? 'Undo repost' : 'Repost'" @click="toggleRepost">
          {{ shown.reposts }}<span class="sr-only"> {{ shown.reposts === 1 ? 'repost' : 'reposts' }}</span>
        </FabricButton>
        <FabricButton variant="ghost" size="sm" icon="message" :class="{ on: commentsOpen }" :aria-expanded="commentsOpen" title="Comments"
          @click="toggleComments">
          {{ shown.comments }}<span class="sr-only"> {{ shown.comments === 1 ? 'comment' : 'comments' }}</span>
        </FabricButton>
        <span class="spacer" />
        <FabricMenu :items="menu" :label="`More for ${author}'s post`" align="end" />
      </FabricStack>

      <CommentThread v-if="commentsOpen" :post-id="shown.id" :now="now" class="comments"
        @count="delta => emit('patch', shown.id, { comments: Math.max(0, shown.comments + delta) })" />

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

    <ReportDialog v-model:open="reporting" :target="{ postId: shown.id }" />
    <FabricConfirmDialog v-model:open="deleting" title="Delete this post?" message="It goes from every feed, reposts of it included." confirm-label="Delete"
      @confirm="remove" />
    <FabricDialog v-model:open="editing" title="Edit post" description="Posts can be edited for 60 minutes. The new text is checked like a new post." size="md">
      <form class="edit" @submit.prevent="saveEdit">
        <FabricTextarea v-model="editDraft" label="Post" hide-label :rows="4" :maxlength="5000" />
        <FabricNotice v-if="editError" tone="bad">{{ editError }}</FabricNotice>
        <FabricStack direction="row" justify="end" :gap="2">
          <FabricButton variant="ghost" @click="editing = false">Cancel</FabricButton>
          <FabricButton type="submit" :loading="saving" :disabled="!editDraft.trim() || editDraft.trim() === shown.content">Save</FabricButton>
        </FabricStack>
      </form>
    </FabricDialog>
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
.actions { margin: calc(var(--fabric-space-1) * -1) calc(var(--fabric-space-2) * -1) 0; }
.actions .on { color: var(--fabric-accent); }
.actions .on :deep(svg) { fill: currentColor; }
.spacer { flex: 1; }
.comments { border-top: 1px solid var(--fabric-border); padding-top: var(--fabric-space-3); }
.edit { display: flex; flex-direction: column; gap: var(--fabric-space-3); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
@media (max-width: 520px) { .factors li { grid-template-columns: minmax(0, 1fr) auto; } .detail { grid-column: 1 / -1; grid-row: 2; } }
</style>
