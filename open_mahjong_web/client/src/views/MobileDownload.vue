<template>
  <div class="mobile-download">
    <div class="page-header">
      <h1>{{ page.title }}</h1>
      <p class="subtitle">{{ page.description }}</p>
    </div>

    <el-card class="download-card" shadow="hover">
      <div class="card-icon">
        <el-icon :size="56">
          <Cellphone />
        </el-icon>
      </div>
      <h2>{{ download.label }}</h2>
      <p class="platform-hint">{{ download.hint }}</p>
      <el-button type="primary" size="large" class="download-btn" @click="openMobileDownload">
        下载 {{ download.label }}
      </el-button>
    </el-card>
  </div>
</template>

<script setup>
import { Cellphone } from '@element-plus/icons-vue'
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
  max-width: 480px;
  margin: 0 auto;
  padding: 12px 20px 40px;
}

.page-header {
  text-align: center;
  margin-bottom: 40px;
  color: white;
}

.page-header h1 {
  font-size: 2.4rem;
  margin: 0 0 16px;
  font-weight: bold;
  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.3);
}

.subtitle {
  margin: 0;
  font-size: 1.1rem;
  line-height: 1.7;
  opacity: 0.95;
}

.download-card {
  text-align: center;
  border-radius: 16px;
  padding: 8px 4px 12px;
}

.card-icon {
  margin-bottom: 12px;
  color: #409eff;
}

.download-card h2 {
  margin: 0 0 12px;
  font-size: 1.4rem;
  color: #303133;
}

.platform-hint {
  margin: 0 0 20px;
  color: #606266;
  line-height: 1.6;
}

.download-btn {
  width: 100%;
  max-width: 240px;
}

@media (max-width: 768px) {
  .page-header h1 {
    font-size: 1.8rem;
  }
}
</style>
