<!-- 国标麻将算分与拆解页面 -->
<template>
  <div class="chinese-mahjong">
    <div class="page-header">
      <h1>国标麻将牌型解算</h1>
      <p>选择手牌、副露、和牌张和和牌方式，自动计算番种、得分以及全部和牌拆解形态。</p>
    </div>

    <el-row :gutter="20">
      <el-col :lg="14" :md="24">
        <el-card class="section-card">
          <template #header>
            <div class="section-header">
              <span class="section-title">输入区</span>
              <el-button type="warning" plain size="small" @click="resetAll">清空全部</el-button>
            </div>
          </template>

          <!-- 手牌 -->
          <div class="block">
            <div class="block-title">
              <span>手牌（含和牌张，共 {{ totalHandTiles }} 张）</span>
              <el-tag size="small" :type="handCountTagType">{{ handCountText }}</el-tag>
            </div>
            <div class="tile-bar">
              <TileChip
                v-for="(id, idx) in sortedHand"
                :key="`hand-${idx}-${id}`"
                :tile-id="id"
                :highlighted="id === form.getTile && idx === lastGetTileIndex"
                size="md"
                @click="removeHandTile(idx)"
              />
              <el-empty
                v-if="form.hand.length === 0"
                description="点击下方牌面添加手牌"
                :image-size="40"
                class="empty-hint"
              />
            </div>
          </div>

          <!-- 副露 -->
          <div class="block">
            <div class="block-title">
              <span>副露 / 暗刻 / 暗杠（最多 4 组）</span>
              <el-button type="primary" plain size="small" :disabled="form.fulus.length >= 4" @click="addFulu">
                添加副露
              </el-button>
            </div>
            <div v-if="form.fulus.length === 0" class="hint">门清和牌时无需添加副露</div>
            <div v-else class="fulu-list">
              <div v-for="(fulu, idx) in form.fulus" :key="idx" class="fulu-item">
                <div class="fulu-controls">
                  <el-select v-model="fulu.kind" size="small" style="width: 110px;" @change="onFuluKindChange(idx)">
                    <el-option label="明刻 (碰)" value="k" />
                    <el-option label="暗刻" value="K" />
                    <el-option label="明杠" value="g" />
                    <el-option label="暗杠" value="G" />
                    <el-option label="明顺 (吃)" value="s" />
                    <el-option label="暗顺 (手内顺子)" value="S" />
                  </el-select>
                  <span class="fulu-tile-label">中心牌：</span>
                  <span class="fulu-tile-display" v-if="fulu.tileId">
                    <TileChip :tile-id="fulu.tileId" size="sm" highlighted />
                  </span>
                  <span v-else class="hint" style="color:#f56c6c;">未选</span>
                  <el-button type="danger" link @click="removeFulu(idx)">删除</el-button>
                </div>
                <div class="fulu-tip">
                  <template v-if="fulu.kind === 's' || fulu.kind === 'S'">
                    顺子请选择中心牌（如 [3,4,5] 选 4）；不可包含字牌。
                  </template>
                  <template v-else>
                    刻子/杠请选择对应牌种。
                  </template>
                </div>
                <TilePalette :size="'sm'" @pick="setFuluTile(idx, $event)" />
              </div>
            </div>
          </div>

          <!-- 花牌 -->
          <div class="block">
            <div class="block-title">
              <span>花牌（每张 1 番）</span>
            </div>
            <div class="tile-bar">
              <TileChip
                v-for="(id, idx) in form.flowers"
                :key="`flower-${idx}-${id}`"
                :tile-id="id"
                size="sm"
                @click="removeFlower(idx)"
              />
              <span v-if="form.flowers.length === 0" class="hint">无花牌</span>
            </div>
          </div>

          <!-- 牌面选择 -->
          <div class="block">
            <div class="block-title">
              <span>牌面 — 点击添加到手牌</span>
              <el-radio-group v-model="palette.target" size="small">
                <el-radio-button label="hand">填入手牌</el-radio-button>
                <el-radio-button label="get_tile">设为和牌张</el-radio-button>
                <el-radio-button label="flower">添加花牌</el-radio-button>
              </el-radio-group>
            </div>
            <TilePalette
              :size="'md'"
              :include-flowers="palette.target === 'flower'"
              @pick="onPalettePick"
            />
          </div>

          <!-- 和牌方式 -->
          <div class="block">
            <div class="block-title"><span>和牌方式</span></div>
            <div class="ways-row">
              <el-radio-group v-model="form.hepaiType">
                <el-radio-button label="dianhe">点和（荣和）</el-radio-button>
                <el-radio-button label="zimo">自摸</el-radio-button>
              </el-radio-group>

              <el-select v-model="form.changFeng" placeholder="圈风（场风）" style="width: 130px;">
                <el-option label="场风东" value="场风东" />
                <el-option label="场风南" value="场风南" />
                <el-option label="场风西" value="场风西" />
                <el-option label="场风北" value="场风北" />
              </el-select>

              <el-select v-model="form.menFeng" placeholder="门风（自风）" style="width: 130px;">
                <el-option label="自风东" value="自风东" />
                <el-option label="自风南" value="自风南" />
                <el-option label="自风西" value="自风西" />
                <el-option label="自风北" value="自风北" />
              </el-select>
            </div>

            <div class="ways-row">
              <el-checkbox v-model="form.flagSet.heDanZhang">和单张（边/嵌/钓）</el-checkbox>
              <el-checkbox v-model="form.flagSet.heJueZhang">和绝张</el-checkbox>
              <el-checkbox v-model="form.flagSet.gangShangKaiHua">杠上开花</el-checkbox>
              <el-checkbox v-model="form.flagSet.qiangGangHe">抢杠和</el-checkbox>
              <el-checkbox v-model="form.flagSet.miaoShouHuiChun">妙手回春</el-checkbox>
              <el-checkbox v-model="form.flagSet.haiDiLaoYue">海底捞月</el-checkbox>
            </div>
          </div>

          <div class="action-row">
            <el-button type="primary" :loading="loading" :disabled="!canSubmit" @click="calculateScore">
              计算得分
            </el-button>
            <el-button :loading="loading" :disabled="!canSubmit" @click="calculateDecompose">
              查看全部拆解
            </el-button>
          </div>
        </el-card>
      </el-col>

      <el-col :lg="10" :md="24">
        <el-card class="section-card result-card">
          <template #header>
            <span class="section-title">计算结果</span>
          </template>

          <div v-if="loading" class="empty-state">
            <el-icon class="is-loading" :size="36"><Loading /></el-icon>
            <p>正在计算...</p>
          </div>

          <div v-else-if="!result" class="empty-state">
            <p>请输入手牌、副露与和牌方式后点击「计算得分」。</p>
          </div>

          <div v-else-if="result.mode === 'score'" class="result-block">
            <div :class="['score-banner', result.is_hepai ? 'success' : 'fail']">
              <div class="score-num">{{ result.score }}</div>
              <div class="score-text">{{ result.is_hepai ? '番' : '不能和牌' }}</div>
            </div>
            <div v-if="result.is_hepai" class="fan-list">
              <h4>番种构成</h4>
              <div class="fan-tags">
                <el-tag v-for="(name, idx) in result.fan_list" :key="idx" type="success" effect="dark">
                  {{ name }}
                </el-tag>
              </div>
            </div>
            <div v-else class="hint" style="text-align:center;">{{ result.message || '该牌型不构成和牌' }}</div>
          </div>

          <div v-else-if="result.mode === 'decompose'" class="result-block">
            <div v-if="!result.is_hepai" class="empty-state">
              <p>该牌型不能和牌，无可用拆解。</p>
            </div>
            <div v-else class="decomp-list">
              <div class="decomp-summary">
                共 {{ result.decompositions.length }} 种拆解，按番数从高到低展示：
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
                    <div class="decomp-group-tiles">
                      <TileChip v-for="(t, tIdx) in group.tiles" :key="tIdx" :tile-id="t" size="sm" />
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
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Loading } from '@element-plus/icons-vue'
import axios from 'axios'
import TileChip from '@/components/TileChip.vue'
import TilePalette from '@/components/TilePalette.vue'
import { sortTiles, TILE_NAME, combinationLabel } from '@/composables/useMahjongTiles'

