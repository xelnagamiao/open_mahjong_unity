<template>
  <nav class="topnav">
    <div class="nav-bar-row">
      <button
        v-if="isMobile"
        type="button"
        class="nav-toggle"
        :aria-expanded="menuOpen"
        aria-label="打开导航菜单"
        @click="menuOpen = !menuOpen"
      >{{ menuOpen ? '关闭' : '菜单' }}</button>
      <div class="nav-links" :class="{ open: menuOpen || !isMobile }">
        <template v-for="item in items" :key="item.label + (item.to || item.href)">
          <a
            v-if="item.href"
            class="nav-link"
            :href="item.href"
            target="_blank"
            rel="noopener noreferrer"
            @click="closeMenu"
          >{{ item.label }}</a>
          <router-link
            v-else
            :to="item.to"
            class="nav-link"
            :class="{ on: isActive(item) }"
            @click="closeMenu"
          >{{ item.label }}</router-link>
        </template>
      </div>
      <div class="nav-spacer" />
      <router-link
        v-if="isLoggedIn"
        to="/account"
        class="nav-link auth"
        :class="{ on: route.path === '/account' }"
        @click="closeMenu"
      >{{ displayName }}</router-link>
      <router-link
        v-else
        to="/login?redirect=/"
        class="nav-link auth"
        :class="{ on: route.path === '/login' }"
        @click="closeMenu"
      >登录</router-link>
    </div>
  </nav>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { useMobile } from '@/composables/useMobile'

const route = useRoute()
const auth = usePlayerAuthStore()
const { username, userId, loaded } = storeToRefs(auth)
const isLoggedIn = computed(() => auth.isLoggedIn)
const displayName = computed(() => username.value || (userId.value != null ? `用户${userId.value}` : '账户'))
const { isMobile } = useMobile()
const menuOpen = ref(false)

const STEAM_STORE_URL = 'https://store.steampowered.com/app/4565740/Salasasa/'

const items = [
  { to: '/', label: '首页', match: (p) => p === '/' || p === '' },
  { to: '/events', label: '比赛', match: (p) => p.startsWith('/events') },
  { to: '/game-unity', label: '进入平台', match: (p) => p.startsWith('/game-unity') },
  { to: '/2d', label: '2D版', match: (p) => p === '/2d' || p.startsWith('/2d/') },
  { href: STEAM_STORE_URL, label: 'Steam商店' },
  { to: '/player-data', label: '历史记录', match: (p) => p === '/player-data' || p === '/player-data/' },
  { to: '/player-data/platform', label: '数据统计', match: (p) => p.includes('/platform') },
  { to: '/paili', label: '牌理' },
  { to: '/chinese', label: '国标计算器' },
  { to: '/rulebook', label: '规则书', match: (p) => p.startsWith('/rulebook') },
  { to: '/seed-verify', label: '种子验证' },
  { to: '/mobile-download', label: '手机版' },
  { to: '/guide', label: '使用说明', match: (p) => p.startsWith('/guide') },
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

function closeMenu() {
  menuOpen.value = false
}

watch(isMobile, (mobile) => {
  if (!mobile) menuOpen.value = false
})

watch(() => route.path, () => {
  menuOpen.value = false
})

onMounted(() => {
  if (!loaded.value) auth.fetchMe()
})
</script>

<style scoped>
.topnav {
  background: #1a1a1a;
  color: #ddd;
  padding: 0 20px;
  min-height: 54px;
  position: sticky;
  top: 0;
  z-index: 1000;
}

.nav-bar-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  min-height: 54px;
}

.nav-toggle {
  display: none;
  background: transparent;
  border: 1px solid #555;
  color: #ddd;
  padding: 8px 12px;
  font-size: 14px;
  font-family: inherit;
  cursor: pointer;
  margin: 8px 0;
}

.nav-toggle:hover {
  color: #fff;
  border-color: #888;
  background: #2a2a2a;
}

.nav-links {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
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

@media (max-width: 768px) {
  .topnav {
    padding: 0 12px;
  }

  .nav-toggle {
    display: inline-block;
  }

  .nav-links {
    display: none;
    width: 100%;
    order: 3;
    border-top: 1px solid #333;
    padding: 4px 0 8px;
    grid-template-columns: 1fr 1fr;
    gap: 0;
  }

  .nav-links.open {
    display: grid;
  }

  .nav-link {
    padding: 12px 10px;
    text-align: center;
    white-space: normal;
    line-height: 1.3;
  }

  .nav-spacer {
    display: none;
  }

  .nav-bar-row {
    justify-content: space-between;
  }

  .nav-link.auth {
    margin-left: auto;
  }
}
</style>
