<!--
  虹雀² 和牌计算器（隐藏测试入口 /hongque-calc）
  按虹雀² v1.6 规则计算和牌拆解与番数/分数，交互设计参照国标计算器。
  牌码格式：颜色码 AX/AY/BX/.../GY + 数字 1..9（共 126 张唯一牌）。
  规则要点：所有牌必须且仅能属于一个合法牌组（3 张及以上），无雀头。
-->
<template>
  <div class="hongque-calc">
    <div class="page-header">
      <h1>虹雀² 和牌计算器</h1>
      <p class="subtitle">
        输入牌码（如 AX1 AX2 AX3）或点击下方牌面；所有牌必须分成 3 张及以上的合法牌组，无雀头。
        副露按整组输入，用于规则开发者校验和牌与算分。
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
        <el-button type="primary" size="small" plain :loading="loading" @click="calculateScore">解析</el-button>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">手牌 {{ form.hand.length }} 张</span>
          <el-tag size="small" :type="handCountTagType" effect="plain">{{ handCountText }}</el-tag>
        </div>
        <div class="hand-row">
          <div class="hand-bar" :class="{ 'is-empty': form.hand.length === 0 }">
            <template v-if="form.hand.length">
              <span
                v-for="(code, idx) in form.hand"
                :key="'h-' + code"
                class="hq-tile"
                :style="tileStyle(code)"
                :title="code"
                @click="removeHandTile(idx)"
              >{{ tileShort(code) }}</span>
            </template>
            <span v-else class="empty-hint">点击下方牌面添加手牌，点击手牌移除</span>
          </div>
        </div>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">副露牌组（{{ melds.length }}/4，每组至少 3 张）</span>
          <el-button type="text" size="small" :disabled="melds.length >= 4" @click="addMeld">+ 添加牌组</el-button>
        </div>
        <div class="meld-list">
          <div v-for="(meld, idx) in melds" :key="'m-' + idx" class="meld-slot">
            <el-input
              :model-value="meld.text"
              size="small"
              placeholder="例：CX7 DX7 EX7"
              @update:model-value="(v) => onMeldInput(idx, v)"
            />
            <el-button size="small" type="danger" plain @click="removeMeld(idx)">删除</el-button>
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
        <div v-if="loading" class="empty">
          <el-icon class="is-loading" :size="18"><Loading /></el-icon>
          <span>正在计算...</span>
        </div>
        <div v-else-if="!result" class="empty">
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
        <div v-else-if="result.mode === 'decompose'">
          <div v-if="!result.is_hepai" class="msg-inline">该牌型不能和牌，无可拆分拆解。</div>
          <div v-else class="decomp-list">
            <div class="decomp-summary">共 {{ result.decompositions.length }} 种拆解（按分数从高到低）</div>
            <div
              v-for="(item, idx) in result.decompositions"
              :key="idx"
              class="decomp-item"
            >
              <div class="decomp-header">
                <span class="decomp-rank">#{{ idx + 1 }}</span>
                <span class="decomp-score">{{ item.points }} 分（{{ item.fan_total }} 番）</span>
              </div>
              <div class="decomp-groups">
                <div v-for="(group, gIdx) in item.groups" :key="gIdx" class="decomp-group">
                  <span
                    v-for="code in group"
                    :key="code"
                    class="hq-tile hq-tile-sm"
                    :style="tileStyle(code)"
                    :title="code"
                  >{{ tileShort(code) }}</span>
                </div>
              </div>
              <div class="decomp-fans">
                <el-tag v-for="(fan, fIdx) in item.fans" :key="fIdx" size="small" effect="plain">
                  {{ fan.name }} ×{{ fan.count }}
                </el-tag>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="row block">
        <div class="row-line"><span class="row-label">牌面（点击加入手牌）</span></div>
        <div class="palette">
          <div v-for="group in paletteGroups" :key="group.colour" class="palette-row">
            <span class="palette-code">{{ group.codePrefix }}</span>
            <span
              v-for="number in 9"
              :key="number"
              class="hq-tile hq-tile-sm"
              :class="{ selected: isInHand(group.codePrefix + number) }"
              :style="tileStyle(group.codePrefix + number)"
              :title="group.codePrefix + number"
              @click="addHandTile(group.codePrefix + number)"
            >{{ number }}</span>
          </div>
        </div>
      </div>

      <div class="actions">
        <el-button type="primary" size="default" :loading="loading" @click="calculateScore">计算得分</el-button>
        <el-button size="default" :loading="loading" @click="calculateDecompose">查看全部拆解</el-button>
      </div>
    </section>
  </div>
