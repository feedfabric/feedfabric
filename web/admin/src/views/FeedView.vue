<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  FabricAvatar, FabricBadge, FabricButton, FabricCard, FabricEmptyState, FabricNotice, FabricPage, FabricSelect, FabricSkeleton, FabricSpinner,
  FabricStack, FabricTabs, type SelectOption, type TabItem,
} from '@feedfabric/fabric-ui'
import { api, describeError } from '../api'
import FeedPostCard from '../components/FeedPostCard.vue'
import UserPicker from '../components/UserPicker.vue'
import { useNow } from '../composables/useNow'
import { useRouteQuery } from '../composables/useRouteQuery'
import { me } from '../me'
import { describeRanker } from '../ranking'
import type { HomeFeedPost, ProfileSummary, UserRef } from '../types'

// Someone's feeds as they'd see them right now, for staff: Following newest first, and For You ranked, each post
// with why it's there. Looking isn't logged as served and doesn't touch their own For You session, so it never
// becomes training data or changes what they see.
const PageSize = 20

const props = defineProps<{ externalUserId?: string }>()
const router = useRouter()
const now = useNow()

const tab = useRouteQuery('tab', 'following')
const profile = useRouteQuery('profile')
const viewer = computed(() => props.externalUserId || me.value?.externalUserId || '')
const viewerName = ref<string | null>(null)

const tabs: TabItem<string>[] = [
  { value: 'following', label: 'Following', icon: 'users' },
  { value: 'foryou', label: 'For You', icon: 'sparkle' },
]

const profiles = ref<ProfileSummary[]>([])
const profileOptions = computed<SelectOption<string>[]>(() => [
  { value: '', label: 'Default profile' },
  ...profiles.value.map(p => ({ value: p.key, label: p.preset ? p.name : `${p.name} (v${p.version})` })),
])

const posts = ref<HomeFeedPost[]>([])
const cursor = ref<string | null>(null)
const done = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
// About the feed as a whole: how For You ranked it, or where Following read it from
const ranker = ref<string | null>(null)
const candidates = ref<number | null>(null)
const source = ref<string | null>(null)
let generation = 0

async function loadMore() {
  if (loading.value || done.value || !viewer.value) return
  loading.value = true
  const mine = generation
  try {
    const page = tab.value === 'foryou'
      ? await api.forYou(viewer.value, cursor.value, profile.value || null, PageSize)
      : await api.following(viewer.value, cursor.value, PageSize)
    // A newer reset (another tab, viewer or profile) has started since this was asked for
    if (mine !== generation) return
    posts.value = [...posts.value, ...page.posts]
    cursor.value = page.nextCursor
    done.value = !page.nextCursor
    if ('ranker' in page) {
      ranker.value = page.ranker
      candidates.value = page.candidates
    } else {
      source.value = page.source
    }
    error.value = null
  } catch (e) {
    if (mine === generation) error.value = describeError(e)
  } finally {
    if (mine === generation) loading.value = false
  }
}

// Back to the first page, which for For You ranks again
function reset() {
  generation++
  posts.value = []
  cursor.value = null
  done.value = false
  loading.value = false
  error.value = null
  ranker.value = candidates.value = source.value = null
  loadMore()
}

// The profile only matters to For You
watch([viewer, () => tab.value, () => (tab.value === 'foryou' ? profile.value : '')], reset)

// Loads the next page as the end of the list comes into view
const sentinel = ref<HTMLElement | null>(null)
let observer: IntersectionObserver | undefined
onMounted(() => {
  observer = new IntersectionObserver(entries => entries.some(e => e.isIntersecting) && !error.value && loadMore(), { rootMargin: '600px' })
  watch(sentinel, (el, old) => {
    if (old) observer?.unobserve(old)
    if (el) observer?.observe(el)
  }, { immediate: true })
  if (viewer.value) reset()
})
onBeforeUnmount(() => observer?.disconnect())

// The viewer's name, from the user directory; staff without the moderator role can't read it, so the ID stands in
watch(viewer, async id => {
  viewerName.value = null
  if (!id) return
  if (id === me.value?.externalUserId && me.value.username) viewerName.value = me.value.username
  try {
    const { username } = (await api.user(id)).user
    // Another viewer may have been picked while this was loading
    if (id === viewer.value) viewerName.value = username
  } catch {
    // Shown by ID
  }
}, { immediate: true })

watch(() => me.value?.rankingAdmin, async rankingAdmin => {
  if (rankingAdmin && !profiles.value.length) profiles.value = await api.profiles().catch(() => [])
}, { immediate: true })

