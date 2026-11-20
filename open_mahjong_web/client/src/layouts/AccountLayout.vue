<template>
  <el-container class="account-layout">
    <el-aside :width="asideWidth" class="account-aside">
      <div class="account-brand">账户面板</div>
      <div class="aside-body">
        <el-menu :key="activeMenu" :default-active="activeMenu" class="account-menu" @select="onSelect">
          <el-menu-item
            v-for="item in menuItems"
            :key="item.index"
            :index="item.index"
          ><span>{{ item.label }}</span><span v-if="item.index === 'sec-mail' && assets.unread" class="mail-badge">{{ assets.unread > 99 ? '99+' : assets.unread }}</span></el-menu-item>
        </el-menu>
        <div class="aside-footer">
          <el-button class="home-btn" @click="router.push('/')">返回主站</el-button>
        </div>
      </div>
    </el-aside>
    <el-container class="account-body">
      <el-header class="account-header">
        <template v-if="auth.isLoggedIn">
          <span class="account-user">{{ auth.username }} ({{ auth.userId }})</span>
          <el-button type="danger" link @click="onLogout">退出</el-button>
        </template>
        <template v-else>
          <el-button type="primary" link @click="$router.push({ path: '/login', query: { redirect: route.fullPath } })">去登录</el-button>
        </template>
      </el-header>
      <el-main class="account-main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { usePlayerAssetsStore } from '@/stores/playerAssets'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import { useMobile } from '@/composables/useMobile'
import { usePanelViewportLock } from '@/composables/usePanelViewportLock'

const route = useRoute()
const router = useRouter()
const auth = usePlayerAuthStore()
const assets = usePlayerAssetsStore()
const eventAuth = useEventAdminAuthStore()
const { isMobile } = useMobile()
usePanelViewportLock()

const asideWidth = computed(() => (isMobile.value ? '132px' : '196px'))

const menuItems = [
  { index: 'sec-profile', label: '个人资料' },
  { index: 'sec-inventory', label: '物品栏' },
  { index: 'sec-mail', label: '邮件' },
  { index: 'sec-account', label: '账户设置' },
  { index: 'sec-redeem', label: '兑换码' },
  { index: 'sec-uploads', label: '上传内容' },
  { index: 'sec-duplicate-personal', label: '复式管理' },
  { index: 'sec-manage-event', label: '赛事与基地' },
]

const LEGACY_HASH = {
  'sec-apply': 'sec-manage-event',
  'sec-apply-event': 'sec-manage-event',
  'sec-apply-base': 'sec-manage-event',
  'sec-manage-base': 'sec-manage-event',
  'sec-duplicate-event': 'sec-duplicate-personal',
  'sec-manage': 'sec-manage-event',
}

const activeMenu = computed(() => {
  const hash = (route.hash || '').replace(/^#/, '')
  const mapped = LEGACY_HASH[hash] || hash
  if (menuItems.some((item) => item.index === mapped)) return mapped
  return 'sec-profile'
})

function onSelect(index) {
  router.replace({ path: '/account', hash: `#${index}` })
}

function onLogout() {
  auth.logout()
  eventAuth.logout()
  router.replace('/')
}

function refreshMail() { if (auth.isLoggedIn && !document.hidden) assets.refreshSummary().catch(() => {}) }
watch(() => auth.userId, () => { assets.syncOwner(); refreshMail() }, { immediate: true })
let summaryTimer
onMounted(async () => {
  summaryTimer = setInterval(refreshMail, 30000)
  window.addEventListener('focus', refreshMail)
  document.addEventListener('visibilitychange', refreshMail)
  if (!auth.loaded) await auth.fetchMe()
  refreshMail()
})
onBeforeUnmount(() => { clearInterval(summaryTimer); window.removeEventListener('focus', refreshMail); document.removeEventListener('visibilitychange', refreshMail) })
</script>

<style scoped>
.account-layout {
  min-height: 100vh;
  height: 100vh;
  height: 100dvh;
  width: 100%;
  max-width: 100%;
  overflow: hidden;
  background: #f5f7fa;
}
.account-aside {
  background: #ffffff;
  color: #303133;
  border-right: 1px solid #e4e7ed;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  overflow-x: hidden;
  overflow-y: auto;
}
.account-brand {
  padding: 20px 16px;
  font-weight: 400;
  font-size: 16px;
  color: #303133;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
}
.aside-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.account-aside :deep(.account-menu.el-menu) {
  border-right: none;
  background: #ffffff;
  flex: 1;
}
.account-aside :deep(.el-menu-item) {
  color: #303133;
}
.account-aside :deep(.el-menu-item.is-active) {
  color: #409eff;
  background-color: #ecf5ff;
}
.account-aside :deep(.el-menu-item:hover) {
  color: #409eff;
  background-color: #f5f7fa;
}
.aside-footer {
  padding: 12px 16px 20px;
  border-top: 1px solid #e4e7ed;
}
.home-btn {
  width: 100%;
}
.account-body {
  min-width: 0;
  flex: 1;
  overflow: hidden;
  max-width: 100%;
}
.account-header {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  padding: 0 16px;
  height: 56px;
  flex-shrink: 0;
}
.account-user {
  color: #606266;
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.mail-badge { margin-left:10px; padding:0 6px; border-radius:10px; background:#e7efe7; color:#4a7458; font-size:11px; line-height:19px; }
.account-main {
  padding: 20px;
  min-width: 0;
  max-width: 100%;
  overflow-x: auto;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}
@media (max-width: 768px) {
  .account-brand {
    padding: 14px 10px;
    font-size: 14px;
  }
  .account-aside :deep(.el-menu-item) {
    padding-left: 12px !important;
    height: 44px;
    line-height: 44px;
    font-size: 13px;
  }
  .aside-footer {
    padding: 10px;
  }
  .account-main {
    padding: 10px;
  }
  .account-user {
    max-width: 120px;
  }
}
@media (max-width: 640px) {
  .account-layout { flex-direction: column; }
  .account-aside {
    width: 100% !important;
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    overflow: visible;
    border-right: 0;
    border-bottom: 1px solid #e4e7ed;
  }
  .account-brand { padding: 10px 12px; border-bottom: 0; }
  .aside-body { display: contents; }
  .account-aside :deep(.account-menu.el-menu) {
    grid-column: 1 / -1;
    grid-row: 2;
    display: flex;
    flex-wrap: wrap;
    padding: 4px 6px;
    border-top: 1px solid #e4e7ed;
  }
  .account-aside :deep(.el-menu-item) {
    height: 36px;
    line-height: 36px;
    padding: 0 10px !important;
  }
  .aside-footer { grid-column: 2; grid-row: 1; padding: 6px 12px; border-top: 0; }
  .home-btn { width: auto; height: 28px; }
  .account-body { min-height: 0; }
  .account-header { height: 42px; padding: 0 12px; }
}
</style>
