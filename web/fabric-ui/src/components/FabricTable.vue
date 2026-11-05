<script setup lang="ts" generic="Row">
import type { SortDirection, TableColumn } from '../types'
import FabricIcon from './FabricIcon.vue'
import FabricSkeleton from './FabricSkeleton.vue'

// Rows of data that scroll sideways on their own when narrow, never the page. Each cell shows row[column.key]
// (or column.value(row)); a #cell-<key> slot draws it instead. Rows emit row-click when clickable, and Enter on a
// focused row does the same. Sortable columns emit sort; the table doesn't sort itself, since the data often
// comes sorted from the server.
const props = withDefaults(defineProps<{
  columns: TableColumn<Row>[]
  rows: Row[]
  rowKey: (row: Row) => string | number
  caption?: string
  clickable?: boolean
  loading?: boolean
  loadingRows?: number
  sortKey?: string
  sortDirection?: SortDirection
  dense?: boolean
}>(), { loadingRows: 5, sortDirection: 'asc' })
const emit = defineEmits<{ 'row-click': [row: Row]; sort: [key: string, direction: SortDirection] }>()

function cell(row: Row, column: TableColumn<Row>): unknown {
  return column.value ? column.value(row) : (row as Record<string, unknown>)[column.key]
}

function sort(column: TableColumn<Row>) {
  const direction: SortDirection = props.sortKey === column.key && props.sortDirection === 'asc' ? 'desc' : 'asc'
  emit('sort', column.key, direction)
}

function ariaSort(column: TableColumn<Row>) {
  if (!column.sortable || props.sortKey !== column.key) return undefined
  return props.sortDirection === 'asc' ? 'ascending' : 'descending'
}
</script>

<template>
  <div class="fabric-table" :class="{ dense }">
    <table>
      <caption v-if="caption" class="fabric-sr-only">{{ caption }}</caption>
      <thead>
        <tr>
          <th v-for="c in columns" :key="c.key" scope="col" :class="c.align" :style="{ width: c.width }" :aria-sort="ariaSort(c)">
            <button v-if="c.sortable" type="button" class="sort" @click="sort(c)">
              {{ c.label }}
              <FabricIcon :name="sortKey === c.key && sortDirection === 'desc' ? 'chevron-down' : 'chevron-up'" :size="12"
                :class="{ idle: sortKey !== c.key }" />
            </button>
            <template v-else>{{ c.label }}</template>
          </th>
        </tr>
      </thead>
      <tbody v-if="loading">
        <tr v-for="n in loadingRows" :key="n">
          <td v-for="c in columns" :key="c.key"><FabricSkeleton :width="c.align === 'right' ? '40%' : '70%'" /></td>
        </tr>
      </tbody>
      <tbody v-else-if="rows.length">
        <tr v-for="row in rows" :key="rowKey(row)" :class="{ clickable }" :tabindex="clickable ? 0 : undefined"
          @click="clickable && emit('row-click', row)" @keydown.enter="clickable && emit('row-click', row)">
          <td v-for="c in columns" :key="c.key" :class="c.align">
            <slot :name="`cell-${c.key}`" :row="row" :value="cell(row, c)">{{ cell(row, c) ?? '–' }}</slot>
          </td>
        </tr>
      </tbody>
    </table>
    <div v-if="!loading && !rows.length" class="empty"><slot name="empty">Nothing to show.</slot></div>
  </div>
</template>

<style scoped>
.fabric-table { overflow-x: auto; min-width: 0; }
table { width: 100%; border-collapse: collapse; font-size: var(--fabric-text-sm); }
th {
  position: sticky; top: 0; z-index: 1; text-align: left; padding: 8px 12px; white-space: nowrap;
  font-size: var(--fabric-text-xs); font-weight: 600; color: var(--fabric-muted); background: var(--fabric-surface);
  border-bottom: 1px solid var(--fabric-line);
}
td { padding: 10px 12px; border-bottom: 1px solid var(--fabric-line); vertical-align: top; }
.dense td { padding: 6px 12px; }
tbody tr:last-child td { border-bottom: 0; }
.right { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
.center { text-align: center; }
.sort { display: inline-flex; align-items: center; gap: 3px; padding: 0; border: 0; background: none; font: inherit; color: inherit; cursor: pointer; }
.sort:hover { color: var(--fabric-ink); }
.sort:focus-visible { outline: none; box-shadow: var(--fabric-focus); border-radius: 4px; }
.idle { opacity: .35; }
tr.clickable { cursor: pointer; }
tr.clickable:hover td { background: color-mix(in srgb, var(--fabric-accent) 5%, transparent); }
tr.clickable:focus-visible { outline: none; }
tr.clickable:focus-visible td { background: var(--fabric-accent-soft); }
.empty { padding: var(--fabric-space-6) var(--fabric-space-4); text-align: center; color: var(--fabric-muted); font-size: var(--fabric-text-sm); }
</style>
