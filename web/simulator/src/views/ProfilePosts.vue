<script setup lang="ts">
import { watch } from 'vue'
import { useLiveFeed } from '../composables/useLiveFeed'
import type { CardPost, Profile } from '../types'
import NewPostsButton from '../components/NewPostsButton.vue'
import PostList from '../components/PostList.vue'

const props = defineProps<{ profile: Profile }>()
const feed = useLiveFeed<CardPost>()

// The profile page refreshes the profile; new posts in it wait behind the button like any feed
watch(() => props.profile.externalUserId, () => feed.reset())
watch(() => props.profile.posts, posts => feed.update(posts), { immediate: true })
</script>

<template>
  <div class="stack">
    <NewPostsButton :count="feed.waiting.value.length" @show="feed.showNew" />
    <PostList :posts="feed.shown.value" empty="No posts yet." />
  </div>
</template>
