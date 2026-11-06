import { createApp } from 'vue'
import App from './App.vue'
import '@feedfabric/fabric-ui/styles.css'
import { router } from './router'
import './style.css'

createApp(App).use(router).mount('#app')
