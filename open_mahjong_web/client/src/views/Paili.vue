<!-- 牌理：13 张直接分析听牌/进张，14 张分析切牌；空提交随机 14 张 -->
<template>
  <div class="paili">
    <div class="page-header">
      <PailiSwitcher />
      <p class="subtitle">13 张显示听牌与进张，14 张显示和牌拆解或切牌分析（副露按等价张数计算）。输入为空时将随机生成示例。</p>
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

      <div class="row option-row">
        <span class="row-label">特殊牌型</span>
        <div class="special-options">
          <el-switch
            v-model="options.mcrSevenPairs"
            size="small"
            active-text="国标七对"
            title="允许出现相同对子"
            :disabled="loading"
            @change="onMcrSevenPairsChange"
          />
          <el-switch
            v-model="options.riichiSevenPairs"
            size="small"
            active-text="日麻七对"
            title="不许出现相同对子"
            :disabled="loading"
            @change="onRiichiSevenPairsChange"
          />
          <el-switch
            v-model="options.thirteenOrphans"
            size="small"
            active-text="十三幺"
            :disabled="loading"
          />
          <el-switch
            v-model="options.unrelatedTiles"
            size="small"
            active-text="全不靠"
            :disabled="loading"
          />
          <el-switch
            v-model="options.combinationDragon"
            size="small"
            active-text="组合龙"
            :disabled="loading"
          />
        </div>
      </div>

      <div class="row block">
        <div class="row-line">
          <span class="row-label">手牌 {{ draft.hand.length }}/{{ expectedCountLabel }}</span>
          <el-tag size="small" :type="handCountTagType" effect="plain">{{ handCountText }}</el-tag>
        </div>
        <div
          class="hand-bar"
          :class="{ active: activeFuluIdx < 0 }"
          role="group"
          aria-label="手牌"
          tabindex="0"
          @click="activateHand"
          @keydown.enter.self.prevent="activateHand"
          @keydown.space.self.prevent="activateHand"
        >
          <TileChip
            v-for="(id, idx) in draft.hand"
            :key="'h-' + idx"
            :tile-id="id"
            size="sm"
            @click="onHandChipClick(idx)"
          />
          <TileInputPlaceholder
            v-if="activeFuluIdx < 0 && draft.hand.length < expectedTotalCount"
            label="手牌输入位置"
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

      <div class="result-embed" aria-live="polite" :aria-busy="loading">
        <div v-if="loading" class="empty">
          <el-icon class="is-loading" :size="18"><Loading /></el-icon>
          <span>正在计算...</span>
        </div>
        <div v-else-if="!result" class="empty"><span class="input-target-bar">{{ inputTargetLabel }}</span></div>
        <GuobiaoScoreResult v-else-if="best" :best="best" :conditions="conditionText" />
        <PailiResult v-else-if="pailiResult" :result="pailiResult" @discard="applyDiscard" @draw="applyDraw" />
      </div>

      <div class="row block">
        <TilePalette :size="'sm'" @pick="onPalettePick" />
      </div>

      <div class="actions">
        <el-button type="primary" size="default" :loading="loading" @click="analyze">
          计算牌理
        </el-button>
        <el-button size="default" @click="transfer('/calc/chinese')">国标计算器</el-button>
      </div>
    </section>
    <GuobiaoDecompositions v-if="showDecompositions && result?.decompositions?.length" :decompositions="result.decompositions" />
  </div>
</template>
<script setup>
import PailiSwitcher from '@/components/PailiSwitcher.vue'
import { computed, watch } from 'vue'
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
const { draft, textInput, options, result, loading, best, pailiResult, showDecompositions, conditionText,
  expectedHandCount, expectedTotalCount, resetAll, loadDemo, applyDraw, applyDiscard } = session
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
  if (route.path !== '/paili') return
  if (typeof id === 'string') {
    if (await session.loadExample(id)) {
      const query = { ...route.query }; delete query.example
      await router.replace({ path: route.path, query })
    }
  }
  else if (!result.value && !loading.value && [13, 14].includes(draft.value.hand.length + 3 * draft.value.melds.length)) await session.calculate({ decompose: true })
  if (best.value) showDecompositions.value = true
}, { immediate: true })

const expectedCountLabel = computed(() => `${expectedHandCount.value} 或 ${expectedTotalCount.value}`)
const handCountText = computed(() => `${draft.value.hand.length}/${expectedCountLabel.value}`)
const handCountTagType = computed(() => [expectedHandCount.value, expectedTotalCount.value].includes(draft.value.hand.length) ? 'success' : draft.value.hand.length > expectedTotalCount.value ? 'danger' : 'warning')
const onPalettePick = id => session.pickTile(id)
const onHandChipClick = index => session.removeTile(index)
const analyze = () => session.calculate({ random: true, decompose: true })
const onMcrSevenPairsChange = enabled => session.setSevenPairs('mcrSevenPairs', enabled)
const onRiichiSevenPairsChange = enabled => session.setSevenPairs('riichiSevenPairs', enabled)
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
.option-row { flex-wrap: wrap; }
.special-options {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px 14px;
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
