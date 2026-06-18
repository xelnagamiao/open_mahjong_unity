<template>
  <el-menu
    :default-active="activeMenu"
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
          <img class="logo-img" src="/logo.svg" width="32" height="32" alt="OpenMahjong" />
          <span class="logo-text">salasasa.cn</span>
        </el-menu-item>

        <el-menu-item index="/game-unity">
          <el-icon><VideoPlay /></el-icon>
          <span>对战平台</span>
        </el-menu-item>

        <el-menu-item index="/mobile-download">
          <el-icon><Iphone /></el-icon>
          <span>手机版</span>
        </el-menu-item>

        <el-menu-item index="/github">
          <el-icon><Link /></el-icon>
          <span>GitHub</span>
        </el-menu-item>

        <el-menu-item index="/player-data">
          <el-icon><User /></el-icon>
          <span>玩家数据</span>
        </el-menu-item>

        <el-menu-item index="/paili">
          <el-icon><Histogram /></el-icon>
          <span>牌理</span>
        </el-menu-item>

        <el-menu-item index="/chinese">
          <el-icon><Operation /></el-icon>
          <span>国标计算器</span>
        </el-menu-item>

        <el-menu-item index="/rulebook">
          <el-icon><Reading /></el-icon>
          <span>规则书</span>
        </el-menu-item>

        <el-menu-item index="/seed-verify">
          <el-icon><Key /></el-icon>
          <span>种子验证</span>
        </el-menu-item>

        <el-menu-item index="/docs">
          <el-icon><Document /></el-icon>
          <span>开发手册</span>
        </el-menu-item>
      </div>
    </div>
  </el-menu>
</template>

<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import {
  VideoPlay,
  User,
  Histogram,
  Operation,
  Reading,
  Document,
  Link,
  Key,
  Iphone,
} from '@element-plus/icons-vue'

const route = useRoute()

const activeMenu = computed(() => {
  const p = route.path || '/'
  if (p === '/' || p === '') return '/'
  if (p.startsWith('/rulebook')) return '/rulebook'
  if (p === '/github') return '/github'
  if (p === '/mobile-download') return '/mobile-download'
  return p
})
</script>

<style scoped>
.nav-menu {
  border-bottom: none;
}

.nav-container {
  display: flex;
  justify-content: flex-start;
  align-items: center;
  max-width: 1200px;
  margin: 0 auto;
  padding: 0 20px;
}

.nav-left {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
}

.logo {
  font-size: 18px;
  font-weight: bold;
  display: inline-flex;
  align-items: center;
}

.logo-img {
  margin-right: 10px;
  vertical-align: middle;
  border-radius: 6px;
  flex-shrink: 0;
}

.logo-text {
  line-height: 1;
}

.el-menu-item .el-icon {
  margin-right: 4px;
}
</style>
