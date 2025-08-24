<template>
  <div id="app">
    <el-container>
      <el-header>
        <nav-bar />
      </el-header>
      <el-main>
        <router-view />
      </el-main>
      <el-footer>
        <app-footer />
      </el-footer>
    </el-container>
  </div>
</template>

<script setup>
import NavBar from '@/components/NavBar.vue'
import AppFooter from '@/components/AppFooter.vue'
</script>

<style scoped>
#app {
  min-height: 100vh;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}

.el-container {
  min-height: 100vh;
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