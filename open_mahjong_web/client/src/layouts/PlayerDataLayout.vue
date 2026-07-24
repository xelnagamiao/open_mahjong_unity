<template>
  <div class="pd-layout">
    <header class="pd-topbar">
      <div class="pd-topbar-inner">
        <div class="pd-brand">
          <span class="pd-brand-title">玩家/平台数据站</span>
          <span class="pd-brand-sub">Player Data</span>
          <nav class="pd-section-nav">
            <router-link to="/player-data" class="pd-nav-link" exact-active-class="active">玩家数据</router-link>
            <router-link to="/player-data/platform" class="pd-nav-link" exact-active-class="active">平台数据</router-link>
          </nav>
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
// 独立全屏布局：玩家数据 + 平台数据
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
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
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
.pd-section-nav {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-left: 8px;
  padding-left: 12px;
  border-left: 1px solid #e4e7ed;
}
.pd-nav-link {
  font-size: 13px;
  color: #606266;
  text-decoration: none;
  padding: 4px 10px;
  border-radius: 4px;
}
.pd-nav-link:hover { color: #1f2329; background: #f4f5f7; }
.pd-nav-link.active {
  color: #409eff;
  font-weight: 600;
  background: #ecf5ff;
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
@media (max-width: 720px) {
  .pd-topbar-inner { height: auto; min-height: 48px; padding-top: 8px; padding-bottom: 8px; }
  .pd-section-nav {
    margin-left: 0;
    padding-left: 0;
    border-left: none;
    width: 100%;
  }
}
</style>
