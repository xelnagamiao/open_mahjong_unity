<!-- 牌理：单列紧凑，结果嵌在输入与按钮之间；空提交随机 14 张 -->
<template>
  <div class="paili">
    <div class="page-header">
      <h1>牌理</h1>
      <p class="subtitle">14 张（或副露等价张数）。手牌与简写均为空时点击「计算」将随机生成示例。</p>
    </div>

    <MahjongNotationHelp />

    <section class="main-card">
      <header class="card-header">
        <span>输入</span>
        <div class="header-actions">
          <TileFaceStyleSwitch />
          <el-button type="text" size="small" @click="loadDemo">示例</el-button>
          <el-button type="text" size="small" @click="resetAll">清空</el-button>
        </div>
      </header>

      <div class="row">
        <label class="row-label">牌面简写</label>
        <el-input
          v-model="textInput"
          placeholder="如 35m146678p24s344z5m"
          size="small"
          clearable
          @keydown.enter.prevent="analyze"
        />
        <el-button type="primary" size="small" plain :loading="loading" @click="analyze">解析</el-button>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">手牌 {{ form.hand.length }}/{{ expectedCount }}</span>
          <el-tag size="small" :type="handCountTagType" effect="plain">{{ handCountText }}</el-tag>
        </div>
        <div
          class="hand-bar"
          :class="{ active: activeFuluIdx < 0 }"
          @click="activateHand"
        >
          <TileChip
            v-for="(id, idx) in form.hand"
            :key="'h-' + idx"
            :tile-id="id"
            size="sm"
            @click="onHandChipClick(idx)"
          />
        </div>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">副露（{{ lockedFuluCount }}/4）</span>
        </div>
        <FuluSlots
          :slots="fuluSlots"
          :active-idx="activeFuluIdx"
          @activate="activateFulu"
          @clear="clearFuluSlot"
          @input="onFuluSlotInput"
          @lock="lockFuluSlot"
          @remove-draft="removeFuluDraft"
          @remove-locked="removeFuluLocked"
        />
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
          <div class="banner success nowrap">
            <strong>{{ result.is_tingpai ? '听牌' : `向听 ${formatShanten(result.shanten)}` }}</strong>
            <span class="banner-sub">进张 {{ result.total_accept }} 张 · {{ result.accept.length }} 种</span>
          </div>
          <div class="paili-block">
            <h4>进张</h4>
            <div class="accept-list nowrap-scroll">
              <span v-for="a in result.accept" :key="'a-' + a.tile" class="accept-inline">
                <TileMiniGlyph :tile-id="a.tile" /><span class="accept-count">{{ a.remaining }}</span>
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
              <span class="col-tile"><TileMiniGlyph :tile-id="d.discard" /></span>
              <span class="col-shanten">{{ formatShanten(d.shanten) }}</span>
              <span class="col-total">
                <strong>{{ d.total_accept }}</strong><span class="hint">/{{ d.accept.length }}</span>
              </span>
              <span class="col-accept">
                <span v-if="d.accept.length === 0" class="hint">无</span>
                <span v-else class="accept-inline-row nowrap-scroll">
                  <span v-for="a in d.accept" :key="'da-' + d.discard + '-' + a.tile" class="accept-inline">
                    <TileMiniGlyph :tile-id="a.tile" /><span class="accept-count-mini">{{ a.remaining }}</span>
                  </span>
                </span>
              </span>
            </div>
          </div>
        </template>
      </div>

      <div class="row block">
        <TilePalette :size="'sm'" @pick="onPalettePick" />
      </div>

      <div class="actions">
        <el-button type="primary" size="default" :loading="loading" @click="analyze">
          计算牌理
        </el-button>
      </div>
    </section>
  </div>
</template>
<script setup>
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Loading } from '@element-plus/icons-vue'
import axios from 'axios'
import TileChip from '@/components/TileChip.vue'
import TilePalette from '@/components/TilePalette.vue'
import TileMiniGlyph from '@/components/TileMiniGlyph.vue'
import FuluSlots from '@/components/FuluSlots.vue'
import TileFaceStyleSwitch from '@/components/TileFaceStyleSwitch.vue'
import MahjongNotationHelp from '@/components/MahjongNotationHelp.vue'
import {
  TILE_NAME,
  parseNotationText,
  tilesToNotationText,
  randomHandTiles,
  meldDisplayTiles,
} from '@/composables/useMahjongTiles'
import { useFuluSlots } from '@/composables/useFuluSlots'

const form = reactive({
  hand: [],
})

const textInput = ref('')
const loading = ref(false)
const result = ref(null)

