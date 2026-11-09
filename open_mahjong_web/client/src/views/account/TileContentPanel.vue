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
        <div class="rights-notice">
          <strong>分享美术资源规约</strong>
          <ol>
            <li>上传内容须由你拥有版权，或取得明确允许非商业使用和公开分享的许可证。</li>
            <li>没有版权或许可证的资源只能私下使用，不能上传到平台分享站。</li>
          </ol>
        </div>
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
            <el-checkbox v-model="backgroundRightsConfirmed" class="rights-confirm">我确认已取得上传和分享此资源的权利</el-checkbox>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="backgroundLoading" :disabled="!backgroundRightsConfirmed" @click="submitBackground">上传</el-button>
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
import { ElMessage, ElMessageBox } from 'element-plus'
import playerApi from '@/api/playerClient'

const FACE_HELP = `上传格式

上传一个 .zip，直接放一套按牌面代号命名的 PNG 和一个 tiles.cfg。2D 手牌与 3D 牌面共用原图，不再需要两份图片；原有 hand/、table/ 格式仍可上传和使用。

PNG 保留原始比例及透明通道，不拉伸、不二次压缩。可在 tiles.cfg 分别设置 2D、3D 的整包默认位置和缩放，以及每张牌的微调。

牌面代号：万子 11-19、筒子 21-29、索子 31-39、东南西北 41-44、中白发 45-47、春夏秋冬梅兰竹菊 51-58、通用花牌 59；红五万/筒/索为 105/205/305，纯白白板为 2。

其他须知：

1. PNG 单边不超过 4096 像素，单张不超过 4MB，压缩包及解压后总大小不超过 128MB；缺失牌面使用官方牌面递补。

2. 配置使用 UTF-8，支持 # 整行或行末注释。[2d]、[3d] 设置整包默认值，[2d.11]、[3d.11] 设置单牌相对调整；x/y 是位置百分比（-100～100），scale 是缩放百分比（25～200）。未配置时位置 0、缩放 100；单牌设置叠加在整包设置上。

3. 压缩包内容示例（不需要其他文件）：
NewCardFace.zip
├── 11.png
├── 12.png
├── 105.png
└── tiles.cfg

# tiles.cfg 示例
[2d]
x = 0
y = 0
scale = 100
[3d]
scale = 100
[2d.11]
y = 2 # 一万向上移动 2%
[3d.11]
scale = 95`


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
const backgroundRightsConfirmed = ref(false)
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
  if (!backgroundRightsConfirmed.value) {
    ElMessage.warning('请先确认资源版权或许可证情况')
    return
  }
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
    backgroundRightsConfirmed.value = false
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
  if (row.kind === 'tile_background') {
    try {
      await ElMessageBox.confirm(
        '请确认此牌面背景由你拥有版权，或使用允许非商业使用的许可证。没有版权或许可证的资源只能私下使用，不能上传到平台分享站。',
        '分享美术资源规约',
        { type: 'warning', confirmButtonText: '确认并重新提交', cancelButtonText: '取消' },
      )
    } catch {
      return
    }
  }
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
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20px;
}
.upload-block {
  min-width: 0;
}
.upload-block h3 {
  margin: 0 0 10px;
  font-size: 15px;
  font-weight: 600;
  white-space: nowrap;
}
.format-help {
  margin: 0 0 16px;
  padding: 8px 10px;
  background: #f5f7fa;
  border-left: 3px solid #409eff;
  color: #606266;
  font-size: 12px;
  line-height: 1.65;
  white-space: pre-line;
  word-break: keep-all;
  overflow-wrap: break-word;
  font-family: inherit;
}
.rights-notice {
  margin-bottom: 18px;
  padding: 12px 16px;
  border: 1px solid #e6a23c;
  border-radius: 4px;
  background: #fdf6ec;
  color: #7a4b00;
  font-size: 13px;
  line-height: 1.7;
  overflow-wrap: anywhere;
}
.rights-notice ol {
  margin: 6px 0 0;
  padding-left: 20px;
}
.rights-confirm {
  height: auto;
  max-width: 100%;
}
.rights-confirm :deep(.el-checkbox__label) {
  white-space: normal;
  line-height: 1.7;
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
