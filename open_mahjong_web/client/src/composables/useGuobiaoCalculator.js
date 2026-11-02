import { ref, reactive, computed, watch, effectScope } from 'vue'
import { ElMessage } from 'element-plus'
import { useFuluSlots } from './useFuluSlots.js'
import { tilesToNotationText, randomHandTiles, parseMeldSlotInput } from './useMahjongTiles.js'
import {
  emptyDraft, cloneDraft, validateDraft, parseHandText, draftNotation, effectiveCount,
  expandMeld, exampleDraft, HAND_EXAMPLES, setWinMethod, discardTile, drawTile, analyzeHand,
} from '../utils/handAnalysis.ts'
import { CALCULATOR_RULES, ruleOptions, RULE_LABELS, calculatorPath, pailiPath } from '../utils/calculatorRules.ts'

const STORAGE_KEY = 'salasasa.guobiao-calculator.v1'
const defaultOptions = ruleOptions

/** Both existing pages use this state. The factory also allows deterministic worker/race tests. */
export function createGuobiaoCalculator({ storage = null, onError = message => ElMessage.error(message), workerFactory, restoreUnified = false } = {}) {
  const scope = effectScope(true)
  return scope.run(() => {
    const draft = ref(emptyDraft())
    const textInput = ref('')
    const options = reactive(defaultOptions())
    const rule = ref('guobiao')
    const result = ref(null)
    const loading = ref(false)
    const showDecompositions = ref(false)
    let revision = 0
    let worker = null
    let pending = null
    let restoring = false
    let restoreWarning = ''

    const fail = error => { onError(error.message || String(error)); return false }
    function invalidate() {
      revision++
      result.value = null
      loading.value = false
      showDecompositions.value = false
      // Termination cancels costly obsolete work as well as preventing stale rendering.
      worker?.terminate()
      worker = null
      pending?.(null)
      pending = null
    }
    const fulu = useFuluSlots({
      canLock(meld, index) {
        const codes = fulu.slots.map((slot, i) => i === index ? meld.code : slot.locked?.code).filter(Boolean)
        const candidate = cloneDraft(draft.value)
        candidate.melds = codes
        if (effectiveCount(candidate) !== 14) candidate.winTile = null
        try { validateDraft(candidate); return true } catch (error) { return fail(error) }
      },
    })
    function restoreMelds(codes) {
      restoring = true
      fulu.resetAll()
      codes.forEach((code, index) => {
        const displayTiles = expandMeld(code)
        const input = tilesToNotationText(displayTiles)
        const parsed = parseMeldSlotInput(input)
        const opt = [parsed.auto, ...parsed.options].find(item => item?.kind === code[0])
        fulu.slots[index].input = input
        fulu.slots[index].locked = { kind: code[0], tileId: Number(code.slice(1)), label: opt?.label || '暗顺', code, input, displayTiles }
      })
      restoring = false
    }
    function commit(next, { melds = false } = {}) {
      validateDraft(next)
      draft.value = cloneDraft(next)
      if (melds) restoreMelds(next.melds)
      textInput.value = draftNotation(next)
    }

    try {
      let saved = JSON.parse(storage?.getItem(STORAGE_KEY) || 'null')
      if (restoreUnified && !saved?.separatePages) {
        try {
          const merged = JSON.parse(storage?.getItem('salasasa.unified-calculator.v1') || 'null')
          if (merged && CALCULATOR_RULES.includes(merged.rule)) {
            validateDraft(merged.draft)
            saved = merged
          }
        } catch { /* Keep the previous separate-page session if the merged draft is unreadable. */ }
      }
      if (saved) {
        validateDraft(saved.draft)
        commit(saved.draft, { melds: true })
        if (typeof saved.text === 'string' && saved.text.length < 8000) {
          const legacyText = draftNotation({ ...saved.draft, melds: [] })
          textInput.value = saved.text === legacyText ? draftNotation(saved.draft) : saved.text
        }
        if (CALCULATOR_RULES.includes(saved.rule)) rule.value = saved.rule
        Object.assign(options, defaultOptions(rule.value))
        for (const key of Object.keys(options)) if (typeof saved.options?.[key] === 'boolean') options[key] = saved.options[key]
        if (options.riichiSevenPairs) options.mcrSevenPairs = false
      }
    } catch { restoreWarning = '上次的输入无法读取，请重新输入。' }

    function save() {
      if (restoring) return
      try { storage?.setItem(STORAGE_KEY, JSON.stringify({ draft: draft.value, text: textInput.value, options, rule: rule.value, separatePages: true })) } catch { /* Storage is optional; calculations remain available. */ }
    }
    watch(() => fulu.lockedList.value.map(m => m.code), codes => {
      if (restoring) return
      const next = cloneDraft(draft.value)
      next.melds = codes
      if (effectiveCount(next) !== 14) next.winTile = null
      next.river = []
      draft.value = next
      textInput.value = draftNotation(next)
    }, { flush: 'sync' })
    watch([draft, textInput, options, rule, fulu.slots], () => { invalidate(); save() }, { deep: true, flush: 'sync' })
    if (restoreUnified) save()

    const expectedHandCount = computed(() => 13 - draft.value.melds.length * 3)
    const expectedTotalCount = computed(() => expectedHandCount.value + 1)
    const concealedHand = computed(() => {
      const hand = [...draft.value.hand]
      if (draft.value.winTile !== null) hand.splice(hand.indexOf(draft.value.winTile), 1)
      return hand
    })
    const contextValue = (key, toUi = value => value, fromUi = value => value) => computed({
      get: () => toUi(draft.value.context[key]),
      set: value => { draft.value.context[key] = fromUi(value) },
    })
    const flagSet = reactive({
      heJueZhang: contextValue('lastCopy'),
      gangShangKaiHua: computed({ get: () => draft.value.context.kongWin, set: value => {
        if (value) draft.value = setWinMethod(draft.value, 'tsumo')
        draft.value.context.kongWin = value
      } }),
      qiangGangHe: computed({ get: () => draft.value.context.robKong, set: value => {
        if (value) { draft.value = setWinMethod(draft.value, 'ron'); draft.value.context.lastTile = false }
        draft.value.context.robKong = value
      } }),
      miaoShouHuiChun: computed({ get: () => draft.value.context.lastTile && draft.value.context.method === 'tsumo', set: value => {
        if (value) draft.value = setWinMethod(draft.value, 'tsumo')
        draft.value.context.lastTile = value
      } }),
      haiDiLaoYue: computed({ get: () => draft.value.context.lastTile && draft.value.context.method === 'ron', set: value => {
        if (value) { draft.value = setWinMethod(draft.value, 'ron'); draft.value.context.robKong = false }
        draft.value.context.lastTile = value
      } }),
    })
    const form = reactive({
      hand: concealedHand,
      getTile: computed(() => draft.value.winTile),
      flowerCount: contextValue('flowers'),
      hepaiType: computed({ get: () => draft.value.context.method === 'tsumo' ? 'zimo' : 'dianhe', set: value => { draft.value = setWinMethod(draft.value, value === 'zimo' ? 'tsumo' : 'ron') } }),
      changFeng: contextValue('round', value => `场风${value}`, value => value.replace('场风', '')),
      menFeng: contextValue('seat', value => `自风${value}`, value => value.replace('自风', '')),
      riichi: contextValue('riichi', value => value || 0),
      ippatsu: contextValue('ippatsu', value => !!value),
      dora: contextValue('dora', value => value || 0),
      uraDora: contextValue('uraDora', value => value || 0),
      redDora: contextValue('redDora', value => value || 0),
      flagSet,
    })

    function parseInput({ random = false, requireComplete = true } = {}) {
      if (fulu.slots.some(slot => !slot.locked && slot.input.trim())) throw new Error('请先完成副露输入并选择副露类型，或清空未完成的副露。')
      let next = cloneDraft(draft.value)
      if (textInput.value.trim()) next = parseHandText(textInput.value, next, { replaceMelds: true })
      else if (random && !next.hand.length && !next.melds.length) {
        next.hand = randomHandTiles(14)
        next.winTile = next.hand.at(-1)
      } else if (!textInput.value.trim()) {
        next.hand = []
        next.melds = []
        next.winTile = null
        next.river = []
      }
      validateDraft(next)
      if (requireComplete && ![13, 14].includes(effectiveCount(next))) throw new Error(`手牌须为 ${13 - next.melds.length * 3} 或 ${14 - next.melds.length * 3} 张（副露每组按 3 张计算），当前 ${next.hand.length} 张`)
      commit(next, { melds: next.melds.join(',') !== draft.value.melds.join(',') })
      return true
    }

    async function compute(snapshot, selectedOptions, selectedRule, id) {
      const makeWorker = workerFactory || (() => typeof Worker === 'undefined' ? null : new Worker(new URL('../utils/handAnalysisWorker.ts', import.meta.url), { type: 'module' }))
      try { worker = makeWorker() } catch { worker = null }
      if (!worker) return analyzeHand(snapshot, selectedOptions, selectedRule)
      return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
          worker?.terminate(); worker = null; pending = null
          reject(new Error('计算超时，请重试。'))
        }, 15000)
        pending = value => { clearTimeout(timer); resolve(value) }
        worker.onmessage = ({ data }) => {
          if (data.id !== id) return
          clearTimeout(timer); pending = null
          if (data.error) reject(new Error(data.error)); else resolve(data.result)
        }
        worker.onerror = () => {
          if (id !== revision) return
          clearTimeout(timer); pending = null; worker?.terminate(); worker = null
          try { resolve(analyzeHand(snapshot, selectedOptions, selectedRule)) } catch (error) { reject(error) }
        }
        worker.postMessage({ id, draft: snapshot, options: selectedOptions, rule: selectedRule })
      })
    }
    async function calculate({ random = false, decompose = false, parse = true } = {}) {
      invalidate()
      try { if (parse) parseInput({ random }) } catch (error) { return fail(error) }
      const id = revision
      const snapshot = cloneDraft(draft.value)
      const selectedOptions = { ...options }
      loading.value = true
      try {
        const answer = await compute(snapshot, selectedOptions, rule.value, id)
        if (id !== revision || !answer) return false
        result.value = answer
        showDecompositions.value = decompose && !!answer.decompositions?.length
        return true
      } catch (error) { if (id === revision) fail(error); return false }
      finally { if (id === revision) loading.value = false }
    }
    function edit(mutator) {
      try {
        const next = cloneDraft(draft.value)
        mutator(next)
        next.river = []
        if (effectiveCount(next) !== 14) next.winTile = null
        commit(next)
      } catch (error) { fail(error) }
    }
    function pickTile(tile, calculator = false) {
      if (fulu.appendTileToActive(tile)) return
      edit(next => {
        if (calculator && next.winTile !== null) next.hand.splice(next.hand.indexOf(next.winTile), 1)
        if (effectiveCount(next) >= 14) throw new Error(`手牌已达上限 ${expectedTotalCount.value} 张`)
        next.hand.push(tile)
        next.winTile = effectiveCount(next) === 14 ? tile : null
      })
    }
    function removeTile(index, calculator = false) {
      fulu.activateHand()
      edit(next => {
        if (calculator) {
          const hand = [...concealedHand.value]
          hand.splice(index, 1)
          if (next.winTile !== null) hand.push(next.winTile)
          next.hand = hand
        } else next.hand.splice(index, 1)
      })
    }
    function clearGetTile() { edit(next => { if (next.winTile !== null) next.hand.splice(next.hand.indexOf(next.winTile), 1); next.winTile = null }) }
    async function applyDiscard(tile) {
      try { commit(discardTile(draft.value, tile)); return await calculate({ parse: false }) } catch (error) { return fail(error) }
    }
    async function applyDraw(tile, discard = null) {
      try {
        const next = discard === null ? draft.value : discardTile(draft.value, discard)
        commit(drawTile(next, tile))
        return await calculate({ parse: false, decompose: true })
      } catch (error) { return fail(error) }
    }
    function resetAll() {
      commit(emptyDraft(), { melds: true })
    }
    async function loadExample(id) {
      if (!HAND_EXAMPLES.some(example => example.id === id)) return false
      Object.assign(options, defaultOptions(rule.value))
      commit(exampleDraft(id), { melds: true })
      return calculate({ parse: false, decompose: true })
    }
    async function loadDemo() {
      commit(parseHandText('35m146678p24s344z5m', emptyDraft()), { melds: true })
      return calculate({ parse: false, decompose: true })
    }
    function prepareTransfer() {
      try { return parseInput({ requireComplete: false }) } catch (error) { return fail(error) }
    }
    function setSevenPairs(kind, enabled) {
      options[kind] = enabled
      if (enabled) options[kind === 'mcrSevenPairs' ? 'riichiSevenPairs' : 'mcrSevenPairs'] = false
    }
    function setRule(value) {
      if (!CALCULATOR_RULES.includes(value) || rule.value === value) return
      rule.value = value
      Object.assign(options, defaultOptions(value))
    }
    async function setHandOption(key, value) {
      if (key.endsWith('SevenPairs')) setSevenPairs(key, value)
      else options[key] = value
      if ([13, 14].includes(effectiveCount(draft.value))) return calculate({ decompose: true })
    }
    const best = computed(() => result.value?.decompositions?.[0] || null)
    const pailiResult = computed(() => best.value ? null : result.value?.paili || null)
    const conditionText = computed(() => [RULE_LABELS[rule.value], form.hepaiType === 'zimo' ? '自摸' : rule.value === 'riichi' ? '荣和' : '点和', form.changFeng, form.menFeng, ...(rule.value === 'guobiao' ? [`花牌 ${form.flowerCount}`] : []), ...Object.entries(flagSet).filter(([key, value]) => value && (rule.value === 'guobiao' || key !== 'heJueZhang')).map(([key]) => ({ heJueZhang: '和绝张', gangShangKaiHua: '杠上开花', qiangGangHe: '抢杠和', miaoShouHuiChun: '妙手回春', haiDiLaoYue: '海底捞月' })[key])].join(' · '))
    return {
      draft, textInput, options, rule, setRule, form, fulu, result, loading, best, pailiResult, showDecompositions, conditionText,
      ruleLabel: computed(() => RULE_LABELS[rule.value]), calculatorRoute: computed(() => calculatorPath(rule.value)), pailiRoute: computed(() => pailiPath(rule.value)),
      closed: computed(() => draft.value.melds.every(code => code[0] === code[0].toUpperCase())), setHandOption,
      expectedHandCount, expectedTotalCount, calculate, pickTile, removeTile, clearGetTile, applyDiscard, applyDraw,
      resetAll, loadDemo, loadExample, prepareTransfer, setSevenPairs, restoreWarning,
      destroy() { invalidate(); scope.stop() },
    }
  })
}

let shared = null
export function useGuobiaoCalculator() {
  if (!shared) {
    let storage = null
    try { storage = window.sessionStorage } catch { /* Private mode can deny storage. */ }
    shared = createGuobiaoCalculator({ storage, restoreUnified: true })
    if (shared.restoreWarning) ElMessage.warning(shared.restoreWarning)
  }
  return shared
}
