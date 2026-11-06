<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  FabricBadge, FabricButton, FabricCard, FabricEmptyState, FabricInput, FabricNotice, FabricPage, FabricSegmented, FabricSkeleton, FabricStack,
  FabricTable, type SegmentOption, type TableColumn,
} from '@feedfabric/fabric-ui'
import SchemaFields from '../components/SchemaFields.vue'
import { useRouteQuery } from '../composables/useRouteQuery'
import { me } from '../me'
import {
  constraints, endpoints, jsonSchema, tagName, typeLabel, type Endpoint, type Method, type OpenApiDocument, type Parameter,
} from '../openapi'

// The API's OpenAPI document, to browse: every endpoint by area, with its parameters, body and responses. The panel
// ships a saved copy (public/openapi.json, refreshed with make openapi), so this works wherever the panel is
// deployed; in Development the API also serves its live document, and the page says when the copy is out of date.
type Source = 'saved' | 'live'
const Sources: Record<Source, string> = { saved: '/openapi.json', live: '/openapi/v1.json' }
const Other = 'FeedFabric.Api'
const MethodTones: Record<Method, 'info' | 'good' | 'warn' | 'bad' | 'accent'> = { get: 'info', post: 'good', put: 'warn', patch: 'accent', delete: 'bad' }

const docs = ref<Partial<Record<Source, OpenApiDocument>>>({})
const failed = ref<Partial<Record<Source, string>>>({})
const source = useRouteQuery('source', 'saved')
const q = useRouteQuery('q')
const method = useRouteQuery('method', 'all')
const opened = useRouteQuery('op')

async function load(from: Source) {
  try {
    const res = await fetch(Sources[from])
    if (!res.ok || !res.headers.get('content-type')?.includes('json')) throw new Error(`${res.status} ${res.statusText}`)
    docs.value = { ...docs.value, [from]: await res.json() }
  } catch (e) {
    failed.value = { ...failed.value, [from]: e instanceof Error ? e.message : String(e) }
  }
}
onMounted(() => Promise.all([load('saved'), load('live')]))

const doc = computed(() => docs.value[source.value as Source])
const stale = computed(() => !!docs.value.saved && !!docs.value.live && JSON.stringify(docs.value.saved) !== JSON.stringify(docs.value.live))
const sourceOptions = computed<SegmentOption<string>[]>(() => [
  { value: 'saved', label: 'Saved copy' },
  { value: 'live', label: failed.value.live ? 'Live (unavailable)' : 'Live from the API' },
])
const methodOptions: SegmentOption<string>[] = [
  { value: 'all', label: 'All' }, { value: 'get', label: 'GET' }, { value: 'post', label: 'POST' }, { value: 'put', label: 'PUT' },
  { value: 'delete', label: 'DELETE' },
]

const all = computed(() => (doc.value ? endpoints(doc.value) : []))
const shown = computed(() => {
  const words = q.value.toLowerCase().split(/\s+/).filter(Boolean)
  return all.value.filter(e => (method.value === 'all' || e.method === method.value)
    && words.every(w => `${e.method} ${e.path} ${e.tag} ${tagName(e.tag)} ${e.operation.summary ?? ''}`.toLowerCase().includes(w)))
})
const groups = computed(() => {
  const byTag = new Map<string, Endpoint[]>()
  for (const e of shown.value) byTag.set(e.tag, [...(byTag.get(e.tag) ?? []), e])
  // Areas in the document's order, with the endpoints no controller claims last
  return [...byTag].map(([tag, list]) => ({ tag, list })).sort((x, y) => Number(x.tag === Other) - Number(y.tag === Other))
})

const key = (e: Endpoint) => `${e.method} ${e.path}`
const toggle = (e: Endpoint) => (opened.value = opened.value === key(e) ? '' : key(e))
// Who can call it: the admin and ranking areas need a staff role outside Development
const audience = (path: string) => (path.startsWith('/admin') || path.startsWith('/ranking') ? 'staff' : path.startsWith('/blobs') ? 'signed link' : 'members')

