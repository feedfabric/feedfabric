import { createRouter, createWebHistory } from 'vue-router'
import LiveView from './views/LiveView.vue'
import HomeView from './views/HomeView.vue'
import ProfileView from './views/ProfileView.vue'
import ProfilePosts from './views/ProfilePosts.vue'
import ProfileUsers from './views/ProfileUsers.vue'
import PostView from './views/PostView.vue'
import NotFoundView from './views/NotFoundView.vue'
import TagView from './views/TagView.vue'
import DashboardView from './views/DashboardView.vue'
import MentionsView from './views/MentionsView.vue'
import ByUsernameView from './views/ByUsernameView.vue'
import StudioView from './views/StudioView.vue'

// Keep the top-level paths in step with the fallback routes in src/FeedFabric.Api/Program.cs
export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/live' },
    { path: '/live', name: 'live', component: LiveView },
    { path: '/home', name: 'home', component: HomeView },
    {
      path: '/u/:externalUserId',
      component: ProfileView,
      props: true,
      children: [
        { path: '', name: 'profile', component: ProfilePosts },
        { path: 'followers', name: 'followers', component: ProfileUsers, props: { kind: 'followers' } },
        { path: 'following', name: 'following', component: ProfileUsers, props: { kind: 'following' } },
      ],
    },
    { path: '/post/:postId', name: 'post', component: PostView, props: true },
    { path: '/tags/:tag', name: 'tag', component: TagView, props: true },
    { path: '/mentions', name: 'mentions', component: MentionsView },
    { path: '/dashboard', name: 'dashboard', component: DashboardView },
    // Ranking profiles: /studio opens the one For You uses here
    { path: '/studio/:key?', name: 'studio', component: StudioView },
    // @mention links: resolves the username, then replaces the URL with the profile's
    { path: '/by/:username', name: 'by-username', component: ByUsernameView, props: true },
    { path: '/:pathMatch(.*)*', name: 'not-found', component: NotFoundView },
  ],
  // Back / Forward return to where you were (the feeds are cached, so the posts are still there); anything else starts at the top
  scrollBehavior: (_to, _from, saved) => saved ?? { top: 0 },
})
