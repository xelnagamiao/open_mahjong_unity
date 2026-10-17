import catalog from '../../../shared/riichiRuleOptions.json' with { type: 'json' }
import { riichiWinRequirements } from './riichiWinRequirements.js'

import captain from '../../../shared/washizuDoubles.json' with { type: 'json' }

export const riichiModes = [
  { value: 'riichi/standard', label: '四人立直' },
  { value: 'riichi/sanma', label: '三人立直' },
  { value: captain.sub_rule, label: captain.label },
]
export const isWashizuCaptain = form => form.sub_rule === captain.sub_rule
export const captainFixedKeys = Object.keys(captain.detailed_config)

export function enforceCaptainRules(form) {
  if (!isWashizuCaptain(form)) return
  Object.assign(form, captain.room)
  Object.assign(form.riichi_detailed_config, captain.detailed_config)
}

export function changeRiichiMode(form, subRule) {
  form.sub_rule = subRule
  Object.assign(form, riichiRoomDefaults)
  if (subRule === 'riichi/sanma') form.starting_score = 35000
  form.riichi_detailed_config = defaults(subRule)
  form.minimum_win_score = form.riichi_detailed_config.minimum_win_score
  enforceCaptainRules(form)
}

export const riichiPresets = catalog.presets
export const riichiOptions = catalog.options
export const riichiRoomDefaults = {
  game_round: 2, starting_score: 25000, red_dora: true, allow_kuikae: false,
  open_xiru: true, open_tobi: true, hepai_way: 'multi_ron', open_cuohe: false,
}
const commonDefaults = { room_name: '', round_timer: 20, step_timer: 5, tips: false, tourist_limit: false, allow_spectator: true }

function defaults(subRule) {
  const sanma = subRule === 'riichi/sanma' ? riichiPresets.find(p => p.id === 'sanma_majsoul').values : {}
  return Object.fromEntries(riichiOptions.map(o => [o.key, sanma[o.key] ?? o.default]))
}

export function loadRiichiForm(form, config = {}) {
  form.sub_rule = config.sub_rule || 'riichi/standard'
  for (const [key, value] of Object.entries({ ...commonDefaults, ...riichiRoomDefaults })) {
    form[key] = config[key] ?? (key === 'starting_score' && form.sub_rule === 'riichi/sanma' ? 35000 : value)
  }
  form.riichi_hepai_limit = config.hepai_limit ?? 1
  form.riichi_detailed_config = { ...defaults(form.sub_rule), ...config.detailed_config }
  form.minimum_win_score = form.riichi_detailed_config.minimum_win_score
  enforceCaptainRules(form)
}

export function applyRiichiPreset(form, id) {
  const preset = riichiPresets.find(p => p.id === id)
  if (!preset) return
  Object.assign(form, preset.room, {
    sub_rule: preset.sub_rule || 'riichi/standard',
    riichi_hepai_limit: preset.room.hepai_limit,
    minimum_win_score: preset.values.minimum_win_score,
    riichi_detailed_config: { ...preset.values },
  })
}

export function selectedRiichiPreset(form) {
  return riichiPresets.find(p => (p.sub_rule || 'riichi/standard') === form.sub_rule
    && Object.entries(p.room).every(([k, v]) => (k === 'hepai_limit' ? form.riichi_hepai_limit : form[k]) === v)
    && riichiOptions.every(o => (o.key === 'minimum_win_score' ? form.minimum_win_score : form.riichi_detailed_config?.[o.key]) === p.values[o.key]))
}

export function buildRiichiRoomPayload(form) {
  if (form.tips && form.open_cuohe) throw new Error('提示与错和不能同时开启')
  const score = form.starting_score
  if (!Number.isInteger(score) || score < 1000 || score > 1000000 || score % 100 !== 0) {
    throw new Error('起始点数须为 1000–1000000 之间的整数，且为 100 的倍数')
  }
  const requirements = riichiWinRequirements(form)
  const details = { ...form.riichi_detailed_config, ...requirements.detailed_config }
  if (Object.keys(details).some(key => !riichiOptions.some(o => o.key === key))) throw new Error('未知日麻详细规则')
  for (const o of riichiOptions) if (!o.values.includes(details[o.key])) throw new Error(`请选择有效的${o.label}`)
  if (!riichiModes.some(mode => mode.value === form.sub_rule)) throw new Error('请选择有效的立直子规则')
  if (form.sub_rule !== 'riichi/sanma' && (details.rank_points.startsWith('sanma_') || details.noten_penalty === 2000)) {
    throw new Error('三人顺位点和2000点不听罚符仅适用于三人立直')
  }
  if (isWashizuCaptain(form)) Object.assign(details, captain.detailed_config)
  const room_config = Object.fromEntries(Object.keys({ ...commonDefaults, ...riichiRoomDefaults }).map(key => [key, form[key]]))
  if (isWashizuCaptain(form)) Object.assign(room_config, captain.room)
  return { room_rule: 'riichi', password: String(form.password || '').trim(), room_config: {
    ...room_config, room_name: String(form.room_name || '').trim(), sub_rule: form.sub_rule,
    hepai_limit: requirements.hepai_limit, detailed_config: details,
  } }
}
