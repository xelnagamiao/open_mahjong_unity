<!--
  虹雀² 和牌计算器（路由 /calc/hongque）
  按虹雀² v1.6 规则计算和牌拆解与番数/分数，交互参照国标计算器。
  牌面使用 Unity 中的 HQv3.1 贴图（public/game2d-assets/hongque-tiles/）。
  副露牌组与国标计算器一致：点击牌组框激活，再从牌面点选添加，点牌移除。
  规则要点：所有牌必须且仅能属于一个合法牌组（3 张及以上），无雀头。
-->
<template>
  <div class="hongque-calc">
    <div class="page-header">
      <CalculatorSwitcher />
      <p class="subtitle">
        点击下方牌面添加手牌；点击副露框后，只有能组成合法牌组的牌会高亮可点。
        手牌 + 副露合计 12～14 张，所有牌必须分成 3 张及以上的合法牌组，无雀头。
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
          placeholder="例：AX1 AX2 AX3 BX4 BX5 BX6"
          size="small"
          clearable
          @keydown.enter.prevent="calculateScore"
        />
        <el-button type="primary" size="small" plain @click="calculateScore">解析</el-button>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">手牌 {{ form.hand.length }} 张</span>
          <el-tag size="small" :type="handCountTagType" effect="plain">{{ handCountText }}</el-tag>
        </div>
        <div
          class="hand-row"
          :class="{ active: activeMeldIdx < 0 }"
          @click="activateHand"
        >
          <div class="hand-bar">
            <template v-if="form.hand.length">
              <span
                v-for="(code, idx) in form.hand"
                :key="'h-' + code"
                class="hq-tile"
                :class="{ selected: isInMeld(code) }"
                :title="code"
                @click.stop="removeHandTile(idx)"
              >
                <img :src="tileFaceUrl(code)" :alt="code" draggable="false" @error="onTileError" />
              </span>
            </template>
            <span v-else class="empty-hint">点击下方牌面添加手牌，点击手牌移除</span>
          </div>
        </div>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">副露牌组（{{ activeMelds.length }}/4，点击方块后从牌面选牌）</span>
        </div>
        <div class="meld-grid">
          <div
            v-for="(meld, idx) in melds"
            :key="'m-' + idx"
            class="meld-slot"
            :class="{ active: activeMeldIdx === idx, filled: meld.codes.length > 0 }"
            @click="activateMeld(idx)"
          >
            <div class="meld-slot-head">
              <span class="slot-index">#{{ idx + 1 }}</span>
              <span v-if="meldKindLabel(idx)" class="meld-kind" :class="{ invalid: meldInvalid(idx) }">{{ meldKindLabel(idx) }}</span>
              <el-button v-if="meld.codes.length" type="danger" link size="small" @click.stop="clearMeld(idx)">清空</el-button>
            </div>
            <div class="meld-tiles">
              <template v-if="meld.codes.length">
                <span
                  v-for="code in meld.codes"
                  :key="code"
                  class="hq-tile hq-tile-sm"
                  :title="code"
                  @click.stop="removeMeldTile(idx, code)"
                >
                  <img :src="tileFaceUrl(code)" :alt="code" draggable="false" @error="onTileError" />
                </span>
              </template>
              <span v-else class="empty-hint">点击本框后点牌面添加</span>
            </div>
            <div v-if="activeMeldIdx === idx && meldCandidates.size" class="meld-candidate-hint">
              还可选 {{ meldCandidates.size }} 张（{{ meldNeedCount }} 张成组）
            </div>
          </div>
          <span v-if="melds.length === 0" class="empty-hint">无副露（门清）时无需填写</span>
        </div>
      </div>

      <div class="row block">
        <div class="row-line"><span class="row-label">和牌方式</span></div>
        <div class="ways">
          <el-checkbox v-model="form.selfDraw">自摸</el-checkbox>
          <el-checkbox v-model="form.beforeFirstDiscard">首发前和牌（天和）</el-checkbox>
          <el-checkbox v-model="form.wallEmpty">牌山已空（海底）</el-checkbox>
        </div>
      </div>

      <div class="result-embed">
        <div v-if="!result" class="empty">
          <span class="input-target-bar">输入手牌后计算和牌与番数</span>
        </div>
        <div v-else-if="result.mode === 'score'">
          <div :class="['banner', result.is_hepai ? 'success' : 'fail']">
            <div class="banner-num">{{ result.is_hepai ? result.result.points : '—' }}</div>
            <div class="banner-text">{{ result.is_hepai ? '分' : '不能和牌' }}</div>
          </div>
          <div v-if="result.is_hepai" class="score-lines">
            <div class="score-line">
              <span>底分</span><strong>{{ result.result.base }}</strong>
            </div>
            <div class="score-line">
              <span>番数合计</span><strong>{{ result.result.fan_total }}</strong>
            </div>
            <div class="score-line">
              <span>牌型</span><strong>{{ result.result.concealed ? '门清' : '副露' }}</strong>
            </div>
          </div>
          <div v-if="result.is_hepai" class="fan-block">
            <h4>番种构成（{{ result.result.fans.length }} 项）</h4>
            <div class="fan-tags nowrap-scroll">
              <el-tag
                v-for="(fan, idx) in result.result.fans"
                :key="idx"
                type="success"
                effect="plain"
                size="small"
              >{{ fan.name }} ×{{ fan.count }}（{{ fan.value }} 番）</el-tag>
            </div>
          </div>
          <div v-else class="msg-inline">该牌型不能构成和牌（所有牌必须全部组成 3 张及以上的合法牌组）。</div>
        </div>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">
            牌面（{{ activeMeldIdx >= 0 ? `添加到副露 #${activeMeldIdx + 1}，仅高亮牌可组成合法牌组` : `添加到手牌（合计上限 14 张）` }}）
          </span>
        </div>
        <div class="palette">
          <div v-for="group in paletteGroups" :key="group.colour" class="palette-row">
            <span class="palette-code" :style="{ color: `hsl(${hueOf(group.colour)} 62% 38%)` }">{{ group.codePrefix }}</span>
            <span
              v-for="number in 9"
              :key="number"
              class="hq-tile hq-tile-sm"
              :class="paletteTileClass(group.codePrefix + number)"
              :title="group.codePrefix + number"
              @click="onPalettePick(group.codePrefix + number)"
            >
              <img :src="tileFaceUrl(group.codePrefix + number)" :alt="group.codePrefix + number" draggable="false" @error="onTileError" />
            </span>
          </div>
        </div>
      </div>

      <div class="actions">
        <el-button type="primary" size="default" @click="calculateScore">计算得分</el-button>
        <el-button size="default" @click="calculateDecompose">查看全部拆解</el-button>
      </div>
    </section>

    <section id="hongque-decompose-section" class="decompose-section" v-if="decomposeResult">
      <div class="decompose-head">
        <h2>全部拆解</h2>
        <span class="decompose-count">
          {{ decomposeResult.is_hepai ? `共 ${decomposeResult.decompositions.length} 种（按分数从高到低）` : '不能和牌，无可拆分拆解' }}
        </span>
      </div>
      <div v-if="decomposeResult.is_hepai" class="decomp-list">
        <div
          v-for="(item, idx) in decomposeResult.decompositions"
          :key="idx"
          class="decomp-item"
        >
          <div class="decomp-header">
            <span class="decomp-rank">#{{ idx + 1 }}</span>
            <span class="decomp-score">{{ item.points }} 分（{{ item.fan_total }} 番）</span>
            <span class="decomp-kind">{{ item.concealed ? '门清' : '副露' }}</span>
          </div>
          <div class="decomp-groups">
            <div v-for="(group, gIdx) in item.groups" :key="gIdx" class="decomp-group">
              <span
                v-for="code in group"
                :key="code"
                class="hq-tile hq-tile-sm"
                :title="code"
              >
                <img :src="tileFaceUrl(code)" :alt="code" draggable="false" @error="onTileError" />
              </span>
            </div>
          </div>
          <div class="decomp-fans">
            <el-tag v-for="(fan, fIdx) in item.fans" :key="fIdx" size="small" effect="plain">
              {{ fan.name }} ×{{ fan.count }}
            </el-tag>
          </div>
        </div>
      </div>
      <div v-else class="msg-inline">该牌型不能和牌。</div>
    </section>
  </div>
