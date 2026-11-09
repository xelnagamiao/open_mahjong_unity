import catalog from '../../../shared/eventRoomCatalog.json' with { type: 'json' }
import taiwan from '../../../shared/taiwanRoomOptions.json' with { type: 'json' }
import { isSichuanDuplicate, supportsDuplicateKey } from './sichuanDuplicate.js'

export const eventRoomRuleOptions = catalog.rules
export const supplementFields = rule => catalog.supplementFields[rule] || []
export const duplicateRoomActive = form => (form.room_rule === 'sichuan' && isSichuanDuplicate(form.sub_rule)) || (supportsDuplicateKey(form) && Boolean(form.duplicate_enabled || form.auto_duplicate || form.duplicate_key?.trim()))
export const eventFieldDefinitions = {
  game_round: { label: '圈数', min: 1, max: 4, default: 4 },
  tips: { label: '提示', default: false },
  count_tips: { label: '剩余张数提示', default: false },
  pointer_tips: { label: '指针提示', default: true },
  tourist_limit: { label: '限制游客', default: false },
  allow_spectator: { label: '允许观战', default: true },
  tactical_call: { label: '战术鸣牌', default: true },
  claim_protection: { label: '鸣牌保护', default: true },
  blood_battle: { label: '血战到底', default: true },
  hepai_limit: { label: '起和番', min: 0, max: 64, default: 0 },
  open_cuohe: { label: '允许错和宣告', default: false },
  cuohe_type: { label: '错和形式', min: 0, max: 1, default: 0 },
  jinluo_mode: { label: '金锣赛', default: false },
  jinluo_weather: { label: '金锣赛天气', default: 'dragon_pairs' },
}
export const jinluoWeathers = [
  ['dragon_pairs', '箭牌对子×2'], ['seat_round_winds', '门风+场风'], ['terminal_pairs', '数牌幺九对×3'],
  ['all_simples', '断幺'], ['missing_suit', '无字+缺门'], ['consecutive_pairs', '连对：两个及以上同花色相邻数牌对子'],
  ['same_rank_pairs', '同对：两个及以上相同序数数牌对子'], ['four_of_a_kind', '四归一：有四张相同牌的七对子'],
].map(([value, label]) => ({ value, label }))

export function normalizeSceneSeed(value) {
  if (value == null || value === 0 || value === '' || value === '0') return ''
  if (typeof value !== 'string') throw new Error('随机种子须为64位十六进制字符串')
  const seed = value.trim().replace(/^0x/i, '').toLowerCase()
  if (!/^[0-9a-f]{64}$/.test(seed) || /^0+$/.test(seed)) throw new Error('随机种子须为64位非零十六进制字符串')
  return seed
}

export function loadEventRoomExtensions(form, config = {}) {
  for (const key of supplementFields(form.room_rule)) {
    if (key === 'password') continue
    const fallback = key === 'claim_protection' && form.room_rule === 'riichi' ? false : eventFieldDefinitions[key]?.default
    form[key] = config[key] ?? fallback
  }
  form.random_seed = config.random_seed ? String(config.random_seed) : ''
  form.scene_reproduction = Boolean(form.random_seed)
  if (form.room_rule === 'taiwan') {
    form.taiwan_details = { ...Object.fromEntries(taiwan.options.map(o => [o.key, o.default])), ...config.detailed_config }
    form.taiwan_fan_overrides = { ...config.detailed_config?.fan_tai_overrides }
  }
}

export function extendEventRoomPayload(form, payload) {
  const config = payload.room_config
  for (const key of supplementFields(form.room_rule)) {
    if (key === 'password') continue
    const definition = eventFieldDefinitions[key]
    const value = form[key] === undefined ? (key === 'claim_protection' && form.room_rule === 'riichi' ? false : definition.default) : form[key]
    if (typeof definition.default === 'boolean' && typeof value !== 'boolean') throw new Error(`${definition.label}须为开关值`)
    if (definition.min != null && (!Number.isSafeInteger(value) || value < definition.min || value > definition.max)) throw new Error(`${definition.label}须为 ${definition.min}–${definition.max} 的整数`)
    config[key] = value
  }
  if (form.scene_reproduction) {
    config.random_seed = normalizeSceneSeed(form.random_seed)
    if (!config.random_seed) throw new Error('请输入随机种子')
    if (duplicateRoomActive(form)) throw new Error('场景复现与复式不能同时使用，请在普通房间填写随机种子')
  }
  if (duplicateRoomActive(form)) config.allow_spectator = false
  if (form.room_rule === 'sichuan' && form.sub_rule.includes('doubles')) {
    config.hepai_limit = 0
    config.blood_battle = !form.sub_rule.includes('xueliu')
  }
  if (form.room_rule === 'guobiao') {
    if (!jinluoWeathers.some(w => w.value === config.jinluo_weather)) throw new Error('请选择金锣赛天气')
    if (config.jinluo_mode) {
      if (form.sub_rule !== 'guobiao/standard') throw new Error('金锣赛仅适用于标准国标')
      Object.assign(config, { hepai_limit: 8, open_cuohe: true, tips: false })
    }
  }
  if (form.room_rule === 'taiwan') {
    const details = { ...Object.fromEntries(taiwan.options.map(o => [o.key, o.default])), ...form.taiwan_details, fan_tai_overrides: { ...form.taiwan_fan_overrides } }
    for (const option of taiwan.options) if (!option.values.includes(details[option.key])) throw new Error(`请选择有效的${option.label}`)
    for (const [key, value] of Object.entries(details.fan_tai_overrides)) {
      if (!taiwan.fans.some(f => f.key === key) || !Number.isInteger(value) || value < 1 || value > 64) throw new Error('自定义台值须为1–64的整数')
    }
    config.detailed_config = details
  }
  if (config.open_cuohe && config.tips) throw new Error('提示与错和不能同时开启')
  return payload
}

export function eventExtensionRows(form) {
  const rows = supplementFields(form.room_rule).filter(key => key !== 'password' && eventFieldDefinitions[key]).map(key => {
    const value = form[key]
    return { label:eventFieldDefinitions[key].label, value:typeof value === 'boolean' ? value ? '开启' : '关闭' : key === 'jinluo_weather' ? jinluoWeathers.find(w=>w.value===value)?.label || value : String(value) }
  })
  rows.push({ label:'场景复现', value:form.scene_reproduction ? '开启' : '关闭' })
  if (form.scene_reproduction) rows.push({ label:'随机种子', value:form.random_seed })
  if (form.room_rule === 'taiwan') {
    for (const option of taiwan.options) rows.push({ label:option.label, value:option.choices[option.values.indexOf(form.taiwan_details[option.key])] })
    for (const [key,value] of Object.entries(form.taiwan_fan_overrides)) rows.push({ label:`自定义台值 · ${taiwan.fans.find(f=>f.key===key)?.label || key}`, value:`${value}台` })
  }
  return rows
}