// ======== 表单数据 ========
const form = reactive({
  hand: [],          // 手牌（含和牌张），number[]
  fulus: [],         // 副露：[{ kind: 'k'|'K'|'g'|'G'|'s'|'S', tileId: number }]
  flowers: [],       // 花牌
  getTile: null,     // 和牌张
  hepaiType: 'dianhe',
  changFeng: '场风东',
  menFeng: '自风东',
  flagSet: {
    heDanZhang: false,
    heJueZhang: false,
    gangShangKaiHua: false,
    qiangGangHe: false,
    miaoShouHuiChun: false,
    haiDiLaoYue: false,
  }
})

const palette = reactive({ target: 'hand' })

const loading = ref(false)
const result = ref(null)

// ======== 计算属性 ========
const sortedHand = computed(() => sortTiles(form.hand))

// 用于高亮显示和牌张：仅高亮排序后第一次出现的位置，避免重复高亮多张同号牌
const lastGetTileIndex = computed(() => {
  if (!form.getTile) return -1
  return sortedHand.value.indexOf(form.getTile)
})

const totalHandTiles = computed(() => form.hand.length)
const expectedHandCount = computed(() => 14 - form.fulus.length * 3)
const handCountText = computed(() => `${form.hand.length}/${expectedHandCount.value}`)
const handCountTagType = computed(() => {
  if (form.hand.length === expectedHandCount.value) return 'success'
  if (form.hand.length > expectedHandCount.value) return 'danger'
  return 'warning'
})