</template>

<script setup>
import { ref, reactive, computed, nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import CalculatorSwitcher from '@/components/CalculatorSwitcher.vue'
import {
  bestWinResult,
  allWinResults,
  meldCandidateTiles,
  inferMeldKind,
} from '@/game2d/calc/hongque'

const TILE_BASE_URL = '/game2d-assets/hongque-tiles/'
const MAX_TOTAL_TILES = 14

const COLOUR_GROUPS = [
  { codePrefix: 'AX', label: '红', colour: 0 },
  { codePrefix: 'AY', label: '红橙', colour: 1 },
  { codePrefix: 'BX', label: '橙', colour: 2 },
  { codePrefix: 'BY', label: '橙黄', colour: 3 },
  { codePrefix: 'CX', label: '黄', colour: 4 },
  { codePrefix: 'CY', label: '黄绿', colour: 5 },
  { codePrefix: 'DX', label: '绿', colour: 6 },
  { codePrefix: 'DY', label: '绿青', colour: 7 },
  { codePrefix: 'EX', label: '青', colour: 8 },
  { codePrefix: 'EY', label: '青蓝', colour: 9 },
  { codePrefix: 'FX', label: '蓝', colour: 10 },
  { codePrefix: 'FY', label: '蓝紫', colour: 11 },
  { codePrefix: 'GX', label: '紫', colour: 12 },
  { codePrefix: 'GY', label: '紫红', colour: 13 },
]

const paletteGroups = COLOUR_GROUPS

function hueOf(colour) {
  return Math.round((colour / 14) * 360)
}

function tileFaceUrl(code) {
  return `${TILE_BASE_URL}${code}.png`
}

const tileColourOf = (code) => COLOUR_GROUPS.findIndex((group) => group.codePrefix === code.slice(0, 2))
const tileNumberOf = (code) => Number(code[2])

/** 手牌按（花色，数字）排序，与牌面调色板/对局手牌一致。 */
const sortHandCodes = (codes) => {
  codes.sort((a, b) => tileColourOf(a) - tileColourOf(b) || tileNumberOf(a) - tileNumberOf(b))
  return codes
}

/** 副露组内按（数字，花色）排序：顺子/彩虹按数字自然递增，同数刻子按花色。 */
const sortMeldCodes = (codes) => {
  codes.sort((a, b) => tileNumberOf(a) - tileNumberOf(b) || tileColourOf(a) - tileColourOf(b))
  return codes
}

const textInput = ref('')
const result = ref(null)
const decomposeResult = ref(null)
const activeMeldIdx = ref(-1)

const form = reactive({
  hand: [],
  melds: [{ codes: [] }, { codes: [] }, { codes: [] }, { codes: [] }],
  selfDraw: true,
  beforeFirstDiscard: false,
  wallEmpty: false,
})

const melds = computed(() => form.melds)
const activeMelds = computed(() => form.melds.filter((meld) => meld.codes.length > 0))

const handCountText = computed(() => {
  const total = form.hand.length + activeMelds.value.reduce((sum, meld) => sum + meld.codes.length, 0)
  return `${total}/${MAX_TOTAL_TILES} 张（手牌 ${form.hand.length} + 副露 ${total - form.hand.length}）`
})

const handCountTagType = computed(() => {
  const total = form.hand.length + activeMelds.value.reduce((sum, meld) => sum + meld.codes.length, 0)
  if (total === 0) return 'warning'
  if (total > MAX_TOTAL_TILES) return 'danger'
  if (total < 12) return 'warning'
  return total % 3 === 0 ? 'success' : 'warning'
})

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

const applyTextInput = () => {
  if (!textInput.value.trim()) return true
  try {
    form.hand = sortHandCodes(parseCodes(textInput.value))
    return true
  } catch (error) {
    ElMessage.error(`简写解析失败：${error.message}`)
    return false
  }
}

const allUsedCodes = computed(() => {
  const codes = new Set(form.hand)
  for (const meld of form.melds) {
    for (const code of meld.codes) codes.add(code)
  }
  return codes
})

const totalTiles = computed(() => {
  let total = form.hand.length
  for (const meld of form.melds) total += meld.codes.length
  return total
})

const activeMeld = computed(() => (activeMeldIdx.value >= 0 ? form.melds[activeMeldIdx.value] : null))

/** 当前激活副露框的可选下一张牌（能组成合法牌组的高亮集合）。 */
const meldCandidates = computed(() => {
  const meld = activeMeld.value
  if (!meld) return new Set()
  return meldCandidateTiles(meld.codes, allUsedCodes.value)
})

const meldNeedCount = computed(() => {
  const meld = activeMeld.value
  if (!meld) return 0
  return Math.max(0, 3 - meld.codes.length)
})

const isUsed = (code) => allUsedCodes.value.has(code)
const isInMeld = (code) => form.melds.some((meld) => meld.codes.includes(code))

const paletteTileClass = (code) => {
  if (isUsed(code)) return { used: true }
  if (activeMeldIdx.value >= 0 && !meldCandidates.value.has(code)) return { disabled: true }
  return {}
}

const syncTextInput = () => {
  textInput.value = form.hand.join(' ')
}

const addHandTile = (code) => {
  if (form.hand.includes(code)) {
    removeHandTile(form.hand.indexOf(code))
    return
  }
  if (isUsed(code)) {
    ElMessage.warning(`牌 ${code} 已在手牌或副露中`)
    return
  }
  if (totalTiles.value >= MAX_TOTAL_TILES) {
    ElMessage.warning(`合计已达 ${MAX_TOTAL_TILES} 张上限`)
    return
  }
  form.hand.push(code)
  sortHandCodes(form.hand)
  syncTextInput()
}

const removeHandTile = (idx) => {
  form.hand.splice(idx, 1)
  syncTextInput()
}

const activateHand = () => {
  activeMeldIdx.value = -1
}

const clearMeld = (idx) => {
  form.melds[idx].codes = []
}

const activateMeld = (idx) => {
  activeMeldIdx.value = idx
}

const addMeldTile = (code) => {
  const meld = form.melds[activeMeldIdx.value]
  if (!meld) return
  if (!meldCandidates.value.has(code)) {
    ElMessage.warning(`牌 ${code} 不能与当前副露组成合法牌组`)
    return
  }
  if (meld.codes.includes(code)) {
    removeMeldTile(activeMeldIdx.value, code)
    return
  }
  if (totalTiles.value >= MAX_TOTAL_TILES) {
    ElMessage.warning(`合计已达 ${MAX_TOTAL_TILES} 张上限`)
    return
  }
  meld.codes.push(code)
  sortMeldCodes(meld.codes)
  // 长组可继续追加；成组后仍允许选择能继续扩展的牌。
}

const removeMeldTile = (idx, code) => {
  const meld = form.melds[idx]
  const index = meld.codes.indexOf(code)
  if (index >= 0) meld.codes.splice(index, 1)
}

const onPalettePick = (code) => {
  if (activeMeldIdx.value >= 0) {
    addMeldTile(code)
  } else {
    addHandTile(code)
  }
}

const meldKindLabel = (idx) => {
  const meld = form.melds[idx]
  if (!meld || meld.codes.length < 3) return ''
  const info = inferMeldKind(meld.codes)
  if (!info) return '非法牌组'
  return info.label
}

const meldInvalid = (idx) => {
  const meld = form.melds[idx]
  return meld && meld.codes.length >= 3 && !inferMeldKind(meld.codes)
}

const buildRequestBody = () => {
  const openMelds = []
  for (const meld of form.melds) {
    if (!meld.codes.length) continue
    if (meld.codes.length < 3) {
      throw new Error(`副露 #${form.melds.indexOf(meld) + 1} 需至少 3 张，当前 ${meld.codes.length} 张`)
    }
    openMelds.push({ tiles: [...meld.codes] })
  }
  const allCodes = [...form.hand, ...openMelds.flatMap((meld) => meld.tiles)]
  if (new Set(allCodes).size !== allCodes.length) {
    throw new Error('手牌与副露存在重复牌')
  }
  if (allCodes.length < 12) {
    throw new Error(`牌数最少 12 张（当前 ${allCodes.length} 张）`)
  }
  return {
    hand: [...form.hand],
    open_melds: openMelds,
    self_draw: form.selfDraw,
    before_first_discard: form.beforeFirstDiscard,
    wall_empty: form.wallEmpty,
  }
}

const calculateScore = () => {
  if (!applyTextInput()) return
  let body
  try {
    body = buildRequestBody()
  } catch (error) {
    ElMessage.error(error.message)
    return
  }
  result.value = null
  try {
    const score = bestWinResult(body.hand, body.open_melds, {
      selfDraw: body.self_draw,
      beforeFirstDiscard: body.before_first_discard,
      wallEmpty: body.wall_empty,
    })
    result.value = {
      mode: 'score',
      is_hepai: score !== null,
      result: score ? {
        ...score,
        fan_total: score.fanTotal,
      } : null,
    }
  } catch (error) {
    ElMessage.error(`计算失败：${error.message}`)
  }
}

const calculateDecompose = () => {
  if (!applyTextInput()) return
  let body
  try {
    body = buildRequestBody()
  } catch (error) {
    ElMessage.error(error.message)
    return
  }
  decomposeResult.value = null
  try {
    const decompositions = allWinResults(body.hand, body.open_melds, {
      selfDraw: body.self_draw,
      beforeFirstDiscard: body.before_first_discard,
      wallEmpty: body.wall_empty,
    })
    decomposeResult.value = {
      is_hepai: decompositions.length > 0,
      decompositions: decompositions.map((item) => ({
        ...item,
        fan_total: item.fanTotal,
      })),
    }
    nextTick(() => {
      document.getElementById('hongque-decompose-section')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    })
  } catch (error) {
    ElMessage.error(`计算失败：${error.message}`)
  }
}

const onTileError = (event) => {
  const img = event.target
  img.style.opacity = '0.25'
}

const loadDemo = () => {
  form.hand = sortHandCodes(parseCodes('AX1 AX2 AX3 BX4 BX5 BX6'))
  form.melds = [
    { codes: sortMeldCodes(parseCodes('CY7 DY7 EY7')) },
    { codes: sortMeldCodes(parseCodes('FX1 GX1 GY1')) },
    { codes: [] },
    { codes: [] },
  ]
  form.selfDraw = true
  form.beforeFirstDiscard = false
  form.wallEmpty = false
  textInput.value = form.hand.join(' ')
  result.value = null
  decomposeResult.value = null
  activeMeldIdx.value = -1
}

const resetAll = () => {
  form.hand = []
  form.melds = [{ codes: [] }, { codes: [] }, { codes: [] }, { codes: [] }]
  form.selfDraw = true
  form.beforeFirstDiscard = false
  form.wallEmpty = false
  textInput.value = ''
  result.value = null
  decomposeResult.value = null
  activeMeldIdx.value = -1
}
</script>

<style scoped>
.hongque-calc {
  max-width: 920px;
  margin: 0 auto;
  padding: 14px 0 24px;
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
  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.35);
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
  box-shadow: 0 6px 20px rgba(15, 45, 110, 0.25);
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
  margin-bottom: 8px;
  gap: 10px;
}

