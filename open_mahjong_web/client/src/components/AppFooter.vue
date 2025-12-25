<!-- 底部 页脚 -->
<template>
  <footer class="app-footer">
    <div class="footer-content">
      <div class="footer-section">
        <h3>OpemMahjong</h3>
        <p>一个简易的麻将服务网站</p>
      </div>
      
      <div class="footer-section">
        <h4>联系我们</h4>
        <p>玩家交流QQ群：906497522</p>
        <p>管理员：Q1448826180</p>
        <p>立直麻雀研习社：432372422</p>
      </div>

      <div class="footer-section">
        <h4>鸣谢</h4>
        <ul>
          <li>棋牌游戏研究院</li>
          <li>国标麻将吧</li>
          <li>雪枫XueFun9</li>
        </ul>
      </div>

    </div>
    
    <div class="footer-bottom">
      <p>&copy; 2026 立直麻雀研习社</p>
    </div>
  </footer>
</template>

<script setup>
// 无需额外逻辑
</script>

<style scoped>
.app-footer {
  background-color: #2c3e50;
  color: #ecf0f1;
  padding: 40px 0 20px;
  margin-top: auto;
}

.footer-content {
  max-width: 1200px;
  margin: 0 auto;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 30px;
  padding: 0 20px;
}

.footer-section h3,
.footer-section h4 {
  margin-bottom: 15px;
  color: #3498db;
}

.footer-section h3 {
  font-size: 24px;
}

.footer-section h4 {
  font-size: 18px;
}

.footer-section p {
  margin-bottom: 8px;
  line-height: 1.6;
}

.footer-section ul {
  list-style: none;
  padding: 0;
}

.footer-section ul li {
  margin-bottom: 8px;
  padding-left: 20px;
  position: relative;
}

.footer-section ul li::before {
  content: "•";
  color: #3498db;
  position: absolute;
  left: 0;
}

.footer-bottom {
  text-align: center;
  margin-top: 30px;
  padding-top: 20px;
  border-top: 1px solid #34495e;
}

.footer-bottom p {
  margin: 0;
  color: #bdc3c7;
}

@media (max-width: 768px) {
  .footer-content {
    grid-template-columns: 1fr;
    text-align: center;
  }
  
  .footer-section ul li {
    padding-left: 0;
  }
  
  .footer-section ul li::before {
    display: none;
  }
}
</style> 