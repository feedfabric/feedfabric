<script setup lang="ts">
import FabricButton from './FabricButton.vue'
import FabricDialog from './FabricDialog.vue'

// "Are you sure?" before something that's hard to undo. The safe choice has focus when it opens, so Enter never
// confirms by accident. busy keeps it open with a spinner while the action runs.
withDefaults(defineProps<{ title: string; message?: string; confirmLabel?: string; cancelLabel?: string; tone?: 'danger' | 'primary'; busy?: boolean }>(),
  { confirmLabel: 'Confirm', cancelLabel: 'Cancel', tone: 'danger' })
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ confirm: []; cancel: [] }>()

function cancel() {
  open.value = false
  emit('cancel')
}
</script>

<template>
  <FabricDialog v-model:open="open" :title="title" size="sm" :persistent="busy">
    <p v-if="message" class="message">{{ message }}</p>
    <slot />
    <template #footer>
      <FabricButton autofocus :disabled="busy" @click="cancel">{{ cancelLabel }}</FabricButton>
      <FabricButton :variant="tone" :loading="busy" @click="emit('confirm')">{{ confirmLabel }}</FabricButton>
    </template>
  </FabricDialog>
</template>

<style scoped>
.message { margin: 0; color: var(--fabric-muted); }
</style>
