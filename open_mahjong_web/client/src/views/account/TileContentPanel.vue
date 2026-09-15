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

const FACE_HELP = `上传格式

一个 .zip文件，并且在主文件夹下包含两个文件夹，文件夹下存储png图片：

文件夹1：手牌牌面/ 或 hand/  用于手牌显示，平台使用272*389的尺寸，但是上传文件没有高度与宽度要求，手牌牌面会通过固定的手牌宽度自动等比例匹配高度。

文件夹2：3D牌面/ 或 table/ 建议宽高比 1:1.33、400×532 像素（或 600×798），不符合推荐比例的图片将被拉伸，用于3D牌面渲染。

所有卡牌遵循万子11-19、筒子21-29、索子31-39、东南西北41-44、中白发45-48、春夏秋冬梅兰竹菊51-58、红宝牌使用105、205、305标记0m、0p与0s，2代表纯白白板(如果卡牌默认白板是回型白板，可以添加此纯白白板作为可配置项使用)。以上手牌牌面与3D牌面都可以以包含透明通道的方式上传，这样可以通过牌面背景功能自定义替换不同的牌面背景。

其他须知：

1.png单边宽度或高度小于1024像素，单张大小小于500KB；总文件夹解压后小于20MB

2.可以缺少部分牌面，缺少的内容会使用官方牌面递补

3.文件夹示例：

├── NewCardFace.zip/        # 上传的压缩包

│   ├── 手牌牌面/                  # 存放手牌牌面

│   ├── 3D牌面/                    # 存放3D牌面

│   │   ├── 11.png/                # 牌面文件

│   │   ├── 12.png/

│   │   ├── 13.png/

│   │   ├── 105.png/               # 红宝牌`

const BACKGROUND_HELP = `手牌牌面背景：用于在手牌牌面使用透明通道的情况下替换背景，默认尺寸 272*389。
手牌牌背：用于在里宝牌区或者2D展示中显示牌背
3D牌面背景：用于在3D卡牌的正面叠底在3D花纹下显示
上传成对 zip格式：hand-back.png + hand-bg.png。`

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
