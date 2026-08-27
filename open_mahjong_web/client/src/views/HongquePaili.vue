<!--
  虹雀² 牌理页面（路由 /paili/hongque）
  前端计算向听与进张效率：已和仍可扩展听牌；所有向听（含 3/4/5 向听以上）都列出能减少向听的进张。
  每种牌只有一张。不请求服务器。
-->
<template>
  <div class="hongque-paili">
    <div class="page-header">
      <PailiSwitcher />
      <p class="subtitle">
        12/13 张显示向听与进张效率，14 张显示切牌效率。
        已和牌仍会列出可继续扩展的进张；一向听、两向听、三向听及以上都会计算能减少向听的进张。
        每种牌只有一张。输入为空时随机生成 14 张。计算在浏览器本地完成。
      </p>
    </div>

    <section class="main-card">
      <header class="card-header">
        <span>输入</span>
        <div class="header-actions">
          <el-button type="text" size="small" @click="loadDemo">示例</el-button>
          <el-button type="text" size="small" @click="resetAll">清空</el-button>
        </div>
      </header>

      <div class="row">
        <label class="row-label">牌码简写</label>
        <el-input
          v-model="textInput"
          placeholder="如 AX1 AX2 AX3 BX4 BX5 BX6"
          size="small"
          clearable
          @keydown.enter.prevent="analyze"
        />
        <el-button type="primary" size="small" plain :loading="loading" @click="analyze">解析</el-button>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">手牌 {{ form.hand.length }} 张</span>
          <el-tag size="small" :type="handCountTagType" effect="plain">{{ handCountText }}</el-tag>
        </div>
        <div class="hand-bar">
          <span
            v-for="(code, idx) in form.hand"
            :key="'h-' + code + '-' + idx"
            class="hq-tile"
            :title="code"
            @click.stop="onHandChipClick(idx)"
          >
            <img :src="tileFaceUrl(code)" :alt="code" draggable="false" @error="onTileError" />
          </span>
          <span v-if="!form.hand.length" class="empty-hint">点击下方牌面添加手牌，点击手牌移除</span>
        </div>
      </div>

      <div class="result-embed">
        <div v-if="loading" class="empty">
          <el-icon class="is-loading" :size="18"><Loading /></el-icon>
          <span>正在计算...</span>
        </div>
        <div v-else-if="!result" class="empty">
          <span class="input-target-bar">输入手牌后计算向听与进张</span>
        </div>
        <template v-else-if="result && result.mode === 'shanten'">
          <div class="meta-line nowrap">
            <span>当前向听 <strong>{{ formatShanten(result.shanten) }}</strong></span>
          </div>
          <div class="banner" :class="result.shanten < 1 ? 'success' : 'warning'">
            <strong>{{ result.shanten === -1 ? '和牌' : (result.is_tingpai ? '听牌' : formatShanten(result.shanten)) }}</strong>
            <span class="banner-sub">{{ acceptCaption(result.shanten) }} {{ result.total_accept }} 张</span>
          </div>
          <div class="paili-block">
            <h4>{{ acceptCaption(result.shanten) }}</h4>
            <div class="accept-list nowrap-scroll">
              <span v-for="a in result.accept" :key="'a-' + a.tile" class="accept-inline">
                <span class="hq-tile hq-tile-mini" :title="a.tile">
                  <img :src="tileFaceUrl(a.tile)" :alt="a.tile" draggable="false" @error="onTileError" />
                </span>
              </span>
              <span v-if="result.accept.length === 0" class="hint">无进张</span>
            </div>
          </div>
        </template>
        <template v-else-if="result && result.mode === 'discard'">
          <div class="meta-line nowrap">
            <span v-if="result.is_hepai">当前已和牌，切牌后仍可继续听</span>
            <span>最佳向听 <strong>{{ formatShanten(result.best_shanten) }}</strong></span>
          </div>
          <div class="discard-table">
            <div class="discard-row discard-head nowrap">
              <span class="col-tile">切</span>
              <span class="col-shanten">向听</span>
              <span class="col-total">进张</span>
              <span class="col-accept-h">有效进张</span>
            </div>
            <div
              v-for="d in result.discards"
              :key="'d-' + d.discard"
              class="discard-row nowrap"
              :class="{ 'is-best': d.shanten === result.best_shanten }"
            >
              <span class="col-tile">
                <span class="hq-tile hq-tile-mini" :title="d.discard">
                  <img :src="tileFaceUrl(d.discard)" :alt="d.discard" draggable="false" @error="onTileError" />
                </span>
              </span>
              <span class="col-shanten">{{ formatShanten(d.shanten) }}</span>
              <span class="col-total">
                <strong>{{ d.total_accept }}</strong>
              </span>
              <span class="col-accept">
                <span v-if="d.accept.length === 0" class="hint">无</span>
                <span v-else class="accept-inline-row nowrap-scroll">
                  <span v-for="a in d.accept" :key="'da-' + d.discard + '-' + a.tile" class="accept-inline">
                    <span class="hq-tile hq-tile-mini" :title="a.tile">
                      <img :src="tileFaceUrl(a.tile)" :alt="a.tile" draggable="false" @error="onTileError" />
                    </span>
                  </span>
                </span>
              </span>
            </div>
          </div>
        </template>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">牌面（添加到手牌，合计上限 14 张）</span>
        </div>
        <div class="palette">
          <div v-for="group in paletteGroups" :key="group.colour" class="palette-row">
            <span class="palette-code" :style="{ color: `hsl(${hueOf(group.colour)} 62% 38%)` }">{{ group.codePrefix }}</span>
            <span
              v-for="number in 9"
              :key="number"
              class="hq-tile hq-tile-sm"
              :class="{ used: isUsed(group.codePrefix + number) }"
              :title="group.codePrefix + number"
              @click="onPalettePick(group.codePrefix + number)"
            >
              <img :src="tileFaceUrl(group.codePrefix + number)" :alt="group.codePrefix + number" draggable="false" @error="onTileError" />
            </span>
          </div>
        </div>
      </div>

      <div class="actions">
        <el-button type="primary" size="default" :loading="loading" @click="analyze">计算牌理</el-button>
      </div>
    </section>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onBeforeUnmount } from 'vue'
