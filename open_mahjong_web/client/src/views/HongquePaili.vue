<!--
  虹雀² 牌理页面（路由 /paili/hongque）
  基于虹雀² v1.6 规则：分析手牌听牌/进张（12/13张），或切牌分析（14张）。
  所有牌必须组成 3+ 张的合法牌组，无雀头。
  牌面使用虹雀手牌贴图。
-->
<template>
  <div class="hongque-paili">
    <div class="page-header">
      <PailiSwitcher />
      <p class="subtitle">
        12/13 张直接显示听牌与进张，14 张显示切牌分析。
        输入为空时将随机生成示例。
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
        <div
          class="hand-bar"
          :class="{ active: activeFuluIdx < 0 }"
          @click="activateHand"
        >
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
          <span class="input-target-bar">{{ inputTargetLabel }}</span>
        </div>
        <template v-else-if="result && result.mode === 'shanten'">
          <div class="meta-line nowrap">
            <span>当前向听 <strong>{{ formatShanten(result.shanten) }}</strong></span>
          </div>
          <div class="banner" :class="result.is_tingpai ? 'success' : 'warning'">
            <strong>{{ result.is_tingpai ? '听牌' : `向听 ${formatShanten(result.shanten)}` }}</strong>
            <span class="banner-sub">进张 {{ result.total_accept }} 张 · {{ result.accept.length }} 种</span>
          </div>
          <div class="paili-block">
            <h4>进张</h4>
            <div class="accept-list nowrap-scroll">
              <span v-for="a in result.accept" :key="'a-' + a.tile" class="accept-inline">
                <span class="hq-tile hq-tile-mini" :title="a.tile">
                  <img :src="tileFaceUrl(a.tile)" :alt="a.tile" draggable="false" @error="onTileError" />
                </span>
                <span class="accept-count">{{ a.remaining }}</span>
              </span>
              <span v-if="result.accept.length === 0" class="hint">已和牌或无进张</span>
            </div>
          </div>
        </template>
        <template v-else-if="result && result.mode === 'discard'">
          <div class="meta-line nowrap">
            <span>最佳向听 <strong>{{ formatShanten(result.best_shanten) }}</strong></span>
          </div>
          <div class="discard-table">
            <div class="discard-row discard-head nowrap">
              <span class="col-tile">切</span>
              <span class="col-shanten">向听</span>
              <span class="col-total">进张</span>
              <span class="col-accept-h">摸</span>
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
                <strong>{{ d.total_accept }}</strong><span class="hint">/{{ d.accept.length }}</span>
              </span>
              <span class="col-accept">
                <span v-if="d.accept.length === 0" class="hint">无</span>
                <span v-else class="accept-inline-row nowrap-scroll">
                  <span v-for="a in d.accept" :key="'da-' + d.discard + '-' + a.tile" class="accept-inline">
                    <span class="hq-tile hq-tile-mini" :title="a.tile">
                      <img :src="tileFaceUrl(a.tile)" :alt="a.tile" draggable="false" @error="onTileError" />
                    </span>
                    <span class="accept-count-mini">{{ a.remaining }}</span>
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
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Loading } from '@element-plus/icons-vue'
import PailiSwitcher from '@/components/PailiSwitcher.vue'
import { winningDecompositions } from '@/game2d/calc/hongque'

const TILE_BASE_URL = '/game2d-assets/hongque-hand/'

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

function hueOf(colour) {
  return Math.round((colour / 14) * 360)
}

function tileFaceUrl(code) {
  return `${TILE_BASE_URL}${code}.png`
}

const textInput = ref('')
const loading = ref(false)
const result = ref(null)
const activeFuluIdx = ref(-1)

const form = reactive({
  hand: [],
})

const expectedCount = 14

const allUsedCodes = computed(() => new Set(form.hand))

const totalTiles = computed(() => form.hand.length)

const isUsed = (code) => allUsedCodes.value.has(code)

const handCountText = computed(() => `${form.hand.length}/${expectedCount}`)

const handCountTagType = computed(() => {
  if (form.hand.length === 0) return 'warning'
  if (form.hand.length === 12 || form.hand.length === 13) return 'success'
  if (form.hand.length === expectedCount) return 'success'
  if (form.hand.length > expectedCount) return 'danger'
  return 'warning'
})

const inputTargetLabel = computed(() => {
  if (activeFuluIdx.value >= 0) return `输入副露 #${activeFuluIdx.value + 1}`
  return '输入手牌'
})

