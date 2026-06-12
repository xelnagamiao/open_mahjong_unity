<template>
  <el-container class="admin-layout">
    <el-aside width="220px" class="admin-aside">
      <div class="admin-brand">OMU管理后台</div>
      <el-menu :default-active="activeMenu" router class="admin-menu">
        <el-menu-item index="/admin">仪表盘</el-menu-item>
        <el-menu-item index="/admin/users">用户</el-menu-item>
        <el-menu-item index="/admin/rank">段位</el-menu-item>
        <el-menu-item index="/admin/games">对局</el-menu-item>
        <el-menu-item index="/admin/audit">审计</el-menu-item>
        <el-menu-item index="/admin/messages">消息</el-menu-item>
      </el-menu>
    </el-aside>
    <el-container>
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

const route = useRoute()
const router = useRouter()
const auth = useAdminAuthStore()

const activeMenu = computed(() => {
  if (route.path.startsWith('/admin/users')) return '/admin/users'
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
  background: #f5f7fa;
}
.admin-aside {
  background: #ffffff;
  color: #303133;
  border-right: 1px solid #e4e7ed;
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
.admin-header {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
}
.admin-main {
  padding: 20px;
}
.admin-user {
  color: #606266;
  font-size: 14px;
}
</style>