const parameterColumns: TableColumn<Parameter>[] = [
  { key: 'name', label: 'Name' },
  { key: 'in', label: 'In' },
  { key: 'type', label: 'Type', value: p => typeLabel(p.schema) },
  { key: 'notes', label: 'Notes', value: p => [p.required ? 'required' : '', ...constraints(p.schema)].filter(Boolean).join(' · ') },
]

// A request to paste into a terminal, signed in the way this panel is
function curl(e: Endpoint): string {
  const origin = location.origin
  const path = e.path.replace(/\{(\w+)\}/g, (_, name) => `<${name}>`)
  const auth = me.value?.environment === 'Development' ? `-H 'X-Dev-User: ${me.value.externalUserId}'` : `-H 'Authorization: Bearer <token>'`
  const body = e.operation.requestBody ? ` \\\n  -H 'Content-Type: application/json' -d '{…}'` : ''
  return `curl -X ${e.method.toUpperCase()} '${origin}${path}' \\\n  ${auth}${body}`
}
</script>

<template>
  <FabricPage title="API reference" :description="doc ? `${doc.info.title} · OpenAPI ${doc.openapi} · ${all.length} endpoints` : 'Every endpoint, from the API’s OpenAPI document.'">
    <template #actions>
      <FabricButton icon="external" :href="Sources[source as Source]" target="_blank">Open the JSON</FabricButton>
    </template>

    <FabricNotice v-if="stale && source === 'saved'" tone="warn" title="The saved copy is out of date">
      The running API's document differs from the one this panel ships. Run <code>make openapi</code> with the API running in Development, and
      commit <code>web/admin/public/openapi.json</code>.
      <template #actions><FabricButton size="sm" @click="source = 'live'">Show the live one</FabricButton></template>
    </FabricNotice>
    <FabricNotice v-if="failed[source as Source]" tone="bad" :title="source === 'live' ? 'The live document isn’t available' : 'The saved copy didn’t load'">
      {{ source === 'live'
        ? 'The API serves /openapi/v1.json in Development only, and the panel’s dev server must be able to reach it.'
        : `GET ${Sources.saved}: ${failed.saved}` }}
    </FabricNotice>

    <FabricCard padding="sm">
      <FabricStack direction="row" align="center" wrap :gap="3">
        <FabricSegmented v-model="source" :options="sourceOptions" label="Document" size="sm" />
        <FabricSegmented v-model="method" :options="methodOptions" label="Method" size="sm" />
        <FabricInput v-model="q" type="search" icon="search" label="Search endpoints" hide-label clearable size="sm" placeholder="Search" class="search" />
      </FabricStack>
    </FabricCard>

    <template v-if="doc">
      <FabricCard v-for="g in groups" :key="g.tag" :title="tagName(g.tag)" :subtitle="`${g.list.length} endpoint${g.list.length > 1 ? 's' : ''}`" padding="none">
        <ul class="ops">
          <li v-for="e in g.list" :key="key(e)" :class="{ open: opened === key(e) }">
            <button type="button" class="op" :aria-expanded="opened === key(e)" @click="toggle(e)">
              <FabricBadge :tone="MethodTones[e.method]" size="sm" class="method">{{ e.method.toUpperCase() }}</FabricBadge>
              <code class="path">{{ e.path }}</code>
              <span v-if="e.operation.summary" class="summary">{{ e.operation.summary }}</span>
              <FabricBadge v-if="audience(e.path) !== 'members'" size="sm" class="audience">{{ audience(e.path) }}</FabricBadge>
            </button>
            <div v-if="opened === key(e)" class="detail">
              <p v-if="e.operation.description" class="text">{{ e.operation.description }}</p>
              <section v-if="e.operation.parameters?.length">
                <h3>Parameters</h3>
                <FabricTable :columns="parameterColumns" :rows="e.operation.parameters" :row-key="p => `${p.in}:${p.name}`" dense />
              </section>
              <section v-if="e.operation.requestBody">
                <h3>Request body <span class="type">{{ typeLabel(jsonSchema(e.operation.requestBody.content)) }}</span></h3>
                <SchemaFields v-if="jsonSchema(e.operation.requestBody.content)" :doc="doc" :schema="jsonSchema(e.operation.requestBody.content)!" />
              </section>
              <section>
                <h3>Responses</h3>
                <div v-for="(r, status) in e.operation.responses" :key="status" class="response">
                  <FabricStack direction="row" align="baseline" :gap="2">
                    <FabricBadge :tone="String(status).startsWith('2') ? 'good' : 'bad'" size="sm">{{ status }}</FabricBadge>
                    <span class="text">{{ r.description }}</span>
                    <span v-if="jsonSchema(r.content)" class="type">{{ typeLabel(jsonSchema(r.content)) }}</span>
                  </FabricStack>
                  <SchemaFields v-if="jsonSchema(r.content)" :doc="doc" :schema="jsonSchema(r.content)!" />
                </div>
                <p class="text muted">Errors come back as Problem Details with a <code>code</code> (docs/errors.md).</p>
              </section>
              <section>
                <h3>Try it</h3>
                <pre class="curl">{{ curl(e) }}</pre>
              </section>
            </div>
          </li>
        </ul>
      </FabricCard>
      <FabricEmptyState v-if="!groups.length" icon="search" title="No endpoints match" description="Try fewer words, or another method.">
        <FabricButton size="sm" @click="q = ''; method = 'all'">Clear the search</FabricButton>
      </FabricEmptyState>
    </template>
    <FabricCard v-else-if="!failed[source as Source]"><FabricSkeleton :lines="6" /></FabricCard>
  </FabricPage>
