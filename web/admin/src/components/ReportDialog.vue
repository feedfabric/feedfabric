<script setup lang="ts">
import { ref, watch } from 'vue'
import { FabricButton, FabricDialog, FabricNotice, FabricSelect, FabricStack, useToast, type SelectOption } from '@feedfabric/fabric-ui'
import { api, describeError } from '../api'
import type { ReportReason } from '../types'

// Report a post or comment as the signed-in user, the way a member would. Reporting twice counts once; three
// different reporters hold it for review.
const props = defineProps<{ target: { postId: string } | { commentId: string } }>()
const open = defineModel<boolean>('open', { default: false })

const reasons: SelectOption<ReportReason>[] = [
  { value: 'spam', label: 'Spam' },
  { value: 'harassment', label: 'Harassment' },
  { value: 'hate', label: 'Hate' },
  { value: 'threat', label: 'Threat' },
  { value: 'sexual', label: 'Sexual content' },
  { value: 'self_harm', label: 'Self-harm' },
  { value: 'other', label: 'Something else' },
]
const reason = ref<ReportReason>('spam')
const sending = ref(false)
const error = ref<string | null>(null)
const { toast } = useToast()

watch(open, isOpen => {
  if (isOpen) error.value = null
})

async function send() {
  sending.value = true
  try {
    await api.report(props.target, reason.value)
    open.value = false
    toast({ title: 'Reported', message: 'Three different reporters hold it for review.', tone: 'good' })
  } catch (e) {
    error.value = describeError(e)
  } finally {
    sending.value = false
  }
}
</script>

<template>
  <FabricDialog v-model:open="open" :title="'commentId' in target ? 'Report comment' : 'Report post'" description="Reported as you, the way a member would." size="sm">
    <FabricStack :gap="3">
      <FabricSelect v-model="reason" :options="reasons" label="Reason" />
      <FabricNotice v-if="error" tone="bad">{{ error }}</FabricNotice>
      <FabricStack direction="row" justify="end" :gap="2">
        <FabricButton variant="ghost" @click="open = false">Cancel</FabricButton>
        <FabricButton variant="danger" icon="flag" :loading="sending" @click="send">Report</FabricButton>
      </FabricStack>
    </FabricStack>
  </FabricDialog>
</template>
