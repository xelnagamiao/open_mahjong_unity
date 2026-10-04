<!-- 国标计算器：单列紧凑，结果嵌在输入与按钮之间 -->
<template>
  <div class="chinese">
    <div class="page-header">
      <CalculatorSwitcher />
      <p class="subtitle">13 张显示听牌与进张；14 张计算番种、得分及全部拆解，未和牌时显示切牌分析。</p>
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
          role="group"
          aria-label="手牌"
          tabindex="0"
          @click="activateHand"
          @keydown.enter.self.prevent="activateHand"
          @keydown.space.self.prevent="activateHand"
        >
          <div class="hand-bar">
            <TileChip
              v-for="(id, idx) in form.hand"
              :key="'h-' + idx"
              :tile-id="id"
              size="sm"
              @click="onHandChipClick(idx)"
            />
            <TileInputPlaceholder
              v-if="activeFuluIdx < 0 && form.hand.length < expectedHandCount"
              label="手牌输入位置"
            />
          </div>
          <div class="get-tile-box" :class="{ filled: form.getTile }" @click.stop="activateHand">
            <span class="get-tile-label">和牌</span>
            <TileChip
              v-if="form.getTile"
              :tile-id="form.getTile"
              size="sm"
              highlighted
              :selected="activeFuluIdx < 0 && form.hand.length === expectedHandCount"
              @click="onGetTileClick"
            />
            <TileInputPlaceholder
              v-else-if="activeFuluIdx < 0 && form.hand.length === expectedHandCount"
              label="和牌输入位置"
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

      <div class="result-embed" aria-live="polite" :aria-busy="loading">
        <div v-if="loading" class="empty">
          <el-icon class="is-loading" :size="18"><Loading /></el-icon>
          <span>正在计算...</span>
        </div>
        <div v-else-if="!result" class="empty"><span class="input-target-bar">{{ inputTargetLabel }}</span></div>
        <GuobiaoScoreResult v-else-if="best" :best="best" />
        <PailiResult v-else-if="pailiResult" :result="pailiResult" @discard="applyDiscard" @draw="applyDraw" />
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
        <el-button size="default" @click="transfer('/paili')">国标牌理</el-button>
      </div>
    </section>

    <GuobiaoDecompositions v-if="showDecompositions && result?.decompositions?.length" :decompositions="result.decompositions" />
  </div>
</template>
<script setup>
import CalculatorSwitcher from '@/components/CalculatorSwitcher.vue'
import { computed, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Loading } from '@element-plus/icons-vue'
import TileChip from '@/components/TileChip.vue'
import TileInputPlaceholder from '@/components/TileInputPlaceholder.vue'
import TilePalette from '@/components/TilePalette.vue'
import FuluSlots from '@/components/FuluSlots.vue'
import TileFaceStyleSwitch from '@/components/TileFaceStyleSwitch.vue'
import MahjongNotationHelp from '@/components/MahjongNotationHelp.vue'
import PailiResult from '@/components/PailiResult.vue'
import GuobiaoScoreResult from '@/components/GuobiaoScoreResult.vue'
import GuobiaoDecompositions from '@/components/GuobiaoDecompositions.vue'
import { useGuobiaoCalculator } from '@/composables/useGuobiaoCalculator'

const route = useRoute()
const router = useRouter()
const session = useGuobiaoCalculator()
const { draft, form, textInput, result, loading, best, pailiResult, showDecompositions,
  expectedHandCount, expectedTotalCount, resetAll, clearGetTile, applyDraw, applyDiscard } = session
const fulu = session.fulu
const fuluSlots = fulu.slots
const activeFuluIdx = fulu.activeIdx
const lockedFuluCount = fulu.lockedCount
const activateFulu = fulu.activate
const activateHand = fulu.activateHand
const clearFuluSlot = fulu.clearSlot
const onFuluSlotInput = fulu.onSlotInput
const lockFuluSlot = fulu.lockSlot
const removeFuluDraft = fulu.removeDraftTile
const removeFuluLocked = fulu.removeLockedTile
const inputTargetLabel = computed(() => activeFuluIdx.value >= 0 ? `输入副露 #${activeFuluIdx.value + 1}` : '输入手牌')
async function transfer(path) {
  if (session.prepareTransfer()) await router.push(path)
}
watch(() => route.query.example, async id => {
  if (route.path !== '/calc/chinese') return
  if (typeof id === 'string') {
    if (await session.loadExample(id)) {
      const query = { ...route.query }; delete query.example
      await router.replace({ path: route.path, query })
    }
  }
  else if (!result.value && !loading.value && [13, 14].includes(draft.value.hand.length + 3 * draft.value.melds.length)) await session.calculate({ decompose: false })
}, { immediate: true })

const filledTileCount = computed(() => draft.value.hand.length)
const handCountText = computed(() => `${filledTileCount.value}/${expectedTotalCount.value}`)
const handCountTagType = computed(() => [expectedHandCount.value, expectedTotalCount.value].includes(filledTileCount.value) ? 'success' : filledTileCount.value > expectedTotalCount.value ? 'danger' : 'warning')
const onPalettePick = id => session.pickTile(id, true)
const onHandChipClick = index => session.removeTile(index, true)
const onGetTileClick = () => {
  activateHand()
  clearGetTile()
}
const calculateScore = () => session.calculate()
async function calculateDecompose() {
  await session.calculate({ decompose: true })
  if (showDecompositions.value) { await nextTick(); document.getElementById('gb-decompose-section')?.scrollIntoView({ behavior: 'smooth', block: 'start' }) }
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

.decompose-section {
  margin-top: 16px;
  background: rgba(255, 255, 255, 0.96);
  border: 1px solid var(--omu-border, #ebeef5);
  border-radius: 12px;
  padding: 12px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
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

.actions { flex-wrap: wrap; }
</style>
