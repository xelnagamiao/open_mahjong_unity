<template>
  <div class="record-convert">
    <header class="page-heading">
      <h1>牌谱格式转换</h1>
    </header>

    <section v-if="sharedLoading || sharedError" class="section-block" aria-live="polite">
      <el-alert v-if="sharedLoading" title="正在读取分享牌谱并自动转换…" type="info" :closable="false" show-icon />
      <template v-else>
        <el-alert title="无法打开分享牌谱" :description="sharedError" type="error" :closable="false" show-icon />
        <el-button @click="loadSharedRecord">重新加载</el-button>
      </template>
    </section>

    <section class="section-block mode-section">
      <div class="sec-h">选择转换方向</div>
      <div class="mode-grid">
        <button
          v-for="item in modes"
          :key="item.id"
          type="button"
          class="mode-option"
          :class="{ active: modeId === item.id }"
          :aria-pressed="modeId === item.id"
          :disabled="sharedLoading || fetchBusy || busy"
          @click="selectMode(item.id)"
        >
          {{ item.label }}
        </button>
      </div>
    </section>

    <p v-if="currentMode?.hint" class="mode-hint">{{ currentMode.hint }}</p>

    <div v-if="!currentMode" class="choose-tip">
      请选择转换方向。
    </div>

    <template v-else>
      <section class="section-block">
        <div class="info-panel">
          <div class="data-impact">
            <h2>转换限制</h2>
            <p class="impact-intro">以下字段无法完整转换。</p>
            <div class="impact-list">
              <article v-for="(row, index) in affectedRows" :key="index" class="impact-item">
                <div class="impact-title">
                  <strong>{{ row.field }}</strong>
                  <span class="impact-kind">{{ lossKind(row.status) }}</span>
                </div>
                <p>{{ row.how }}</p>
                <details>
                  <summary>示例</summary>
                  <code>{{ row.example }}</code>
                </details>
              </article>
            </div>
          </div>
        </div>
      </section>

      <section class="converter-box">
        <template v-if="modeId === 'tz2sala'">
          <div class="fetch-form">
            <el-input
              v-model="tziakchaInput"
              :disabled="fetchBusy || sharedLoading"
              @input="onInputChanged"
              clearable
              placeholder="粘贴雀渣牌谱链接或牌谱 ID"
              @keyup.enter="fetchTziakchaRecord"
            />
            <el-button type="primary" :loading="fetchBusy" :disabled="sharedLoading || busy" @click="fetchTziakchaRecord">转换</el-button>
          </div>
        </template>

        <template v-else>
          <input ref="fileInput" type="file" :accept="currentMode.accept" hidden @change="onFile" />
          <div class="source-toolbar">
            <el-button type="primary" plain :disabled="busy || sharedLoading" @click="fileInput?.click()">选择牌谱文件</el-button>
            <span>{{ selectedFileName || '或在下方粘贴牌谱内容' }}</span>
            <el-button v-if="inputText" link type="primary" :disabled="busy || sharedLoading" @click="clearAll">清空</el-button>
          </div>
          <el-input
            id="record-source"
            v-model="inputText"
            :disabled="busy || sharedLoading"
            type="textarea"
            :rows="9"
            :placeholder="`粘贴“${currentMode.label}”的源牌谱内容`"
            class="mono-area"
            @input="onInputChanged"
          />
          <div class="convert-row">
            <el-button
              type="primary"
              :loading="busy"
              :disabled="!inputText.trim() || sharedLoading"
              @click="runConvert"
            >转换</el-button>
          </div>
        </template>

        <el-alert
          v-if="error"
          type="error"
          title="转换失败"
          :description="error"
          :closable="false"
          show-icon
        />

        <div v-if="outputText" ref="resultPanel" class="result-actions" aria-live="polite">
          <strong>转换完成</strong>
          <div>
            <el-button type="success" @click="downloadOut">下载 JSON</el-button>
            <el-button v-if="canOpenReplay" type="warning" @click="openIn2d">打开 2D 阅览</el-button>
            <el-button v-if="canOpenReplay" type="warning" @click="openIn3d">打开 3D 阅览</el-button>
            <el-button v-if="canOpenReplay" type="primary" :loading="shareBusy" @click="shareRecord">分享转换牌谱链接</el-button>
          </div>
        </div>
        <div v-if="shareUrl" class="share-panel">
          <p>打开链接后可自动转换牌谱。</p>
          <el-input :model-value="shareUrl" readonly aria-label="牌谱分享链接" @focus="$event.target.select()">
            <template #append><el-button @click="copyShareUrl">复制链接</el-button></template>
          </el-input>
        </div>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import axios from 'axios'
