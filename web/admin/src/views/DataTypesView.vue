<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  FabricBadge, FabricButton, FabricCard, FabricConfirmDialog, FabricEmptyState, FabricInput, FabricNotice, FabricPage, FabricTextarea, useToast,
} from '@feedfabric/fabric-ui'
import { api, describeError } from '../api'
import { useNow } from '../composables/useNow'
import { ago } from '../format'
import type { DataType } from '../types'

// The kinds of structured data your apps attach to posts, such as a weight reading. The API checks every post's
// data against its type's JSON Schema, and the SDKs read the same schemas for their typed helpers.
const Example = JSON.stringify({
  type: 'object',
  required: ['kg'],
  additionalProperties: false,
  properties: { kg: { type: 'number', minimum: 20, maximum: 400 }, note: { type: 'string', maxLength: 200 } },
}, null, 2)

const types = ref<DataType[] | null>(null)
const error = ref<string | null>(null)
const editing = ref<{ isNew: boolean; key: string; label: string; description: string; schema: string } | null>(null)
const saving = ref(false)
const deleting = ref<DataType | null>(null)
const busy = ref(false)
const now = useNow()
const { toast } = useToast()

async function load() {
  try {
    types.value = await api.dataTypes()
    error.value = null
  } catch (e) {
    error.value = describeError(e)
  }
}
onMounted(load)

function startNew() {
  editing.value = { isNew: true, key: '', label: '', description: '', schema: Example }
}

function edit(t: DataType) {
  editing.value = { isNew: false, key: t.key, label: t.label, description: t.description ?? '', schema: JSON.stringify(t.schema, null, 2) }
}

// The schema as typed, or why it isn't JSON yet
const parsed = computed<{ value?: unknown; problem?: string }>(() => {
  if (!editing.value) return {}
  try {
    return { value: JSON.parse(editing.value.schema) }
  } catch (e) {
    return { problem: e instanceof Error ? e.message : String(e) }
  }
})

async function save() {
  const e = editing.value
  if (!e || parsed.value.problem) return
  saving.value = true
  try {
    const saved = await api.saveDataType(e.key.trim(), { label: e.label.trim(), description: e.description.trim() || null, schema: parsed.value.value })
    toast({ title: 'Saved', message: `${saved.label} is at version ${saved.version}.`, tone: 'good' })
    editing.value = null
    await load()
  } catch (err) {
    toast({ title: 'Not saved', message: describeError(err), tone: 'bad' })
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!deleting.value) return
  busy.value = true
  try {
    await api.deleteDataType(deleting.value.key)
    deleting.value = null
    await load()
  } catch (e) {
    toast({ title: 'Not deleted', message: describeError(e), tone: 'bad' })
  } finally {
    busy.value = false
  }
}

const fields = (t: DataType) => Object.keys((t.schema.properties as Record<string, unknown> | undefined) ?? {})
</script>

<template>
  <FabricPage title="Data types" description="Structured data your apps attach to posts, each checked against its JSON Schema.">
    <template #actions>
      <FabricButton v-if="!editing" variant="primary" icon="plus" @click="startNew">New data type</FabricButton>
    </template>
    <FabricNotice v-if="error" tone="bad">{{ error }}</FabricNotice>

    <FabricCard v-if="editing" :title="editing.isNew ? 'New data type' : `Edit ${editing.key}`">
      <form class="form" @submit.prevent="save">
        <div class="row">
          <FabricInput v-model="editing.key" label="Key" hint="What posts send as data.type" placeholder="weight-reading" required
            :disabled="!editing.isNew" pattern="[a-z][a-z0-9-]{1,39}" />
          <FabricInput v-model="editing.label" label="Name" placeholder="Weight reading" :maxlength="80" required />
        </div>
        <FabricInput v-model="editing.description" label="Description" placeholder="One weigh-in, in kilograms" :maxlength="500" />
        <FabricTextarea v-model="editing.schema" label="JSON Schema" :rows="14" :error="parsed.problem ?? null"
          hint="The root must be &quot;type&quot;: &quot;object&quot;. A changed schema is a new version; earlier posts keep their data." class="schema" />
        <div class="actions">
          <FabricButton type="button" @click="editing = null">Cancel</FabricButton>
          <FabricButton type="submit" variant="primary" icon="check" :loading="saving" :disabled="!!parsed.problem">Save</FabricButton>
        </div>
      </form>
    </FabricCard>

    <FabricEmptyState v-if="types && !types.length && !editing" title="No data types yet"
      description="Add one to let your apps post typed data, such as weight readings, alongside or instead of text." />

    <FabricCard v-for="t in types ?? []" :key="t.key" :title="t.label" :subtitle="t.description ?? undefined">
      <template #actions>
        <FabricButton size="sm" variant="ghost" icon="edit" @click="edit(t)">Edit</FabricButton>
        <FabricButton size="sm" variant="ghost" icon="trash" @click="deleting = t">Delete</FabricButton>
      </template>
      <p class="meta">
        <code>{{ t.key }}</code>
        <FabricBadge size="sm">version {{ t.version }}</FabricBadge>
        <span class="muted">saved {{ ago(t.updatedAt, now) }}<template v-if="t.updatedBy"> by {{ t.updatedBy }}</template></span>
      </p>
      <p v-if="fields(t).length" class="muted">Fields: <code v-for="f in fields(t)" :key="f">{{ f }}</code></p>
    </FabricCard>

    <FabricConfirmDialog :open="!!deleting" :title="`Delete ${deleting?.label ?? 'this data type'}?`" :busy="busy"
      message="New posts can't use it. Posts that already have it keep their data." confirm-label="Delete"
      @update:open="v => !v && (deleting = null)" @confirm="remove" @cancel="deleting = null" />
  </FabricPage>
</template>

<style scoped>
.form { display: grid; gap: 12px; }
.row { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 240px), 1fr)); gap: 12px; }
.schema :deep(textarea) { font-family: var(--fabric-font-mono, ui-monospace, monospace); font-size: 13px; }
.actions { display: flex; justify-content: flex-end; gap: 8px; }
.meta { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 0 0 6px; }
code + code { margin-left: 4px; }
</style>
