<!-- 顶部导航栏 -->
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
          <span>salasasa.cn</span>
        </el-menu-item>

        <el-menu-item index="/game-unity">
          <el-icon><VideoPlay /></el-icon>
          <span>麻将对战平台</span>
        </el-menu-item>

        <el-menu-item index="/player-data">
          <el-icon><User /></el-icon>
          <span>玩家数据</span>
        </el-menu-item>

        <el-menu-item index="/shanten">
          <el-icon><DataAnalysis /></el-icon>
          <span>听牌判断</span>
        </el-menu-item>

        <el-menu-item index="/chinese">
          <el-icon><Trophy /></el-icon>
          <span>国标解算</span>
        </el-menu-item>

        <el-menu-item index="/docs">
          <el-icon><Document /></el-icon>
          <span>开发手册</span>
        </el-menu-item>

        <el-menu-item index="/github">
          <el-icon><Link /></el-icon>
          <span>GitHub</span>
        </el-menu-item>
      </div>

      <div class="nav-right">
        <!-- 登录和注册功能暂不提供 -->
      </div>
    </div>
  </el-menu>
</template>

<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import {
  Grid,
  DataAnalysis,
  Trophy,
  User,
  Link,
  Document,
  VideoPlay
} from '@element-plus/icons-vue'

const route = useRoute()
const activeIndex = computed(() => route.path)
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