import { ElMessage } from 'element-plus'
import { Loading } from '@element-plus/icons-vue'
import PailiSwitcher from '@/components/PailiSwitcher.vue'
import { calculateHongquePaili } from '@/game2d/calc/hongque'

const TILE_BASE_URL = '/game2d-assets/hongque-hand/'
const MAX_HAND = 14

const COLOUR_GROUPS = [
  { codePrefix: 'AX', colour: 0 },
  { codePrefix: 'AY', colour: 1 },
  { codePrefix: 'BX', colour: 2 },
  { codePrefix: 'BY', colour: 3 },
  { codePrefix: 'CX', colour: 4 },
  { codePrefix: 'CY', colour: 5 },
  { codePrefix: 'DX', colour: 6 },
  { codePrefix: 'DY', colour: 7 },
  { codePrefix: 'EX', colour: 8 },
  { codePrefix: 'EY', colour: 9 },
  { codePrefix: 'FX', colour: 10 },
  { codePrefix: 'FY', colour: 11 },
  { codePrefix: 'GX', colour: 12 },
  { codePrefix: 'GY', colour: 13 },
]

const paletteGroups = COLOUR_GROUPS
const ALL_CODES = COLOUR_GROUPS.flatMap((group) =>
  Array.from({ length: 9 }, (_, index) => group.codePrefix + (index + 1)),
)

function hueOf(colour) {
  return Math.round((colour / 14) * 360)
}

function tileFaceUrl(code) {
  return `${TILE_BASE_URL}${code}.png`
}

