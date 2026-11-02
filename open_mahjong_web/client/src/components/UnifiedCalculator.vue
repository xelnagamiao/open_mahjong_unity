<template>
  <div class="unified-shell" :class="{ standalone }">
    <nav v-if="standalone" class="site-nav" aria-label="网站导航">
      <a href="/">首页</a><a href="/events">比赛</a><a href="/game-unity">进入平台</a><a href="/2d">2D版</a>
      <a href="/player-data">历史记录</a><a href="/player-data/platform">数据统计</a><a href="/rulebook">规则书</a>
      <span class="current">麻雀计算器</span><a href="/login" class="login">登录</a>
    </nav>
    <div class="workspace">
      <MahjongNotationHelp meld-codes />
      <header class="tool-heading"><h1 class="sr-only">麻雀计算器</h1>
        <div class="rule-tabs" role="tablist" aria-label="麻将规则">
          <button v-for="(label,key) in RULE_LABELS" :id="`rule-${key}`" :key="key" role="tab" aria-controls="hand-workspace" :tabindex="s.rule === key ? 0 : -1" :aria-selected="s.rule === key" :class="{ active: s.rule === key }" @click="changeRule(key)" @keydown="changeRuleByKey($event,key)">{{ label }}计算器</button>
          <button type="button" class="hongque-link" @click="openHongque">虹雀² 计算器</button>
        </div>
      </header>
      <section id="hand-workspace" class="main-card" role="tabpanel" :aria-labelledby="`rule-${s.rule}`">
        <header class="card-header"><strong>输入</strong><div class="toolbar">
          <TileFaceStyleSwitch />
          <el-button size="small" link :disabled="!canUndo" @click="lab.undo" title="撤销上一步">撤销</el-button>
          <el-button size="small" link :disabled="!canRedo" @click="lab.redo" title="重做上一步">重做</el-button>
          <el-select v-model="exampleKey" placeholder="示例" aria-label="选择示例" size="small" class="sample-select" @change="loadExample">
            <el-option v-for="item in examples" :key="item.key" :label="item.label" :value="item.key" />
          </el-select>
          <el-button size="small" link @click="shareHand">分享</el-button><el-button size="small" link @click="lab.clear">清空</el-button>
        </div></header>
        <form class="notation-row" @submit.prevent="lab.parse">
          <label for="unified-notation">牌面简写</label><el-input id="unified-notation" v-model="s.textInput" placeholder="567m45s22z(s13)(K23) + 6s" size="small" clearable />
          <el-button native-type="submit" type="primary" size="small" :loading="s.loading">解析</el-button>
        </form>
        <HandShapeOptions :options="s.options" :rule="s.rule" :loading="s.loading" @change="lab.changeOption" />
        <div v-if="error" class="inline-error" role="alert">{{ error }}</div>
        <div v-if="notice" class="inline-notice" role="status">{{ notice }}</div>
        <div class="hand-section">
          <div class="section-line"><span>手牌 {{ s.draft.hand.length }} / {{ s.expectedTotalCount }} <span v-if="s.draft.melds.length" class="muted"> · 副露 {{ s.draft.melds.length }} 组</span></span>
            <el-button v-if="count === 14" size="small" link :type="choosingWin ? 'primary' : 'default'" @click="choosingWin = !choosingWin">{{ choosingWin ? '取消指定' : '指定和牌张' }}</el-button>
          </div>
          <div class="hand-row" :class="{ choosing: choosingWin, active: s.fulu.activeIdx < 0 }" tabindex="0" @click="s.fulu.activateHand()" @keydown.enter.self.prevent="s.fulu.activateHand()" @keydown.space.self.prevent="s.fulu.activateHand()">
            <div class="hand-tiles" role="group" aria-label="手牌">
              <button v-for="(tile,index) in s.form.hand" :key="index" class="tile-button" :aria-label="`${choosingWin ? '指定和牌' : '移除'}${TILE_NAME[tile]}`" @click="choosingWin ? lab.chooseWin(tile) : lab.remove(index)"><TileChip :tile-id="tile" size="sm" /></button>
              <span v-if="!s.form.hand.length" class="hand-placeholder">选择下方牌面或粘贴简写</span>
            </div>
            <div class="last-tile"><span>{{ s.best ? '和牌张' : '摸入牌' }}</span>
              <button v-if="s.form.getTile" class="tile-button" aria-label="移除末张" @click="lab.clearWin"><TileChip :tile-id="s.form.getTile" size="sm" highlighted /></button>
              <span v-else class="empty-tile">＋</span>
            </div>
          </div>
          <div v-if="choosingWin" class="selection-tip">点选手牌指定和牌张</div>
        </div>
        <section class="fulu-section" aria-label="副露">
          <div class="section-line"><strong>副露（{{ s.draft.melds.length }}/4）</strong><span v-if="s.fulu.activeIdx >= 0" class="input-target">输入副露 #{{ s.fulu.activeIdx + 1 }}</span></div>
          <div class="fulu-wrap"><FuluSlots :slots="s.fulu.slots" :active-idx="s.fulu.activeIdx"
            @activate="s.fulu.activate" @clear="idx => lab.meldAction('clearSlot',idx)" @input="idx => lab.meldAction('onSlotInput',idx)"
            @lock="(idx,opt) => lab.meldAction('lockSlot',idx,opt)" @remove-draft="(idx,tile) => lab.meldAction('removeDraftTile',idx,tile)" @remove-locked="(idx,tile) => lab.meldAction('removeLockedTile',idx,tile)" /></div>
        </section>
        <section class="conditions-section" aria-label="和牌条件">
          <HandWinConditions :form="s.form" :rule="s.rule" :closed="s.closed" />
        </section>
        <details class="settings"><summary>已见牌与牌河 <span class="summary-value">已见 {{ s.draft.visible.length }} 张 · 推演舍牌 {{ s.draft.river.length }} 张</span></summary>
          <form class="notation-row" @submit.prevent="lab.applyVisible"><label for="visible-tiles">其他已见牌</label><el-input id="visible-tiles" v-model="visibleInput" size="small" placeholder="如 11p4p" /><el-button size="small" native-type="submit">应用</el-button></form>
          <div v-if="s.draft.river.length" class="river"><TileMiniGlyph v-for="(tile,index) in s.draft.river" :key="index" :tile-id="tile" /><el-button size="small" link @click="lab.clearRiver">清空牌河</el-button></div>
        </details>
      <section class="result-card result-embed" aria-label="分析结果" :aria-busy="s.loading" aria-live="polite">
        <header class="card-header"><strong>{{ resultTitle }}</strong><span class="muted" v-if="count >= 13">{{ count }} 张等效手牌</span></header>
        <div v-if="s.loading" class="empty-result">正在计算…</div>
        <div v-else-if="error" class="empty-result">请修正输入后继续</div>
        <div v-else-if="!s.result" class="empty-result">{{ lab.pendingMeld.value ? '请完成副露输入' : count < 13 ? `再输入 ${13 - count} 张` : '按“解析”应用输入' }}</div>
        <template v-else-if="s.best">
          <GuobiaoScoreResult :best="s.best" />
          <details class="decompositions"><summary>全部拆解 <span>{{ s.result.decompositions.length }} 种</span></summary><GuobiaoDecompositions :decompositions="s.result.decompositions" /></details>
          <details class="continue-analysis" :open="!s.best.canWin"><summary>继续推演 · 切牌分析</summary><PailiResult :result="s.result.paili" @discard="lab.discard" @draw="lab.draw" /></details>
          <div class="result-actions"><el-button size="small" @click="lab.clearWin">退回听牌</el-button><el-button size="small" :disabled="!canUndo" @click="lab.undo">撤销上一步</el-button></div>
        </template>
        <template v-else-if="s.result.state === 'ready'">
          <div class="ready-summary"><strong>听牌</strong><span>{{ s.result.waits.length }} 种 · 进张 {{ s.result.paili.total_accept }} 张</span></div>
          <p v-if="!s.result.waits.length" class="empty-result">所有待牌均已见完</p>
          <div class="wait-table" role="table" aria-label="待牌得分比较">
            <div class="wait-row wait-head" role="row"><span role="columnheader">待牌</span><span role="columnheader">剩余</span><span role="columnheader">{{ s.rule === 'riichi' ? '荣和' : '点和' }}</span><span role="columnheader">自摸</span></div>
            <div v-for="wait in s.result.waits" :key="wait.tile" class="wait-row" role="row">
              <button class="tile-button" :aria-label="`摸${TILE_NAME[wait.tile]}`" @click="lab.draw(wait.tile)"><TileChip :tile-id="wait.tile" size="sm" /></button>
              <span>{{ wait.remaining }} 张</span>
              <button class="score-action" :class="{ invalid: !wait.ron?.canWin }" :aria-label="`${TILE_NAME[wait.tile]}点和试算`" @click="lab.draw(wait.tile,null,'ron')">{{ scoreText(wait.ron) }}</button>
              <button class="score-action" :class="{ invalid: !wait.tsumo?.canWin }" :aria-label="`${TILE_NAME[wait.tile]}自摸试算`" @click="lab.draw(wait.tile,null,'tsumo')">{{ scoreText(wait.tsumo) }}</button>
            </div>
          </div>
        </template>
        <PailiResult v-else-if="s.pailiResult" :result="s.pailiResult" @discard="lab.discard" @draw="lab.draw" />
      </section>
        <div class="palette-block" aria-label="牌面输入">
          <div v-for="group in paletteGroups" :key="group.label" class="palette-row"><span>{{ group.label }}</span><div>
            <button v-for="tile in group.tiles" :key="tile" class="tile-button" :aria-label="`添加${TILE_NAME[tile]}`" :disabled="paletteDisabled(tile)" @click="lab.pick(tile)"><TileChip :tile-id="tile" size="sm" :disabled="paletteDisabled(tile)" /></button>
          </div></div>
        </div>
        <div class="actions"><el-button type="primary" :loading="s.loading" @click="lab.parse">计算</el-button><el-button :disabled="!s.best" @click="openDecompositions">查看全部拆解</el-button></div>
      </section>
      <details class="history-card"><summary>推演记录 <span>{{ cursor + 1 }} / {{ history.length }}</span></summary>
        <ol><li v-for="(item,index) in history" :key="index"><button :aria-current="index === cursor ? 'step' : undefined" :class="{ active: index === cursor }" @click="lab.restore(index)">{{ index + 1 }}. {{ item.label }} <span>{{ item.state.text || '空手牌' }}</span></button></li></ol>
      </details>
    </div>
    <footer v-if="standalone" class="site-footer"><strong>OpenMahjong</strong><a href="/">一个简易的麻将服务网站</a></footer>
    <el-dialog v-model="shareOpen" title="分享手牌" width="min(540px, 92vw)"><el-input v-model="shareUrl" readonly type="textarea" :rows="4" aria-label="手牌分享链接" /><template #footer><el-button @click="shareOpen = false">关闭</el-button></template></el-dialog>
  </div>
