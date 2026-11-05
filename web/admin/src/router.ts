import { createRouter, createWebHistory } from 'vue-router'
import LogView from './views/LogView.vue'
import OverviewView from './views/OverviewView.vue'
import PostView from './views/PostView.vue'
import PostsView from './views/PostsView.vue'
import ReportsView from './views/ReportsView.vue'
import ReviewView from './views/ReviewView.vue'
import StudioView from './views/StudioView.vue'
import SystemView from './views/SystemView.vue'
import UserView from './views/UserView.vue'
import UsersView from './views/UsersView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/overview' },
    { path: '/overview', name: 'overview', component: OverviewView },
    { path: '/review', name: 'review', component: ReviewView },
    { path: '/reports', name: 'reports', component: ReportsView },
    { path: '/log', name: 'log', component: LogView },
    { path: '/users', name: 'users', component: UsersView },
    { path: '/users/:externalUserId', name: 'user', component: UserView, props: true },
    { path: '/posts', name: 'posts', component: PostsView },
    { path: '/posts/:postId', name: 'post', component: PostView, props: true },
    // Ranking profiles: /studio opens the default one
    { path: '/studio/:key?', name: 'studio', component: StudioView },
    { path: '/system', name: 'system', component: SystemView },
    { path: '/:pathMatch(.*)*', redirect: '/overview' },
  ],
  scrollBehavior: (_to, _from, saved) => saved ?? { top: 0 },
})
