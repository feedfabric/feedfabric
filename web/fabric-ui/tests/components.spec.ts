import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  FabricButton, FabricDialog, FabricFileUpload, FabricInput, FabricMenu, FabricMeter, FabricSegmented, FabricSwitch, FabricTable, FabricTabs, FabricToaster,
  toast, type TableColumn,
} from '../src'
import { toasts } from '../src/composables/useToast'

describe('FabricButton', () => {
  it('is a link when given href, and a button otherwise', () => {
    expect(mount(FabricButton, { props: { href: '/posts' }, slots: { default: 'Posts' } }).element.tagName).toBe('A')
    expect(mount(FabricButton, { slots: { default: 'Save' } }).element.tagName).toBe('BUTTON')
  })

  it("can't be pressed while loading, and says it's busy", async () => {
    const wrapper = mount(FabricButton, { props: { loading: true }, slots: { default: 'Save' }, attrs: { onClick: vi.fn() } })
    expect(wrapper.attributes('disabled')).toBeDefined()
    expect(wrapper.attributes('aria-busy')).toBe('true')
  })

  it('names an icon-only button by its label', () => {
    const wrapper = mount(FabricButton, { props: { icon: 'edit', label: 'Edit post' } })
    expect(wrapper.attributes('aria-label')).toBe('Edit post')
    expect(wrapper.text()).toBe('')
  })
})

describe('FabricInput', () => {
  it('ties its label, hint and error to the input', () => {
    const wrapper = mount(FabricInput, { props: { label: 'Email', hint: 'For receipts', error: 'Not an email' } })
    const input = wrapper.get('input')
    expect(wrapper.get('label').attributes('for')).toBe(input.attributes('id'))
    expect(input.attributes('aria-invalid')).toBe('true')
    const described = input.attributes('aria-describedby')!.split(' ')
    expect(described.map(id => wrapper.get(`#${id}`).text())).toEqual(['Not an email', 'For receipts'])
  })

  it('clears with its clear button', async () => {
    const wrapper = mount(FabricInput, { props: { label: 'Search', clearable: true, modelValue: 'run', 'onUpdate:modelValue': (v: string) => wrapper.setProps({ modelValue: v }) } })
    await wrapper.get('button[aria-label="Clear"]').trigger('click')
    expect(wrapper.props('modelValue')).toBe('')
  })
})

describe('FabricSwitch', () => {
  it('is a switch that reports and toggles its state', async () => {
    const wrapper = mount(FabricSwitch, { props: { label: 'Notify', modelValue: false, 'onUpdate:modelValue': (v: boolean) => wrapper.setProps({ modelValue: v }) } })
    const button = wrapper.get('[role="switch"]')
    expect(button.attributes('aria-checked')).toBe('false')
    await button.trigger('click')
    expect(button.attributes('aria-checked')).toBe('true')
  })
})

describe('FabricSegmented', () => {
  const options = [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }, { value: 'c', label: 'C' }]

  it('moves with the arrow keys, wrapping round, and only the chosen one is in the tab order', async () => {
    // A v-model the test can watch: each update is fed back in as the new value
    const wrapper: VueWrapper<any> = mount(FabricSegmented, {
      props: { label: 'Pick', options, modelValue: 'a', 'onUpdate:modelValue': (v: string | number) => wrapper.setProps({ modelValue: v }) },
      attachTo: document.body,
    })
    const radios = () => wrapper.findAll('[role="radio"]')
    expect(radios().map(radio => radio.attributes('tabindex'))).toEqual(['0', '-1', '-1'])
    await radios()[0].trigger('keydown', { key: 'ArrowLeft' })
    expect(wrapper.props('modelValue')).toBe('c')
    await radios()[2].trigger('keydown', { key: 'Home' })
    expect(wrapper.props('modelValue')).toBe('a')
    expect(document.activeElement).toBe(radios()[0].element)
    wrapper.unmount()
  })
})

describe('FabricTabs', () => {
  it('links the panel to the selected tab', async () => {
    const tabs = [{ value: 'one', label: 'One' }, { value: 'two', label: 'Two' }]
    // A v-model the test can watch: each update is fed back in as the new value
    const wrapper: VueWrapper<any> = mount(FabricTabs, {
      props: { label: 'Sections', tabs, modelValue: 'two', 'onUpdate:modelValue': (v: string | number) => wrapper.setProps({ modelValue: v }) },
      slots: { panel: '<template #panel="{ value }">Panel {{ value }}</template>' },
    })
    const panel = wrapper.get('[role="tabpanel"]')
    expect(panel.text()).toBe('Panel two')
    expect(wrapper.get(`#${panel.attributes('aria-labelledby')}`).text()).toBe('Two')
    await wrapper.findAll('[role="tab"]')[1].trigger('keydown', { key: 'ArrowRight' })
    expect(wrapper.props('modelValue')).toBe('one')
  })
})