.hand-row {
  display: flex;
  padding: 6px;
  background: #f8fafc;
  border-radius: 6px;
  border: 1px dashed #93c5fd;
  min-height: 52px;
  cursor: pointer;
  transition: border-color 0.12s ease, background 0.12s ease;
}

.hand-row.active {
  border-color: #2563eb;
  border-style: solid;
  background: #eff6ff;
}

.hand-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  min-height: 40px;
  align-items: center;
  flex: 1;
}

.empty-hint {
  color: #94a3b8;
  font-size: 12px;
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
  transition: transform 0.1s ease, box-shadow 0.1s ease, border-color 0.1s ease;
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

.hq-tile.selected {
  outline: 2px solid #2563eb;
  outline-offset: 1px;
}

.hq-tile.used {
  opacity: 0.35;
  cursor: not-allowed;
  filter: grayscale(0.6);
}

.hq-tile.disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.meld-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 6px;
}

.meld-slot {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 6px;
  background: #f8fafc;
  border-radius: 6px;
  border: 1px dashed #93c5fd;
  min-height: 84px;
  min-width: 0;
  cursor: pointer;
  transition: border-color 0.12s ease, background 0.12s ease;
}

.meld-slot.active {
  border-color: #2563eb;
  border-style: solid;
  background: #eff6ff;
}

