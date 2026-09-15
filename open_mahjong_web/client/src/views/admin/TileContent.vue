<template>
  <div class="admin-tile-content">
    <h2 class="page-title">牌面审核</h2>
    <div class="toolbar">
      <el-radio-group v-model="statusFilter" size="small" @change="onFilterChange">
        <el-radio-button label="">全部</el-radio-button>
        <el-radio-button label="pending">待审</el-radio-button>
        <el-radio-button label="approved">已通过</el-radio-button>
        <el-radio-button label="rejected">已打回</el-radio-button>
      </el-radio-group>
      <el-radio-group v-model="kindFilter" size="small" @change="onFilterChange">
        <el-radio-button label="">全部类型</el-radio-button>
        <el-radio-button label="tile_face">牌面</el-radio-button>
        <el-radio-button label="tile_background">牌面背景</el-radio-button>
      </el-radio-group>
      <el-button size="small" :loading="loading" @click="load">刷新</el-button>
    </div>

    <el-table :data="items" v-loading="loading" stripe>
      <el-table-column prop="submission_id" label="ID" width="70" />
      <el-table-column label="提交人" min-width="140">
        <template #default="{ row }">
          {{ row.username || '—' }} ({{ row.user_id }})
        </template>
      </el-table-column>
      <el-table-column label="类型" width="100">
        <template #default="{ row }">{{ kindLabel(row.kind) }}</template>
      </el-table-column>
      <el-table-column prop="name" label="名称" min-width="120" />
      <el-table-column label="描述" min-width="160" show-overflow-tooltip>
        <template #default="{ row }">{{ row.description || '—' }}</template>
      </el-table-column>
      <el-table-column prop="original_filename" label="文件" min-width="140" show-overflow-tooltip />
      <el-table-column label="内容" min-width="140">
        <template #default="{ row }">{{ summaryText(row) }}</template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="statusType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="提交时间" width="170">
        <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="200" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openPreview(row)">预览</el-button>
          <el-button link type="primary" @click="downloadRow(row)">下载</el-button>
          <template v-if="row.status === 'pending'">
            <el-button type="success" link @click="openApprove(row)">通过</el-button>
            <el-button type="danger" link @click="openReject(row)">打回</el-button>
          </template>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination
        background
        layout="total, prev, pager, next"
        :total="total"
        :page-size="pageSize"
        v-model:current-page="page"
        @current-change="load"
      />
    </div>

    <el-dialog v-model="previewVisible" :title="previewTitle" width="760px" @closed="revokePreviews">
      <p v-if="currentRow?.description" class="pack-desc">{{ currentRow.description }}</p>
      <p v-if="currentRow?.meta?.warnings?.length" class="warn-list">
        {{ currentRow.meta.warnings.join('；') }}
      </p>
      <div v-loading="previewLoading" class="preview-grid">
        <figure v-for="item in previewItems" :key="item.url" class="preview-item">
          <img :src="item.url" :alt="item.label" />
          <figcaption>{{ item.label }}</figcaption>
        </figure>
      </div>
    </el-dialog>

    <el-dialog v-model="approveVisible" title="通过" width="480px">
      <el-form label-width="88px">
        <el-form-item label="审核意见">
          <el-input v-model="approveNote" type="textarea" rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="approveVisible = false">取消</el-button>
        <el-button type="primary" :loading="acting" @click="doApprove">确认通过</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="rejectVisible" title="打回" width="480px">
      <el-form label-width="88px">
        <el-form-item label="审核意见" required>
          <el-input v-model="rejectNote" type="textarea" rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="rejectVisible = false">取消</el-button>
        <el-button type="danger" :loading="acting" @click="doReject">确认打回</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'

const items = ref([])
const loading = ref(false)
const acting = ref(false)
const statusFilter = ref('pending')
const kindFilter = ref('')
const page = ref(1)
const pageSize = 20
const total = ref(0)
const currentRow = ref(null)
const previewVisible = ref(false)
const previewLoading = ref(false)
const previewItems = ref([])
const approveVisible = ref(false)
const rejectVisible = ref(false)
const approveNote = ref('')
const rejectNote = ref('')

const previewTitle = computed(() => {
  const row = currentRow.value
  if (!row) return '预览'
  return `${kindLabel(row.kind)} · ${row.name || row.original_filename}`
})

