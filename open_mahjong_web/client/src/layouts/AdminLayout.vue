<template>
  <el-container class="admin-layout">
    <el-aside :width="asideWidth" class="admin-aside">
      <div class="admin-brand">OMU管理后台</div>
      <el-menu :default-active="activeMenu" router class="admin-menu">
        <el-menu-item
          v-for="item in menuItems"
          :key="item.index"
          :index="item.index"
        >{{ item.label }}</el-menu-item>
      </el-menu>
    </el-aside>
    <el-container class="admin-body">
      <el-header class="admin-header">
        <span class="admin-user">{{ auth.username }} ({{ auth.userId }})</span>
        <el-button type="danger" link @click="onLogout">退出</el-button>
      </el-header>
      <el-main class="admin-main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminAuthStore } from '@/stores/adminAuth'
import { useMobile } from '@/composables/useMobile'
import { usePanelViewportLock } from '@/composables/usePanelViewportLock'

const route = useRoute()
const router = useRouter()
const auth = useAdminAuthStore()
const { isMobile } = useMobile()
usePanelViewportLock()

const asideWidth = computed(() => (isMobile.value ? '132px' : '196px'))

const menuItems = [
  { index: '/admin', label: '仪表盘' },
  { index: '/admin/users', label: '用户' },
  { index: '/admin/events', label: '赛事' },
  { index: '/admin/event-applications', label: '办赛申请' },
  { index: '/admin/rank', label: '段位' },
  { index: '/admin/games', label: '对局记录' },
  { index: '/admin/game-control', label: '对局管理' },
  { index: '/admin/audit', label: '审计' },
  { index: '/admin/messages', label: '消息' },
  { index: '/admin/send-email', label: '发送邮件' },
  { index: '/admin/ip-bans', label: 'IP 封禁' },
  { index: '/admin/stats', label: '全站统计' },
]

const activeMenu = computed(() => {
  if (route.path.startsWith('/admin/users')) return '/admin/users'
  if (route.path.startsWith('/admin/event-applications')) return '/admin/event-applications'
  if (route.path.startsWith('/admin/events')) return '/admin/events'
  if (route.path.startsWith('/admin/ip-bans')) return '/admin/ip-bans'
  return route.path
})

function onLogout() {
  auth.logout()
  router.push('/admin/login')
}
</script>

<style scoped>
.admin-layout {
  min-height: 100vh;
  height: 100vh;
  height: 100dvh;
  width: 100%;
  max-width: 100%;
  overflow: hidden;
  background: #f5f7fa;
}
.admin-aside {
  background: #ffffff;
  color: #303133;
  border-right: 1px solid #e4e7ed;
  flex-shrink: 0;
  overflow-x: hidden;
  overflow-y: auto;
}
.admin-brand {
  padding: 20px 16px;
  font-weight: 600;
  font-size: 16px;
  color: #303133;
  border-bottom: 1px solid #e4e7ed;
}
.admin-aside :deep(.admin-menu.el-menu) {
  border-right: none;
  background: #ffffff;
}
.admin-aside :deep(.el-menu-item) {
  color: #303133;
}
.admin-aside :deep(.el-menu-item.is-active) {
  color: #409eff;
  background-color: #ecf5ff;
}
.admin-aside :deep(.el-menu-item:hover) {
  color: #409eff;
  background-color: #f5f7fa;
}
.admin-body {
  min-width: 0;
  flex: 1;
  overflow: hidden;
  max-width: 100%;
}
.admin-header {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
  padding: 0 16px;
  height: 56px;
  flex-shrink: 0;
}
.admin-main {
  padding: 20px;
  min-width: 0;
  max-width: 100%;
  overflow-x: auto;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}
.admin-user {
  color: #606266;
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
@media (max-width: 768px) {
  .admin-brand {
    padding: 14px 10px;
    font-size: 14px;
  }
  .admin-aside :deep(.el-menu-item) {
    padding-left: 12px !important;
    height: 44px;
    line-height: 44px;
    font-size: 13px;
  }
  .admin-main {
    padding: 10px;
  }
  .admin-user {
    max-width: 120px;
  }
}
</style>