</template>

<script setup>
import { computed, ref, watch, nextTick } from 'vue'
import TileChip from './TileChip.vue'
import TileMiniGlyph from './TileMiniGlyph.vue'
import TileFaceStyleSwitch from './TileFaceStyleSwitch.vue'
import FuluSlots from './FuluSlots.vue'
import HandShapeOptions from './HandShapeOptions.vue'
import HandWinConditions from './HandWinConditions.vue'
import PailiResult from './PailiResult.vue'
import GuobiaoScoreResult from './GuobiaoScoreResult.vue'
import GuobiaoDecompositions from './GuobiaoDecompositions.vue'
import MahjongNotationHelp from './MahjongNotationHelp.vue'
import { RULE_LABELS } from '../utils/calculatorRules.ts'
import { effectiveCount, expandMeld } from '../utils/handAnalysis.ts'
import { preciseFan } from '../utils/calculatorScoreText.js'
import { TILE_NAME } from '../composables/useMahjongTiles.js'
import { tryParseMeldTiles } from '../composables/useFuluSlots.js'
import { useUnifiedCalculator } from '../composables/useUnifiedCalculator.js'

const props = defineProps({ standalone: Boolean, rule: { type: String, default: null } })
const emit = defineEmits(['rule-change', 'hongque'])
const lab = useUnifiedCalculator(props.standalone ? { storageKey: 'salasasa.unified-calculator.prototype.v1', fallbackStorageKeys: [], initialRule: props.rule } : { initialRule: props.rule })
defineExpose({ lab })
async function changeRule(rule) { return lab.changeRule(rule) }
function openHongque() {
  if (props.standalone) location.href = '/calc/hongque'
  else emit('hongque')
}
async function openDecompositions() {
  await nextTick()
  const details = document.querySelector('details.decompositions')
  if (details) { details.open = true; details.scrollIntoView({ behavior: 'smooth', block: 'start' }) }
}
const { session: s, error, notice, history, cursor, choosingWin, visibleInput, canUndo, canRedo } = lab
watch(() => s.value.rule, rule => emit('rule-change', rule), { immediate: true })
watch(() => props.rule, async rule => {
  if (rule && rule !== s.value.rule && !await changeRule(rule)) emit('rule-change', s.value.rule)
})
const count = computed(() => effectiveCount(s.value.draft))
watch(count, value => { if (value !== 14) choosingWin.value = false })
const paletteGroups = [{label:'万',tiles:[11,12,13,14,15,16,17,18,19]},{label:'饼',tiles:[21,22,23,24,25,26,27,28,29]},{label:'条',tiles:[31,32,33,34,35,36,37,38,39]},{label:'风箭',tiles:[41,42,43,44,45,46,47]}]
const knownCounts = computed(() => {
  const d = s.value.draft, tiles = [...d.hand,...d.melds.flatMap(expandMeld),...d.visible,...d.river]
  for (const slot of s.value.fulu.slots) if (!slot.locked) tiles.push(...(tryParseMeldTiles(slot.input) || []))
  return tiles.reduce((counts,tile) => {counts[tile]=(counts[tile]||0)+1;return counts}, {})
})
function paletteDisabled(tile) {
  if (knownCounts.value[tile] >= 4) return true
  const active = s.value.fulu.activeIdx
  return active >= 0 ? (tryParseMeldTiles(s.value.fulu.slots[active].input) || []).length >= 4 : count.value >= 14
}
const resultTitle = computed(() => s.value.best ? (s.value.best.canWin ? '和牌得分' : '和牌形检查') : s.value.result?.state === 'ready' ? '待牌与得分' : count.value === 14 ? '切牌分析' : '向听与进张')
async function changeRuleByKey(event, key) {
  const keys = Object.keys(RULE_LABELS), index = keys.indexOf(key)
  const next = event.key === 'ArrowRight' ? (index + 1) % keys.length : event.key === 'ArrowLeft' ? (index + keys.length - 1) % keys.length : event.key === 'Home' ? 0 : event.key === 'End' ? keys.length - 1 : -1
  if (next < 0) return
  event.preventDefault()
  document.getElementById(`rule-${keys[next]}`).focus()
  await changeRule(keys[next])
}
const exampleKey = ref(''), shareOpen = ref(false), shareUrl = ref('')
const examples = [{key:'improve',label:'一向听'},{key:'ready',label:'两面听牌'},{key:'discard',label:'14 张切牌'},{key:'win',label:'和牌'},{key:'orphans',label:'十三幺'},{key:'pairs',label:'多种拆解'},{key:'noYaku',label:'日麻无役'},{key:'melds',label:'副露简写'}]
async function loadExample(value) { await lab.example(value); exampleKey.value = '' }
async function shareHand() { shareUrl.value = await lab.share(); if (shareUrl.value) shareOpen.value = true }
function scoreText(score) {
  if (!score) return '—'
  if (!score.canWin) return score.rule === 'riichi' ? '无役' : `${preciseFan(score.fan)} 番 · 不足起和`
  return score.rule === 'riichi' ? `${score.points} 点` : score.rule === 'qingque' ? `${preciseFan(score.fan)} 番 · ${score.points} 底分` : `${score.fan} 番`
}
</script>