const formatShanten = (s) => {
  if (s === undefined || s === null) return '?'
  if (s === -1) return '和牌'
  if (s === 0) return '听牌'
  return `${s} 向听`
}

// Parse tile codes from text input
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
    codes.push(token)
  }
  return codes
}

// Sort hand codes by (colour, number)
const sortHandCodes = (codes) => {
  codes.sort((a, b) => {
    const colourA = COLOUR_GROUPS.findIndex(g => g.codePrefix === a.slice(0, 2))
    const colourB = COLOUR_GROUPS.findIndex(g => g.codePrefix === b.slice(0, 2))
    if (colourA !== colourB) return colourA - colourB
    return Number(a[2]) - Number(b[2])
  })
  return codes
}

// Count remaining tiles in deck
function countRemainingInDeck(hand, tile) {
  const DECK_SIZE = 3 // Each tile appears 3 times in the deck
  return DECK_SIZE - hand.filter(t => t === tile).length
}

// Calculate shanten for Hongque hand
// Shanten = how many tiles needed to reach a winning hand
// Winning hand: all tiles form valid groups (3+ each), total 12-14 tiles
function calculateShanten(hand) {
  // A winning Hongque hand must have 12-14 tiles all in valid groups
  // Total tiles = 3 * groupCount (no head/pair)
  const n = hand.length
  if (n === 0) return 14
  if (n < 12) return 12 - n
  if (n === 12 || n === 13 || n === 14) {
    // Check if it's a winning hand
    const decomps = winningDecompositions(hand, [])
    if (decomps.length > 0) return -1 // Already winning (tenpai)
    // Not winning, calculate shanten
    if (n === 14) {
      // For 14 tiles, we need to find the best discard
      // Shanten = minimum tiles needed to reach a winning state
      // For a 14-tile hand, shanten = 2 means: removing one tile gives a 13-tile hand with shanten=1
      // Removing one tile from winning hand gives shanten = 1
      // Removing two tiles from winning hand gives shanten = 2
      return Math.max(0, 2 - (14 - n))
    }
    return 1 // 12 or 13 tiles not winning -> at least 1 away
  }
  return 14 - n
}

// Find all winning decompositions for a hand
function findWinners(hand) {
  return winningDecompositions(hand, [])
}

// Analyze tingpai (waiting) - for 12/13 tile hands
function analyzeTingpai(hand) {
  if (hand.length < 12 || hand.length > 13) return null

  // Check if hand is already winning
  const winners = findWinners(hand)
  if (winners.length > 0) {
    return {
      mode: 'shanten',
      shanten: -1,
      is_tingpai: true,
      accept: [],
      total_accept: 0,
    }
  }

  // For each possible tile to draw, check if it completes a winning hand
  const accept = []
  const tried = new Set(hand)
  for (const code of tried) continue // skip tiles already in hand

  // Try each possible tile
  const allCodes = COLOUR_GROUPS.flatMap(group =>
    Array.from({ length: 9 }, (_, i) => group.codePrefix + (i + 1))
  )

  for (const candidate of allCodes) {
    if (tried.has(candidate)) continue
    const testHand = [...hand, candidate]
    const decomps = winningDecompositions(testHand, [])
    if (decomps.length > 0) {
      accept.push({
        tile: candidate,
        remaining: countRemainingInDeck(hand, candidate),
      })
    }
  }

  const shanten = hand.length < 12 ? 12 - hand.length : 1
  return {
    mode: 'shanten',
    shanten,
    is_tingpai: accept.length > 0,
    accept: accept.sort((a, b) => a.tile.localeCompare(b.tile)),
    total_accept: accept.reduce((sum, a) => sum + a.remaining, 0),
  }
}