const textInput = ref('')
const loading = ref(false)
const result = ref(null)
const form = reactive({ hand: [] })

const workerRequests = new Map()
let pailiWorker = null
let workerRequestId = 0

const terminateWorker = (error) => {
  pailiWorker?.terminate()
  pailiWorker = null
  for (const request of workerRequests.values()) {
    clearTimeout(request.timer)
    request.reject(error)
  }
  workerRequests.clear()
}

const getWorker = () => {
  if (pailiWorker || typeof Worker === 'undefined') return pailiWorker
  try {
    pailiWorker = new Worker(
      new URL('../utils/hongquePailiWorker.ts', import.meta.url),
      { type: 'module' },
    )
    pailiWorker.onmessage = ({ data }) => {
      const request = workerRequests.get(data.id)
      if (!request) return
      workerRequests.delete(data.id)
      clearTimeout(request.timer)
      if (data.error) request.reject(new Error(data.error))
      else request.resolve(data.result)
    }
    pailiWorker.onerror = () => {
      terminateWorker(new Error('Web Worker 运行失败'))
    }
  } catch (error) {
    pailiWorker = null
    console.warn('无法创建虹雀牌理 Worker，将在主线程计算', error)
  }
  return pailiWorker
}

const calculateInWorker = (hand) => {
  const worker = getWorker()
  if (!worker) return Promise.resolve(calculateHongquePaili(hand))
  return new Promise((resolve, reject) => {
    const id = ++workerRequestId
    const timer = setTimeout(() => {
      terminateWorker(new Error('Web Worker 计算超时'))
    }, 20_000)
    workerRequests.set(id, { resolve, reject, timer })
    worker.postMessage({ id, hand })
  })
}

onBeforeUnmount(() => terminateWorker(new Error('页面已关闭')))

const allUsedCodes = computed(() => new Set(form.hand))
const isUsed = (code) => allUsedCodes.value.has(code)
const handCountText = computed(() => `${form.hand.length}/${MAX_HAND}`)
const handCountTagType = computed(() => {
  if (form.hand.length === 12 || form.hand.length === 13 || form.hand.length === MAX_HAND) return 'success'
  if (form.hand.length > MAX_HAND) return 'danger'
  return 'warning'
})

const formatShanten = (s) => {
  if (s === undefined || s === null) return '?'
  if (s === -1) return '和牌'
  if (s === 0) return '听牌'
  return `${s} 向听`
}

const acceptCaption = (shanten) => {
  if (shanten === -1) return '可继续扩展'
  if (shanten === 0) return '听牌进张'
  return '有效进张'
}

const parseCodes = (text) => {
  const tokens = String(text || '')
    .split(/[\s,，;；]+/)
    .map((token) => token.trim().toUpperCase())
    .filter(Boolean)
  const codes = []
  for (const token of tokens) {
    if (!/^[A-G][XY][1-9]$/.test(token)) {
      throw new Error(`非法牌码：${token}（应为 AX1～GY9 格式）`)
    }
    if (codes.includes(token)) throw new Error(`虹雀牌不可重复：${token}`)
    codes.push(token)
  }
  return codes
}

const sortHandCodes = (codes) => {
  codes.sort((a, b) => {
    const colourA = COLOUR_GROUPS.findIndex((group) => group.codePrefix === a.slice(0, 2))
    const colourB = COLOUR_GROUPS.findIndex((group) => group.codePrefix === b.slice(0, 2))
    if (colourA !== colourB) return colourA - colourB
    return Number(a[2]) - Number(b[2])
  })
  return codes
}

const addHandTile = (code) => {
  if (form.hand.includes(code)) {
    form.hand.splice(form.hand.indexOf(code), 1)
    textInput.value = form.hand.join(' ')
    return
  }
  if (form.hand.length >= MAX_HAND) {
    ElMessage.warning(`手牌已达上限 ${MAX_HAND} 张`)
    return
  }
  form.hand.push(code)
  sortHandCodes(form.hand)
  textInput.value = form.hand.join(' ')
}

