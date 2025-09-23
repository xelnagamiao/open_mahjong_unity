<template>

  <div id="app">
    <el-container>

      <el-header>
        <!-- 导航栏 -->
        <nav-bar />
      </el-header>

      <el-main>
        <!-- 路由视图 -->
        <router-view />
      </el-main>

      <el-footer>
        <!-- 底部 -->
        <app-footer />
      </el-footer>

    </el-container>
  </div>

</template>

<script setup>
import NavBar from '@/components/NavBar.vue' // 导入导航栏组件
import AppFooter from '@/components/AppFooter.vue' // 导入底部组件
</script>

<style scoped>
#app {
  min-height: 100vh; /* 最小高度100vh */
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); /* 背景渐变 */
}

.el-container {
  min-height: 100vh; /* 最小高度100vh */
}

.el-header {
  padding: 0; /* 无内边距 */
  height: auto; /* 高度自动 */
}

.el-main {
  padding: 20px; /* 内边距20px */
  flex: 1; /* 弹性布局 1 */
}

.el-footer {
  padding: 0; /* 无内边距 */
  height: auto; /* 高度自动 */
}
</style> 