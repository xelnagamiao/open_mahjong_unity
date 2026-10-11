export const SHENYANG_RULE = 'shenyang'
export const SHENYANG_SUB_RULE = 'shenyang/mil2025'
export const SHENYANG_VERSION = 'mil-shenyang-2025-om1'
export const SHENYANG_DESCRIPTION = 'MIL 沈阳麻将（推广）2025：136张，须有刻子或将牌为中发白、有幺九、齐三门或纯一色；七对例外。喜杠、旋风杠和杠后公示，六番封顶，最后四张分张自摸。'
const SWITCHES = { tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true }
export function loadShenyangForm(form, config = {}) {
  form.sub_rule = SHENYANG_SUB_RULE
  for (const [key, fallback] of Object.entries({ game_round: 4, round_timer: 20, step_timer: 5, ...SWITCHES })) form[key] = config[key] ?? fallback
  return form
}
export function buildShenyangRoomPayload(form) {
  const integer = (key, low, high) => {
    const value = form[key]
    if (!Number.isSafeInteger(value) || value < low || value > high) throw new Error(`${key}须为 ${low}–${high} 之间的整数`)
    return value
  }
  const boolean = (key, fallback) => {
    const value = form[key] ?? fallback
    if (typeof value !== 'boolean') throw new Error(`${key}须为开关值`)
    return value
  }
  if (form.sub_rule !== SHENYANG_SUB_RULE) throw new Error('沈阳麻将仅支持 MIL 2025')
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  const room_name = String(form.room_name || '').trim()
  if (room_name.length > 128) throw new Error('房间名最多128字')
  return {
    room_rule: SHENYANG_RULE, password: String(form.password || '').trim(),
    room_config: {
      room_name, sub_rule: SHENYANG_SUB_RULE,
      game_round: integer('game_round', 1, 4), round_timer: integer('round_timer', 0, 1000), step_timer: integer('step_timer', 0, 100),
      ...Object.fromEntries(Object.entries(SWITCHES).map(([key, fallback]) => [key, boolean(key, fallback)])),
      use_flowers: false, tactical_call: false, open_cuohe: false, claim_protection: false, tian_di_ren_he: false,
      detailed_config: { rule_version: SHENYANG_VERSION },
    },
  }
}
