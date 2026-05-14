<template>
  <el-container class="default-layout">
    <el-header class="layout-header">
      <nav-bar />
    </el-header>

    <el-main class="layout-main">
      <router-view />
    </el-main>

    <el-footer class="layout-footer">
      <app-footer />
    </el-footer>
  </el-container>
</template>

<script setup>
import NavBar from '@/components/NavBar.vue'
import AppFooter from '@/components/AppFooter.vue'
</script>

<style scoped>
.default-layout {
  min-height: 100vh;
  background: transparent;
}

.layout-header {
  padding: 0;
  height: auto;
  background: transparent;
}

.layout-main {
  padding: 20px;
  flex: 1;
}

.layout-footer {
  padding: 0;
  height: auto;
}
</style>
