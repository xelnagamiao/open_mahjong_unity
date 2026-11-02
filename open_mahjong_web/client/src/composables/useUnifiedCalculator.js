import { ref, reactive, computed, watch, onScopeDispose } from 'vue'
import { createGuobiaoCalculator } from './useGuobiaoCalculator.js'
import { emptyDraft, cloneDraft, validateDraft, effectiveCount, draftNotation, setWinMethod } from '../utils/handAnalysis.ts'
import { parseNotationText, tilesToNotationText } from './useMahjongTiles.js'
import { CALCULATOR_RULES, ruleOptions } from '../utils/calculatorRules.ts'

const STORAGE_KEY = 'salasasa.unified-calculator.v1'
const clone = value => JSON.parse(JSON.stringify(value))
export const initialState = () => ({ rule: 'guobiao', draft: emptyDraft(), text: '', options: ruleOptions() })

export function validateState(value) {
  if (!value || !CALCULATOR_RULES.includes(value.rule)) throw new Error('无法识别手牌规则')
  validateDraft(value.draft)
  if (typeof value.text !== 'string' || value.text.length > 8000) throw new Error('手牌简写不正确')
  const canonical = draftNotation(value.draft)
  const legacy = draftNotation({ ...value.draft, melds: [] })
  if (value.text !== canonical && value.text !== legacy) throw new Error('手牌简写与保存的数据不一致')
  for (const key of Object.keys(ruleOptions())) if (typeof value.options?.[key] !== 'boolean') throw new Error('和牌牌型设置不正确')
  if (value.options.mcrSevenPairs && value.options.riichiSevenPairs) throw new Error('两种七对设置不能同时开启')
  if (value.rule !== 'guobiao' && (value.options.unrelatedTiles || value.options.combinationDragon)) throw new Error('当前规则不支持此和牌牌型')
  if (value.rule === 'riichi' && value.options.mcrSevenPairs) throw new Error('日麻须使用日麻七对')
  return { ...clone(value), text: canonical }
}

export function encodeState(value) {
  return btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(validateState(value)))))
}
export function decodeState(encoded) {
  if (encoded.length > 16000) throw new Error('手牌链接过长')
  try { return validateState(JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(encoded), c => c.charCodeAt(0))))) }
  catch { throw new Error('手牌链接无法读取，请检查是否复制完整') }
}

