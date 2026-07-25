<template>
  <el-container class="default-layout" :class="layoutToneClass">
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
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import NavBar from '@/components/NavBar.vue'
import AppFooter from '@/components/AppFooter.vue'

const route = useRoute()

const layoutToneClass = computed(() => {
  const path = route.path || ''
  if (path === '/paili' || path.startsWith('/paili/')) return 'tone-paili'
  if (path === '/chinese' || path.startsWith('/chinese/')) return 'tone-chinese'
  return ''
})
</script>

<style scoped>
.default-layout {
  min-height: 100vh;
  background: #f5f5f5;
}

.default-layout.tone-paili {
  background: #9b59b6;
}

.default-layout.tone-chinese {
  background: #45b7d1;
}

.layout-header {
  padding: 0;
  height: auto;
  background: transparent;
  position: sticky;
  top: 0;
  z-index: 1000;
}

.layout-main {
  padding: 20px 16px 48px;
  flex: 1;
  max-width: 1100px;
  width: 100%;
  margin: 0 auto;
  box-sizing: border-box;
  min-width: 0;
  /* Element Plus .el-main 默认 overflow:auto，会打断子元素 position:sticky */
  overflow: visible;
}

.layout-footer {
  padding: 0;
  height: auto;
}

@media (max-width: 768px) {
  .layout-main {
    padding: 12px 12px 32px;
  }
}
</style>