import { ElMessage } from 'element-plus'
import { CONVERT_MODES, getMode } from '@/utils/recordConvert'
import { saveLocalReplayRecord, UNITY_LOCAL_REPLAY_ID } from '@/game2d/replay/localReplayRecord'

const modes = CONVERT_MODES
const router = useRouter()
const route = useRoute()
const modeId = ref('')
const inputText = ref('')
const outputText = ref('')
const busy = ref(false)
const error = ref('')
const fileInput = ref(null)
const selectedFileName = ref('')
const tziakchaInput = ref('')
const fetchBusy = ref(false)
const sharedLoading = ref(false)
const sharedError = ref('')
const shareBusy = ref(false)
const shareUrl = ref('')
const resultPanel = ref(null)
let revision = 0
let convertedSource = null

function invalidateResult() {
  revision += 1
  busy.value = false
  outputText.value = ''
  shareUrl.value = ''
  convertedSource = null
}

const currentMode = computed(() => getMode(modeId.value))
const affectedRows = computed(() => currentMode.value?.approxRows?.filter((row) => row.status !== '完整') || [])
const canOpenReplay = computed(() => outputText.value && ['tz2sala', 'bz2sala', 'mjai2sala'].includes(modeId.value))

function selectMode(id) {
  invalidateResult()
  modeId.value = id
  outputText.value = ''
  error.value = ''
}

function lossKind(status) {
  if (status.includes('缺失') && status.includes('近似')) return '缺失或推算'
  if (status.includes('缺失')) return '缺失'
  if (status === '格式限制') return '格式限制'
  return '推算或占位'
}

async function readFile(file) {
  if (!file) return
  invalidateResult()
  const requestRevision = revision
  try {
    const text = await file.text()
    if (requestRevision !== revision) return
    inputText.value = text
    selectedFileName.value = file.name
    outputText.value = ''
    error.value = ''
  } catch {
    error.value = '无法读取这个文件，请重新选择，或直接粘贴文件内容'
  }
}

async function onFile(event) {
  await readFile(event.target.files?.[0])
  event.target.value = ''
}

function onInputChanged() {
  invalidateResult()
  selectedFileName.value = ''
  outputText.value = ''
  error.value = ''
}

function clearAll() {
  invalidateResult()
  inputText.value = ''
  outputText.value = ''
  selectedFileName.value = ''
  error.value = ''
}

async function fetchTziakchaRecord() {
  if (fetchBusy.value || sharedLoading.value) return
  const input = tziakchaInput.value.trim()
  if (!input) {
    ElMessage.warning('请先粘贴雀渣牌谱链接或牌谱 ID')
    return
  }
  fetchBusy.value = true
  invalidateResult()
  const requestRevision = revision
  error.value = ''
  try {
    const response = await axios.post('/api/mahjong/tziakcha-record', { input })
    if (requestRevision !== revision) return
    inputText.value = JSON.stringify(response.data?.data || {}, null, 2)
    selectedFileName.value = '已从雀渣读取牌谱'
    await runConvert()
  } catch (cause) {
    if (requestRevision !== revision) return
    error.value = cause?.response?.data?.message || cause?.message || '无法读取雀渣牌谱'
  } finally {
    fetchBusy.value = false
  }
}