const countMeldTiles = (meld, tileId) => {
  if (!meld) return 0
  if (meld.kind === 's' || meld.kind === 'S') {
    if (meld.tileId === tileId || meld.tileId - 1 === tileId || meld.tileId + 1 === tileId) return 1
    return 0
  }
  if (meld.kind === 'g' || meld.kind === 'G') return meld.tileId === tileId ? 4 : 0
  if (meld.kind === 'k' || meld.kind === 'K') return meld.tileId === tileId ? 3 : 0
  return 0
}

const wouldExceedTileLimit = (meld, excludeSlotIdx = -1) => {
  const tiles = meldDisplayTiles(meld.kind, meld.tileId)
  const unique = [...new Set(tiles)]
  for (const tid of unique) {
    let count = form.hand.filter((t) => t === tid).length
    for (let i = 0; i < fulu.slots.length; i++) {
      if (i === excludeSlotIdx) continue
      count += countMeldTiles(fulu.slots[i].locked, tid)
    }
    count += tiles.filter((t) => t === tid).length
    if (count > 4) return tid
  }
  return null
}

const trimHandIfNeeded = () => {
  const max = expectedCount.value
  if (form.hand.length > max) {
    form.hand.splice(max)
    textInput.value = tilesToNotationText(form.hand)
  }
}

const fulu = useFuluSlots({
  checkOverflow: wouldExceedTileLimit,
  onLocked: trimHandIfNeeded,
})

const fuluSlots = fulu.slots
const activeFuluIdx = fulu.activeIdx
const lockedFuluList = fulu.lockedList
const lockedFuluCount = fulu.lockedCount
const activateFulu = (idx) => fulu.activate(idx)
const activateHand = fulu.activateHand
const clearFuluSlot = fulu.clearSlot
const onFuluSlotInput = fulu.onSlotInput
const lockFuluSlot = (idx, opt) => fulu.lockSlot(idx, opt)
const removeFuluDraft = fulu.removeDraftTile
const removeFuluLocked = fulu.removeLockedTile

