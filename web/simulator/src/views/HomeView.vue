<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../api'
import { useForYou } from '../composables/useForYou'
import { useLiveFeed } from '../composables/useLiveFeed'
import { useOlderPages } from '../composables/useOlderPages'
import { usePolling } from '../composables/usePolling'
import { session } from '../session'
import type { CardPost, ProfileSummary } from '../types'
import { withStats } from '../toCardPost'
import NewPostsButton from '../components/NewPostsButton.vue'
import PostComposer from '../components/PostComposer.vue'
import PostList from '../components/PostList.vue'

// Two feeds, each with its own URL: Following (/home, newest first, live) and For You (/home?feed=foryou, ranked)
const route = useRoute()
const router = useRouter()
const feedName = computed(() => (route.query.feed === 'foryou' ? 'foryou' : 'following'))
const show = (feed: 'following' | 'foryou') => router.replace({ query: feed === 'foryou' ? { feed } : {} })

const feed = useLiveFeed<CardPost>()
const source = ref('')
const older = useOlderPages<CardPost>(async cursor => {
  const page = await api.homeFeed(cursor)
  return { items: await withStats(page.posts, api.postStats), nextCursor: page.nextCursor }
})

// Following: the newest page refreshes; new posts wait behind the button, older pages load as you scroll
const following = computed(() => feedName.value === 'following')
const { error, refresh } = usePolling(async () => {
  const page = await api.homeFeed()
  feed.update(await withStats(page.posts, api.postStats))
  source.value = page.source
  older.follow(page.nextCursor)
}, { intervalMs: 5000, enabled: following })

const all = computed(() => feed.shown.value && [...feed.shown.value, ...older.items.value.filter(o => !feed.shown.value!.some(p => p.id === o.id))])

// For You: ranked when first shown, then only when asked
const forYou = useForYou()
watch([feedName, () => session.actingAs, () => session.forYouProfile], ([name]) => name === 'foryou' && forYou.refresh(), { immediate: true })

// Which ranking profile For You uses here (Feed studio makes more)
const profiles = ref<ProfileSummary[]>([])
onMounted(async () => { profiles.value = await api.profiles().catch(() => []) })

// Your own post goes straight into Following, with anything else that was waiting
async function posted() {
  await refresh()
  feed.showNew()
}
</script>

<template>
  <div class="stack">
    <div class="page-head">
      <h1>{{ session.actingAsName }}'s home feed</h1>
      <div class="feeds" role="tablist" aria-label="Feed">
        <button role="tab" :aria-selected="following" :class="{ on: following }" @click="show('following')">Following</button>
        <button role="tab" :aria-selected="!following" :class="{ on: !following }" @click="show('foryou')">For you</button>
      </div>
    </div>
    <p v-if="error" class="error">{{ error }}</p>
    <PostComposer @posted="posted" />

    <template v-if="following">
      <p class="muted">Newest first, from the accounts {{ session.actingAsName }} follows · read from <code>{{ source || '…' }}</code></p>
      <NewPostsButton :count="feed.waiting.value.length" @show="feed.showNew" />
      <PostList :posts="all" show-author empty="Nothing here yet. Follow someone, then wait for them to post."
        :has-more="!!older.nextCursor.value" :loading-more="older.loading.value" @more="older.loadMore" />
    </template>

    <template v-else>
      <div class="ranked">
        <span class="muted">
          Ranked for {{ session.actingAsName }} by <strong>{{ forYou.rankerLabel.value || '…' }}</strong>
          <template v-if="forYou.candidates.value"> from {{ forYou.candidates.value }} candidate posts</template>
        </span>
        <span class="controls">
          <select v-model="session.forYouProfile" aria-label="Ranking profile">
            <option value="">Default profile</option>
            <option v-for="p in profiles" :key="p.key" :value="p.key">{{ p.name }}</option>
          </select>
          <RouterLink :to="{ name: 'studio', params: { key: forYou.profile.value.split('@')[0] || undefined } }" class="tune">Tune</RouterLink>
          <button :disabled="forYou.refreshing.value" @click="forYou.refresh">{{ forYou.refreshing.value ? 'Ranking…' : 'Refresh' }}</button>
        </span>
      </div>
      <p v-if="forYou.error.value" class="error">{{ forYou.error.value }}</p>
      <PostList :posts="forYou.posts.value" show-author empty="Nothing to rank yet. Follow someone or like a few posts."
        :has-more="!!forYou.nextCursor.value" :loading-more="forYou.loadingMore.value" @more="forYou.loadMore" />
    </template>
  </div>
</template>

<style scoped>
.feeds { display: flex; gap: 2px; padding: 3px; border-radius: 99px; background: var(--card); border: 1px solid var(--line); }
.feeds button { border: 0; border-radius: 99px; padding: 4px 14px; color: var(--muted); background: none; }
.feeds button.on { background: var(--accent); color: #fff; }
.controls { display: flex; gap: 6px; align-items: center; }
.controls select { font: inherit; padding: 5px 8px; border: 1px solid var(--line); border-radius: 8px; background: var(--card); color: var(--ink); }
.tune { color: var(--accent); font-size: 14px; padding: 0 4px; }
.ranked { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
p { margin: 0; }
</style>
