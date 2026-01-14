<template>
  <div id="app">
    <!-- 路由视图 - 根据路由配置自动选择布局 -->
    <router-view />
  </div>
</template>

<script setup>
// App.vue 只负责渲染路由视图，布局逻辑由路由配置决定
</script>

<style scoped>
#app {
  min-height: 100vh;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}
</style> 