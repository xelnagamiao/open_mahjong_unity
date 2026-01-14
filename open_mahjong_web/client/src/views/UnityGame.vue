<template>
  <div class="unity-page">
    <div class="unity-game-container">
      <div class="unity-wrapper">
        <iframe 
          src="/unity-game/index.html" 
          class="unity-iframe"
          frameborder="0"
          allowfullscreen
        ></iframe>
      </div>
    </div>
  </div>
</template>

<script setup>
// Unity游戏视图组件 - 纯黑全屏页面
</script>

<style scoped>
/* 全屏页面容器 - 固定定位覆盖整个视口 */
.unity-page {
  position: fixed; /* 固定定位，脱离文档流 */
  top: 0; /* 顶部对齐 */
  left: 0; /* 左侧对齐 */
  width: 100vw; /* 视口宽度100% */
  height: 100vh; /* 视口高度100% */
  background-color: #000; /* 纯黑背景 */
  overflow: hidden; /* 隐藏溢出内容 */
}

/* 游戏容器 - 使用flex布局居中游戏 */
.unity-game-container {
  display: flex; /* 弹性布局 */
  justify-content: center; /* 水平居中 */
  align-items: center; /* 垂直居中 */
  width: 100%; /* 宽度100% */
  height: 100%; /* 高度100% */
  padding: 0; /* 无填充 */
  box-sizing: border-box; /* 包含padding在内计算尺寸 */
}

/* 游戏包装器 - 包含iframe的容器 */
.unity-wrapper {
  position: relative; /* 相对定位，作为iframe的定位参考 */
  width: 100%; /* 宽度100% */
  max-width: 90%; /* 最大宽度90% */
  aspect-ratio: 16 / 9; /* 保持16:9宽高比 */
  background-color: #000; /* 黑色背景 */
}

/* Unity游戏iframe - 嵌入Unity游戏页面 */
.unity-iframe {
  position: absolute; /* 绝对定位，填满wrapper */
  top: 0; /* 顶部对齐 */
  left: 0; /* 左侧对齐 */
  width: 100%; /* 宽度100% */
  height: 100%; /* 高度100% */
  border: none; /* 无边框 */
}

/* 响应式设计 - 移动端适配 */
@media (max-width: 768px) {
  .unity-game-container {
    padding: 0; /* 无填充 */
  }
  
  .unity-wrapper {
    max-width: 100%; /* 移动端最大宽度100% */
  }
}
</style>
