<template>
  <el-menu
    :default-active="activeIndex"
    class="nav-menu"
    mode="horizontal"
    router
    background-color="#545c64"
    text-color="#fff"
    active-text-color="#ffd04b"
  >
    <div class="nav-container">
      <div class="nav-left">
        <el-menu-item index="/" class="logo">
          <el-icon><Grid /></el-icon>
          <span>Mahjong.fit</span>
        </el-menu-item>
        
        <el-menu-item index="/shanten">
          <el-icon><DataAnalysis /></el-icon>
          <span>听牌判断</span>
        </el-menu-item>
        
        <el-menu-item index="/chinese">
          <el-icon><Trophy /></el-icon>
          <span>国标麻将</span>
        </el-menu-item>
        
        <el-menu-item index="/riichi">
          <el-icon><Star /></el-icon>
          <span>立直麻将</span>
        </el-menu-item>
      </div>
      
      <div class="nav-right">
        <template v-if="!isLoggedIn">
          <el-menu-item index="/login">
            <el-icon><User /></el-icon>
            <span>登录</span>
          </el-menu-item>
          <el-menu-item index="/register">
            <el-icon><UserFilled /></el-icon>
            <span>注册</span>
          </el-menu-item>
        </template>
        
        <template v-else>
          <el-sub-menu index="user">
            <template #title>
              <el-icon><User /></el-icon>
              <span>{{ username }}</span>
            </template>
            <el-menu-item @click="logout">
              <el-icon><SwitchButton /></el-icon>
              <span>退出登录</span>
            </el-menu-item>
          </el-sub-menu>
        </template>
      </div>
    </div>
  </el-menu>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import {
  Grid,
  DataAnalysis,
  Trophy,
  Star,
  User,
  UserFilled,
  SwitchButton
} from '@element-plus/icons-vue'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const activeIndex = computed(() => route.path)
const isLoggedIn = computed(() => authStore.isLoggedIn)
const username = computed(() => authStore.user?.username || '')

const logout = () => {
  authStore.logout()
  router.push('/')
}
</script>

<style scoped>
.nav-menu {
  border-bottom: none;
}

.nav-container {
  display: flex;
  justify-content: space-between;
  align-items: center;
  max-width: 1200px;
  margin: 0 auto;
  padding: 0 20px;
}

.nav-left {
  display: flex;
  align-items: center;
}

.nav-right {
  display: flex;
  align-items: center;
}

.logo {
  font-size: 18px;
  font-weight: bold;
}

.logo .el-icon {
  margin-right: 8px;
  font-size: 20px;
}

.el-menu-item .el-icon {
  margin-right: 4px;
}
</style> 