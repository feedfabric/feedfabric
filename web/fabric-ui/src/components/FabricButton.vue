<script setup lang="ts">
import { computed, resolveDynamicComponent } from 'vue'
import FabricIcon, { type IconName } from './FabricIcon.vue'
import FabricSpinner from './FabricSpinner.vue'

// A button, or a link that looks like one when given href, or to (an in-app route, through <RouterLink> when Vue
// Router is installed). While loading it keeps its width, shows a spinner and
// can't be pressed again. An icon-only button needs a label, which becomes its accessible name and tooltip.
const props = withDefaults(defineProps<{
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'good'
  size?: 'sm' | 'md' | 'lg'
  type?: 'button' | 'submit' | 'reset'
  icon?: IconName
  iconAfter?: IconName
  label?: string
  href?: string
  to?: string | Record<string, unknown>
  target?: string
  loading?: boolean
  disabled?: boolean
  block?: boolean
}>(), { variant: 'secondary', size: 'md', type: 'button' })

const iconOnly = computed(() => !!props.icon && !!props.label)
const inactive = computed(() => props.disabled || props.loading)
const iconSize = computed(() => (props.size === 'sm' ? 14 : props.size === 'lg' ? 20 : 16))

const routerLink = resolveDynamicComponent('RouterLink')
const link = computed(() => {
  if (inactive.value) return null
  if (props.to != null) {
    return typeof routerLink !== 'string' ? { is: routerLink, bind: { to: props.to } } : { is: 'a', bind: { href: typeof props.to === 'string' ? props.to : '#' } }
  }
  return props.href ? { is: 'a', bind: { href: props.href, target: props.target, rel: props.target === '_blank' ? 'noopener' : undefined } } : null
})
</script>

<template>
  <component :is="link.is" v-if="link" v-bind="link.bind" class="fabric-button" :class="[variant, size, { block, 'icon-only': iconOnly }]"
    :aria-label="iconOnly ? label : undefined" :title="iconOnly ? label : undefined">
    <FabricIcon v-if="icon" :name="icon" :size="iconSize" />
    <span v-if="!iconOnly" class="text"><slot /></span>
    <FabricIcon v-if="iconAfter" :name="iconAfter" :size="iconSize" />
  </component>
  <button v-else class="fabric-button" :class="[variant, size, { block, loading, 'icon-only': iconOnly }]" :type="type"
    :disabled="inactive" :aria-busy="loading || undefined" :aria-label="iconOnly ? label : undefined" :title="iconOnly ? label : undefined">
    <FabricSpinner v-if="loading" class="spinner" :size="iconSize" />
    <span class="content">
      <FabricIcon v-if="icon" :name="icon" :size="iconSize" />
      <span v-if="!iconOnly" class="text"><slot /></span>
      <FabricIcon v-if="iconAfter" :name="iconAfter" :size="iconSize" />
    </span>
  </button>
</template>

<style scoped>
.fabric-button {
  --h: 34px; --px: 14px;
  position: relative; display: inline-flex; align-items: center; justify-content: center; gap: 6px;
  min-height: var(--h); padding: 0 var(--px); border-radius: var(--fabric-radius-md);
  border: 1px solid var(--fabric-line); background: var(--fabric-surface); color: var(--fabric-ink);
  font: 500 var(--fabric-text-sm)/1 var(--fabric-font); text-decoration: none; white-space: nowrap; cursor: pointer;
  transition: background var(--fabric-fast) var(--fabric-ease), border-color var(--fabric-fast), color var(--fabric-fast), box-shadow var(--fabric-fast);
}
.content { display: inline-flex; align-items: center; gap: 6px; }
.fabric-button:focus-visible { outline: none; box-shadow: var(--fabric-focus); }
.fabric-button:disabled { cursor: default; }
.fabric-button:disabled:not(.loading) { opacity: .5; }
.loading .content { visibility: hidden; }
.spinner { position: absolute; }
.block { display: flex; width: 100%; }

.sm { --h: 28px; --px: 10px; font-size: var(--fabric-text-xs); }
.lg { --h: 42px; --px: 18px; font-size: var(--fabric-text-md); }
.icon-only { --px: 0; width: var(--h); }

.secondary:hover:not(:disabled) { border-color: var(--fabric-line-strong); background: var(--fabric-sunken); }
.primary { background: var(--fabric-accent); border-color: var(--fabric-accent); color: var(--fabric-on-accent); }
.primary:hover:not(:disabled) { background: var(--fabric-accent-hover); border-color: var(--fabric-accent-hover); }
.ghost { background: transparent; border-color: transparent; color: var(--fabric-muted); }
.ghost:hover:not(:disabled) { background: var(--fabric-accent-soft); color: var(--fabric-accent); }
.danger { color: var(--fabric-bad); }
.danger:hover:not(:disabled) { background: var(--fabric-bad-soft); border-color: var(--fabric-bad); }
.good { color: var(--fabric-good); }
.good:hover:not(:disabled) { background: var(--fabric-good-soft); border-color: var(--fabric-good); }
</style>
