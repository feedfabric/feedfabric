<script setup lang="ts">
import { computed, ref } from 'vue'
import { FabricBadge } from '@feedfabric/fabric-ui'
import { constraints, objectSchema, refName, typeLabel, type OpenApiDocument, type Schema } from '../openapi'

// An object schema's fields, one per row; a field that is itself an object (or a list of them) opens in place. depth
// stops a schema that refers to itself from opening forever.
const props = withDefaults(defineProps<{ doc: OpenApiDocument; schema: Schema; depth?: number }>(), { depth: 0 })
const MaxDepth = 6

const object = computed(() => objectSchema(props.doc, props.schema))
const fields = computed(() => Object.entries(object.value?.properties ?? {}).map(([name, schema]) => ({
  name,
  schema,
  required: object.value?.required?.includes(name) ?? false,
  nested: props.depth < MaxDepth ? objectSchema(props.doc, schema) : undefined,
})))
const open = ref<Set<string>>(new Set())
const toggle = (name: string) => {
  const next = new Set(open.value)
  if (!next.delete(name)) next.add(name)
  open.value = next
}
</script>

<template>
  <ul v-if="fields.length" class="fields" :class="{ nested: depth > 0 }">
    <li v-for="f in fields" :key="f.name">
      <div class="row">
        <button v-if="f.nested" type="button" class="name expander" :aria-expanded="open.has(f.name)" @click="toggle(f.name)">
          <span class="caret" aria-hidden="true">{{ open.has(f.name) ? '▾' : '▸' }}</span>{{ f.name }}
        </button>
        <code v-else class="name">{{ f.name }}</code>
        <span class="type">{{ typeLabel(f.schema) }}</span>
        <FabricBadge v-if="f.required" tone="warn" size="sm">required</FabricBadge>
        <span v-for="c in constraints(f.schema)" :key="c" class="note">{{ c }}</span>
      </div>
      <p v-if="f.schema.description" class="description">{{ f.schema.description }}</p>
      <SchemaFields v-if="f.nested && open.has(f.name)" :doc="doc" :schema="f.schema" :depth="depth + 1" />
    </li>
  </ul>
  <p v-else class="note">{{ schema.$ref ? refName(schema.$ref) : typeLabel(schema) }}</p>
</template>

<style scoped>
.fields { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; font-size: var(--fabric-text-sm); }
.fields.nested { margin: 6px 0 2px 10px; padding-left: 12px; border-left: 2px solid var(--fabric-line); }
.row { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 8px; }
.name { font-family: var(--fabric-font-mono); font-size: var(--fabric-text-sm); font-weight: 600; color: var(--fabric-ink);
  background: none; padding: 0; }
.expander { border: 0; cursor: pointer; display: inline-flex; gap: 4px; align-items: baseline; border-radius: var(--fabric-radius-sm); }
.expander:hover { color: var(--fabric-accent); }
.expander:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
.caret { width: 1em; color: var(--fabric-muted); }
.type { color: var(--fabric-accent); font-family: var(--fabric-font-mono); font-size: var(--fabric-text-xs); }
.note { margin: 0; color: var(--fabric-muted); font-size: var(--fabric-text-xs); }
.description { margin: 2px 0 0; color: var(--fabric-muted); font-size: var(--fabric-text-xs); }
</style>