// 14 张：副露 0 时手牌 14 张；每副露 -3 张
const expectedCount = computed(() => 14 - lockedFuluCount.value * 3)
const handCountText = computed(() => `${form.hand.length}/${expectedCount.value}`)
const handCountTagType = computed(() => {
  if (form.hand.length === expectedCount.value) return 'success'
  if (form.hand.length > expectedCount.value) return 'danger'
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

const countTileEverywhere = (id) => {
  let count = form.hand.filter(t => t === id).length
  for (const meld of lockedFuluList.value) {
    count += countMeldTiles(meld, id)
  }
  return count
}

const onPalettePick = (id) => {
  if (fulu.appendTileToActive(id)) return
  addHandTile(id)
}

const addHandTile = (id) => {
  if (form.hand.length >= expectedCount.value) {
    ElMessage.warning(`手牌已达上限 ${expectedCount.value} 张`)
    return
  }
  if (countTileEverywhere(id) >= 4) {
    ElMessage.warning(`牌 ${TILE_NAME[id]} 已达 4 张上限`)
    return
  }
  form.hand.push(id)
  textInput.value = tilesToNotationText(form.hand)
}

const removeHandTile = (idx) => {
  form.hand.splice(idx, 1)
  textInput.value = tilesToNotationText(form.hand)
}

const onHandChipClick = (idx) => {
  activateHand()
  removeHandTile(idx)
}

const loadDemo = () => {
  textInput.value = '35m146678p24s344z5m'
  analyze()
}

const resetAll = () => {
  form.hand = []
  textInput.value = ''
  result.value = null
  fulu.resetAll()
}

const ensureReadyForAnalyze = () => {
  const exp = expectedCount.value
  if (textInput.value?.trim()) {
    try {
      const parsed = parseNotationText(textInput.value)
      if (parsed.length > exp) {
        ElMessage.error(`手牌应为 ${exp} 张，当前简写解析为 ${parsed.length} 张`)
        return false
      }
      form.hand = parsed
      textInput.value = tilesToNotationText(form.hand)
    } catch (e) {
      ElMessage.error(`简写解析失败：${e.message}`)
      return false
    }
  }
  if (form.hand.length === 0 && !textInput.value?.trim()) {
    form.hand = randomHandTiles(exp)
    textInput.value = tilesToNotationText(form.hand)
    return true
  }
  if (form.hand.length !== exp) {
    ElMessage.error(`手牌须恰好 ${exp} 张（当前 ${form.hand.length} 张）`)
    return false
  }
  return true
}

const analyze = async () => {
  if (!ensureReadyForAnalyze()) return
  loading.value = true
  result.value = null
  try {
    const tilesCombination = lockedFuluList.value.map((m) => m.code)
    const resp = await axios.post('/api/mahjong/paili', {
      hand_tiles: [...form.hand],
      tiles_combination: tilesCombination
    })
    if (!resp.data.success) {
      ElMessage.error(resp.data.message || '计算失败')
      return
    }
    result.value = resp.data.data
  } catch (err) {
    console.error(err)
    const msg = err.response?.data?.message || err.message
    ElMessage.error(`计算失败：${msg}`)
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.paili {
  max-width: 880px;
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
  background: rgba(255, 255, 255, 0.95);
  border: 1px solid var(--omu-border, #ebeef5);
  border-radius: 12px;
  overflow: hidden;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
}

.paili :deep(.notation-collapse) {
  margin-bottom: 8px;
}

.card-header {
  padding: 8px 12px;
  background: #f5f7fa;
  border-bottom: 1px solid var(--omu-border, #ebeef5);
  font-size: 13px;
  font-weight: 600;
  color: var(--omu-text-soft, #606266);
  display: flex;
  justify-content: space-between;
  align-items: center;
  letter-spacing: 0.5px;
}

.header-actions { display: inline-flex; align-items: center; gap: 8px; }

.row {
  padding: 8px 12px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.row.block { display: block; }

.row + .row {
  border-top: 1px dashed var(--omu-border, #ebeef5);
}

.row-label {
  font-size: 12.5px;
  color: var(--omu-text-soft, #475569);
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
  min-height: 48px;
  padding: 6px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px dashed var(--omu-border, #ebeef5);
  cursor: pointer;
  transition: border-color 0.12s ease, background 0.12s ease;
}

.hand-bar.active {
  border-color: #409eff;
  border-style: solid;
  background: #f0f7ff;
}

.input-target-bar {
  display: inline-block;
  font-size: 1.15rem;
  font-weight: 700;
  color: #1f2933;
  letter-spacing: 0.5px;
  line-height: 1.3;
}

.hint {
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12.5px;
}
.hint.warn { color: var(--omu-warning, #d97706); }

.result-embed {
  border-top: 1px solid var(--omu-border, #ebeef5);
  background: #fafbfc;
  min-height: 48px;
}

.result-embed .empty {
  padding: 10px 12px;
  text-align: center;
  color: var(--omu-text-muted, #94a3b8);
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
  color: var(--omu-text-soft, #475569);
}
.meta-line strong { color: var(--omu-accent, #409eff); margin-left: 4px; }

.actions {
  padding: 8px 12px;
  border-top: 1px solid var(--omu-border, #ebeef5);
  background: var(--omu-surface-soft, #f5f7fa);
  text-align: right;
}

.banner {
  margin: 8px 12px;
  padding: 10px 12px;
  border-radius: 8px;
  display: flex;
  align-items: baseline;
  gap: 10px;
  border: 1px solid;
  background: #ecfdf5;
  border-color: #6ee7b7;
  color: #065f46;
}
.banner strong { font-size: 0.95rem; }
.banner-sub { font-size: 12px; opacity: 0.85; }

.paili-block { padding: 0 12px 10px; }
.paili-block h4 {
  margin: 6px 0;
  font-size: 12.5px;
  color: var(--omu-text-soft, #475569);
  letter-spacing: 0.5px;
}

.accept-list {
  padding: 4px 6px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px dashed var(--omu-border, #ebeef5);
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
  gap: 1px;
  margin-right: 6px;
}
.accept-inline .accept-count {
  font-size: 10px;
  color: var(--omu-text-soft, #475569);
  font-family: var(--omu-mono, 'Consolas', monospace);
}

.accept-count-mini {
  font-size: 9px;
  color: var(--omu-text-muted, #94a3b8);
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
  border-bottom: 1px solid var(--omu-border, #ebeef5);
  min-height: 28px;
}

.discard-row:last-child { border-bottom: none; }

.discard-head {
  background: var(--omu-surface-soft, #f5f7fa);
  font-size: 11px;
  letter-spacing: 0.5px;
  color: var(--omu-text-soft, #475569);
  font-weight: 600;
}

.discard-row.is-best {
  background: #f0f9ff;
}
.discard-row.is-best .col-shanten { color: var(--omu-accent, #409eff); font-weight: 600; }

.col-tile { flex: 0 0 auto; }
.col-shanten {
  flex: 0 0 64px;
  font-family: var(--omu-mono, 'Consolas', monospace);
  color: var(--omu-text, #1f2933);
}
.col-total {
  flex: 0 0 68px;
  display: inline-flex;
  align-items: baseline;
  gap: 2px;
  font-family: var(--omu-mono, 'Consolas', monospace);
}
.col-total strong { color: var(--omu-text, #1f2933); font-size: 0.9rem; }

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
</style>
