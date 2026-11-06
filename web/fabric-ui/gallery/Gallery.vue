<script setup lang="ts">
import { ref } from 'vue'
import {
  FabricAppShell, FabricAvatar, FabricBadge, FabricButton, FabricCard, FabricChip, FabricConfirmDialog, FabricDialog, FabricDisclosure,
  FabricEmptyState, FabricFileUpload, FabricGrid, FabricInput, FabricList, FabricListItem, FabricMenu, FabricMeter, FabricNotice,
  FabricPage, FabricPager, FabricProgress, FabricRangeSlider, FabricSegmented, FabricSelect, FabricSidebar, FabricSkeleton, FabricSpinner,
  FabricStack, FabricStatCard, FabricSwitch, FabricTable, FabricTabs, FabricTextarea, FabricThemeToggle, FabricToaster, FabricTooltip,
  useToast, type FileRejection, type SidebarSection, type SortDirection, type TableColumn,
} from '../src'

// Every component in the states it has, on one page, to look at in both themes and at phone width
const { toast } = useToast()

const sections: SidebarSection[] = [
  { links: [{ label: 'Overview', to: '#overview', icon: 'chart' }, { label: 'Review queue', to: '#review', icon: 'shield', badge: 12 }] },
  { title: 'Content', links: [{ label: 'Posts', to: '#posts', icon: 'message' }, { label: 'Users', to: '#users', icon: 'users' }] },
  { title: 'Ranking', links: [{ label: 'Feed studio', to: '#studio', icon: 'sparkle' }, { label: 'System', to: '#system', icon: 'settings' }] },
]

const name = ref('')
const search = ref('running')
const post = ref('5k along the canal before work #running')
const reason = ref<'spam' | 'abuse' | 'other'>('spam')
const notify = ref(true)
const quiet = ref(false)
const window = ref<'1h' | '24h' | '7d'>('24h')
const halfLife = ref(6)
const tab = ref<'queue' | 'reports' | 'log'>('queue')
const chips = ref(['running', 'cycling', 'sleep'])
const followingOnly = ref(true)
const dialogOpen = ref(false)
const confirmOpen = ref(false)
const confirmBusy = ref(false)
const files = ref<string[]>([])
const rejected = ref<string[]>([])
const page = ref(1)

interface Row { user: string; posts: number; reports: number; status: 'active' | 'suspended' }
const rows: Row[] = [
  { user: 'maya_runs', posts: 214, reports: 0, status: 'active' },
  { user: 'canal_cyclist', posts: 88, reports: 3, status: 'active' },
  { user: 'buy_followers_now', posts: 1210, reports: 41, status: 'suspended' },
]
const columns: TableColumn<Row>[] = [
  { key: 'user', label: 'User', sortable: true },
  { key: 'posts', label: 'Posts', align: 'right', sortable: true },
  { key: 'reports', label: 'Reports', align: 'right' },
  { key: 'status', label: 'Status' },
]
const sortKey = ref('posts')
const sortDirection = ref<SortDirection>('desc')

function onFiles(list: File[]) {
  files.value = [...files.value, ...list.map(f => f.name)]
}

function onRejected(list: FileRejection[]) {
  rejected.value = list.map(r => `${r.file.name}: ${r.reason}`)
}

function confirm() {
  confirmBusy.value = true
  setTimeout(() => {
    confirmBusy.value = false
    confirmOpen.value = false
    toast({ title: 'User suspended', message: 'buy_followers_now can no longer post.', tone: 'good', action: { label: 'Undo', onSelect: () => toast({ title: 'Reinstated' }) } })
  }, 900)
}
</script>

