<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  FabricBadge, FabricButton, FabricCard, FabricConfirmDialog, FabricInput, FabricNotice, FabricPage, FabricTable, useToast, type TableColumn,
} from '@feedfabric/fabric-ui'
import { api, describeError } from '../api'
import { useNow } from '../composables/useNow'
import { ago } from '../format'
import type { ApiKey, CreatedApiKey } from '../types'

// Keys for the host's own servers, which call the API through the SDKs (sdks/) and act for one member at a time with
// the FeedFabric-User header. A key is shown once, when it's made; after that only its first characters are.
const keys = ref<ApiKey[] | null>(null)
const error = ref<string | null>(null)
const name = ref('')
const creating = ref(false)
const created = ref<CreatedApiKey | null>(null)
const revoking = ref<ApiKey | null>(null)
const busy = ref(false)
const now = useNow()
const { toast } = useToast()

async function load() {
  try {
    keys.value = await api.apiKeys()
    error.value = null
  } catch (e) {
    error.value = describeError(e)
  }
}
onMounted(load)

async function create() {
  creating.value = true
  try {
    created.value = await api.createApiKey(name.value.trim())
    name.value = ''
    await load()
  } catch (e) {
    toast({ title: 'Not made', message: describeError(e), tone: 'bad' })
  } finally {
    creating.value = false
  }
}

async function copy() {
  if (!created.value) return
  await navigator.clipboard.writeText(created.value.key)
  toast({ title: 'Copied', message: 'Keep it in your secret store; this page won’t show it again.', tone: 'good' })
}

async function revoke() {
  if (!revoking.value) return
  busy.value = true
  try {
    await api.revokeApiKey(revoking.value.id)
    if (created.value?.id === revoking.value.id) created.value = null
    revoking.value = null
    await load()
  } catch (e) {
    toast({ title: 'Not revoked', message: describeError(e), tone: 'bad' })
  } finally {
    busy.value = false
  }
}

const columns: TableColumn<ApiKey>[] = [
  { key: 'name', label: 'Name' },
  { key: 'prefix', label: 'Key' },
  { key: 'createdAt', label: 'Made' },
  { key: 'lastUsedAt', label: 'Last used' },
  { key: 'status', label: 'Status' },
  { key: 'actions', label: '', align: 'right' },
]
const active = computed(() => keys.value?.filter(k => !k.revokedAt).length ?? 0)
</script>

<template>
  <FabricPage title="API keys" description="For your own servers: each key acts for one of your members at a time, through the SDKs.">
    <FabricNotice v-if="error" tone="bad">{{ error }}</FabricNotice>

    <FabricNotice v-if="created" tone="good" title="Your new key" dismissible @dismiss="created = null">
      <p class="once">Copy it now: it's only shown this once. Anyone with it can act as any member, so keep it in a secret store, not in code.</p>
      <code class="key">{{ created.key }}</code>
      <template #actions><FabricButton size="sm" icon="check" @click="copy">Copy</FabricButton></template>
    </FabricNotice>

    <FabricCard title="Make a key">
      <form class="make" @submit.prevent="create">
        <FabricInput v-model="name" label="What it's for" placeholder="Web app backend" :maxlength="80" required class="name" />
        <FabricButton type="submit" variant="primary" icon="plus" :loading="creating" :disabled="!name.trim()">Make key</FabricButton>
      </form>
      <p class="muted hint">
        Servers send it as <code>Authorization: Bearer ffk_…</code> with <code>FeedFabric-User: &lt;member ID&gt;</code>. Keys never reach
        staff pages, and a revoked key stops working within a minute.
      </p>
    </FabricCard>

    <FabricCard :title="`Keys`" :subtitle="keys ? `${active} in use` : undefined" padding="none">
      <FabricTable :columns="columns" :rows="keys ?? []" :row-key="k => k.id" :loading="!keys && !error">
        <template #cell-prefix="{ row }"><code>{{ row.prefix }}…</code></template>
        <template #cell-createdAt="{ row }">{{ ago(row.createdAt, now) }}<span v-if="row.createdBy" class="muted"> by {{ row.createdBy }}</span></template>
        <template #cell-lastUsedAt="{ row }">{{ row.lastUsedAt ? ago(row.lastUsedAt, now) : 'never' }}</template>
        <template #cell-status="{ row }">
          <FabricBadge :tone="row.revokedAt ? 'bad' : 'good'" size="sm">{{ row.revokedAt ? `revoked ${ago(row.revokedAt, now)}` : 'active' }}</FabricBadge>
        </template>
        <template #cell-actions="{ row }">
          <FabricButton v-if="!row.revokedAt" size="sm" variant="ghost" icon="trash" @click="revoking = row">Revoke</FabricButton>
        </template>
        <template #empty>No keys yet. Make one for each server or app, so you can revoke one without the others.</template>
      </FabricTable>
    </FabricCard>

    <FabricConfirmDialog :open="!!revoking" :title="`Revoke ${revoking?.name ?? 'this key'}?`" :busy="busy"
      message="Servers using it get 401s from now on. This can't be undone; make a new key instead." confirm-label="Revoke"
      @update:open="v => !v && (revoking = null)" @confirm="revoke" @cancel="revoking = null" />
  </FabricPage>
</template>

<style scoped>
.make { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-end; }
.name { flex: 1 1 280px; }
.hint { margin: 12px 0 0; font-size: 13px; }
.once { margin: 0 0 8px; }
.key { display: block; padding: 8px 10px; border-radius: 8px; background: var(--fabric-surface-2, rgba(127, 127, 127, 0.12)); overflow-wrap: anywhere; user-select: all; }
</style>
