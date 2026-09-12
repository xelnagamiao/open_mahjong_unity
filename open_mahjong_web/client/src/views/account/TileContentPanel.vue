<template>
  <el-card class="block section">
    <template #header>上传内容</template>
    <div class="upload-grid">
      <section class="upload-block">
        <h3>牌面</h3>
        <pre class="format-help">{{ FACE_HELP }}</pre>
        <el-form label-width="72px" @submit.prevent="submitFace">
          <el-form-item label="名称">
            <el-input v-model="faceName" maxlength="64" show-word-limit />
          </el-form-item>
          <el-form-item label="描述">
            <el-input v-model="faceDescription" type="textarea" :rows="3" maxlength="500" show-word-limit />
          </el-form-item>
          <el-form-item label="压缩包">
            <div class="file-row">
              <input ref="faceInput" type="file" accept=".zip,application/zip" hidden @change="onPickFace" />
              <el-button @click="faceInput.click()">选择 zip</el-button>
              <span class="file-name">{{ faceFile ? faceFile.name : '' }}</span>
            </div>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="faceLoading" @click="submitFace">上传</el-button>
          </el-form-item>
        </el-form>
      </section>

      <section class="upload-block">
        <h3>牌面背景</h3>
        <pre class="format-help">{{ BACKGROUND_HELP }}</pre>
        <el-form label-width="72px" @submit.prevent="submitBackground">
          <el-form-item label="名称">
            <el-input v-model="backgroundName" maxlength="64" show-word-limit />
          </el-form-item>
          <el-form-item label="描述">
            <el-input v-model="backgroundDescription" type="textarea" :rows="3" maxlength="500" show-word-limit />
          </el-form-item>
          <el-form-item label="压缩包">
            <div class="file-row">
              <input ref="backgroundInput" type="file" accept=".zip,application/zip" hidden @change="onPickBackground" />
              <el-button @click="backgroundInput.click()">选择 zip</el-button>
              <span class="file-name">{{ backgroundFile ? backgroundFile.name : '' }}</span>
            </div>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="backgroundLoading" @click="submitBackground">上传</el-button>
          </el-form-item>
        </el-form>
      </section>
    </div>

    <el-divider content-position="left">提交记录</el-divider>
    <div class="fit-table-wrap">
      <el-table :data="items" v-loading="loading" size="small" empty-text="暂无记录" class="fit-table">
        <el-table-column label="类型" width="100">
          <template #default="{ row }">{{ kindLabel(row.kind) }}</template>
        </el-table-column>
        <el-table-column prop="name" label="名称" min-width="120" />
        <el-table-column label="描述" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">{{ row.description || '—' }}</template>
        </el-table-column>
        <el-table-column prop="original_filename" label="文件" min-width="140" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="审核说明" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">{{ row.review_note || '—' }}</template>
        </el-table-column>
        <el-table-column label="提交时间" width="170">
          <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="160">
          <template #default="{ row }">
            <el-button link type="primary" @click="downloadRow(row)">下载</el-button>
            <el-button
              v-if="row.status === 'rejected'"
              link
              type="warning"
              @click="pickResubmit(row)"
            >重新提交</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>
    <input ref="resubmitInput" type="file" accept=".zip,application/zip" hidden @change="onResubmitFile" />
  </el-card>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import playerApi from '@/api/playerClient'

const FACE_HELP =
  '上传格式（仅标准麻将）\n'
  + '• 一个 .zip，可选带 manifest.json（format=om-tilepack，family=standard）\n'
  + '• 必须同时包含两个文件夹（缺一不可）：\n'
  + '  hand/{id}.png     或 手牌牌面/{id}.png   2D 手牌，宽高比 272:389，推荐 272×389 像素\n'
  + '  table/{id}.png    或 3D牌面/{id}.png    3D 牌面，宽高比 1:1.33，推荐 400×532 像素（或 600×798）\n'
  + '• 万 11–19，饼 21–29，条 31–39\n'
  + '• 字 41–47（东南西北中白发：45 中、46 回型白板、47 发）\n'
  + '• 花 51–58，赤宝 105 万 / 205 饼 / 305 条，纯白白板 2（无图案，可选）\n'
  + '• 根目录 PNG 不会当手牌\n'
  + '• 比例和像素仅作推荐，支持任意合法尺寸；完整等比居中，不裁切、不拉伸\n'
  + '• 保留原图透明留白，显示底色或所选背景；3D 预览与对局使用相同排版\n'
  + '• 仅 PNG；单边 ≤1024；单张 ≤500KB；解压后 ≤20MB\n'
  + '• 缺图回退官方牌面；虹雀锁定官方 HQv3.1\n'
  + '• 手牌背景与牌背在独立标签管理；3D 背景在「3D 卡牌设计」中设置'

