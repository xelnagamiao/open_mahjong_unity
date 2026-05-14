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
        <div class="hand-bar">
          <TileChip
            v-for="(id, idx) in form.hand"
            :key="'h-' + idx"
            :tile-id="id"
            size="sm"
            @click="removeHandTile(idx)"
          />
          <span v-if="form.hand.length === 0" class="hint">点击下方牌面或输入文本添加手牌</span>
        </div>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">副露 / 暗刻 / 暗杠 ({{ form.fulus.length }}/4)</span>
          <el-button type="primary" plain size="small" :disabled="form.fulus.length >= 4" @click="addFulu">
            添加
          </el-button>
        </div>
        <div v-if="form.fulus.length === 0" class="hint">门清牌理可不添加副露</div>
        <div v-else class="fulu-list">
          <div v-for="(fulu, idx) in form.fulus" :key="idx" class="fulu-item">
            <el-select v-model="fulu.kind" size="small" style="width: 110px;" @change="onFuluKindChange(idx)">
              <el-option label="明刻 (碰)" value="k" />
              <el-option label="暗刻" value="K" />
              <el-option label="明杠" value="g" />
              <el-option label="暗杠" value="G" />
              <el-option label="明顺 (吃)" value="s" />
              <el-option label="暗顺" value="S" />
            </el-select>
            <span class="fulu-tile" v-if="fulu.tileId">
              <TileChip :tile-id="fulu.tileId" size="sm" highlighted />
            </span>
            <span v-else class="hint warn">未选中心牌</span>
            <el-button type="danger" link size="small" @click="removeFulu(idx)">删除</el-button>
            <TilePalette :size="'sm'" class="palette-inline" @pick="setFuluTile(idx, $event)" />
          </div>
        </div>
      </div>

      <div class="result-embed">
        <div v-if="loading" class="empty">
          <el-icon class="is-loading" :size="18"><Loading /></el-icon>
          <span>正在计算...</span>
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
        <div class="row-line"><span class="row-label">点击添加到手牌</span></div>
        <TilePalette :size="'sm'" @pick="addHandTile" />
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
import MahjongNotationHelp from '@/components/MahjongNotationHelp.vue'
import {
  TILE_NAME,
  parseNotationText,
  tilesToNotationText,
  randomHandTiles,
} from '@/composables/useMahjongTiles'

const form = reactive({
  hand: [],
  fulus: []
})

const textInput = ref('')
const loading = ref(false)
const result = ref(null)

// 14 张：副露 0 时手牌 14 张；每副露 -3 张
const expectedCount = computed(() => 14 - form.fulus.length * 3)
const handCountText = computed(() => `${form.hand.length}/${expectedCount.value}`)
const handCountTagType = computed(() => {
  if (form.hand.length === expectedCount.value) return 'success'
  if (form.hand.length > expectedCount.value) return 'danger'
  return 'warning'
})

const formatShanten = (s) => {
  if (s === undefined || s === null) return '?'
  if (s === -1) return '和牌'
  if (s === 0) return '听牌'
  return `${s} 向听`
}

const countTileEverywhere = (id) => {
  let count = form.hand.filter(t => t === id).length
  for (const fulu of form.fulus) {
    if (!fulu.tileId) continue
    if (fulu.kind === 's' || fulu.kind === 'S') {
      if (fulu.tileId === id || fulu.tileId - 1 === id || fulu.tileId + 1 === id) count += 1
    } else if (fulu.kind === 'g' || fulu.kind === 'G') {
      if (fulu.tileId === id) count += 4
    } else if (fulu.kind === 'k' || fulu.kind === 'K') {
      if (fulu.tileId === id) count += 3
    }
  }
  return count
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

const loadDemo = () => {
  textInput.value = '35m146678p24s344z5m'
  analyze()
}

const addFulu = () => {
  if (form.fulus.length >= 4) return
  form.fulus.push({ kind: 'k', tileId: null })
}

const removeFulu = (idx) => {
  form.fulus.splice(idx, 1)
}

const setFuluTile = (idx, tileId) => {
  const fulu = form.fulus[idx]
  if (fulu.kind === 's' || fulu.kind === 'S') {
    if (tileId > 40 || tileId % 10 === 1 || tileId % 10 === 9) {
      ElMessage.warning('顺子中心牌应在 2-8 之间且不能为字牌')
      return
    }
  }
  fulu.tileId = tileId
}

const onFuluKindChange = (idx) => {
  const fulu = form.fulus[idx]
  if ((fulu.kind === 's' || fulu.kind === 'S') && fulu.tileId) {
    if (fulu.tileId > 40 || fulu.tileId % 10 === 1 || fulu.tileId % 10 === 9) {
      fulu.tileId = null
    }
  }
}

const resetAll = () => {
  form.hand = []
  form.fulus = []
  textInput.value = ''
  result.value = null
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
  if (form.fulus.length > 0 && !form.fulus.every(f => f.tileId)) {
    ElMessage.warning('请为每条副露选中心牌，或删除多余副露')
    return false
  }
  return true
}

const analyze = async () => {
  if (!ensureReadyForAnalyze()) return
  loading.value = true
  result.value = null
  try {
    const tilesCombination = form.fulus
      .filter(f => f.tileId)
      .map(f => `${f.kind}${f.tileId}`)
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
  padding: 12px 12px 20px;
}

.page-header {
  text-align: center;
  margin-bottom: 16px;
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

.header-actions { display: inline-flex; gap: 4px; }

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
}

.fulu-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.fulu-item {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 8px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px solid var(--omu-border, #ebeef5);
}

.fulu-tile { display: inline-flex; }

.palette-inline {
  flex-basis: 100%;
  margin-top: 6px;
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
  padding: 14px;
  text-align: center;
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12.5px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
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
