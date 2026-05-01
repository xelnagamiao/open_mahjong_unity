<!-- 国标麻将听牌待牌判断：单列紧凑，结果嵌在输入与按钮之间 -->
<template>
  <div class="shanten">
    <div class="page-header">
      <h1>听牌判断</h1>
      <p class="subtitle">输入 13 张手牌（或 13 - 副露张数 × 3 张）。手牌与简写均为空时点「分析」将随机生成一手牌。</p>
    </div>

    <MahjongNotationHelp />

    <section class="card main-card">
      <header class="card-header">
        <span>输入</span>
        <el-button type="text" size="small" @click="resetAll">清空</el-button>
      </header>

      <div class="row">
        <label class="row-label">牌面简写</label>
        <el-input
          v-model="textInput"
          placeholder="如 35m146678p24s344z"
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
        <div v-if="form.fulus.length === 0" class="hint">门清听牌可不添加副露</div>
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
          <span>分析中...</span>
        </div>
        <template v-else-if="result">
          <div :class="['banner', result.is_tingpai ? 'success' : 'fail', 'nowrap']">
            <strong>{{ result.is_tingpai ? '听牌' : '未听牌' }}</strong>
            <span v-if="result.is_tingpai" class="banner-sub">共 {{ result.waiting_tiles.length }} 种待牌</span>
          </div>
          <div v-if="result.is_tingpai" class="waiting">
            <h4>待牌</h4>
            <div class="waiting-tiles nowrap-scroll">
              <TileMiniGlyph
                v-for="id in result.waiting_tiles"
                :key="'w-' + id"
                :tile-id="id"
              />
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
          分析听牌
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

const expectedCount = computed(() => 13 - form.fulus.length * 3)
const handCountText = computed(() => `${form.hand.length}/${expectedCount.value}`)
const handCountTagType = computed(() => {
  if (form.hand.length === expectedCount.value) return 'success'
  if (form.hand.length > expectedCount.value) return 'danger'
  return 'warning'
})

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
    const resp = await axios.post('/api/mahjong/gb/tingpai', {
      hand_tiles: [...form.hand],
      tiles_combination: tilesCombination
    })
    if (!resp.data.success) {
      ElMessage.error(resp.data.message || '分析失败')
      return
    }
    result.value = resp.data.data
  } catch (err) {
    console.error(err)
    const msg = err.response?.data?.message || err.message
    ElMessage.error(`分析失败：${msg}`)
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.shanten {
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
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1);
}

.main-card .card-header {
  padding: 6px 10px;
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

.row {
  padding: 6px 10px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.row.block {
  display: block;
}

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
  margin-bottom: 8px;
}

.hand-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 3px;
  min-height: 44px;
  padding: 4px;
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

.actions {
  padding: 8px 10px;
  border-top: 1px solid var(--omu-border, #ebeef5);
  background: var(--omu-surface-soft, #f5f7fa);
  text-align: right;
}

.result-embed {
  border-top: 1px dashed var(--omu-border, #ebeef5);
  min-height: 48px;
}

.result-embed .banner {
  margin: 8px 10px;
  padding: 8px 10px;
}

.result-embed .waiting {
  padding: 0 10px 10px;
}

.result-embed .empty {
  padding: 12px;
  text-align: center;
  color: var(--omu-text-muted, #94a3b8);
  font-size: 13px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.banner {
  margin: 12px 14px;
  padding: 12px 14px;
  border-radius: 8px;
  display: flex;
  align-items: baseline;
  gap: 10px;
  border: 1px solid;
}
.banner.success {
  background: #ecfdf5;
  border-color: #6ee7b7;
  color: #065f46;
}
.banner.fail {
  background: #fef2f2;
  border-color: #fca5a5;
  color: #991b1b;
}
.banner strong { font-size: 1rem; }
.banner-sub { font-size: 12.5px; opacity: 0.8; }

.waiting {
  padding: 0 14px 14px;
}
.waiting h4 {
  margin: 4px 0 8px;
  font-size: 13px;
  color: var(--omu-text-soft, #606266);
  letter-spacing: 0.5px;
}
.waiting-tiles {
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

</style>