describe('FabricTable', () => {
  interface Row { id: number; name: string; posts: number }
  const columns: TableColumn<Row>[] = [{ key: 'name', label: 'Name', sortable: true }, { key: 'posts', label: 'Posts', align: 'right' }]
  const rows: Row[] = [{ id: 1, name: 'maya', posts: 3 }, { id: 2, name: 'sam', posts: 7 }]

  it('shows each cell, lets a slot draw one, and emits row-click on click and Enter', async () => {
    const onRowClick = vi.fn()
    const wrapper = mount(FabricTable<Row>, {
      props: { columns, rows, rowKey: (r: Row) => r.id, clickable: true, onRowClick },
      slots: { 'cell-name': '<template #cell-name="{ row }"><b>{{ row.name }}</b></template>' },
    })
    const cells = wrapper.findAll('tbody tr')[1].findAll('td')
    expect(cells[0].html()).toContain('<b>sam</b>')
    expect(cells[1].text()).toBe('7')
    await wrapper.findAll('tbody tr')[0].trigger('click')
    await wrapper.findAll('tbody tr')[1].trigger('keydown', { key: 'Enter' })
    expect(onRowClick.mock.calls.map(c => c[0].id)).toEqual([1, 2])
  })

  it('asks to sort the other way when the sorted column is pressed again', async () => {
    const onSort = vi.fn()
    const wrapper = mount(FabricTable<Row>, { props: { columns, rows, rowKey: (r: Row) => r.id, sortKey: 'name', sortDirection: 'asc', onSort } })
    expect(wrapper.get('th').attributes('aria-sort')).toBe('ascending')
    await wrapper.get('th button').trigger('click')
    expect(onSort).toHaveBeenCalledWith('name', 'desc')
  })

  it('says when there is nothing to show', () => {
    expect(mount(FabricTable<Row>, { props: { columns, rows: [], rowKey: (r: Row) => r.id } }).text()).toContain('Nothing to show.')
  })
})

describe('FabricFileUpload', () => {
  it('passes files that fit and turns the rest away with the reason', async () => {
    const onFiles = vi.fn()
    const onRejected = vi.fn()
    const wrapper = mount(FabricFileUpload, { props: { accept: 'image/jpeg,image/png', maxBytes: 10, maxFiles: 1, onFiles, onRejected } })
    const files = [
      new File(['12345'], 'ok.jpg', { type: 'image/jpeg' }),
      new File(['12345'], 'second.png', { type: 'image/png' }),
      new File(['12345678901'], 'big.jpg', { type: 'image/jpeg' }),
      new File(['x'], 'notes.txt', { type: 'text/plain' }),
    ]
    const input = wrapper.get('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: files })
    await input.trigger('change')
    expect(onFiles.mock.calls[0][0].map((f: File) => f.name)).toEqual(['ok.jpg'])
    expect(onRejected.mock.calls[0][0].map((r: { file: File; reason: string }) => `${r.file.name}:${r.reason}`))
      .toEqual(['second.png:count', 'big.jpg:size', 'notes.txt:type'])
  })
})

describe('FabricMenu', () => {
  it('opens on ArrowDown at the first enabled item, skips disabled ones, and Escape returns focus to the button', async () => {
    const chosen = vi.fn()
    const wrapper = mount(FabricMenu, {
      props: { label: 'Actions', items: [
        { label: 'Edit', onSelect: () => chosen('edit') },
        { label: 'Archive', disabled: true, onSelect: () => chosen('archive') },
        { label: 'Delete', onSelect: () => chosen('delete') },
      ] },
      attachTo: document.body,
    })
    const trigger = wrapper.get('button[aria-haspopup="menu"]')
    await trigger.trigger('keydown', { key: 'ArrowDown' })
    await nextTick()
    const items = wrapper.findAll('[role="menuitem"]')
    expect(document.activeElement).toBe(items[0].element)
    await wrapper.get('[role="menu"]').trigger('keydown', { key: 'ArrowDown' })
    expect(document.activeElement).toBe(items[2].element)
    await wrapper.get('[role="menu"]').trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[role="menu"]').exists()).toBe(false)
    expect(document.activeElement).toBe(trigger.element)
    expect(chosen).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})

describe('toasts', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    toasts.splice(0)
  })
  afterEach(() => vi.useRealTimers())

  it('go after their timeout, except problems, which stay until dismissed', async () => {
    const wrapper = mount(FabricToaster)
    toast({ title: 'Saved', tone: 'good' })
    toast({ title: 'Failed', tone: 'bad' })
    await nextTick()
    expect(wrapper.findAll('[role="status"]').length).toBe(1)
    expect(wrapper.findAll('[role="alert"]').length).toBe(1)
    vi.advanceTimersByTime(6000)
    await nextTick()
    expect(toasts.map(t => t.title)).toEqual(['Failed'])
    wrapper.unmount()
  })

  it('keep only the newest few', () => {
    for (let i = 0; i < 6; i++) toast({ title: `t${i}` })
    expect(toasts.map(t => t.title)).toEqual(['t2', 't3', 't4', 't5'])
  })

  it('never push out a problem to make room while something else can go', () => {
    toast({ title: 'Failed', tone: 'bad' })
    for (let i = 0; i < 6; i++) toast({ title: `t${i}` })
    expect(toasts.map(t => t.title)).toEqual(['Failed', 't3', 't4', 't5'])
  })
})

describe('FabricMeter', () => {
  it('takes the threshold tone at or past it, warns just under, and is fine well under', () => {
    const tone = (value: number) => mount(FabricMeter, { props: { label: 'Nudity', value, threshold: 0.8 } }).classes()
    expect(tone(0.85)).toContain('bad')
    expect(tone(0.7)).toContain('warn')
    expect(tone(0.2)).toContain('good')
  })
})

describe('FabricDialog', () => {
  it('opens when it mounts already open', async () => {
    const wrapper = mount(FabricDialog, { props: { title: 'Edit', open: true }, attachTo: document.body })
    await nextTick()
    expect(wrapper.get('dialog').attributes('open')).toBeDefined()
    wrapper.unmount()
  })
})

describe('FabricFileUpload hint', () => {
  it('states the size limit truthfully, small ones included', () => {
    const hint = (maxBytes: number) => mount(FabricFileUpload, { props: { maxBytes, multiple: false } }).text()
    expect(hint(20 * 1024 * 1024)).toContain('up to 20 MB')
    expect(hint(1.5 * 1024 * 1024)).toContain('up to 1.5 MB')
    expect(hint(300 * 1024)).toContain('up to 300 KB')
  })
})
