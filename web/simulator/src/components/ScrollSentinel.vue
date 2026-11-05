<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { scrollRoot } from '../scrollRoot'

// Sits at the end of a feed and asks for more while it's within a couple of screens of view, so the next page
// is usually there before you reach it
const props = defineProps<{ active: boolean; loading: boolean }>()
const emit = defineEmits<{ more: [] }>()

const el = ref<HTMLElement>()
let near = false
let observer: IntersectionObserver | undefined

function ask() {
  if (near && props.active && !props.loading) emit('more')
}

onMounted(() => {
  observer = new IntersectionObserver(entries => {
    near = entries.some(e => e.isIntersecting)
    ask()
  }, { root: scrollRoot(), rootMargin: '0px 0px 1500px 0px' })
  if (el.value) observer.observe(el.value)
})
onUnmounted(() => observer?.disconnect())

// A page that landed without filling the screen: keep going
watch(() => props.loading, loading => !loading && ask())
</script>

<template>
  <div ref="el" aria-hidden="true" />
</template>
