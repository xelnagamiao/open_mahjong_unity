<template>
  <div class="pd-layout">
    <header class="pd-topbar">
      <div class="pd-topbar-inner">
        <div class="pd-brand">
          <span class="pd-brand-title">玩家数据</span>
          <span class="pd-brand-sub">Player Data</span>
        </div>
        <nav class="pd-topnav">
          <router-link to="/" class="pd-back">← 返回主站</router-link>
        </nav>
      </div>
    </header>
    <main class="pd-main">
      <router-view />
    </main>
  </div>
</template>

<script setup>
// 独立全屏布局：无主站 NavBar/AppFooter，极简顶栏
</script>

<style scoped>
.pd-layout {
  min-height: 100vh;
  background: #f4f5f7;
  color: #1f2329;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC",
    "Microsoft YaHei", Helvetica, Arial, sans-serif;
}
.pd-topbar {
  background: #ffffff;
  border-bottom: 1px solid #dcdfe6;
  position: sticky;
  top: 0;
  z-index: 20;
}
.pd-topbar-inner {
  max-width: 1280px;
  margin: 0 auto;
  padding: 0 20px;
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.pd-brand {
  display: flex;
  align-items: baseline;
  gap: 10px;
}
.pd-brand-title {
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 0.5px;
  color: #1f2329;
}
.pd-brand-sub {
  font-size: 12px;
  color: #8a8f99;
  letter-spacing: 1px;
}
.pd-topnav {
  display: flex;
  align-items: center;
}
.pd-back {
  font-size: 13px;
  color: #54585f;
  text-decoration: none;
  padding: 4px 8px;
}
.pd-back:hover {
  color: #1f2329;
}
.pd-main {
  max-width: 1280px;
  margin: 0 auto;
  padding: 16px 20px 48px;
}
</style>