export function useUnifiedCalculator({ storageKey = STORAGE_KEY, fallbackStorageKeys = ['salasasa.guobiao-calculator.v1'], initialRule = null } = {}) {
  const error = ref(''), notice = ref(''), history = ref([]), cursor = ref(-1)
  const choosingWin = ref(false), visibleInput = ref('')
  let timer, suspended = false, stopContext, actionId = 0
  const start = initialState()
  try {
    if (location.hash.startsWith('#hand=')) Object.assign(start, decodeState(location.hash.slice(6)))
    else {
      for (const key of [storageKey, ...fallbackStorageKeys]) {
        const saved = sessionStorage.getItem(key)
        if (saved) {
          const state = JSON.parse(saved)
          if (key !== storageKey && state.rule == null) state.rule = 'guobiao'
          Object.assign(start, validateState(state))
          break
        }
      }
    }
  } catch (e) { error.value = e.message }
  if (!location.hash.startsWith('#hand=') && CALCULATOR_RULES.includes(initialRule) && start.rule !== initialRule) {
    start.rule = initialRule
    start.options = ruleOptions(initialRule)
  }
  const makeSession = value => reactive(createGuobiaoCalculator({
    storage: { getItem: () => JSON.stringify(value), setItem: () => {} },
    onError: message => { error.value = message },
  }))
  const session = ref(makeSession(start))
  visibleInput.value = tilesToNotationText(start.draft.visible)
  const snapshot = () => clone({ rule: session.value.rule, draft: session.value.draft, text: session.value.textInput, options: session.value.options })
  function save() {
    const state = snapshot()
    try { sessionStorage.setItem(storageKey, JSON.stringify(state)) } catch {}
    if (typeof window !== 'undefined' && location.hash.startsWith('#hand=') && !error.value) {
      try { window.history.replaceState(window.history.state, '', `#hand=${encodeState(state)}`) } catch {}
    }
  }
  function checkpoint(label) {
    const state = snapshot()
    if (JSON.stringify(history.value[cursor.value]?.state) === JSON.stringify(state)) return
    history.value.splice(cursor.value + 1)
    history.value.push({ label, state })
    if (history.value.length > 60) history.value.shift()
    cursor.value = history.value.length - 1
    save()
  }
  const pendingMeld = () => session.value.fulu.slots.some(slot => !slot.locked && slot.input.trim())
  async function compute() {
    if (!session.value.result && !pendingMeld() && [13, 14].includes(effectiveCount(session.value.draft))) return session.value.calculate({ parse: false })
    return true
  }
  async function action(label, edit, { parse = false, replace = false } = {}) {
    const id = ++actionId
    clearTimeout(timer); error.value = ''; notice.value = ''; suspended = true
    try {
      const unparsed = session.value.textInput !== draftNotation(session.value.draft)
      if (!replace && (parse || (unparsed && !pendingMeld())) && !session.value.prepareTransfer()) return false
      const pending = edit()
      suspended = false
      const accepted = await pending
      if (id !== actionId || accepted === false || error.value) return false
      suspended = true
      normalizeConditions()
      suspended = false
      if (!pendingMeld()) checkpoint(label)
      return await compute()
    } catch (e) { error.value = e.message; return false }
    finally { if (id === actionId) suspended = false }
  }
  const parse = () => action('输入手牌', () => session.value.prepareTransfer())
  const pick = tile => action('录入牌', () => session.value.pickTile(tile))
  const remove = index => action('移除手牌', () => session.value.removeTile(index, true))
  const discard = tile => action('切牌', () => session.value.applyDiscard(tile))
  const draw = (tile, discardTile = null, method = null) => action(method === 'ron' ? '点和试算' : '摸牌', async () => {
    if (method) session.value.draft = setWinMethod(session.value.draft, method)
    return session.value.applyDraw(tile, discardTile)
  })
  const changeRule = rule => action('切换规则', () => session.value.setRule(rule), { parse: true })
  const changeOption = (key, value) => action('调整和牌牌型', () => session.value.setHandOption(key, value))
  const clear = () => action('清空手牌', () => { session.value.resetAll(); visibleInput.value = ''; choosingWin.value = false }, { replace: true })
  const chooseWin = tile => action('指定和牌张', () => {
    const draft = cloneDraft(session.value.draft); draft.winTile = tile; validateDraft(draft)
    session.value.draft = draft; session.value.textInput = draftNotation(draft); choosingWin.value = false
  })
  const clearWin = () => action('退回摸牌前', () => {
    const draft = cloneDraft(session.value.draft)
    if (draft.winTile !== null) draft.hand.splice(draft.hand.indexOf(draft.winTile), 1)
    draft.winTile = null
    session.value.draft = draft; session.value.textInput = draftNotation(draft)
  })
  const applyVisible = () => action('调整已见牌', () => {
    const draft = cloneDraft(session.value.draft); draft.visible = parseNotationText(visibleInput.value)
    validateDraft(draft); session.value.draft = draft
  })
  const clearRiver = () => action('清空推演牌河', () => { session.value.draft.river = [] })
  function normalizeConditions() {
    const c = session.value.draft.context
    if (!session.value.closed && c.riichi) c.riichi = 0
    if (!c.riichi) {
      if (c.ippatsu) c.ippatsu = false
      if (c.uraDora) c.uraDora = 0
    }
  }
  function observeContext() {
    stopContext?.()
    stopContext = watch(() => session.value.draft.context, () => {
      if (suspended) return
      clearTimeout(timer)
      timer = setTimeout(() => action('调整和牌条件', () => true), 180)
    }, { deep: true, flush: 'sync' })
  }
  async function restore(index) {
    if (!Number.isInteger(index) || !history.value[index]) return false
    ++actionId
    clearTimeout(timer); suspended = true; error.value = ''; choosingWin.value = false
    stopContext?.(); session.value.destroy()
    session.value = makeSession(history.value[index].state)
    cursor.value = index
    visibleInput.value = tilesToNotationText(session.value.draft.visible)
    observeContext(); save(); suspended = false
    return compute()
  }
  function meldAction(name, ...args) { return action('调整副露', () => session.value.fulu[name](...args)) }
  async function example(key) {
    return action('示例', async () => {
      session.value.resetAll(); visibleInput.value = ''
      session.value.textInput = ({ improve: '123456m23p55s11z2z', ready: '123456789m23p55s', win: '123456789m23p55s + 1p', discard: '123456789m23p55s9s', orphans: '19m19p19s1234567z', pairs: '1122334455667m + 7m', noYaku: '123m456p678s12s22p + 3s', low: '345m456p67s22p(s22) + 8s', melds: '567m45s22z(s13)(K23) + 6s' })[key]
      return session.value.prepareTransfer()
    }, { replace: true })
  }
  async function share() {
    if (!await parse() || pendingMeld()) { if (pendingMeld()) error.value = '请先完成副露'; return '' }
    const url = new URL(location.href); url.hash = `hand=${encodeState(snapshot())}`
    try { await navigator.clipboard.writeText(url.href); notice.value = '手牌链接已复制' }
    catch { notice.value = '请复制下方手牌链接' }
    return url.href
  }
  async function importHash() {
    if (!location.hash.startsWith('#hand=')) return
    try {
      const state = decodeState(location.hash.slice(6))
      history.value.splice(cursor.value + 1)
      history.value.push({ label: '载入分享手牌', state })
      if (history.value.length > 60) history.value.shift()
      await restore(history.value.length - 1)
    } catch (e) { error.value = e.message }
  }
  observeContext(); checkpoint('初始手牌'); compute()
  if (typeof window !== 'undefined') window.addEventListener('hashchange', importHash)
  onScopeDispose(() => {
    clearTimeout(timer); stopContext?.(); session.value.destroy()
    if (typeof window !== 'undefined') window.removeEventListener('hashchange', importHash)
  })
  return { session, error, notice, history, cursor, choosingWin, visibleInput, snapshot,
    parse, pick, remove, discard, draw, changeRule, changeOption, clear, chooseWin, clearWin, applyVisible, clearRiver, meldAction, example, share,
    undo: () => cursor.value > 0 && restore(cursor.value - 1), redo: () => cursor.value < history.value.length - 1 && restore(cursor.value + 1), restore,
    canUndo: computed(() => cursor.value > 0), canRedo: computed(() => cursor.value < history.value.length - 1),
    pendingMeld: computed(pendingMeld),
  }
}