const removeHandTile = (idx) => {
  form.hand.splice(idx, 1)
  textInput.value = form.hand.join(' ')
}

const onPalettePick = (code) => addHandTile(code)
const onHandChipClick = (idx) => removeHandTile(idx)

const loadDemo = () => {
  form.hand = sortHandCodes(parseCodes('AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 CX9 DY1 DY2 GY9'))
  textInput.value = form.hand.join(' ')
  analyze()
}

const resetAll = () => {
  form.hand = []
  textInput.value = ''
  result.value = null
}

const onTileError = (event) => {
  event.target.style.opacity = '0.25'
}

const ensureReadyForAnalyze = () => {
  if (textInput.value?.trim()) {
    try {
      form.hand = sortHandCodes(parseCodes(textInput.value))
      textInput.value = form.hand.join(' ')
    } catch (error) {
      ElMessage.error(`简写解析失败：${error.message}`)
      return false
    }
  }
  if (form.hand.length === 0) {
    const shuffled = [...ALL_CODES]
    for (let i = shuffled.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1))
      ;[shuffled[i], shuffled[j]] = [shuffled[j], shuffled[i]]
    }
    form.hand = sortHandCodes(shuffled.slice(0, MAX_HAND))
    textInput.value = form.hand.join(' ')
    return true
  }
  if (form.hand.length < 12 || form.hand.length > MAX_HAND) {
    ElMessage.error(`手牌须为 12～14 张，当前 ${form.hand.length} 张`)
    return false
  }
  return true
}

