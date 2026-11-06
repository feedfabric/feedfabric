import type { IconName } from './components/FabricIcon.vue'

export type { IconName }

export type Tone = 'neutral' | 'accent' | 'good' | 'warn' | 'bad' | 'info'

export interface SelectOption<V> { value: V; label: string; disabled?: boolean }

// Becomes an <optgroup>
export interface SelectGroup<V> { label: string; options: SelectOption<V>[] }

export interface SegmentOption<V> { value: V; label: string; icon?: IconName; iconOnly?: boolean }

export interface TabItem<V> { value: V; label: string; icon?: IconName; badge?: string | number }

// align: right for numbers. width: any CSS width. sortable: the header becomes a button that emits sort.
export interface TableColumn<Row> {
  key: string
  label: string
  align?: 'left' | 'right' | 'center'
  width?: string
  sortable?: boolean
  value?: (row: Row) => unknown
}

export type SortDirection = 'asc' | 'desc'

export interface SidebarLink { label: string; to: string; icon?: IconName; badge?: string | number; exact?: boolean }

export interface SidebarSection { title?: string; links: SidebarLink[] }

export interface MenuAction { label: string; icon?: IconName; tone?: 'danger'; disabled?: boolean; onSelect: () => void }

// What FabricFileUpload hands back for each file it accepted or turned away
export interface FileRejection { file: File; reason: 'type' | 'size' | 'count' }
