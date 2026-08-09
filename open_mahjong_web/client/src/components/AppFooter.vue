<template>
  <footer class="app-footer">
    <div class="footer-content">
      <div class="footer-section">
        <h3>OpenMahjong</h3>
        <p>一个简易的麻将服务网站</p>
      </div>

      <div class="footer-section">
        <h4>联系我们</h4>
        <p>
          salasasa 麻将平台测试群：
          <a
            href="https://qm.qq.com/q/MGGZV58hOO"
            target="_blank"
            rel="noopener noreferrer"
            class="footer-link"
          >906497522</a>
        </p>
        <p>
          open mahjong unity 项目交流：
          <a
            href="https://qm.qq.com/q/MualpyNFoi"
            target="_blank"
            rel="noopener noreferrer"
            class="footer-link"
          >1084537740</a>
        </p>
        <p>网站管理员：Q1448826180</p>
      </div>

      <div class="footer-section">
        <h4>鸣谢</h4>
        <p>棋牌游戏研究院</p>
        <p>国标麻将吧</p>
        <p>雪枫 XueFun9</p>
      </div>
    </div>

    <div class="footer-bottom">
      <p>© 2026 立直麻雀研习社 苏ICP备2026002546号</p>
    </div>
  </footer>
</template>

<style scoped>
.app-footer {
  background-color: #2c3e50;
  color: #ecf0f1;
  padding: 32px 0 20px;
  margin-top: auto;
}

.footer-content {
  max-width: 1200px;
  margin: 0 auto;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 40px;
  padding: 0 32px;
}

.footer-section h3,
.footer-section h4 {
  margin: 0 0 10px;
  color: #3498db;
}

.footer-section h3 {
  font-size: 1.5rem;
}

.footer-section h4 {
  font-size: 1.15rem;
}

.footer-section p {
  margin: 0 0 6px;
  line-height: 1.6;
  font-size: 14px;
  color: #dfe6e9;
}

.footer-link,
a.footer-link {
  padding: 0;
  border: none;
  background: none;
  color: #5dade2;
  font: inherit;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 3px;
}

.footer-link:hover {
  color: #85c1e9;
}

.footer-bottom {
  text-align: center;
  margin-top: 24px;
  padding-top: 14px;
  border-top: 1px solid #34495e;
}

.footer-bottom p {
  margin: 0;
  color: #bdc3c7;
  font-size: 13px;
}

@media (max-width: 768px) {
  .footer-content {
    grid-template-columns: 1fr;
    gap: 24px;
    text-align: center;
  }
}
</style>
