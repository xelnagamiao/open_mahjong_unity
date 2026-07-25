<template>
  <div class="record-convert">
    <header class="page-banner">
      <h1>牌谱格式转换</h1>
      <p>
        浏览器本地转换，支持国标：雀渣 ↔ salasasa ↔ Botzone；日麻：salasasa ↔ MJAI。
        反向重建（尤其 Botzone→salasasa、salasasa→雀渣）在隐藏信息处为近似，请核对后再用于训练。
      </p>
    </header>

    <section class="card">
      <header class="card-header"><span>转换方向</span></header>
      <div class="mode-grid">
        <button
          v-for="m in modes"
          :key="m.id"
          type="button"
          class="mode-btn"
          :class="{ active: modeId === m.id, riichi: m.group === 'riichi' }"
          @click="modeId = m.id"
        >
          {{ m.label }}
        </button>
      </div>
      <p class="hint">{{ currentMode?.hint }}</p>
      <p v-if="currentMode?.reliability" class="reliability">
        可靠程度：<strong>{{ currentMode.reliability }}</strong>
      </p>

      <div v-if="currentMode?.approxRows?.length" class="field-table-wrap">
        <table class="field-table">
          <thead>
            <tr>
              <th>字段 / 内容</th>
              <th>结果</th>
              <th>怎么处理</th>
              <th>例子</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(row, i) in currentMode.approxRows" :key="i">
              <td class="col-field">{{ row.field }}</td>
              <td class="col-status">
                <span class="tag" :class="statusClass(row.status)">{{ row.status }}</span>
              </td>
              <td class="col-how">{{ row.how }}</td>
              <td class="col-ex"><code>{{ row.example }}</code></td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="card">
      <header class="card-header">
        <span>输入</span>
        <div class="header-actions">
          <input ref="fileInput" type="file" :accept="currentMode?.accept" hidden @change="onFile" />
          <el-button size="small" @click="fileInput?.click()">选择文件</el-button>
          <el-button size="small" @click="inputText = ''">清空</el-button>
        </div>
      </header>
      <el-input
        v-model="inputText"
        type="textarea"
        :rows="14"
        placeholder="粘贴 JSON / NDJSON / Botzone 协议文本…"
        class="mono-area"
      />
    </section>

    <div class="actions">
      <el-button type="primary" :loading="busy" @click="runConvert">转换</el-button>
      <el-button :disabled="!outputText" @click="copyOut">复制结果</el-button>
      <el-button :disabled="!outputText" @click="downloadOut">下载</el-button>
      <span v-if="error" class="err">{{ error }}</span>
      <span v-else-if="okMsg" class="ok">{{ okMsg }}</span>
    </div>

    <section class="card">
      <header class="card-header"><span>输出</span></header>
      <el-input v-model="outputText" type="textarea" :rows="16" readonly class="mono-area" />
    </section>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { CONVERT_MODES, getMode } from '@/utils/recordConvert'

const modes = CONVERT_MODES
const modeId = ref('tz2sala')
const inputText = ref('')
const outputText = ref('')
const busy = ref(false)
const error = ref('')
const okMsg = ref('')
const fileInput = ref(null)

const currentMode = computed(() => getMode(modeId.value))

function statusClass(status) {
  if (status === '完整') return 'ok'
  if (status === '格式限制') return 'limit'
  if (status === '缺失') return 'miss'
  return 'approx'
}

async function onFile(ev) {
  const file = ev.target.files?.[0]
  if (!file) return
  inputText.value = await file.text()
  ev.target.value = ''
}

async function runConvert() {
  error.value = ''
  okMsg.value = ''
  outputText.value = ''
  const mode = currentMode.value
  if (!mode) return
  if (!inputText.value.trim()) {
    error.value = '请先粘贴或选择输入'
    return
  }
  busy.value = true
  try {
    outputText.value = await mode.convert(inputText.value)
    okMsg.value = `完成：${mode.label}`
  } catch (e) {
    error.value = e?.message || String(e)
  } finally {
    busy.value = false
  }
}

async function copyOut() {
  try {
    await navigator.clipboard.writeText(outputText.value)
    ElMessage.success('已复制')
  } catch {
    ElMessage.error('复制失败')
  }
}

function downloadOut() {
  const mode = currentMode.value
  const blob = new Blob([outputText.value], { type: 'application/json;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = mode?.filename || 'converted.json'
  a.click()
  URL.revokeObjectURL(url)
}
</script>

<style scoped>
.record-convert {
  max-width: 960px;
  margin: 0 auto;
}

.page-banner h1 {
  margin: 0 0 8px;
  font-size: 1.6rem;
}

.page-banner p {
  margin: 0 0 16px;
  color: #555;
  line-height: 1.55;
  font-size: 0.95rem;
}

.reliability {
  margin: 8px 0 12px;
  font-size: 0.9rem;
  color: #444;
}

.field-table-wrap {
  overflow-x: auto;
  border: 1px solid #e8e8e8;
  border-radius: 8px;
}

.field-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
  line-height: 1.45;
}

.field-table th,
.field-table td {
  border-bottom: 1px solid #eee;
  padding: 8px 10px;
  vertical-align: top;
  text-align: left;
}

.field-table th {
  background: #f7f7f7;
  font-weight: 600;
  white-space: nowrap;
}

.field-table tr:last-child td {
  border-bottom: none;
}

.col-field {
  min-width: 140px;
  font-weight: 600;
  color: #333;
}

.col-status {
  width: 88px;
  white-space: nowrap;
}

.col-how {
  min-width: 200px;
  color: #444;
}

.col-ex code {
  display: block;
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 0.78rem;
  background: #f4f6f8;
  padding: 6px 8px;
  border-radius: 4px;
  color: #1a1a1a;
}

.tag {
  display: inline-block;
  padding: 1px 7px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
}

.tag.ok {
  background: #e8f8ef;
  color: #1f7a45;
}

.tag.approx {
  background: #fff4e0;
  color: #9a6700;
}

.tag.miss {
  background: #fdecea;
  color: #b42318;
}

.tag.limit {
  background: #eef2ff;
  color: #3b4cca;
}

.card {
  background: #fff;
  border-radius: 10px;
  border: 1px solid #e8e8e8;
  padding: 14px 16px 16px;
  margin-bottom: 14px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
  margin-bottom: 10px;
}

.header-actions {
  display: flex;
  gap: 8px;
}

.mode-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 8px;
}

.mode-btn {
  text-align: left;
  padding: 10px 12px;
  border-radius: 8px;
  border: 1px solid #ddd;
  background: #fafafa;
  cursor: pointer;
  font-size: 0.9rem;
}

.mode-btn.riichi {
  border-color: #c5d4f0;
}

.mode-btn.active {
  border-color: #409eff;
  background: #ecf5ff;
  color: #1d4f91;
  font-weight: 600;
}

.mode-btn.riichi.active {
  border-color: #67c23a;
  background: #f0f9eb;
  color: #3a7a1c;
}

.hint {
  margin: 10px 0 0;
  color: #888;
  font-size: 0.85rem;
  line-height: 1.45;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
}

.err {
  color: #c0392b;
  font-size: 0.9rem;
}

.ok {
  color: #2d8a4e;
  font-size: 0.9rem;
}

.mono-area :deep(textarea) {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  line-height: 1.45;
}
</style>