.meld-slot.filled {
  border-style: solid;
  background: #f0f9ff;
}

.meld-slot-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.slot-index {
  font-size: 10px;
  font-weight: 700;
  color: #94a3b8;
}

.meld-kind {
  font-size: 10.5px;
  font-weight: 700;
  color: #1d4ed8;
  background: #dbeafe;
  border-radius: 4px;
  padding: 1px 6px;
}

.meld-kind.invalid {
  color: #b91c1c;
  background: #fee2e2;
}

.meld-candidate-hint {
  font-size: 10.5px;
  color: #1d4ed8;
  font-weight: 600;
}

.meld-tiles {
  display: flex;
  flex-wrap: wrap;
  gap: 3px;
  align-content: flex-start;
  min-height: 44px;
}

.ways {
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
  padding: 2px 0;
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

.result-embed {
  padding: 10px 12px;
  background: #f8fafc;
  border-top: 1px dashed #dbeafe;
  min-height: 64px;
}

.empty {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #94a3b8;
  font-size: 13px;
  min-height: 40px;
}

.input-target-bar {
  font-size: 1.05rem;
  font-weight: 700;
  color: #1f2933;
}

.banner {
  display: flex;
  align-items: baseline;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 8px;
  margin-bottom: 8px;
}

.banner.success {
  background: #f0f9eb;
  color: #529b2e;
}

.banner.fail {
  background: #fef0f0;
  color: #f56c6c;
}

.banner-num {
  font-size: 1.8rem;
  font-weight: 800;
}

.banner-text {
  font-size: 1rem;
  font-weight: 600;
}

.score-lines {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  margin-bottom: 8px;
}

.score-line {
  display: flex;
  gap: 6px;
  font-size: 13px;
  color: #475569;
}

.score-line strong {
  color: #1f2933;
}

.fan-block h4 {
  margin: 0 0 6px;
  font-size: 13px;
  color: #475569;
}

.fan-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.msg-inline {
  color: #606266;
  font-size: 13px;
}

.decomp-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.decomp-summary {
  font-size: 12.5px;
  color: #475569;
}

.decomp-item {
  border: 1px solid #dbeafe;
  border-radius: 8px;
  padding: 8px;
  background: white;
}

.decomp-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.decomp-rank {
  font-size: 12px;
  font-weight: 700;
  color: #909399;
}

.decomp-score {
  font-size: 13px;
  font-weight: 700;
  color: #529b2e;
}

.decomp-kind {
  font-size: 11px;
  font-weight: 600;
  color: #1d4ed8;
  background: #dbeafe;
  border-radius: 4px;
  padding: 1px 6px;
}

.decompose-section {
  margin-top: 16px;
  background: rgba(255, 255, 255, 0.96);
  border: 1px solid #bfdbfe;
  border-radius: 12px;
  padding: 12px;
  box-shadow: 0 6px 20px rgba(15, 45, 110, 0.25);
  scroll-margin-top: 16px;
}

.decompose-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.decompose-head h2 {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  color: #1e40af;
}

.decompose-count {
  font-size: 12.5px;
  color: #475569;
  font-weight: 600;
}

.decomp-groups {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.decomp-group {
  display: inline-flex;
  gap: 2px;
  padding: 3px;
  background: #f1f5f9;
  border-radius: 5px;
}

.decomp-fans {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 6px;
}

.actions {
  padding: 10px 12px;
  display: flex;
  gap: 8px;
  border-top: 1px dashed #dbeafe;
}
</style>
