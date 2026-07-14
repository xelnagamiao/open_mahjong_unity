<template>
  <div class="mobile-download">
    <header class="page-banner">
      <h1>{{ page.title }}</h1>
      <p>{{ page.description }}</p>
    </header>

    <section class="panel">
      <h2>{{ download.label }}</h2>
      <p class="platform-hint">{{ download.hint }}</p>
      <div class="action-row">
        <el-button type="success" size="large" class="download-btn" @click="openMobileDownload">
          下载 {{ download.label }}
        </el-button>
        <a
          class="qq-btn"
          href="https://qm.qq.com/q/MGGZV58hOO"
          target="_blank"
          rel="noopener noreferrer"
        >加入QQ群</a>
      </div>
    </section>
  </div>
</template>

<script setup>
import {
  MOBILE_DOWNLOAD,
  MOBILE_DOWNLOAD_PAGE,
  openMobileDownload,
} from '@/utils/mobileDownload'

const page = MOBILE_DOWNLOAD_PAGE
const download = MOBILE_DOWNLOAD
</script>

<style scoped>
.mobile-download {
  --accent: #67c23a;
  --accent-deep: #529b2e;
  color: #333;
}

.page-banner {
  background: var(--accent);
  color: #fff;
  padding: 22px 20px;
}

.page-banner h1 {
  margin: 0 0 6px;
  font-size: 1.45rem;
  font-weight: 700;
}

.page-banner p {
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
  opacity: 0.95;
}

.panel {
  background: #fff;
  border: 1px solid #e0e0e0;
  border-top: 0;
  padding: 28px 20px;
  text-align: center;
}

.panel h2 {
  margin: 0 0 10px;
  font-size: 1.2rem;
  font-weight: 700;
  color: #222;
}

.platform-hint {
  margin: 0 0 20px;
  color: #666;
  font-size: 13px;
  line-height: 1.6;
}

.action-row {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  align-items: center;
  gap: 12px;
}

.download-btn {
  width: 100%;
  max-width: 240px;
}

.qq-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 120px;
  height: 40px;
  padding: 0 20px;
  border-radius: 4px;
  background: #12b7f5;
  color: #fff;
  font-size: 14px;
  font-weight: 600;
  text-decoration: none;
  line-height: 1;
}

.qq-btn:hover {
  background: #0ea5e0;
  color: #fff;
}
</style>
