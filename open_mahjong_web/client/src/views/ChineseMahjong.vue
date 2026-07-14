<!-- 国标计算器：单列紧凑，结果嵌在输入与按钮之间 -->
<template>
  <div class="chinese">
    <div class="page-header">
      <h1>国标计算器</h1>
      <p class="subtitle">选择 14 张牌（含右侧和牌张）、副露与和牌方式，自动计算番种、得分以及全部和牌拆解形态。</p>
    </div>

    <MahjongNotationHelp />

    <section class="main-card">
      <header class="card-header">
        <span>输入</span>
        <div class="header-actions">
          <TileFaceStyleSwitch />
          <el-button type="text" size="small" @click="resetAll">清空</el-button>
        </div>
      </header>

      <div class="row">
        <label class="row-label">牌面简写</label>
        <el-input
          v-model="textInput"
          placeholder="如 11122333m44455p66z + 和牌张"
          size="small"
          clearable
          @keydown.enter.prevent="calculateScore"
        />
        <el-button type="primary" size="small" plain :loading="loading" @click="calculateScore">解析</el-button>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">手牌 {{ handCountText }}</span>
          <el-tag size="small" :type="handCountTagType" effect="plain">{{ handCountText }}</el-tag>
        </div>
        <div
          class="hand-row"
          :class="{ active: activeFuluIdx < 0 }"
          @click="activateHand"
        >
          <div class="hand-bar">
            <TileChip
              v-for="(id, idx) in form.hand"
              :key="'h-' + idx"
              :tile-id="id"
              size="sm"
              @click="onHandChipClick(idx)"
            />
          </div>
          <div class="get-tile-box" :class="{ filled: form.getTile }">
            <span class="get-tile-label">和牌</span>
            <TileChip
              v-if="form.getTile"
              :tile-id="form.getTile"
              size="sm"
              highlighted
              @click.stop="clearGetTile"
            />
          </div>
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

      <div class="row">
        <span class="row-label">花牌（每张 1 番）</span>
        <el-input-number
          v-model="form.flowerCount"
          :min="0"
          :max="8"
          size="small"
          controls-position="right"
        />
      </div>

      <div class="row block">
        <div class="row-line"><span class="row-label">和牌方式</span></div>
        <div class="ways">
          <el-radio-group v-model="form.hepaiType" size="small">
            <el-radio-button label="dianhe">点和</el-radio-button>
            <el-radio-button label="zimo">自摸</el-radio-button>
          </el-radio-group>
          <el-select v-model="form.changFeng" size="small" style="width: 110px;">
            <el-option v-for="opt in ['场风东','场风南','场风西','场风北']" :key="opt" :label="opt" :value="opt" />
          </el-select>
          <el-select v-model="form.menFeng" size="small" style="width: 110px;">
            <el-option v-for="opt in ['自风东','自风南','自风西','自风北']" :key="opt" :label="opt" :value="opt" />
          </el-select>
        </div>
        <div class="ways flags nowrap-scroll">
          <el-checkbox v-model="form.flagSet.heJueZhang" size="small">和绝张</el-checkbox>
          <el-checkbox v-model="form.flagSet.gangShangKaiHua" size="small">杠上开花</el-checkbox>
          <el-checkbox v-model="form.flagSet.qiangGangHe" size="small">抢杠和</el-checkbox>
          <el-checkbox v-model="form.flagSet.miaoShouHuiChun" size="small">妙手回春</el-checkbox>
          <el-checkbox v-model="form.flagSet.haiDiLaoYue" size="small">海底捞月</el-checkbox>
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
        <div v-else-if="result.mode === 'score'">
          <div :class="['banner', result.is_hepai ? 'success' : 'fail']">
            <div class="banner-num">{{ result.score }}</div>
            <div class="banner-text">{{ result.is_hepai ? '番' : '不能和牌' }}</div>
          </div>
          <div v-if="result.is_hepai" class="fan-block">
            <h4>番种构成</h4>
            <div class="fan-tags nowrap-scroll">
              <el-tag v-for="(name, idx) in result.fan_list" :key="idx" type="success" effect="plain" size="small">
                {{ name }}
              </el-tag>
            </div>
          </div>
          <div v-else class="msg-inline">{{ result.message || '该牌型不构成和牌' }}</div>
        </div>
        <div v-else-if="result.mode === 'decompose'">
          <div v-if="!result.is_hepai" class="msg-inline">该牌型不能和牌，无可用拆解。</div>
          <div v-else class="decomp-list">
            <div class="decomp-summary">
              共 {{ result.decompositions.length }} 种拆解
            </div>
            <div
              v-for="(item, idx) in result.decompositions"
              :key="idx"
              class="decomp-item"
            >
              <div class="decomp-header">
                <span class="decomp-rank">#{{ idx + 1 }}</span>
                <span class="decomp-score">{{ item.score }} 番</span>
              </div>
              <div class="decomp-tiles">
                <div
                  v-for="(group, gIdx) in renderDecomposition(item.combinations)"
                  :key="gIdx"
                  class="decomp-group"
                >
                  <div class="decomp-group-label">{{ group.label }}</div>
                  <div class="decomp-group-tiles nowrap-scroll">
                    <TileMiniGlyph v-for="(t, tIdx) in group.tiles" :key="tIdx" :tile-id="t" />
                  </div>
                </div>
              </div>
              <div class="decomp-fans">
                <el-tag v-for="(name, fIdx) in item.fan_list" :key="fIdx" size="small" effect="plain">
                  {{ name }}
                </el-tag>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="row block">
        <TilePalette :size="'sm'" @pick="onPalettePick" />
      </div>

      <div class="actions">
        <el-button type="primary" size="default" :loading="loading" @click="calculateScore">
          计算得分
        </el-button>
        <el-button size="default" :loading="loading" @click="calculateDecompose">
          查看全部拆解
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
  combinationLabel,
  parseNotationWithGetTile,
  notationTextWithGetTile,
  meldDisplayTiles,
} from '@/composables/useMahjongTiles'
import { useFuluSlots } from '@/composables/useFuluSlots'

