import { buildGuobiaoRoomPayload, createDefaultGuobiaoRoomConfig } from './guobiaoRoomConfig.js'

/** Each editor owns a fresh form, so cancelling never changes another setting or preset. */
export function createEventRoomForm(settings = {}) {
  const defaults = createDefaultGuobiaoRoomConfig()
  const config = settings.room_config && typeof settings.room_config === 'object'
    ? settings.room_config
    : {}
  const form = { room_rule: settings.room_rule || 'guobiao', ...defaults }
  for (const key of Object.keys(defaults)) {
    if (key !== 'password' && config[key] !== undefined) form[key] = config[key]
  }
  form.password = String(settings.password || '')
  return form
}

export function buildEventRoomSettings(form) {
  if (form.room_rule === 'guobiao') {
    return { room_rule: form.room_rule, ...buildGuobiaoRoomPayload(form) }
  }
  const roomName = String(form.room_name || '').trim()
  return {
    room_rule: form.room_rule,
    room_config: roomName ? { room_name: roomName } : {},
    password: String(form.password || '').trim(),
  }
}

export function eventRoomSettingsSummary(settings, ruleLabels = {}) {
  const form = createEventRoomForm(settings)
  const rule = ruleLabels[form.room_rule] || form.room_rule
  if (form.room_rule !== 'guobiao') return rule
  const round = ({ 1: '东风战', 2: '东南战', 4: '全庄战' })[form.game_round] || `${form.game_round} 圈`
  return `${rule} · ${round} · 局时 ${form.round_timer}s · 步时 ${form.step_timer}s`
}

/** Full, read-only review of the canonical settings used for this one table. */
export function eventRoomSettingsRows(settings, ruleLabels = {}) {
  const form = createEventRoomForm(settings)
  const enabled = value => value ? '开启' : '关闭'
  const subRules = {
    'guobiao/standard': '国标标准', 'guobiao/xiaolin': '小林', 'guobiao/kshen': 'K神', 'guobiao/lanshi': '蓝氏',
    'riichi/standard': '立直标准', 'qingque/standard': '青雀标准', 'classical/standard': '古典标准',
    'sichuan/standard': '四川标准', 'changsha/classic_double_bird': '长沙经典双鸟', 'taiwan/standard': '台湾标准',
  }
  const rows = [
    { label: '规则', value: ruleLabels[form.room_rule] || form.room_rule },
    { label: '房间名', value: form.room_name || '自动命名' },
    { label: '子规则', value: subRules[form.sub_rule] || form.sub_rule },
    { label: '圈数', value: ({ 1: '东风战', 2: '东南战', 4: '全庄战' })[form.game_round] || `${form.game_round} 圈` },
    { label: '局时', value: Number(form.round_timer) === 0 ? '不限时' : `${form.round_timer} 秒` },
    { label: '步时', value: `${form.step_timer} 秒` },
  ]
  if (form.room_rule === 'guobiao') rows.push({ label: '起和番', value: `${form.hepai_limit} 番` })
  rows.push({ label: '提示', value: enabled(form.tips) })
  if (form.room_rule === 'guobiao') {
    rows.push({ label: '错和', value: enabled(form.open_cuohe) })
    if (form.open_cuohe) rows.push({ label: '错和形式', value: Number(form.cuohe_type) === 1 ? '错和 -40，其余不加分' : '错和 -30，其余各 +10' })
  }
  rows.push({ label: '限制游客', value: enabled(form.tourist_limit) }, { label: '允许观战', value: enabled(form.allow_spectator) })
  if (form.room_rule === 'guobiao') rows.push({ label: '战术鸣牌', value: enabled(form.tactical_call) }, { label: '鸣牌保护', value: enabled(form.claim_protection) })
  return rows
}
