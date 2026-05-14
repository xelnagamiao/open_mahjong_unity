<template>
  <div id="app">
    <router-view />
  </div>
</template>

<script setup>
</script>

<style scoped>
#app {
  min-height: 100vh;
}
</style>

<style>
/* 与早期 style.css + 紫渐变页面对齐的语义变量（工具页内仍可用 var(--omu-*)） */
:root {
  --omu-surface: #ffffff;
  --omu-surface-soft: rgba(255, 255, 255, 0.96);
  --omu-border: #ebeef5;
  --omu-border-strong: #dcdfe6;
  --omu-text: #303133;
  --omu-text-soft: #606266;
  --omu-text-muted: #909399;
  --omu-accent: #409eff;
  --omu-accent-strong: #337ecc;
  --omu-accent-soft: #ecf5ff;
  --omu-accent-ghost: rgba(64, 158, 255, 0.12);
  --omu-success: #67c23a;
  --omu-warning: #e6a23c;
  --omu-danger: #f56c6c;
  --omu-mono: Consolas, Monaco, monospace;
  --omu-shadow-sm: 0 4px 20px rgba(0, 0, 0, 0.1);
  --omu-shadow-md: 0 8px 24px rgba(0, 0, 0, 0.12);
  --omu-radius: 15px;
  --omu-radius-sm: 8px;
}
</style>