const canSubmit = computed(() => {
  return form.hand.length === expectedHandCount.value
    && form.getTile
    && form.hand.includes(form.getTile)
    && form.fulus.every(f => f.tileId)
})

// ======== 操作方法 ========
const onPalettePick = (id) => {
  switch (palette.target) {
    case 'hand':
      addHandTile(id)
      break
    case 'get_tile':
      addHandTile(id)
      form.getTile = id
      break
    case 'flower':
      if (id >= 51 && id <= 58) {
        form.flowers.push(id)
      } else {
        ElMessage.warning('请选择花牌（51-58）')
      }
      break
  }
}

const addHandTile = (id) => {
  if (id >= 51 && id <= 58) {
    form.flowers.push(id)
    return
  }
  if (form.hand.length >= expectedHandCount.value) {
    ElMessage.warning(`手牌已达上限 ${expectedHandCount.value} 张，请先调整副露或删除手牌`)
    return
  }
  // 牌堆约束：每种牌不超过 4 张（含副露中的同号牌、花牌数量不限于此）
  const totalCount = countTileEverywhere(id)
  if (totalCount >= 4) {
    ElMessage.warning(`牌 ${TILE_NAME[id]} 已达 4 张上限`)
    return
  }
  form.hand.push(id)
}

const removeHandTile = (idxInSorted) => {
  const id = sortedHand.value[idxInSorted]
  // 从原始 hand 中移除任一张相同 id 的牌
  const originalIdx = form.hand.indexOf(id)
  if (originalIdx !== -1) {
    form.hand.splice(originalIdx, 1)
  }
  if (form.getTile === id && !form.hand.includes(id)) {
    form.getTile = null
  }
}

const removeFlower = (idx) => {
  form.flowers.splice(idx, 1)
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
    // 顺子中心牌：仅允许 12-18 / 22-28 / 32-38
    if (tileId > 40 || tileId % 10 === 1 || tileId % 10 === 9) {
      ElMessage.warning('顺子中心牌应在 2-8 之间且不能为字牌')
      return
    }
  }
  fulu.tileId = tileId
}

const onFuluKindChange = (idx) => {
  // 如果切换到顺子但当前 tileId 不合法，清空
  const fulu = form.fulus[idx]
  if ((fulu.kind === 's' || fulu.kind === 'S') && fulu.tileId) {
    const tileId = fulu.tileId
    if (tileId > 40 || tileId % 10 === 1 || tileId % 10 === 9) {
      fulu.tileId = null
    }
  }
}

const countTileEverywhere = (id) => {
  let count = form.hand.filter(t => t === id).length
  for (const fulu of form.fulus) {
    if (!fulu.tileId) continue
    if (fulu.kind === 's' || fulu.kind === 'S') {
      // 顺子：中心牌 ± 1 也要计数
      if (fulu.tileId === id || fulu.tileId - 1 === id || fulu.tileId + 1 === id) {
        count += 1
      }
    } else if (fulu.kind === 'g' || fulu.kind === 'G') {
      if (fulu.tileId === id) count += 4
    } else if (fulu.kind === 'k' || fulu.kind === 'K') {
      if (fulu.tileId === id) count += 3
    }
  }
  return count
}

const resetAll = () => {
  form.hand = []
  form.fulus = []
  form.flowers = []
  form.getTile = null
  form.hepaiType = 'dianhe'
  form.changFeng = '场风东'
  form.menFeng = '自风东'
  form.flagSet = {
    heDanZhang: false,
    heJueZhang: false,
    gangShangKaiHua: false,
    qiangGangHe: false,
    miaoShouHuiChun: false,
    haiDiLaoYue: false,
  }
  result.value = null
}

// ======== 接口调用 ========
const buildRequestBody = () => {
  const tilesCombination = form.fulus
    .filter(f => f.tileId)
    .map(f => `${f.kind}${f.tileId}`)

  // 顺子请求时直接送 sNN（NN 为中心牌号），后端用同样的字典查询
  const wayToHepai = []
  wayToHepai.push(form.hepaiType === 'zimo' ? '自摸' : '点和')
  if (form.changFeng) wayToHepai.push(form.changFeng)
  if (form.menFeng) wayToHepai.push(form.menFeng)
  if (form.flagSet.heDanZhang) wayToHepai.push('和单张')
  if (form.flagSet.heJueZhang) wayToHepai.push('和绝张')
  if (form.flagSet.gangShangKaiHua) wayToHepai.push('杠上开花')
  if (form.flagSet.qiangGangHe) wayToHepai.push('抢杠和')
  if (form.flagSet.miaoShouHuiChun) wayToHepai.push('妙手回春')
  if (form.flagSet.haiDiLaoYue) wayToHepai.push('海底捞月')

  return {
    hand_tiles: [...form.hand],
    tiles_combination: tilesCombination,
    way_to_hepai: wayToHepai,
    get_tile: form.getTile,
    flower_tiles: [...form.flowers]
  }
}