function kindLabel(kind) {
  return kind === 'tile_background' ? '牌面背景' : '牌面'
}

function statusLabel(s) {
  return ({ pending: '待审', approved: '已通过', rejected: '已打回' })[s] || s
}

function statusType(s) {
  return ({ pending: 'warning', approved: 'success', rejected: 'danger' })[s] || 'info'
}

function formatDate(v) {
  if (!v) return ''
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  return d.toLocaleString('zh-CN', { hour12: false })
}

function summaryText(row) {
  const meta = row.meta || {}
  if (row.kind === 'tile_background') {
    return (meta.files || []).join('、') || '—'
  }
  const hand = meta.hand_count != null ? `手牌 ${meta.hand_count}` : ''
  const table = meta.table_count != null ? `3D ${meta.table_count}` : ''
  return [hand, table].filter(Boolean).join(' / ') || '—'
}

function onFilterChange() {
  page.value = 1
  load()
}

async function load() {
  loading.value = true
  try {
    const res = await adminApi.get('/tile-content', {
      params: {
        status: statusFilter.value,
        kind: kindFilter.value,
        page: page.value,
        page_size: pageSize,
      },
    })
    items.value = res.data?.data?.items || []
    total.value = res.data?.data?.total || 0
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
  } finally {
    loading.value = false
  }
}

function revokePreviews() {
  for (const item of previewItems.value) {
    URL.revokeObjectURL(item.url)
  }
  previewItems.value = []
}

async function openPreview(row) {
  currentRow.value = row
  previewVisible.value = true
  previewLoading.value = true
  revokePreviews()
  try {
    const list = Array.isArray(row.meta?.previews) ? row.meta.previews.slice(0, 24) : []
    const loaded = await Promise.all(
      list.map(async (item) => {
        const res = await adminApi.get(
          `/tile-content/${row.submission_id}/preview/${encodeURIComponent(item.name)}`,
          { responseType: 'blob' }
        )
        return { label: item.label || item.name, url: URL.createObjectURL(res.data) }
      })
    )
    previewItems.value = loaded
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '预览失败')
  } finally {
    previewLoading.value = false
  }
}

async function downloadRow(row) {
  try {
    const res = await adminApi.get(`/tile-content/${row.submission_id}/file`, {
      responseType: 'blob',
      timeout: 120000,
    })
    const url = URL.createObjectURL(res.data)
    const a = document.createElement('a')
    a.href = url
    a.download = row.original_filename || 'pack.zip'
    a.click()
    URL.revokeObjectURL(url)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '下载失败')
  }
}

function openApprove(row) {
  currentRow.value = row
  approveNote.value = ''
  approveVisible.value = true
}

function openReject(row) {
  currentRow.value = row
  rejectNote.value = ''
  rejectVisible.value = true
}

async function doApprove() {
  if (!currentRow.value) return
  acting.value = true
  try {
    await adminApi.post(`/tile-content/${currentRow.value.submission_id}/approve`, {
      review_note: approveNote.value,
    })
    ElMessage.success('已通过')
    approveVisible.value = false
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  } finally {
    acting.value = false
  }
}

async function doReject() {
  if (!currentRow.value) return
  if (!rejectNote.value.trim()) {
    ElMessage.warning('请填写拒绝原因')
    return
  }
  acting.value = true
  try {
    await adminApi.post(`/tile-content/${currentRow.value.submission_id}/reject`, {
      review_note: rejectNote.value,
    })
    ElMessage.success('已打回')
    rejectVisible.value = false
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  } finally {
    acting.value = false
  }
}

onMounted(load)
onBeforeUnmount(revokePreviews)
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 12px;
  align-items: center;
}
.pager {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}
.warn-list {
  margin: 0 0 12px;
  color: #e6a23c;
  font-size: 13px;
  line-height: 1.6;
}
.pack-desc {
  margin: 0 0 12px;
  color: #606266;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
}
.preview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(112px, 1fr));
  gap: 10px;
  min-height: 80px;
}
.preview-item {
  margin: 0;
}
.preview-item img {
  width: 100%;
  aspect-ratio: 3 / 4;
  object-fit: contain;
  background: #2f2f2f;
  border-radius: 4px;
}
.preview-item figcaption {
  margin-top: 4px;
  font-size: 12px;
  color: #909399;
  word-break: break-all;
}
</style>