async function runConvert() {
  invalidateResult()
  const requestRevision = revision
  error.value = ''
  outputText.value = ''
  const mode = currentMode.value
  const source = inputText.value
  if (!mode || !inputText.value.trim()) return

  busy.value = true
  try {
    const result = await mode.convert(source)
    if (requestRevision !== revision) return
    outputText.value = result
    convertedSource = { mode: mode.id, source }
  } catch (cause) {
    if (requestRevision !== revision) return
    error.value = cause?.message || String(cause)
  } finally {
    if (requestRevision === revision) busy.value = false
  }
}

async function shareRecord() {
  if (!convertedSource || shareBusy.value) return
  if (shareUrl.value) return copyShareUrl()
  const requestRevision = revision
  shareBusy.value = true
  try {
    const { data } = await axios.post('/api/record-convert-shares', convertedSource)
    if (requestRevision !== revision) return
    shareUrl.value = new URL(router.resolve({ name: 'RecordConvert', query: { share: data.id } }).href, window.location.origin).href
    await copyShareUrl()
  } catch (cause) {
    if (requestRevision === revision) ElMessage.error(cause?.response?.data?.message || '生成分享链接失败，请重试')
  } finally {
    shareBusy.value = false
  }
}

async function copyShareUrl() {
  try {
    await navigator.clipboard.writeText(shareUrl.value)
    ElMessage.success('分享链接已复制')
  } catch {
    ElMessage.info('分享链接已生成，请在下方选中并复制')
  }
}

let sharedRequest = 0
async function loadSharedRecord() {
  const request = ++sharedRequest
  invalidateResult()
  error.value = ''
  sharedError.value = ''
  sharedLoading.value = false
  const id = route.query.share
  if (id == null) return
  if (typeof id !== 'string' || !/^[a-f0-9]{32}$/.test(id)) {
    sharedError.value = '分享链接无效，请检查链接是否完整'
    return
  }
  sharedLoading.value = true
  try {
    const { data } = await axios.get(`/api/record-convert-shares/${id}`)
    if (request !== sharedRequest) return
    if (!['tz2sala', 'bz2sala', 'mjai2sala'].includes(data.mode) || typeof data.source !== 'string') throw new Error('分享牌谱格式不正确')
    modeId.value = data.mode
    inputText.value = data.source
    selectedFileName.value = '来自分享链接的牌谱'
    tziakchaInput.value = ''
    await runConvert()
    if (request !== sharedRequest) return
    if (error.value) throw new Error(error.value)
    if (outputText.value) {
      shareUrl.value = new URL(router.resolve({ name: 'RecordConvert', query: { share: id } }).href, window.location.origin).href
      await nextTick()
      resultPanel.value?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    }
  } catch (cause) {
    if (request === sharedRequest) sharedError.value = cause?.response?.data?.message || cause?.message || '读取分享牌谱失败'
  } finally {
    if (request === sharedRequest) sharedLoading.value = false
  }
}

watch(() => route.query.share, loadSharedRecord, { immediate: true })
onBeforeUnmount(() => { sharedRequest += 1; revision += 1 })

function openIn2d() {
  try {
    const gameId = saveLocalReplayRecord(JSON.parse(outputText.value))
    router.push(`/2d/record/${encodeURIComponent(gameId)}`)
  } catch (cause) {
    ElMessage.error(cause?.message || '无法打开 2D 牌谱')
  }
}

function openIn3d() {
  try {
    saveLocalReplayRecord(JSON.parse(outputText.value))
    window.location.assign(`/game-unity?recordId=${encodeURIComponent(UNITY_LOCAL_REPLAY_ID)}`)
  } catch (cause) {
    ElMessage.error(cause?.message || '无法打开 3D 牌谱')
  }
}

