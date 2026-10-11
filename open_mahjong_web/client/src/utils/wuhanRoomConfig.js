export const WUHAN_RULE = 'wuhan'
export const WUHAN_SUB_RULE = 'wuhan/mil2025'
export const WUHAN_VERSION = 'mil2025'
export const WUHAN_CONFIG = Object.freeze({ rule_version: WUHAN_VERSION })

export function loadWuhanForm(form, config = {}) {
  form.sub_rule = WUHAN_SUB_RULE
  for (const [key, fallback] of Object.entries({
    game_round: 4, round_timer: 20, step_timer: 5, tips: true,
    count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true,
  })) form[key] = config[key] ?? fallback
  return form
}

export function buildWuhanRoomPayload(form) {
  const integer = (key, low, high, label) => {
    const value = form[key]
    if (!Number.isInteger(value) || value < low || value > high) throw new Error(`${label}须为 ${low}–${high} 之间的整数`)
    return value
  }
  const boolean = (key, fallback) => {
    const value = form[key] ?? fallback
    if (typeof value !== 'boolean') throw new Error(`${key}须为开关值`)
    return value
  }
  if (form.sub_rule !== WUHAN_SUB_RULE) throw new Error('武汉麻将仅支持 MIL 2025')
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  const room_name = String(form.room_name || '').trim()
  return {
    room_rule: WUHAN_RULE,
    password: String(form.password || '').trim(),
    room_config: {
      ...(room_name ? { room_name } : {}), sub_rule: WUHAN_SUB_RULE,
      game_round: integer('game_round', 1, 4, '局数组'),
      round_timer: integer('round_timer', 0, 1000, '局时'), step_timer: integer('step_timer', 0, 100, '步时'),
      tips: boolean('tips', true), count_tips: boolean('count_tips', false), pointer_tips: boolean('pointer_tips', true),
      tourist_limit: boolean('tourist_limit', false), allow_spectator: boolean('allow_spectator', true),
      use_flowers: false, open_cuohe: false, tactical_call: false, claim_protection: false, tian_di_ren_he: false,
      detailed_config: { ...WUHAN_CONFIG },
    },
  }
}
