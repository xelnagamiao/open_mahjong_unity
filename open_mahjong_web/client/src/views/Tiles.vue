<template>
  <div class="tiles-page">
    <div class="sec-h">■ 牌面</div>
    <el-radio-group v-model="kindFilter" size="small" class="kind-tabs" @change="load">
      <el-radio-button label="tile_face">牌面</el-radio-button>
      <el-radio-button label="tile_background">牌面背景</el-radio-button>
    </el-radio-group>
    <div v-if="loading" class="tip">加载中…</div>
    <div v-else-if="!items.length" class="tip">暂无牌面</div>
    <div v-else class="list">
      <div v-for="row in items" :key="row.submission_id" class="item">
        <div class="name">{{ row.name || row.original_filename }}</div>
        <div class="meta">
          <span>{{ kindLabel(row.kind) }}</span>
          <span>{{ row.username || '—' }}</span>
        </div>
        <div v-if="row.description" class="desc">{{ row.description }}</div>
        <a
          class="dl"
          :href="fileUrl(row)"
          :download="row.original_filename || 'pack.zip'"
        >下载</a>
      </div>
    </div>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import axios from 'axios'

const items = ref([])
const loading = ref(true)
const kindFilter = ref('tile_face')

function kindLabel(kind) {
  return kind === 'tile_background' ? '牌面背景' : '牌面'
}

function fileUrl(row) {
  return `/api/player/tile-content/${row.submission_id}/file`
}

async function load() {
  loading.value = true
  try {
    const res = await axios.get('/api/player/tile-content/catalog', {
      params: { kind: kindFilter.value },
    })
    items.value = res.data?.data?.items || []
  } catch {
    items.value = []
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.sec-h {
  background: rgba(0, 0, 0, 0.75);
  color: #fff;
  padding: 6px 12px;
  font-size: 13px;
  margin-bottom: 12px;
}
.kind-tabs { margin-bottom: 12px; }
.tip { color: #999; font-size: 13px; }
.list {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}
.item {
  display: flex;
  flex-direction: column;
  min-width: 0;
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 14px 16px;
}
.name {
  font-weight: 700;
  margin-bottom: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.meta {
  color: #888;
  font-size: 12px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px 12px;
}
.desc {
  margin-top: 8px;
  color: #555;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 4;
  overflow: hidden;
}
.dl {
  margin-top: auto;
  padding-top: 12px;
  color: #409eff;
  font-size: 14px;
  text-decoration: none;
}
.dl:hover { text-decoration: underline; }
@media (max-width: 900px) {
  .list { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@media (max-width: 560px) {
  .list { grid-template-columns: 1fr; }
}
</style>
