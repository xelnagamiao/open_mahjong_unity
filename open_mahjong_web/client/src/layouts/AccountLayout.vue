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
          >{{ item.label }}</el-menu-item>
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
          <el-button type="primary" link @click="$router.push('/login?redirect=/account')">去登录</el-button>
        </template>
      </el-header>
      <el-main class="account-main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import { useMobile } from '@/composables/useMobile'
import { usePanelViewportLock } from '@/composables/usePanelViewportLock'

const route = useRoute()
const router = useRouter()
const auth = usePlayerAuthStore()
const eventAuth = useEventAdminAuthStore()
const { isMobile } = useMobile()
usePanelViewportLock()

const asideWidth = computed(() => (isMobile.value ? '132px' : '196px'))

const menuItems = [
  { index: 'sec-account', label: '账户' },
  { index: 'sec-apply', label: '办赛申请' },
  { index: 'sec-manage', label: '赛事管理' },
]

const activeMenu = computed(() => {
  const hash = (route.hash || '').replace(/^#/, '')
  if (hash === 'sec-apply' || hash === 'sec-manage' || hash === 'sec-account') return hash
  return 'sec-account'
})

function onSelect(index) {
  router.replace({ path: '/account', hash: `#${index}` })
}

function onLogout() {
  auth.logout()
  eventAuth.logout()
  router.push('/login?redirect=/account')
}

onMounted(async () => {
  if (!auth.loaded) await auth.fetchMe()
})
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
  font-weight: 600;
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
</style>
