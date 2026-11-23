const defaults = { game_round: 4, hepai_limit: 0, round_timer: 20, step_timer: 5, tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true }
export const MAX_NEW_CHINESE_FAN = 2147483647
export const newChineseWinLimit = form => form.sub_rule === 'new_chinese/sanhua' ? 4 : form.exchange_three ? 15 : form.hepai_limit

export function loadNewChineseForm(form, config = {}) {
  form.sub_rule = config.sub_rule ?? 'new_chinese/standard'
  for (const [key, value] of Object.entries(defaults)) form[key] = config[key] ?? value
  form.starting_score = 0
  const detail = config.detailed_config ?? {}
  form.exchange_three = detail.exchange_three ?? false
  return form
}

export function buildNewChineseRoomPayload(form) {
  const integer = (value, low, high, label) => {
    if (!Number.isSafeInteger(value) || value < low || value > high) throw new Error(`${label}须为 ${low}–${high} 之间的整数`)
    return value
  }
  const boolean = (key, fallback = false) => {
    const value = form[key] ?? fallback
    if (typeof value !== 'boolean') throw new Error(`${key}须为开关值`)
    return value
  }
  const sanhua = form.sub_rule === 'new_chinese/sanhua'
  if (!sanhua && form.sub_rule !== 'new_chinese/standard') throw new Error('请选择新中式麻将规则')
  if (String(form.duplicate_key || '').trim()) throw new Error('新中式麻将不支持复式牌山')
  if (![4, 8, 12, 16].includes(form.game_round)) throw new Error('圈数须为4、8、12或16')
  if (typeof (form.room_name ?? '') !== 'string' || (form.room_name ?? '').trim().length > 128) throw new Error('房间名必须是最多128字的文本')
  const roomName = (form.room_name ?? '').trim()
  const detail = sanhua ? { rule_version: 'sanhua-2026-11' } : { rule_version: 'new-chinese-2026-11', exchange_three: boolean('exchange_three') }
  return {
    room_rule: 'new_chinese', password: String(form.password || '').trim(),
    room_config: {
      ...(roomName ? { room_name: roomName } : {}), sub_rule: form.sub_rule,
      game_round: form.game_round, starting_score: 0,
      hepai_limit: integer(newChineseWinLimit(form), 0, MAX_NEW_CHINESE_FAN, '起和番'),
      round_timer: integer(form.round_timer, 0, 1000, '局时储备'), step_timer: integer(form.step_timer, 0, 100, '步时'),
      ...Object.fromEntries(['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'].map(key => [key, boolean(key, defaults[key])])),
      use_flowers: false, open_cuohe: false, tactical_call: false, claim_protection: false,
      detailed_config: detail,
    },
  }
}