function downloadOut() {
  const blob = new Blob([outputText.value], { type: 'application/json;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = currentMode.value?.filename || 'converted.json'
  link.click()
  URL.revokeObjectURL(url)
}
</script>

<style scoped>
.record-convert {
  max-width: 1040px;
  margin: 0 auto;
  color: #303133;
}

.page-heading {
  margin-bottom: 22px;
}

.page-heading h1 {
  margin: 0 0 8px;
  font-size: 30px;
}

.section-block {
  margin-bottom: 24px;
}

.sec-h {
  margin-bottom: 12px;
  padding: 7px 12px;
  color: #fff;
  background: rgba(0, 0, 0, 0.78);
  font-size: 13px;
}

.mode-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}

.mode-option {
  min-height: 64px;
  padding: 12px 16px;
  border: 1px solid #dcdfe6;
  color: #303133;
  background: #fff;
  box-shadow: 0 3px 10px rgba(0, 0, 0, 0.06);
  cursor: pointer;
  font: inherit;
  font-weight: 600;
  line-height: 1.4;
  text-align: left;
  transition: border-color 0.15s ease, color 0.15s ease, background 0.15s ease;
}

.mode-option:hover {
  border-color: #79bbff;
  color: #409eff;
}

.mode-option.active {
  border-color: #409eff;
  color: #fff;
  background: #409eff;
  box-shadow: 0 4px 12px rgba(64, 158, 255, 0.28);
}

.choose-tip {
  padding: 48px 20px;
  border: 1px dashed #c0c4cc;
  color: #909399;
  background: #fff;
  text-align: center;
}

.mode-hint {
  margin: -12px 0 24px;
  color: #606266;
  font-size: 13px;
}

.fetch-form {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 10px;
}

.info-panel {
  padding: 22px;
  border: 1px solid #ebeef5;
  background: #fff;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.06);
}

.data-impact {
  padding: 0;
}

.data-impact h2 {
  margin: 0 0 7px;
  font-size: 19px;
}

.impact-intro {
  margin: 0 0 14px;
  color: #606266;
  line-height: 1.6;
}

.impact-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.impact-item {
  padding: 15px;
  border-left: 4px solid #e6a23c;
  background: #fdf6ec;
}

.impact-title {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.impact-kind {
  flex: 0 0 auto;
  padding: 2px 7px;
  color: #b56b00;
  background: #faecd8;
  font-size: 12px;
  font-weight: 700;
}

.impact-item p {
  margin: 9px 0;
  color: #606266;
  font-size: 13px;
  line-height: 1.6;
}

.impact-item summary {
  color: #8a6200;
  cursor: pointer;
  font-size: 12px;
}

.impact-item code {
  display: block;
  margin-top: 7px;
  padding: 8px;
  color: #4f4f4f;
  background: rgba(255, 255, 255, 0.72);
  font-size: 11px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}

.converter-box {
  padding: 18px;
  border: 1px solid #dcdfe6;
  background: #fff;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.06);
}

.source-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
}

.source-toolbar span {
  flex: 1;
  color: #909399;
  font-size: 13px;
}

.convert-row {
  display: flex;
  justify-content: flex-end;
  margin-top: 10px;
}

.result-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-top: 16px;
  padding: 14px 16px;
  border-left: 4px solid #67c23a;
  background: #f0f9eb;
}

.result-actions strong {
  color: #529b2e;
}

.share-panel { margin-top: 16px; }
.share-panel p { color: #606266; font-size: 13px; }
.result-actions > div { display: flex; flex-wrap: wrap; gap: 8px; }
.result-actions :deep(.el-button + .el-button) { margin-left: 0; }

.mono-area :deep(textarea) {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  line-height: 1.5;
}

@media (max-width: 760px) {
  .page-heading h1 {
    font-size: 25px;
  }

  .mode-grid,
  .impact-list {
    grid-template-columns: 1fr;
  }

  .info-panel {
    padding: 16px;
  }

  .fetch-form {
    display: flex;
    flex-direction: column;
  }

  .impact-title {
    flex-direction: column;
  }

  .source-toolbar,
  .result-actions {
    align-items: flex-start;
    flex-direction: column;
  }

  .result-actions > div {
    display: flex;
    flex-wrap: wrap;
  }
}
</style>