const pick = (user: UserRef) => router.push({ name: 'feed', params: { externalUserId: user.externalUserId }, query: { tab: tab.value === 'following' ? undefined : tab.value, profile: profile.value || undefined } })
const isMe = computed(() => viewer.value === me.value?.externalUserId)
</script>

<template>
  <FabricPage title="Feeds" description="What someone's feeds show them right now. Looking isn't logged and doesn't change what they see.">
    <template #actions>
      <UserPicker v-if="me?.moderator" placeholder="View someone's feed…" @pick="pick" />
    </template>

    <FabricCard padding="sm">
      <FabricStack direction="row" align="center" justify="between" wrap :gap="3">
        <FabricStack direction="row" align="center" :gap="3">
          <FabricAvatar :name="viewerName || viewer || '?'" :size="40" decorative />
          <div class="viewer">
            <strong>{{ viewerName ? `@${viewerName}` : viewer || '…' }}</strong>
            <span>
              {{ isMe ? 'You' : `ID ${viewer}` }}
              <template v-if="me?.moderator && viewer"> · <RouterLink :to="{ name: 'user', params: { externalUserId: viewer } }" class="link">Profile</RouterLink></template>
            </span>
          </div>
        </FabricStack>
        <FabricButton icon="repeat" :loading="loading && !posts.length" @click="reset">
          {{ tab === 'foryou' ? 'Rank again' : 'Refresh' }}
        </FabricButton>
      </FabricStack>
    </FabricCard>

    <FabricTabs v-model="tab" :tabs="tabs" label="Feed" />

    <FabricStack direction="row" align="center" justify="between" wrap :gap="3" class="about">
      <FabricStack direction="row" align="center" wrap :gap="2">
        <template v-if="tab === 'foryou'">
          <FabricBadge v-if="ranker" tone="accent">{{ describeRanker(ranker) }}</FabricBadge>
          <span v-if="candidates != null" class="muted">ranked from {{ candidates }} candidates</span>
        </template>
        <template v-else>
          <FabricBadge v-if="source" :tone="source === 'redis' ? 'good' : 'info'">from {{ source.replace('+', ' and ') }}</FabricBadge>
          <span class="muted">newest first, from accounts they follow and their own posts</span>
        </template>
      </FabricStack>
      <FabricSelect v-if="tab === 'foryou' && me?.rankingAdmin" v-model="profile" :options="profileOptions" label="Ranking profile" hide-label size="sm" />
    </FabricStack>

    <FabricNotice v-if="error" tone="bad" title="Couldn't load the feed">
      {{ error }}
      <template #actions><FabricButton size="sm" @click="posts.length ? loadMore() : reset()">Try again</FabricButton></template>
    </FabricNotice>

    <div class="posts">
      <FeedPostCard v-for="p in posts" :key="p.id" :post="p" :now="now" />
      <template v-if="loading && !posts.length">
        <FabricCard v-for="i in 3" :key="i">
          <FabricStack :gap="3">
            <FabricStack direction="row" align="center" :gap="3"><FabricSkeleton circle width="32px" height="32px" /><FabricSkeleton width="30%" /></FabricStack>
            <FabricSkeleton :lines="3" />
          </FabricStack>
        </FabricCard>
      </template>
    </div>

    <FabricEmptyState v-if="done && !posts.length && !error" :icon="tab === 'foryou' ? 'sparkle' : 'users'"
      :title="tab === 'foryou' ? 'Nothing to rank for them yet' : 'Nothing from the accounts they follow'"
      :description="tab === 'foryou' ? 'For You needs recent posts on their topics, or from people like them.' : 'They may not follow anyone yet, or nobody they follow has posted.'" />

    <div ref="sentinel" class="end">
      <FabricSpinner v-if="loading && posts.length" label="Loading more" />
      <span v-else-if="done && posts.length" class="muted">That's everything: {{ posts.length }} posts.</span>
      <FabricButton v-else-if="posts.length && !error" variant="ghost" size="sm" @click="loadMore">Load more</FabricButton>
    </div>
  </FabricPage>
</template>

<style scoped>
.viewer { display: flex; flex-direction: column; line-height: 1.3; min-width: 0; }
.viewer span { font-size: var(--fabric-text-xs); color: var(--fabric-muted); }
.about { min-height: 32px; }
.muted { color: var(--fabric-muted); font-size: var(--fabric-text-sm); }
.posts { display: flex; flex-direction: column; gap: var(--fabric-space-3); max-width: 680px; width: 100%; }
.end { display: flex; justify-content: center; padding: var(--fabric-space-4) 0; max-width: 680px; }
</style>