const analyze = async () => {
  if (!ensureReadyForAnalyze()) return
  loading.value = true
  result.value = null
  try {
    try {
      result.value = await calculateInWorker([...form.hand])
    } catch (workerError) {
      console.warn('虹雀牌理 Worker 不可用，将在主线程重试', workerError)
      result.value = calculateHongquePaili([...form.hand])
    }
  } catch (error) {
    console.error(error)
    ElMessage.error(`计算失败：${error.message}`)
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.hongque-paili {
  max-width: 920px;
  margin: 0 auto;
  padding: 12px 0 20px;
  box-sizing: border-box;
}

.page-header {
  text-align: center;
  margin: 0 auto 16px;
  color: white;
}

.subtitle {
  margin: 0;
  font-size: 0.9rem;
  color: rgba(255, 255, 255, 0.95);
  opacity: 0.95;
}

.main-card {
  background: rgba(255, 255, 255, 0.96);
  border: 1px solid #bfdbfe;
  border-radius: 12px;
  overflow: hidden;
  box-shadow: 0 4px 16px rgba(15, 45, 110, 0.2);
}

.card-header {
  padding: 8px 12px;
  background: #eff6ff;
  border-bottom: 1px solid #dbeafe;
  font-size: 13px;
  font-weight: 600;
  color: #1e40af;
  display: flex;
  justify-content: space-between;
  align-items: center;
  letter-spacing: 0.5px;
}

.header-actions {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.row {
  padding: 8px 12px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.row.block { display: block; }

.row + .row {
  border-top: 1px dashed #dbeafe;
}

.row-label {
  font-size: 12.5px;
  color: #334155;
  font-weight: 600;
  flex-shrink: 0;
}

.row-line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
}

.hand-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  min-height: 52px;
  padding: 6px;
  background: #f8fafc;
  border-radius: 6px;
  border: 1px dashed #93c5fd;
  align-items: center;
}

.empty-hint {
  color: #94a3b8;
  font-size: 12px;
}

.hint {
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12.5px;
}

.result-embed {
  border-top: 1px solid #dbeafe;
  background: #f8fafc;
  min-height: 48px;
}

.result-embed .empty {
  padding: 10px 12px;
  text-align: center;
  color: #94a3b8;
  font-size: 12.5px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 44px;
}

.meta-line {
  padding: 8px 12px 0;
  font-size: 12.5px;
  color: #475569;
}

.meta-line strong { color: #2563eb; margin-left: 4px; }

.banner {
  margin: 8px 12px;
  padding: 10px 12px;
  border-radius: 8px;
  display: flex;
  align-items: baseline;
  gap: 10px;
  border: 1px solid;
}

.banner.success {
  background: #f0f9eb;
  border-color: #86efac;
  color: #166534;
}

.banner.warning {
  background: #fefce8;
  border-color: #fde047;
  color: #713f12;
}

.banner strong { font-size: 0.95rem; }
.banner-sub { font-size: 12px; opacity: 0.85; }

.paili-block { padding: 0 12px 10px; }
.paili-block h4 {
  margin: 6px 0;
  font-size: 12.5px;
  color: #475569;
  letter-spacing: 0.5px;
}

.accept-list {
  padding: 4px 6px;
  background: #f1f5f9;
  border-radius: 6px;
  border: 1px dashed #cbd5e1;
}

.nowrap { white-space: nowrap; }
.nowrap-scroll {
  white-space: nowrap;
  overflow-x: auto;
  max-width: 100%;
  padding-bottom: 2px;
}

.accept-inline {
  display: inline-flex;
  align-items: baseline;
  gap: 2px;
  margin-right: 6px;
}

.discard-table {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
  font-size: 12px;
}

.discard-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 10px;
  border-bottom: 1px solid #dbeafe;
  min-height: 28px;
}

.discard-row:last-child { border-bottom: none; }

.discard-head {
  background: #f1f5f9;
  font-size: 11px;
  letter-spacing: 0.5px;
  color: #475569;
  font-weight: 600;
}

.discard-row.is-best {
  background: #f0f9ff;
}

.discard-row.is-best .col-shanten { color: #2563eb; font-weight: 600; }

.col-tile { flex: 0 0 auto; }
.col-shanten {
  flex: 0 0 64px;
  font-family: var(--omu-mono, 'Consolas', monospace);
  color: #1f2933;
}
.col-total {
  flex: 0 0 68px;
  display: inline-flex;
  align-items: baseline;
  gap: 2px;
  font-family: var(--omu-mono, 'Consolas', monospace);
}
.col-total strong { color: #1f2933; font-size: 0.9rem; }

.col-accept-h { flex: 1 1 0; min-width: 0; }
.col-accept {
  flex: 1 1 0;
  min-width: 0;
  overflow: hidden;
}

.accept-inline-row {
  display: inline-block;
  vertical-align: middle;
}

.palette {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.palette-row {
  display: flex;
  align-items: center;
  gap: 4px;
}

.palette-code {
  width: 28px;
  font-size: 11px;
  font-weight: 700;
  flex-shrink: 0;
  text-align: center;
}

.hq-tile {
  display: inline-flex;
  width: 34px;
  height: 46px;
  border-radius: 5px;
  overflow: hidden;
  border: 1.5px solid #cbd5e1;
  background: white;
  cursor: pointer;
  user-select: none;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
  transition: transform 0.1s ease, box-shadow 0.1s ease;
  flex-shrink: 0;
}

.hq-tile:hover {
  transform: translateY(-2px);
  box-shadow: 0 3px 8px rgba(0, 0, 0, 0.28);
}

.hq-tile img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  display: block;
}

.hq-tile-sm {
  width: 28px;
  height: 38px;
}

.hq-tile-mini {
  width: 24px;
  height: 32px;
  cursor: default;
}

.hq-tile-mini:hover {
  transform: none;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
}

.hq-tile.used {
  opacity: 0.3;
  cursor: not-allowed;
}

.hq-tile.used:hover {
  transform: none;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
}

.actions {
  padding: 8px 12px;
  border-top: 1px dashed #dbeafe;
  background: #f1f5f9;
  text-align: right;
}
</style>
