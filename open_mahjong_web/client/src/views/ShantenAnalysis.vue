<!-- 国标麻将听牌待牌判断页面 -->
<template>
  <div class="shanten-analysis">
    <div class="page-header">
      <h1>听牌待牌判断</h1>
      <p>选择 13 张手牌（或 13 - 副露张数*3 张），即可分析当前是否听牌以及所有可和的待牌。</p>
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

          <div class="block">
            <div class="block-title">
              <span>手牌（共 {{ form.hand.length }}/{{ expectedCount }} 张）</span>
              <el-tag size="small" :type="handCountTagType">{{ handCountText }}</el-tag>
            </div>
            <div class="tile-bar">
              <TileChip
                v-for="(id, idx) in sortedHand"
                :key="`hand-${idx}-${id}`"
                :tile-id="id"
                size="md"
                @click="removeHandTile(idx)"
              />
              <span v-if="form.hand.length === 0" class="hint">点击下方牌面添加手牌</span>
            </div>
          </div>

          <div class="block">
            <div class="block-title">
              <span>副露 / 暗刻 / 暗杠（最多 4 组）</span>
              <el-button type="primary" plain size="small" :disabled="form.fulus.length >= 4" @click="addFulu">
                添加副露
              </el-button>
            </div>
            <div v-if="form.fulus.length === 0" class="hint">门清听牌可不添加副露</div>
            <div v-else class="fulu-list">
              <div v-for="(fulu, idx) in form.fulus" :key="idx" class="fulu-item">
                <div class="fulu-controls">
                  <el-select v-model="fulu.kind" size="small" style="width: 110px;" @change="onFuluKindChange(idx)">
                    <el-option label="明刻 (碰)" value="k" />
                    <el-option label="暗刻" value="K" />
                    <el-option label="明杠" value="g" />
                    <el-option label="暗杠" value="G" />
                    <el-option label="明顺 (吃)" value="s" />
                    <el-option label="暗顺" value="S" />
                  </el-select>
                  <span class="fulu-tile-label">中心牌：</span>
                  <span v-if="fulu.tileId" class="fulu-tile-display">
                    <TileChip :tile-id="fulu.tileId" size="sm" highlighted />
                  </span>
                  <span v-else class="hint" style="color:#f56c6c;">未选</span>
                  <el-button type="danger" link @click="removeFulu(idx)">删除</el-button>
                </div>
                <TilePalette :size="'sm'" @pick="setFuluTile(idx, $event)" />
              </div>
            </div>
          </div>

          <div class="block">
            <div class="block-title"><span>牌面 — 点击添加到手牌</span></div>
            <TilePalette :size="'md'" @pick="addHandTile" />
          </div>

          <div class="action-row">
            <el-button type="primary" :loading="loading" :disabled="!canSubmit" @click="analyze">
              分析听牌
            </el-button>
          </div>
        </el-card>
      </el-col>

      <el-col :lg="10" :md="24">
        <el-card class="section-card result-card">
          <template #header>
            <span class="section-title">分析结果</span>
          </template>

          <div v-if="loading" class="empty-state">
            <el-icon class="is-loading" :size="36"><Loading /></el-icon>
            <p>正在分析...</p>
          </div>

          <div v-else-if="!result" class="empty-state">
            <p>请输入手牌后点击「分析听牌」。</p>
          </div>

          <template v-else>
            <div :class="['ting-banner', result.is_tingpai ? 'success' : 'fail']">
              <div class="ting-text">
                {{ result.is_tingpai ? '听牌！' : '未听牌' }}
              </div>
              <div v-if="result.is_tingpai" class="ting-sub">
                共 {{ result.waiting_tiles.length }} 种待牌
              </div>
            </div>

            <div v-if="result.is_tingpai" class="waiting-section">
              <h4>待牌</h4>
              <div class="waiting-tiles">
                <TileChip
                  v-for="id in result.waiting_tiles"
                  :key="id"
                  :tile-id="id"
                  size="lg"
                  highlighted
                />
              </div>
            </div>
          </template>
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
import { sortTiles, TILE_NAME } from '@/composables/useMahjongTiles'

const form = reactive({
  hand: [],
  fulus: []
})

const loading = ref(false)
const result = ref(null)

const sortedHand = computed(() => sortTiles(form.hand))
const expectedCount = computed(() => 13 - form.fulus.length * 3)
const handCountText = computed(() => `${form.hand.length}/${expectedCount.value}`)
const handCountTagType = computed(() => {
  if (form.hand.length === expectedCount.value) return 'success'
  if (form.hand.length > expectedCount.value) return 'danger'
  return 'warning'
})

const canSubmit = computed(() => {
  return form.hand.length === expectedCount.value && form.fulus.every(f => f.tileId)
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
}

const removeHandTile = (idxInSorted) => {
  const id = sortedHand.value[idxInSorted]
  const originalIdx = form.hand.indexOf(id)
  if (originalIdx !== -1) {
    form.hand.splice(originalIdx, 1)
  }
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
  result.value = null
}

const analyze = async () => {
  if (!canSubmit.value) {
    ElMessage.warning('请先完整填写手牌和副露')
    return
  }
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
.shanten-analysis {
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
.fulu-tile-label { opacity: 0.85; font-size: 0.9rem; }

.hint {
  opacity: 0.6;
  font-size: 0.85rem;
}

.action-row {
  margin-top: 18px;
}

.empty-state {
  text-align: center;
  padding: 40px 0;
  opacity: 0.7;
}

.ting-banner {
  text-align: center;
  padding: 24px 12px;
  border-radius: 12px;
  margin-bottom: 22px;
}
.ting-banner.success {
  background: linear-gradient(180deg, #4caf50, #1e8e3e);
}
.ting-banner.fail {
  background: linear-gradient(180deg, #b94a48, #802522);
}
.ting-text {
  font-size: 2rem;
  font-weight: bold;
}
.ting-sub {
  margin-top: 6px;
  opacity: 0.9;
}

.waiting-section h4 { margin-bottom: 12px; }
.waiting-tiles {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px;
  background: rgba(0, 0, 0, 0.18);
  border-radius: 10px;
}

.result-card { position: sticky; top: 16px; }

@media (max-width: 768px) {
  .page-header h1 { font-size: 1.8rem; }
  .result-card { position: static; }
}
</style>