const textInput = ref('')

// ======== 表单数据 ========
const form = reactive({
  hand: [],
  flowerCount: 0,
  getTile: null,     // 和牌张
  hepaiType: 'dianhe',
  changFeng: '场风东',
  menFeng: '自风东',
  flagSet: {
    heJueZhang: false,
    gangShangKaiHua: false,
    qiangGangHe: false,
    miaoShouHuiChun: false,
    haiDiLaoYue: false,
  }
})

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
    if (form.getTile === tid) count += 1
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
  const max = expectedHandCount.value
  if (form.hand.length > max) {
    form.hand.splice(max)
    syncTextInput()
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

const buildFlowerTiles = () =>
  Array.from({ length: form.flowerCount }, (_, i) => 51 + i)

// ======== 计算属性 ========
// 手牌栏 13 张 + 右侧和牌 1 张；每副露占 3 张
const expectedHandCount = computed(() => 13 - lockedFuluCount.value * 3)
const expectedTotalCount = computed(() => 14 - lockedFuluCount.value * 3)
const filledTileCount = computed(() => form.hand.length + (form.getTile ? 1 : 0))
const handCountText = computed(() => `${filledTileCount.value}/${expectedTotalCount.value}`)
const handCountTagType = computed(() => {
  if (filledTileCount.value === expectedTotalCount.value) return 'success'
  if (filledTileCount.value > expectedTotalCount.value) return 'danger'
  return 'warning'
})

const inputTargetLabel = computed(() => {
  if (activeFuluIdx.value >= 0) return `输入副露 #${activeFuluIdx.value + 1}`
  return '输入手牌'
})

const syncTextInput = () => {
  textInput.value = notationTextWithGetTile(form.hand, form.getTile)
}

const applyParsedTiles = ({ hand, getTile }) => {
  const exp = expectedHandCount.value
  const expTotal = expectedTotalCount.value

  let finalHand = hand
  let finalGetTile = getTile
  // 兼容旧写法：无 + 且共 14 张时，按输入顺序取末张为和牌张
  if (finalGetTile == null && hand.length === exp + 1) {
    finalHand = hand.slice(0, -1)
    finalGetTile = hand[hand.length - 1]
  }

  const total = finalHand.length + (finalGetTile ? 1 : 0)
  if (total > expTotal) {
    throw new Error(`手牌应为 ${expTotal} 张（含和牌张），当前简写解析为 ${total} 张`)
  }

  form.hand = finalHand
  form.getTile = finalGetTile
  syncTextInput()
}

const ensureReadyForSubmit = () => {
  const expHand = expectedHandCount.value
  const expTotal = expectedTotalCount.value
  if (textInput.value?.trim()) {
    try {
      const parsed = parseNotationWithGetTile(textInput.value)
      applyParsedTiles(parsed)
    } catch (e) {
      ElMessage.error(`简写解析失败：${e.message}`)
      return false
    }
  }
  if (filledTileCount.value !== expTotal) {
    if (form.hand.length !== expHand) {
      ElMessage.error(`手牌栏须 ${expHand} 张（当前 ${form.hand.length} 张）`)
    } else if (!form.getTile) {
      ElMessage.warning('请填写右侧和牌张（手牌栏满后点下方牌面自动填入）')
    } else {
      ElMessage.error(`手牌须凑满 ${expTotal} 张（当前 ${filledTileCount.value} 张，含和牌张）`)
    }
    return false
  }
  return true
}

// ======== 操作方法 ========
const onPalettePick = (id) => {
  if (id >= 51 && id <= 58) return
  if (fulu.appendTileToActive(id)) return
  if (form.hand.length < expectedHandCount.value) {
    addHandTile(id)
  } else {
    setGetTile(id)
  }
}

const setGetTile = (id) => {
  const prev = form.getTile
  form.getTile = null
  if (countTileEverywhere(id) >= 4) {
    form.getTile = prev
    ElMessage.warning(`牌 ${TILE_NAME[id]} 已达 4 张上限`)
    return
  }
  form.getTile = id
  syncTextInput()
}

const clearGetTile = () => {
  form.getTile = null
  syncTextInput()
}

const addHandTile = (id) => {
  if (form.hand.length >= expectedHandCount.value) {
    setGetTile(id)
    return
  }
  if (countTileEverywhere(id) >= 4) {
    ElMessage.warning(`牌 ${TILE_NAME[id]} 已达 4 张上限`)
    return
  }
  form.hand.push(id)
  syncTextInput()
}

const removeHandTile = (idx) => {
  form.hand.splice(idx, 1)
  syncTextInput()
}

const onHandChipClick = (idx) => {
  activateHand()
  removeHandTile(idx)
}

const countTileEverywhere = (id) => {
  let count = form.hand.filter(t => t === id).length
  if (form.getTile === id) count += 1
  for (const meld of lockedFuluList.value) {
    count += countMeldTiles(meld, id)
  }
  return count
}

const resetAll = () => {
  form.hand = []
  form.flowerCount = 0
  form.getTile = null
  form.hepaiType = 'dianhe'
  form.changFeng = '场风东'
  form.menFeng = '自风东'
  form.flagSet = {
    heJueZhang: false,
    gangShangKaiHua: false,
    qiangGangHe: false,
    miaoShouHuiChun: false,
    haiDiLaoYue: false,
  }
  textInput.value = ''
  result.value = null
  fulu.resetAll()
}

// ======== 接口调用 ========
const buildRequestBody = async () => {
  const tilesCombination = lockedFuluList.value.map((m) => m.code)

  const wayToHepai = []
  wayToHepai.push(form.hepaiType === 'zimo' ? '自摸' : '点和')
  if (form.changFeng) wayToHepai.push(form.changFeng)
  if (form.menFeng) wayToHepai.push(form.menFeng)

  // 前 13 张听牌检测：待牌唯一时自动判定和单张
  try {
    const tingResp = await axios.post('/api/mahjong/gb/tingpai', {
      hand_tiles: [...form.hand],
      tiles_combination: tilesCombination,
    })
    if (tingResp.data?.success && tingResp.data.data?.waiting_tiles?.length === 1) {
      wayToHepai.push('和单张')
    }
  } catch (e) {
    console.warn('和单张自动判定失败，将跳过', e)
  }

  if (form.flagSet.heJueZhang) wayToHepai.push('和绝张')
  if (form.flagSet.gangShangKaiHua) wayToHepai.push('杠上开花')
  if (form.flagSet.qiangGangHe) wayToHepai.push('抢杠和')
  if (form.flagSet.miaoShouHuiChun) wayToHepai.push('妙手回春')
  if (form.flagSet.haiDiLaoYue) wayToHepai.push('海底捞月')

  return {
    hand_tiles: [...form.hand, form.getTile],
    tiles_combination: tilesCombination,
    way_to_hepai: wayToHepai,
    get_tile: form.getTile,
    flower_tiles: buildFlowerTiles()
  }
}

const calculateScore = async () => {
  if (!ensureReadyForSubmit()) return
  loading.value = true
  result.value = null
  try {
    const resp = await axios.post('/api/mahjong/gb/score', await buildRequestBody())
    if (!resp.data.success) {
      ElMessage.error(resp.data.message || '计算失败')
      return
    }
    result.value = { mode: 'score', ...resp.data.data }
  } catch (err) {
    console.error(err)
    const msg = err.response?.data?.message || err.message
    ElMessage.error(`计算失败：${msg}`)
  } finally {
    loading.value = false
  }
}

const calculateDecompose = async () => {
  if (!ensureReadyForSubmit()) return
  loading.value = true
  result.value = null
  try {
    const resp = await axios.post('/api/mahjong/gb/decompose', await buildRequestBody())
    if (!resp.data.success) {
      ElMessage.error(resp.data.message || '计算失败')
      return
    }
    result.value = { mode: 'decompose', ...resp.data.data }
  } catch (err) {
    console.error(err)
    const msg = err.response?.data?.message || err.message
    ElMessage.error(`计算失败：${msg}`)
  } finally {
    loading.value = false
  }
}

// ======== 拆解可视化 ========
function renderDecomposition(combinations) {
  const groups = []
  for (const code of combinations) {
    const prefix = code[0]
    const tileId = parseInt(code.slice(1), 10)
    if (prefix === 's' || prefix === 'S') {
      groups.push({
        label: prefix === 'S' ? '暗顺' : '顺子',
        tiles: [tileId - 1, tileId, tileId + 1]
      })
    } else if (prefix === 'k') {
      groups.push({ label: '明刻', tiles: [tileId, tileId, tileId] })
    } else if (prefix === 'K') {
      groups.push({ label: '暗刻', tiles: [tileId, tileId, tileId] })
    } else if (prefix === 'g') {
      groups.push({ label: '明杠', tiles: [tileId, tileId, tileId, tileId] })
    } else if (prefix === 'G') {
      groups.push({ label: '暗杠', tiles: [tileId, tileId, tileId, tileId] })
    } else if (prefix === 'q') {
      groups.push({ label: '雀头', tiles: [tileId, tileId] })
    } else if (prefix === 'z') {
      // 组合龙 / 全不靠：z{set 字符串}，仅展示标签
      groups.push({ label: '组合龙', tiles: [] })
    } else {
      groups.push({ label: combinationLabel(code), tiles: [] })
    }
  }
  return groups
}
</script>

<style scoped>
.chinese {
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

.chinese :deep(.notation-collapse) {
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
  gap: 10px;
}

.hint {
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12px;
  font-weight: 400;
}

.hand-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  min-height: 48px;
  flex: 1 1 0;
  min-width: 0;
  padding: 6px;
  background: transparent;
  border-radius: 0;
  border: none;
}
.hand-bar.small { min-height: 36px; }

.hand-row {
  display: flex;
  align-items: stretch;
  gap: 8px;
  padding: 6px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px dashed var(--omu-border, #ebeef5);
  cursor: pointer;
  transition: border-color 0.12s ease, background 0.12s ease;
}

.hand-row.active {
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

.get-tile-box {
  flex: 0 0 auto;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  min-width: 56px;
  padding: 6px 8px;
  background: #fffbeb;
  border-radius: 6px;
  border: 2px dashed #fbbf24;
}
.get-tile-box.filled {
  border-style: solid;
  background: #fef3c7;
}
.get-tile-label {
  font-size: 10.5px;
  font-weight: 600;
  color: #b45309;
  letter-spacing: 0.5px;
}

.fulu-slots {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 6px;
}

.fulu-slot {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 4px;
  padding: 6px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px dashed var(--omu-border, #ebeef5);
  min-height: 64px;
  min-width: 0;
}

.fulu-slot.locked {
  border-style: solid;
  background: #f0f9ff;
}

.slot-index {
  font-size: 10px;
  font-weight: 700;
  color: var(--omu-text-muted, #94a3b8);
  line-height: 1;
}

.fulu-kind {
  align-self: flex-start;
}

.fulu-input {
  width: 100%;
}

.fulu-tiles {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 2px;
}

.fulu-clear {
  align-self: center;
  padding: 0;
  height: auto;
}

.fulu-options {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.fulu-options .el-button {
  width: 100%;
  margin: 0;
  padding: 4px 0;
}

@media (max-width: 640px) {
  .fulu-slots {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

.fulu-tile { display: inline-flex; }
.palette-inline { flex-basis: 100%; margin-top: 6px; }

.ways {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.ways.flags {
  margin-top: 4px;
  margin-bottom: 0;
  gap: 6px 14px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px solid var(--omu-border, #ebeef5);
  padding: 8px 10px;
}

.actions {
  padding: 8px 12px;
  border-top: 1px solid var(--omu-border, #ebeef5);
  background: var(--omu-surface-soft, #f5f7fa);
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

.hint {
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12.5px;
}
.hint.warn { color: var(--omu-warning, #d97706); }

.result-embed {
  border-top: 1px solid var(--omu-border, #ebeef5);
  background: #fafbfc;
  min-height: 44px;
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

.msg-inline {
  padding: 10px 12px 12px;
  font-size: 12.5px;
  color: var(--omu-text-muted, #94a3b8);
  text-align: center;
}

.empty {
  padding: 24px;
  text-align: center;
  color: var(--omu-text-muted, #94a3b8);
  font-size: 13px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.banner {
  margin: 8px 12px;
  padding: 10px 12px;
  border-radius: 8px;
  display: flex;
  align-items: baseline;
  gap: 8px;
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
.banner-num { font-size: 2rem; font-weight: 700; line-height: 1; }
.banner-text { font-size: 13px; }

.fan-block { padding: 0 12px 10px; }
.fan-block h4 {
  margin: 0 0 8px;
  font-size: 12.5px;
  color: var(--omu-text-soft, #606266);
  letter-spacing: 0.5px;
}
.fan-tags {
  display: flex;
  flex-wrap: nowrap;
  gap: 4px;
}

.nowrap-scroll {
  white-space: nowrap;
  overflow-x: auto;
  max-width: 100%;
  padding-bottom: 2px;
}

.decomp-group-tiles { display: inline-flex; gap: 1px; align-items: center; }

.decomp-list {
  padding: 8px 12px 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.decomp-summary {
  font-size: 12.5px;
  color: var(--omu-text-soft, #475569);
}
.decomp-item {
  border: 1px solid var(--omu-border, #ebeef5);
  border-radius: 8px;
  padding: 8px 10px;
  background: var(--omu-surface-soft, #f5f7fa);
}
.decomp-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}
.decomp-rank {
  font-family: var(--omu-mono, 'Consolas', monospace);
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12px;
}
.decomp-score {
  font-size: 1rem;
  font-weight: 700;
  color: var(--omu-accent, #409eff);
}
.decomp-tiles {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 6px;
}
.decomp-group {
  background: #ffffff;
  border: 1px solid var(--omu-border, #ebeef5);
  border-radius: 6px;
  padding: 4px 6px;
}
.decomp-group-label {
  font-size: 10.5px;
  color: var(--omu-text-muted, #94a3b8);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 2px;
}
.decomp-fans { display: flex; flex-wrap: wrap; gap: 4px; }
</style>