// Analyze discards (for 14 tile hands)
function analyzeDiscards(hand) {
  if (hand.length !== 14) return null

  const winners = findWinners(hand)
  if (winners.length > 0) {
    return {
      mode: 'shanten',
      shanten: -1,
      is_tingpai: true,
      accept: [],
      total_accept: 0,
    }
  }

  const discardResults = []

  for (let i = 0; i < hand.length; i++) {
    const discarded = hand[i]
    const remaining = hand.filter((_, idx) => idx !== i)
    const decomps = winningDecompositions(remaining, [])

    let shanten = 1 // Removing one tile from 14 -> 13 tiles, usually shanten=1 unless already winning
    if (decomps.length > 0) {
      shanten = 0 // Removing this tile makes it a winning hand -> tenpai
    }

    // Find accept tiles for the remaining hand
    const accept = []
    const tried = new Set(remaining)
    const allCodes = COLOUR_GROUPS.flatMap(group =>
      Array.from({ length: 9 }, (_, i) => group.codePrefix + (i + 1))
    )

    for (const candidate of allCodes) {
      if (tried.has(candidate)) continue
      const testHand = [...remaining, candidate]
      const candDecomps = winningDecompositions(testHand, [])
      if (candDecomps.length > 0) {
        accept.push({
          tile: candidate,
          remaining: countRemainingInDeck(remaining, candidate),
        })
      }
    }

    discardResults.push({
      discard: discarded,
      shanten,
      accept: accept.sort((a, b) => a.tile.localeCompare(b.tile)),
      total_accept: accept.reduce((sum, a) => sum + a.remaining, 0),
    })
  }

  // Sort by shanten (best first), then by total accept (most first)
  discardResults.sort((a, b) => {
    if (a.shanten !== b.shanten) return a.shanten - b.shanten
    return b.total_accept - a.total_accept
  })

  return {
    mode: 'discard',
    best_shanten: discardResults[0]?.shanten ?? 1,
    discards: discardResults,
  }
}

const onPalettePick = (code) => {
  addHandTile(code)
}

const addHandTile = (code) => {
  if (form.hand.includes(code)) {
    removeHandTile(form.hand.indexOf(code))
    return
  }
  if (form.hand.length >= expectedCount) {
    ElMessage.warning(`手牌已达上限 ${expectedCount} 张`)
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

const onHandChipClick = (idx) => {
  activateHand()
  removeHandTile(idx)
}

const activateHand = () => {
  activeFuluIdx.value = -1
}

const activateFulu = (idx) => {
  activeFuluIdx.value = idx
}

const syncTextInput = () => {
  textInput.value = form.hand.join(' ')
}

const loadDemo = () => {
  form.hand = sortHandCodes(parseCodes('AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 CX9'))
  textInput.value = form.hand.join(' ')
  analyze()
}

const resetAll = () => {
  form.hand = []
  textInput.value = ''
  result.value = null
  activeFuluIdx.value = -1
}

const onTileError = (event) => {
  const img = event.target
  img.style.opacity = '0.25'
}

const ensureReadyForAnalyze = () => {
  if (textInput.value?.trim()) {
    try {
      form.hand = sortHandCodes(parseCodes(textInput.value))
      textInput.value = form.hand.join(' ')
    } catch (e) {
      ElMessage.error(`简写解析失败：${e.message}`)
      return false
    }
  }
  if (form.hand.length === 0) {
    // Random demo
    const demoCodes = COLOUR_GROUPS.flatMap(group =>
      Array.from({ length: 9 }, (_, i) => group.codePrefix + (i + 1))
    )
    const shuffled = demoCodes.sort(() => Math.random() - 0.5)
    form.hand = sortHandCodes(shuffled.slice(0, 14))
    textInput.value = form.hand.join(' ')
    return true
  }
  if (form.hand.length < 12 || form.hand.length > 14) {
    ElMessage.error(`手牌须为 12～14 张，当前 ${form.hand.length} 张`)
    return false
  }
  return true
}

const analyze = () => {
  if (!ensureReadyForAnalyze()) return
  loading.value = true
  result.value = null

  setTimeout(() => {
    try {
      if (form.hand.length === 14) {
        const r = analyzeDiscards(form.hand)
        if (r) result.value = r
      } else {
        const r = analyzeTingpai(form.hand)
        if (r) result.value = r
      }
    } catch (err) {
      console.error(err)
      ElMessage.error(`计算失败：${err.message}`)
    } finally {
      loading.value = false
    }
  }, 0)
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

.page-header h1 {
  font-size: 1.75rem;
  margin: 0 0 6px;
  font-weight: bold;
  color: white;
  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.3);
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
  cursor: pointer;
  transition: border-color 0.12s ease, background 0.12s ease;
  align-items: center;
}

.hand-bar.active {
  border-color: #2563eb;
  border-style: solid;
  background: #eff6ff;
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

.accept-count {
  font-size: 10px;
  color: #475569;
  font-family: var(--omu-mono, 'Consolas', monospace);
}

.accept-count-mini {
  font-size: 9px;
  color: #94a3b8;
  font-family: var(--omu-mono, 'Consolas', monospace);
  margin-right: 4px;
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
