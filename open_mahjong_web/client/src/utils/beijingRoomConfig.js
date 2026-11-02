export { BEIJING_DESCRIPTION } from '../constants/beijingRulebooks.js'

export const BEIJING_SUB_RULE = 'beijing/chipengti'
export const BEIJING_VERSION = 'beijing-v1'
export const BEIJING_HUN_MODES = { up: '上滚混儿', down: '下滚混儿', self: '本混儿' }
export const BEIJING_OPTIONS = {
  three_hun_self_draw: '三混儿提', single_call: '吃碰一口提', dingzhuang: '盯庄',
  four_hun_win: '混儿杠', multi_ron: '一炮多响', fan_zimo: '自摸加翻',
  fan_dealer: '庄家加翻', fan_repeat: '连庄加翻', fan_all_pungs: '碰碰和',
  fan_all_waits: '混儿悠', fan_kong: '杠加翻', ron_all_pay: '放炮一家包',
  half_hun: '混儿减半', pao_kong: '包杠', pao_four_calls: '包四口',
}

export const BEIJING_OPTION_GROUPS = [
  { label: '行牌变体', keys: ['three_hun_self_draw', 'single_call', 'dingzhuang', 'four_hun_win', 'multi_ron'] },
  { label: '计翻变体', keys: ['fan_zimo', 'fan_dealer', 'fan_repeat', 'fan_all_pungs', 'fan_all_waits', 'fan_kong'] },
  { label: '支付变体', keys: ['ron_all_pay', 'half_hun', 'pao_kong', 'pao_four_calls'] },
]

const defaults = { game_round: 4, round_timer: 20, step_timer: 5, tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true }

export function loadBeijingForm(form, config = {}) {
  form.sub_rule = config.sub_rule ?? BEIJING_SUB_RULE
  for (const [key, value] of Object.entries(defaults)) form[key] = config[key] ?? value
  form.starting_score = 0
  const detail = config.detailed_config ?? {}
  form.beijing_hun_mode = detail.hun_mode ?? 'up'
  for (const key of Object.keys(BEIJING_OPTIONS)) form['beijing_' + key] = detail[key] ?? false
  return form
}

export function buildBeijingRoomPayload(form) {
  const integer = (key, low, high, label) => {
    if (!Number.isSafeInteger(form[key]) || form[key] < low || form[key] > high) throw new Error(`${label}须为 ${low}–${high} 之间的整数`)
    return form[key]
  }
  const boolean = (key, fallback = false) => {
    const value = form[key] ?? fallback
    if (typeof value !== 'boolean') throw new Error(`${key}须为开关值`)
    return value
  }
  if (form.sub_rule !== BEIJING_SUB_RULE) throw new Error('请选择北京麻将（吃碰提）规则')
  if (String(form.duplicate_key || '').trim()) throw new Error('北京麻将不支持复式牌山')
  if (!Object.hasOwn(BEIJING_HUN_MODES, form.beijing_hun_mode)) throw new Error('请选择上滚、下滚或本混儿')
  if (typeof (form.room_name ?? '') !== 'string' || (form.room_name ?? '').trim().length > 128) throw new Error('房间名必须是最多128字的文本')
  const roomName = (form.room_name ?? '').trim()
  const detail = {
    rule_version: BEIJING_VERSION, hun_mode: form.beijing_hun_mode,
    ...Object.fromEntries(Object.keys(BEIJING_OPTIONS).map(key => [key, boolean('beijing_' + key)])),
  }
  return {
    room_rule: 'beijing', password: String(form.password || '').trim(),
    room_config: {
      ...(roomName ? { room_name: roomName } : {}), sub_rule: BEIJING_SUB_RULE,
      game_round: integer('game_round', 1, 4, '圈数'), starting_score: 0,
      round_timer: integer('round_timer', 0, 1000, '局时储备'), step_timer: integer('step_timer', 0, 100, '步时'),
      ...Object.fromEntries(['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'].map(key => [key, boolean(key, defaults[key])])),
      use_flowers: false, open_cuohe: false, tactical_call: false, claim_protection: false,
      detailed_config: detail,
    },
  }
}

export function beijingRoomRows(form) {
  return [
    { label: '混儿方式', value: BEIJING_HUN_MODES[form.beijing_hun_mode] ?? form.beijing_hun_mode },
    { label: '连庄', value: '庄家和牌连庄，其余轮庄' },
    ...Object.entries(BEIJING_OPTIONS).map(([key, label]) => ({ label, value: form['beijing_' + key] ? '开启' : '关闭' })),
    { label: '剩余张数提示', value: form.count_tips ? '开启' : '关闭' },
    { label: '指针提示', value: form.pointer_tips ? '开启' : '关闭' },
  ]
}