</template>

<script setup>
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Loading } from '@element-plus/icons-vue'
import axios from 'axios'

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

// 14 级色相：红→橙→黄→绿→青→蓝→紫→红
function hueOf(colour) {
  return Math.round((colour / 14) * 360)
}

function tileStyle(code) {
  const match = /^([A-G])([XY])([1-9])$/.exec(code)
  if (!match) return {}
  const colour = 'ABCDEFG'.indexOf(match[1]) * 2 + (match[2] === 'Y' ? 1 : 0)
  const hue = hueOf(colour)
  return {
    background: `linear-gradient(135deg, hsl(${hue} 62% 82%), hsl(${hue} 58% 70%))`,
    borderColor: `hsl(${hue} 55% 45%)`,
    color: `hsl(${hue} 48% 18%)`,
  }
}

function tileShort(code) {
  return code.slice(-1)
}

const textInput = ref('')
const loading = ref(false)
const result = ref(null)

const form = reactive({
  hand: [],
  melds: [],
  selfDraw: true,
  beforeFirstDiscard: false,
  wallEmpty: false,
})

const melds = computed(() => form.melds)

const handCountText = computed(() => {
  const total = form.hand.length + form.melds.reduce((sum, meld) => sum + meld.codes.length, 0)
  return `共 ${total} 张（手牌 ${form.hand.length} + 副露 ${total - form.hand.length}）`
})

