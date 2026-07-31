<template>
  <div class="record-convert">
    <header class="page-heading">
      <h1>牌谱格式转换</h1>
    </header>

    <section class="section-block mode-section">
      <div class="sec-h">■ 选择转换方向</div>
      <div class="mode-grid">
        <button
          v-for="item in modes"
          :key="item.id"
          type="button"
          class="mode-option"
          :class="{ active: modeId === item.id }"
          :aria-pressed="modeId === item.id"
          @click="selectMode(item.id)"
        >
          {{ item.label }}
        </button>
      </div>
    </section>

    <div v-if="!currentMode" class="choose-tip">
      请先在上方选择一种转换方向。
    </div>

    <template v-else>
      <section class="section-block">
        <div class="info-panel">
          <div class="data-impact">
            <h2>转换后不会原样保留的数据</h2>
            <p class="impact-intro">来源格式和目标格式记录的内容不同，以下数据会缺少、被省略，或只能重新推算。</p>
            <div class="impact-list">
              <article v-for="(row, index) in affectedRows" :key="index" class="impact-item">
                <div class="impact-title">
                  <strong>{{ row.field }}</strong>
                  <span class="impact-kind">{{ lossKind(row.status) }}</span>
                </div>
                <p>{{ row.how }}</p>
                <details>
                  <summary>查看示例</summary>
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
              clearable
              placeholder="粘贴雀渣牌谱链接或牌谱 ID"
              @keyup.enter="fetchTziakchaRecord"
            />
            <el-button type="primary" :loading="fetchBusy" @click="fetchTziakchaRecord">转换</el-button>
          </div>
        </template>

        <template v-else>
          <input ref="fileInput" type="file" :accept="currentMode.accept" hidden @change="onFile" />
          <div class="source-toolbar">
            <el-button type="primary" plain @click="fileInput?.click()">选择牌谱文件</el-button>
            <span>{{ selectedFileName || '或在下方粘贴牌谱内容' }}</span>
            <el-button v-if="inputText" link type="primary" @click="clearAll">清空</el-button>
          </div>
          <el-input
            id="record-source"
            v-model="inputText"
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
              :disabled="!inputText.trim()"
              @click="runConvert"
            >转换</el-button>
          </div>
        </template>

        <el-alert
          v-if="error"
          type="error"
          title="转换失败"
          :description="`${error}。请确认转换方向正确，并检查输入内容是否完整。`"
          :closable="false"
          show-icon
        />

        <div v-if="outputText" class="result-actions">
          <strong>转换完成</strong>
          <div>
            <el-button type="success" @click="downloadOut">下载 JSON</el-button>
            <el-button v-if="canOpenIn2d" type="warning" @click="openIn2d">打开 2D 阅览</el-button>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import axios from 'axios'
import { ElMessage } from 'element-plus'
import { CONVERT_MODES, getMode } from '@/utils/recordConvert'
import { saveLocalReplayRecord } from '@/game2d/replay/localReplayRecord'

const modes = CONVERT_MODES
const router = useRouter()
const modeId = ref('')
const inputText = ref('')
const outputText = ref('')
const busy = ref(false)
const error = ref('')
const fileInput = ref(null)
const selectedFileName = ref('')
const tziakchaInput = ref('')
const fetchBusy = ref(false)

const currentMode = computed(() => getMode(modeId.value))
const affectedRows = computed(() => currentMode.value?.approxRows?.filter((row) => row.status !== '完整') || [])
const canOpenIn2d = computed(() => outputText.value && ['tz2sala', 'bz2sala', 'mjai2sala'].includes(modeId.value))

function selectMode(id) {
  modeId.value = id
  outputText.value = ''
  error.value = ''
}

function lossKind(status) {
  if (status.includes('缺失') && status.includes('近似')) return '部分缺失或推算'
  if (status.includes('缺失')) return '不会保留'
  if (status === '格式限制') return '目标格式不记录'
  return '重新推算或使用占位值'
}

async function readFile(file) {
  if (!file) return
  try {
    inputText.value = await file.text()
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
  selectedFileName.value = ''
  outputText.value = ''
  error.value = ''
}

function clearAll() {
  inputText.value = ''
  outputText.value = ''
  selectedFileName.value = ''
  error.value = ''
}

async function fetchTziakchaRecord() {
  const input = tziakchaInput.value.trim()
  if (!input) {
    ElMessage.warning('请先粘贴雀渣牌谱链接或牌谱 ID')
    return
  }
  fetchBusy.value = true
  error.value = ''
  try {
    const response = await axios.post('/api/mahjong/tziakcha-record', { input })
    inputText.value = JSON.stringify(response.data?.data || {}, null, 2)
    selectedFileName.value = '已从雀渣读取牌谱'
    await runConvert()
  } catch (cause) {
    error.value = cause?.response?.data?.message || cause?.message || '无法读取雀渣牌谱'
  } finally {
    fetchBusy.value = false
  }
}

async function runConvert() {
  error.value = ''
  outputText.value = ''
  const mode = currentMode.value
  if (!mode || !inputText.value.trim()) return

  busy.value = true
  try {
    outputText.value = await mode.convert(inputText.value)
  } catch (cause) {
    error.value = cause?.message || String(cause)
  } finally {
    busy.value = false
  }
}

function openIn2d() {
  try {
    const gameId = saveLocalReplayRecord(JSON.parse(outputText.value))
    router.push(`/2d/record/${encodeURIComponent(gameId)}`)
  } catch (cause) {
    ElMessage.error(cause?.message || '无法打开 2D 牌谱')
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
