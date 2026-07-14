<template>
  <nav class="topnav">
    <template v-for="item in items" :key="item.label + (item.to || item.href)">
      <a
        v-if="item.href"
        class="nav-link"
        :href="item.href"
        target="_blank"
        rel="noopener noreferrer"
      >{{ item.label }}</a>
      <router-link
        v-else
        :to="item.to"
        class="nav-link"
        :class="{ on: isActive(item) }"
      >{{ item.label }}</router-link>
    </template>
    <div class="nav-spacer" />
    <router-link
      v-if="isLoggedIn"
      to="/account"
      class="nav-link auth"
      :class="{ on: route.path === '/account' }"
    >{{ displayName }}</router-link>
    <router-link
      v-else
      to="/login?redirect=/account"
      class="nav-link auth"
      :class="{ on: route.path === '/login' }"
    >登录</router-link>
  </nav>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'

const route = useRoute()
const auth = usePlayerAuthStore()
const { username, userId, loaded } = storeToRefs(auth)
const isLoggedIn = computed(() => auth.isLoggedIn)
const displayName = computed(() => username.value || (userId.value != null ? `用户${userId.value}` : '账户'))

const STEAM_STORE_URL = 'https://store.steampowered.com/app/4565740/Salasasa/'

const items = [
  { to: '/', label: '首页', match: (p) => p === '/' || p === '' },
  { to: '/events', label: '比赛', match: (p) => p.startsWith('/events') },
  { to: '/game-unity', label: '进入平台', match: (p) => p.startsWith('/game-unity') },
  { href: STEAM_STORE_URL, label: 'Steam商店' },
  { to: '/player-data', label: '历史记录', match: (p) => p === '/player-data' || p === '/player-data/' },
  { to: '/player-data/platform', label: '数据统计', match: (p) => p.includes('/platform') },
  { to: '/paili', label: '牌理' },
  { to: '/chinese', label: '国标计算器' },
  { to: '/rulebook', label: '规则书', match: (p) => p.startsWith('/rulebook') },
  { to: '/seed-verify', label: '种子验证' },
  { to: '/mobile-download', label: '手机版' },
  { href: 'https://qm.qq.com/q/MGGZV58hOO', label: '加入QQ群' },
  { to: '/docs', label: '开发手册' },
  { to: '/github', label: 'GitHub' },
]

const isActive = (item) => {
  if (item.href) return false
  const p = route.path || '/'
  if (item.match) return item.match(p)
  return p === item.to
}

onMounted(() => {
  if (!loaded.value) auth.fetchMe()
})
</script>

<style scoped>
.topnav {
  background: #1a1a1a;
  color: #ddd;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  padding: 0 20px;
  min-height: 54px;
  position: sticky;
  top: 0;
  z-index: 1000;
}

.nav-spacer {
  flex: 1;
  min-width: 8px;
}

.nav-link {
  color: #bbb;
  padding: 14px 14px;
  font-size: 15px;
  text-decoration: none;
  white-space: nowrap;
  cursor: pointer;
}

.nav-link:hover,
.nav-link.on {
  color: #fff;
  background: #2a2a2a;
}

.nav-link.auth {
  font-weight: 600;
  color: #9cf;
}
</style>