const handCountTagType = computed(() => {
  const total = form.hand.length + form.melds.reduce((sum, meld) => sum + meld.codes.length, 0)
  if (total === 0) return 'warning'
  return total % 3 === 0 ? 'success' : 'danger'
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
  if (!textInput.value.trim()) return
  try {
    form.hand = parseCodes(textInput.value)
  } catch (error) {
    ElMessage.error(`简写解析失败：${error.message}`)
    return false
  }
  return true
}

const isInHand = (code) => form.hand.includes(code)

const addHandTile = (code) => {
  if (form.hand.includes(code)) {
    removeHandTile(form.hand.indexOf(code))
    return
  }
  if (form.hand.length >= 21) {
    ElMessage.warning('手牌已满（最多 21 张）')
    return
  }
  form.hand.push(code)
  syncTextInput()
}

const removeHandTile = (idx) => {
  form.hand.splice(idx, 1)
  syncTextInput()
}

const syncTextInput = () => {
  textInput.value = form.hand.join(' ')
}

const addMeld = () => {
  form.melds.push({ text: '', codes: [] })
}

const removeMeld = (idx) => {
  form.melds.splice(idx, 1)
}

const onMeldInput = (idx, value) => {
  const meld = form.melds[idx]
  meld.text = value
  try {
    meld.codes = parseCodes(value)
  } catch (_) {
    meld.codes = []
  }
}

const buildRequestBody = () => {
  const openMelds = []
  for (const meld of form.melds) {
    if (!meld.codes.length) continue
    if (meld.codes.length < 3) {
      throw new Error(`副露牌组需至少 3 张：${meld.text}`)
    }
    openMelds.push({ tiles: meld.codes })
  }
  const allCodes = [...form.hand, ...openMelds.flatMap((meld) => meld.tiles)]
  if (new Set(allCodes).size !== allCodes.length) {
    throw new Error('手牌与副露存在重复牌')
  }
  return {
    hand: [...form.hand],
    open_melds: openMelds,
    self_draw: form.selfDraw,
    before_first_discard: form.beforeFirstDiscard,
    wall_empty: form.wallEmpty,
  }
}

const calculateScore = async () => {
  if (!applyTextInput()) return
  let body
  try {
    body = buildRequestBody()
  } catch (error) {
    ElMessage.error(error.message)
    return
  }
  loading.value = true
  result.value = null
  try {
    const resp = await axios.post('/api/mahjong/hongque/score', body)
    if (!resp.data.success) {
      ElMessage.error(resp.data.message || '计算失败')
      return
    }
    const data = resp.data.data
    result.value = {
      mode: 'score',
      is_hepai: data.is_hepai,
      result: data.result ? {
        ...data.result,
        fan_total: data.result.fanTotal,
      } : null,
    }
  } catch (err) {
    console.error(err)
    const msg = err.response?.data?.message || err.message
    ElMessage.error(`计算失败：${msg}`)
  } finally {
    loading.value = false
  }
}

const calculateDecompose = async () => {
  if (!applyTextInput()) return
  let body
  try {
    body = buildRequestBody()
  } catch (error) {
    ElMessage.error(error.message)
    return
  }
  loading.value = true
  result.value = null
  try {
    const resp = await axios.post('/api/mahjong/hongque/decompose', body)
    if (!resp.data.success) {
      ElMessage.error(resp.data.message || '计算失败')
      return
    }
    const data = resp.data.data
    result.value = {
      mode: 'decompose',
      is_hepai: data.is_hepai,
      decompositions: data.decompositions.map((item) => ({
        ...item,
        fan_total: item.fanTotal,
      })),
    }
  } catch (err) {
    console.error(err)
    const msg = err.response?.data?.message || err.message
    ElMessage.error(`计算失败：${msg}`)
  } finally {
    loading.value = false
  }
}

const loadDemo = () => {
  form.hand = parseCodes('AX1 AX2 AX3 BX4 BX5 BX6 CY7 DY7 EY7')
  form.melds = []
  form.selfDraw = true
  form.beforeFirstDiscard = false
  form.wallEmpty = false
  textInput.value = form.hand.join(' ')
  result.value = null
}

const resetAll = () => {
  form.hand = []
  form.melds = []
  form.selfDraw = true
  form.beforeFirstDiscard = false
  form.wallEmpty = false
  textInput.value = ''
  result.value = null
}
</script>

<style scoped>
.hongque-calc {
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

.hand-row {
  padding: 6px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px dashed var(--omu-border, #ebeef5);
  min-height: 44px;
}

.hand-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  min-height: 32px;
  align-items: center;
}

.empty-hint {
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12px;
}

.hq-tile {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 40px;
  border-radius: 5px;
  border: 1.5px solid;
  font-weight: 700;
  font-size: 15px;
  cursor: pointer;
  user-select: none;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.18);
  transition: transform 0.1s ease, box-shadow 0.1s ease;
}

.hq-tile:hover {
  transform: translateY(-2px);
  box-shadow: 0 3px 6px rgba(0, 0, 0, 0.22);
}

.hq-tile-sm {
  width: 24px;
  height: 32px;
  font-size: 12px;
}

.hq-tile.selected {
  outline: 2px solid #409eff;
  outline-offset: 1px;
}

.meld-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.meld-slot {
  display: flex;
  align-items: center;
  gap: 6px;
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
  font-weight: 600;
  color: var(--omu-text-soft, #475569);
  flex-shrink: 0;
}

.result-embed {
  padding: 10px 12px;
  background: #fafbfc;
  border-top: 1px dashed var(--omu-border, #ebeef5);
  min-height: 64px;
}

.empty {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--omu-text-muted, #94a3b8);
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
  color: var(--omu-text-soft, #475569);
}

.score-line strong {
  color: #1f2933;
}

.fan-block h4 {
  margin: 0 0 6px;
  font-size: 13px;
  color: var(--omu-text-soft, #475569);
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
  color: var(--omu-text-soft, #475569);
}

.decomp-item {
  border: 1px solid var(--omu-border, #ebeef5);
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

.decomp-groups {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.decomp-group {
  display: inline-flex;
  gap: 2px;
  padding: 3px;
  background: var(--omu-surface-soft, #f5f7fa);
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
  border-top: 1px dashed var(--omu-border, #ebeef5);
}
</style>