<style scoped>
.unified-shell { color: #34445b; font-size: 13px; }.unified-shell.standalone { min-height: 100vh; background: #45b7d1; }
.sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0,0,0,0); white-space: nowrap; border: 0; }
.site-nav { display: flex; align-items: center; flex-wrap: wrap; gap: 24px; padding: 18px 32px; background: #191919; color: #eee; }
.site-nav a { text-decoration: none; color: #ddd; }.site-nav .current { color: #83d0ff; }.site-nav .login { margin-left: auto; }
.workspace { max-width: 880px; margin: 0 auto; padding: 12px 0 20px; box-sizing: border-box; }.standalone .workspace { width: calc(100% - 24px); padding: 24px 0 42px; }
.tool-heading { text-align: center; margin: 0 auto 16px; color: #fff; }
h1 { font-size: 20px; font-weight: 600; }
.rule-tabs { display: inline-flex; flex-wrap: wrap; justify-content: center; max-width: 100%; gap: 4px; background: #ffffff35; border: 1px solid #ffffff80; padding: 4px; border-radius: 8px; box-shadow: 0 2px 8px #0000001f; }
.rule-tabs button { padding: 7px 18px; font-size: 15px; font-family: inherit; letter-spacing: .5px; border: 0; border-radius: 5px; color: #fff; background: transparent; font-weight: 600; cursor: pointer; }
.rule-tabs .active { background: #fff; color: #2449b0; }
.main-card,.result-card,.history-card { background: #ffffffef; border: 1px solid #e4eaf1; border-radius: 12px; overflow: hidden; margin-bottom: 14px; }
.card-header { display: flex; justify-content: space-between; align-items: center; gap: 10px; padding: 10px 12px; background: #f5f7fa; border-bottom: 1px solid #e7ecf4; }
.card-header strong { font-size: 14px; white-space: nowrap; }.toolbar { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; justify-content: flex-end; }.toolbar :deep(.el-button+.el-button) { margin-left: 0; }.sample-select { width: 98px; }
.notation-row { display: flex; align-items: center; padding: 10px 12px; gap: 10px; }.notation-row label { white-space: nowrap; font-weight: 500; }.notation-row :deep(.el-input) { min-width: 0; }
.hand-section { padding: 0 12px 10px; }.section-line { display: flex; justify-content: space-between; align-items: center; min-height: 30px; }
.hand-row.active { border-style: solid; border-color: #409eff; }.hand-row { display: flex; gap: 10px; align-items: center; border: 1px dashed #d4deeb; background: #f5f7fa; border-radius: 6px; min-height: 73px; padding: 6px; }.hand-row.choosing { border-color: #e6a23c; background: #fff9ed; }
.hand-tiles { flex: 1; display: flex; align-items: center; flex-wrap: wrap; min-width: 0; gap: 2px; }.tile-button { display: inline-flex; align-items: center; justify-content: center; background: none; border: 0; border-radius: 3px; cursor: pointer; padding: 0; color: inherit; }.tile-button:focus-visible,.score-action:focus-visible { outline: 2px solid #2563eb; outline-offset: 2px; }
.last-tile { border-left: 1px dashed #b7d8f4; padding-left: 10px; min-width: 50px; display: flex; flex-direction: column; align-items: center; gap: 3px; color: #a56a00; font-size: 11px; }.empty-tile { display: inline-flex; align-items: center; justify-content: center; border: 1px dashed #bdd2e4; width: 26px; height: 36px; color: #a5bed2; }
.hand-placeholder { color: #8696a9; padding: 8px; }.selection-tip { margin-top: 6px; color: #9d6d22; font-size: 12px; }
.palette-block { padding: 4px 12px 10px; }.palette-row { display: flex; align-items: center; gap: 8px; margin: 4px 0; }.palette-row>span { width: 3em; font-size: 12px; flex-shrink: 0; }.palette-row>div { display: flex; gap: 3px; min-width: 0; overflow-x: auto; }.tile-button:disabled { cursor: default; opacity: .38; }
.settings { border-top: 1px dashed #e1e7f0; }.settings summary,.decompositions>summary,.history-card>summary { cursor: pointer; padding: 11px 12px; font-weight: 600; }.summary-value { margin-left: 12px; font-size: 12px; color: #76879a; font-weight: 400; }.settings :deep(.win-conditions),.settings :deep(.shape-options) { border-top: 0; }
.fulu-section { padding: 8px 12px; border-top: 1px dashed #e1e7f0; }.fulu-wrap { padding-top: 6px; }.input-target { color: #2563eb; }.conditions-section { border-top: 1px dashed #e1e7f0; }.river { display: flex; gap: 3px; flex-wrap: wrap; align-items: center; padding: 0 12px 12px; }
.continue-analysis { border-top: 1px solid #e7edf4; }.continue-analysis>summary { cursor: pointer; padding: 11px 12px; font-weight: 600; }
.main-card { box-shadow: 0 4px 16px #00000014; }.main-card>.result-card { border: 0; border-top: 1px dashed #e1e7f0; border-radius: 0; margin: 0; background: #fafbfc; }.actions { padding: 10px 12px; text-align: right; border-top: 1px dashed #e1e7f0; }.palette-block { border-top: 1px dashed #e1e7f0; }.muted { font-size: 12px; color: #7a8a9d; }.inline-error { margin: 0 12px 10px; background: #fef0f0; color: #b32626; border-radius: 4px; padding: 8px 10px; }.inline-notice { padding: 0 12px 8px; color: #26814e; }
.empty-result { padding: 24px 12px; text-align: center; color: #8191a4; }.ready-summary { display: flex; align-items: center; gap: 12px; margin: 12px; padding: 11px 12px; border: 1px solid #6ee7b7; background: #ecfdf5; border-radius: 7px; color: #065f46; }.ready-summary strong { font-size: 20px; }
.wait-table { padding: 0 12px 12px; }.wait-row { display: grid; grid-template-columns: 50px 70px 1fr 1fr; gap: 8px; align-items: center; min-height: 49px; border-bottom: 1px solid #ecf0f6; text-align: center; }.wait-head { min-height: 30px; color: #718297; font-size: 12px; background: #f5f7fa; }.score-action { background: #f0f7ff; color: #286ac1; font: inherit; border: 1px solid #d8e9ff; padding: 6px; border-radius: 4px; cursor: pointer; }.score-action.invalid { color: #a86439; background: #fff8ed; border-color: #f2ddbf; }.score-action:hover { border-color: #409eff; }
.decompositions { border-top: 1px solid #e7edf4; }.decompositions span,.history-card>summary span { font-size: 12px; font-weight: 400; color: #8191a4; margin-left: 10px; }.decompositions :deep(.decompose-section) { margin: 0; border: 0; border-radius: 0; padding-top: 0; }.decompositions :deep(.decompose-head) { display: none; }.result-actions { padding: 10px 12px; text-align: right; border-top: 1px solid #edf1f7; }
.history-card ol { list-style: none; padding: 0 12px 12px; max-height: 240px; overflow: auto; }.history-card li button { border: 0; background: transparent; padding: 9px 6px; width: 100%; text-align: left; cursor: pointer; color: #53647b; }.history-card li button.active { color: #235fba; background: #edf5ff; border-radius: 4px; }.history-card li span { display: inline-block; margin-left: 12px; color: #8b98a8; font-family: Consolas, monospace; }
.site-footer { background: #2c3e50; padding: 34px; display: flex; flex-direction: column; align-items: center; gap: 10px; }.site-footer strong { color: #3498db; font-size: 24px; }.site-footer a { color: #dbe8f3; text-decoration: none; }
@media(max-width:600px) { .site-nav { padding: 12px; gap: 16px; }.site-nav a:nth-child(n+3):not(.login) { display: none; }.workspace { padding-top: 16px; }.tool-heading { flex-wrap: wrap; gap: 10px; } h1 { font-size: 18px; }.rule-tabs button { padding: 7px 16px; }.card-header { align-items: flex-start; }.toolbar { gap: 8px; }.toolbar :deep(.tile-face-switch) { display: none; }.sample-select { width: 88px; }.notation-row { gap: 6px; }.summary-value { margin-left: 8px; font-size: 11px; }.wait-row { grid-template-columns: 32px 40px 1fr 1fr; gap: 4px; }.score-action { font-size: 11px; padding: 6px 3px; }.hand-placeholder { font-size: 12px; }.history-card li span { display: block; margin-left: 0; }.palette-block :deep(.row-label) { width: 26px; }.palette-block :deep(.palette-tiles) { gap: 2px; } }
</style>