const BACKGROUND_HELP =
  '手牌牌面背景：2D 牌体（含顶部牌沿），宽高比 272:389，推荐 272×389 像素。\n'
  + '3D 牌面背景：宽高比 1:1.33，推荐 400×532 像素（或 600×798），其他尺寸也可上传。\n'
  + '普通模式完整等比居中；选择「铺满/延伸」时才拉伸覆盖牌面与牌边。\n'
  + '手牌牌背：2D 暗面图样（里宝牌未翻开等），不是 3D 牌背。\n'
  + '也可上传 zip：hand-back.png + hand-bg.png。\n'
  + '3D 牌背颜色请到「3D 卡牌设计」的「牌背」标签设置。\n'
  + '3D 牌面纯色与「使用 3D 牌面背景」互斥，开启后花纹仍保留、底色换成所选颜色。\n'
  + '透明牌面自动叠加手牌背景；背景与牌背独立上传和恢复，不随牌组切换。'

const faceInput = ref(null)
const backgroundInput = ref(null)
const resubmitInput = ref(null)
const faceFile = ref(null)
const backgroundFile = ref(null)
const faceName = ref('')
const backgroundName = ref('')
const faceDescription = ref('')
const backgroundDescription = ref('')
const faceLoading = ref(false)
const backgroundLoading = ref(false)
const loading = ref(false)
const items = ref([])
const resubmitRow = ref(null)

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

function onPickFace(ev) {
  faceFile.value = ev.target.files?.[0] || null
}

function onPickBackground(ev) {
  backgroundFile.value = ev.target.files?.[0] || null
}

async function loadMine() {
  loading.value = true
  try {
    const res = await playerApi.get('/tile-content/mine')
    items.value = res.data?.data?.items || []
  } catch {
    items.value = []
  } finally {
    loading.value = false
  }
}

async function postZip({ kind, name, description, file, url }) {
  const data = new FormData()
  data.append('kind', kind)
  if (name) data.append('name', name)
  if (description) data.append('description', description)
  data.append('file', file)
  const res = await playerApi.post(url, data, { timeout: 120000 })
  return res.data
}

async function submitFace() {
  if (!faceFile.value) {
    ElMessage.warning('请选择 zip 文件')
    return
  }
  faceLoading.value = true
  try {
    const res = await postZip({
      kind: 'tile_face',
      name: faceName.value,
      description: faceDescription.value,
      file: faceFile.value,
      url: '/tile-content',
    })
    ElMessage.success(res.message || '已提交，请等待管理员审核')
    faceFile.value = null
    faceName.value = ''
    faceDescription.value = ''
    if (faceInput.value) faceInput.value.value = ''
    await loadMine()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '上传失败')
  } finally {
    faceLoading.value = false
  }
}

async function submitBackground() {
  if (!backgroundFile.value) {
    ElMessage.warning('请选择 zip 文件')
    return
  }
  backgroundLoading.value = true
  try {
    const res = await postZip({
      kind: 'tile_background',
      name: backgroundName.value,
      description: backgroundDescription.value,
      file: backgroundFile.value,
      url: '/tile-content',
    })
    ElMessage.success(res.message || '已提交，请等待管理员审核')
    backgroundFile.value = null
    backgroundName.value = ''
    backgroundDescription.value = ''
    if (backgroundInput.value) backgroundInput.value.value = ''
    await loadMine()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '上传失败')
  } finally {
    backgroundLoading.value = false
  }
}

function pickResubmit(row) {
  resubmitRow.value = row
  if (resubmitInput.value) {
    resubmitInput.value.value = ''
    resubmitInput.value.click()
  }
}

async function onResubmitFile(ev) {
  const file = ev.target.files?.[0]
  const row = resubmitRow.value
  resubmitRow.value = null
  if (!file || !row) return
  try {
    const res = await postZip({
      kind: row.kind,
      name: row.name,
      description: row.description,
      file,
      url: `/tile-content/${row.submission_id}/resubmit`,
    })
    ElMessage.success(res.message || '已重新提交，请等待管理员审核')
    await loadMine()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '上传失败')
  }
}

async function downloadRow(row) {
  try {
    const res = await playerApi.get(`/tile-content/${row.submission_id}/file`, {
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

onMounted(loadMine)
</script>

<style scoped>
.block {
  margin-bottom: 16px;
  min-width: 0;
}
.upload-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}
.upload-block h3 {
  margin: 0 0 10px;
  font-size: 15px;
  font-weight: 600;
}
.format-help {
  margin: 0 0 16px;
  padding: 8px 10px;
  background: #f5f7fa;
  border-left: 3px solid #409eff;
  color: #606266;
  font-size: 12px;
  line-height: 1.65;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
}
.file-row {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.file-name {
  color: #606266;
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.fit-table {
  width: 100%;
}
.fit-table-wrap {
  width: 100%;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}
@media (max-width: 900px) {
  .upload-grid {
    grid-template-columns: 1fr;
  }
}
</style>