<template>
  <FabricAppShell>
    <template #brand>FeedFabric <span class="muted">admin</span></template>
    <template #sidebar="{ closeMenu }"><FabricSidebar :sections="sections" active-path="#review" @navigate="closeMenu" /></template>
    <template #sidebar-footer>
      <FabricStack direction="row" align="center" :gap="2"><FabricAvatar name="Kieran" :size="28" /><span>kieran</span></FabricStack>
      <FabricThemeToggle />
    </template>

    <FabricPage title="FabricUI" description="FeedFabric's components: every one here in the states it has. Switch the theme in the sidebar.">
      <template #actions>
        <FabricButton icon="plus" variant="primary" @click="dialogOpen = true">Open dialog</FabricButton>
        <FabricMenu label="More actions" :items="[
          { label: 'Export CSV', icon: 'upload', onSelect: () => toast({ title: 'Exported' }) },
          { label: 'Settings', icon: 'settings', onSelect: () => toast({ title: 'Settings' }) },
          { label: 'Disabled', disabled: true, onSelect: () => {} },
          { label: 'Delete all', icon: 'trash', tone: 'danger', onSelect: () => (confirmOpen = true) },
        ]" />
      </template>

      <FabricGrid min="180px">
        <FabricStatCard label="Posts today" :value="1284" :delta="12.4" delta-label="vs yesterday" />
        <FabricStatCard label="Held for review" :value="12" :delta="-30" :higher-is-better="false" hint="Oldest waiting 2h" />
        <FabricStatCard label="Not interested" value="0.75%" :delta="0" />
        <FabricStatCard label="Loading" loading />
      </FabricGrid>

      <FabricGrid min="320px">
        <FabricCard title="Buttons" subtitle="Variants, sizes and states">
          <FabricStack :gap="3">
            <FabricStack direction="row" wrap :gap="2">
              <FabricButton variant="primary">Primary</FabricButton>
              <FabricButton>Secondary</FabricButton>
              <FabricButton variant="ghost">Ghost</FabricButton>
              <FabricButton variant="good" icon="check">Approve</FabricButton>
              <FabricButton variant="danger" icon="trash">Remove</FabricButton>
            </FabricStack>
            <FabricStack direction="row" wrap align="center" :gap="2">
              <FabricButton size="sm">Small</FabricButton>
              <FabricButton size="lg" variant="primary" icon-after="chevron-right">Large</FabricButton>
              <FabricButton loading variant="primary">Saving</FabricButton>
              <FabricButton disabled>Disabled</FabricButton>
              <FabricButton icon="edit" label="Edit" />
              <FabricButton href="#top" icon="external">Link</FabricButton>
            </FabricStack>
          </FabricStack>
        </FabricCard>

        <FabricCard title="Badges, chips and avatars">
          <FabricStack :gap="3">
            <FabricStack direction="row" wrap :gap="2">
              <FabricBadge>neutral</FabricBadge><FabricBadge tone="accent">accent</FabricBadge><FabricBadge tone="good" dot>visible</FabricBadge>
              <FabricBadge tone="warn" dot>held</FabricBadge><FabricBadge tone="bad" dot>removed</FabricBadge><FabricBadge tone="info">info</FabricBadge>
            </FabricStack>
            <FabricStack direction="row" wrap :gap="2">
              <FabricChip v-for="c in chips" :key="c" removable :remove-label="`Remove ${c}`" @remove="chips = chips.filter(x => x !== c)">#{{ c }}</FabricChip>
              <FabricChip v-model:selected="followingOnly" selectable>Following only</FabricChip>
            </FabricStack>
            <FabricStack direction="row" :gap="2" align="center">
              <FabricAvatar name="Maya Runs" /><FabricAvatar name="canal_cyclist" /><FabricAvatar name="sim-42" :size="40" />
              <FabricAvatar name="Broken picture" src="/missing.png" />
              <FabricTooltip text="Verified by staff"><FabricBadge tone="accent">verified</FabricBadge></FabricTooltip>
              <FabricSpinner label="Loading" />
            </FabricStack>
          </FabricStack>
        </FabricCard>

        <FabricCard title="Fields">
          <FabricStack :gap="3">
            <FabricInput v-model="name" label="Display name" hint="Shown next to the username." required placeholder="e.g. Maya" />
            <FabricInput v-model="search" label="Search" hide-label icon="search" type="search" clearable placeholder="Search posts" />
            <FabricInput model-value="not an email" label="Email" error="That doesn't look like an email address." />
            <FabricSelect v-model="reason" label="Reason" :options="[
              { value: 'spam', label: 'Spam' }, { value: 'abuse', label: 'Abuse or harassment' }, { value: 'other', label: 'Something else' },
            ]" />
            <FabricTextarea v-model="post" label="Post" :maxlength="500" hint="Hashtags become topics." />
          </FabricStack>
        </FabricCard>

        <FabricCard title="Choices">
          <FabricStack :gap="4">
            <FabricSwitch v-model="notify" label="Email me about reports" description="At most one email an hour." />
            <FabricSwitch v-model="quiet" label="Disabled switch" disabled />
            <FabricSegmented v-model="window" label="Time window" :options="[
              { value: '1h', label: 'Hour' }, { value: '24h', label: 'Day' }, { value: '7d', label: 'Week' },
            ]" />
            <FabricRangeSlider v-model="halfLife" label="Freshness half-life" :min="1" :max="48" :format="v => `${v} hours`"
              hint="A post's score halves every this many hours." />
          </FabricStack>
        </FabricCard>

        <FabricCard title="Feedback">
          <FabricStack :gap="3">
            <FabricNotice tone="info" title="Heads up">Scores are from the pretrained model.</FabricNotice>
            <FabricNotice tone="good">Model loaded: model:20261105-045455.</FabricNotice>
            <FabricNotice tone="warn" title="Trained on simulated data">Don't serve this model to real members.</FabricNotice>
            <FabricNotice tone="bad" title="ClamAV is unreachable" dismissible>
              Uploads wait in quarantine until it's back.
              <template #actions><FabricButton size="sm">Retry</FabricButton></template>
            </FabricNotice>
            <FabricStack direction="row" wrap :gap="2">
              <FabricButton size="sm" @click="toast({ title: 'Saved', tone: 'good' })">Toast: good</FabricButton>
              <FabricButton size="sm" @click="toast({ title: 'Profile saved as v3', message: 'Readers get it on their next refresh.' })">Toast: info</FabricButton>
              <FabricButton size="sm" variant="danger" @click="toast({ title: 'Couldn\'t reach the API', tone: 'bad' })">Toast: problem</FabricButton>
            </FabricStack>
          </FabricStack>
        </FabricCard>

        <FabricCard title="Scores and progress">
          <FabricStack :gap="4">
            <FabricMeter label="Nudity" :value="0.93" :threshold="0.8" threshold-label="rejects at 0.8" />
            <FabricMeter label="Insult" :value="0.71" :threshold="0.8" />
            <FabricMeter label="Threat" :value="0.04" :threshold="0.6" />
            <FabricProgress label="Uploading photo.jpg" :value="64" show-value />
            <FabricProgress label="Scanning" show-value tone="warn" />
          </FabricStack>
        </FabricCard>

        <FabricCard title="Upload">
          <FabricStack :gap="3">
            <FabricFileUpload label="Add photos" accept="image/jpeg,image/png,image/webp" :max-bytes="20 * 1024 * 1024" :max-files="4"
              hint="JPEG, PNG or WebP" @files="onFiles" @rejected="onRejected" />
            <FabricFileUpload label="Compact" compact :multiple="false" />
            <p v-if="files.length" class="muted">Chosen: {{ files.join(', ') }}</p>
            <p v-if="rejected.length" class="error">Turned away: {{ rejected.join(', ') }}</p>
          </FabricStack>
        </FabricCard>

        <FabricCard title="Loading and empty">
          <FabricStack :gap="4">
            <FabricStack direction="row" :gap="3" align="center">
              <FabricSkeleton circle width="36px" /><FabricSkeleton :lines="2" />
            </FabricStack>
            <FabricEmptyState title="Nothing waiting" description="Held posts and comments appear here for a decision." icon="shield" compact>
              <FabricButton size="sm">See the log</FabricButton>
            </FabricEmptyState>
          </FabricStack>
        </FabricCard>
      </FabricGrid>

      <FabricCard title="Tabs and disclosure">
        <FabricTabs v-model="tab" label="Moderation" :tabs="[
          { value: 'queue', label: 'Queue', icon: 'shield', badge: 12 }, { value: 'reports', label: 'Reports', icon: 'flag', badge: 3 },
          { value: 'log', label: 'Log', icon: 'clock' },
        ]">
          <template #panel="{ value }">
            <p class="muted">Showing the {{ value }} panel.</p>
            <FabricDisclosure summary="Why was this held?" hint="2 reasons">
              <p class="muted">Insult scored 0.93 (holds at 0.9), and three people reported it.</p>
            </FabricDisclosure>
          </template>
        </FabricTabs>
      </FabricCard>

      <FabricCard title="Table" padding="none">
        <FabricTable :columns="columns" :rows="rows" :row-key="r => r.user" caption="Users" clickable :sort-key="sortKey" :sort-direction="sortDirection"
          @sort="(k, d) => { sortKey = k; sortDirection = d }" @row-click="r => toast({ title: `Opened ${r.user}` })">
          <template #cell-user="{ row }">
            <FabricStack direction="row" align="center" :gap="2"><FabricAvatar :name="row.user" :size="24" decorative />{{ row.user }}</FabricStack>
          </template>
          <template #cell-status="{ row }">
            <FabricBadge :tone="row.status === 'active' ? 'good' : 'bad'" dot>{{ row.status }}</FabricBadge>
          </template>
        </FabricTable>
        <FabricPager :has-previous="page > 1" has-next :summary="`Page ${page}`" @previous="page--" @next="page++" />
      </FabricCard>

      <FabricGrid min="320px">
        <FabricCard title="List" padding="none">
          <FabricList label="Recent reports">
            <FabricListItem title="maya_runs" subtitle="Reported a comment as spam" meta="2m">
              <template #leading><FabricAvatar name="maya_runs" decorative /></template>
            </FabricListItem>
            <FabricListItem title="canal_cyclist" subtitle="Reported a post as abuse" meta="14m" clickable @select="toast({ title: 'Selected' })">
              <template #leading><FabricAvatar name="canal_cyclist" decorative /></template>
            </FabricListItem>
            <FabricListItem title="sim-42" subtitle="Reported a post as other">
              <template #leading><FabricAvatar name="sim-42" decorative /></template>
              <template #trailing><FabricButton size="sm" variant="good">Dismiss</FabricButton></template>
            </FabricListItem>
          </FabricList>
        </FabricCard>
        <FabricCard title="Table states" padding="none">
          <FabricTable :columns="columns" :rows="[]" :row-key="r => r.user" loading :loading-rows="2" />
          <FabricTable :columns="columns" :rows="[]" :row-key="r => r.user">
            <template #empty>No users match that search.</template>
          </FabricTable>
        </FabricCard>
      </FabricGrid>
    </FabricPage>

    <FabricDialog v-model:open="dialogOpen" title="New ranking profile" description="Start from a preset and change what you need.">
      <FabricStack :gap="3">
        <FabricInput label="Name" placeholder="e.g. Weekend long reads" />
        <FabricSelect label="Start from" :options="[{ value: 'balanced', label: 'Balanced' }, { value: 'fresh', label: 'Fresh' }]" model-value="balanced" />
      </FabricStack>
      <template #footer>
        <FabricButton @click="dialogOpen = false">Cancel</FabricButton>
        <FabricButton variant="primary" @click="dialogOpen = false; toast({ title: 'Created', tone: 'good' })">Create</FabricButton>
      </template>
    </FabricDialog>
    <FabricConfirmDialog v-model:open="confirmOpen" title="Suspend buy_followers_now?" message="They can still read, but can't post or interact until reinstated."
      confirm-label="Suspend" :busy="confirmBusy" @confirm="confirm" />
    <FabricToaster />
  </FabricAppShell>
</template>

<style>
.muted { color: var(--fabric-muted); font-size: var(--fabric-text-sm); margin: 0; }
.error { color: var(--fabric-bad); font-size: var(--fabric-text-sm); margin: 0; }
</style>
