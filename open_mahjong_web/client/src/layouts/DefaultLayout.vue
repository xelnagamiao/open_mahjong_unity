<template>
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
</template>

<script setup>
import NavBar from '@/components/NavBar.vue'
import AppFooter from '@/components/AppFooter.vue'
</script>

<style scoped>
.el-container {
  min-height: 100vh;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}

.el-header {
  padding: 0;
  height: auto;
}

.el-main {
  padding: 20px;
  flex: 1;
}

.el-footer {
  padding: 0;
  height: auto;
}
</style>