</template>

<style scoped>
.search { flex: 1; min-width: min(100%, 220px); }
.ops { list-style: none; margin: 0; padding: 0; }
.ops > li { border-top: 1px solid var(--fabric-line); }
.op { display: flex; align-items: center; gap: 10px; width: 100%; padding: 10px var(--fabric-space-4); border: 0; border-radius: 0; background: none;
  text-align: left; cursor: pointer; color: var(--fabric-ink); }
.op:hover { background: var(--fabric-sunken); }
.op:focus-visible { outline: none; box-shadow: inset var(--fabric-focus); }
.open .op { background: var(--fabric-sunken); }
.method { flex: none; min-width: 58px; justify-content: center; }
.audience { margin-left: auto; flex: none; }
.path { font-family: var(--fabric-font-mono); font-size: var(--fabric-text-sm); background: none; color: var(--fabric-ink); padding: 0;
  overflow-wrap: anywhere; }
.summary { flex: 1; min-width: 0; color: var(--fabric-muted); font-size: var(--fabric-text-sm); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.detail { display: flex; flex-direction: column; gap: var(--fabric-space-4); padding: var(--fabric-space-3) var(--fabric-space-4) var(--fabric-space-4); }
h3 { margin: 0 0 8px; font-size: var(--fabric-text-xs); text-transform: uppercase; letter-spacing: .06em; color: var(--fabric-muted); }
.type { color: var(--fabric-accent); font-family: var(--fabric-font-mono); font-size: var(--fabric-text-xs); text-transform: none;
  letter-spacing: 0; }
.response { display: flex; flex-direction: column; gap: 6px; margin-bottom: 10px; }
.text { margin: 0; font-size: var(--fabric-text-sm); }
.muted { color: var(--fabric-muted); }
.curl { margin: 0; padding: 10px 12px; border-radius: var(--fabric-radius-md); background: var(--fabric-sunken); font-size: var(--fabric-text-xs);
  overflow-x: auto; white-space: pre; }
@media (max-width: 640px) { .op { flex-wrap: wrap; } .summary { flex-basis: 100%; white-space: normal; } }
</style>