const calculateScore = async () => {
  if (!canSubmit.value) {
    ElMessage.warning('请先完整填写手牌（含和牌张）和副露')
    return
  }
  loading.value = true
  result.value = null
  try {
    const resp = await axios.post('/api/mahjong/gb/score', buildRequestBody())
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
  if (!canSubmit.value) {
    ElMessage.warning('请先完整填写手牌（含和牌张）和副露')
    return
  }
  loading.value = true
  result.value = null
  try {
    const resp = await axios.post('/api/mahjong/gb/decompose', buildRequestBody())
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
.chinese-mahjong {
  max-width: 1400px;
  margin: 0 auto;
  padding: 20px;
  color: white;
}

.page-header {
  text-align: center;
  margin-bottom: 24px;
}
.page-header h1 {
  font-size: 2.4rem;
  font-weight: bold;
  margin-bottom: 8px;
  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.3);
}
.page-header p {
  opacity: 0.85;
  font-size: 1rem;
  max-width: 720px;
  margin: 0 auto;
}

.section-card {
  background: rgba(255, 255, 255, 0.08);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 14px;
  color: white;
  margin-bottom: 20px;
}
.section-card :deep(.el-card__header) {
  background: transparent;
  border-bottom: 1px solid rgba(255, 255, 255, 0.18);
  color: white;
}
.section-card :deep(.el-card__body) {
  background: transparent;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.section-title {
  font-weight: bold;
  font-size: 1.1rem;
}

.block {
  margin-bottom: 18px;
  padding-bottom: 14px;
  border-bottom: 1px dashed rgba(255, 255, 255, 0.15);
}
.block:last-child { border-bottom: none; padding-bottom: 0; }

.block-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
  font-weight: 600;
  gap: 12px;
}
.block-title :deep(.el-radio-group) { flex-shrink: 0; }

.tile-bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
  min-height: 70px;
  padding: 8px;
  background: rgba(0, 0, 0, 0.18);
  border-radius: 8px;
}
.empty-hint {
  margin: 0 auto;
}
.empty-hint :deep(.el-empty__description) { color: rgba(255,255,255,0.5); }

.fulu-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.fulu-item {
  background: rgba(0, 0, 0, 0.18);
  border-radius: 8px;
  padding: 10px;
}
.fulu-controls {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 8px;
}
.fulu-tile-label {
  font-size: 0.9rem;
  opacity: 0.85;
}
.fulu-tip {
  font-size: 0.8rem;
  opacity: 0.7;
  margin-bottom: 8px;
}

.ways-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
}
.ways-row :deep(.el-checkbox__label) { color: white; }

.action-row {
  margin-top: 18px;
  display: flex;
  gap: 12px;
}

.hint {
  opacity: 0.6;
  font-size: 0.85rem;
}

/* 结果区 */
.result-card {
  position: sticky;
  top: 16px;
}
.empty-state {
  text-align: center;
  padding: 40px 0;
  opacity: 0.7;
}
.score-banner {
  text-align: center;
  padding: 24px 12px;
  border-radius: 12px;
  margin-bottom: 18px;
}
.score-banner.success {
  background: linear-gradient(180deg, #4caf50, #1e8e3e);
}
.score-banner.fail {
  background: linear-gradient(180deg, #b94a48, #802522);
}
.score-num {
  font-size: 3.2rem;
  font-weight: bold;
  line-height: 1;
}
.score-text {
  margin-top: 6px;
  font-size: 1.1rem;
}
.fan-list h4 { margin-bottom: 8px; }
.fan-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.decomp-summary {
  margin-bottom: 12px;
  opacity: 0.85;
}
.decomp-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.decomp-item {
  background: rgba(0, 0, 0, 0.18);
  border-radius: 10px;
  padding: 12px;
}
.decomp-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}
.decomp-rank {
  font-weight: bold;
  opacity: 0.7;
}
.decomp-score {
  font-size: 1.3rem;
  font-weight: bold;
  color: #ffd04b;
}
.decomp-tiles {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 10px;
}
.decomp-group {
  background: rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  padding: 6px 8px;
}
.decomp-group-label {
  font-size: 0.75rem;
  opacity: 0.7;
  margin-bottom: 4px;
}
.decomp-group-tiles {
  display: flex;
  gap: 2px;
}
.decomp-fans {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

@media (max-width: 768px) {
  .page-header h1 { font-size: 1.8rem; }
  .result-card { position: static; }
}
</style>
