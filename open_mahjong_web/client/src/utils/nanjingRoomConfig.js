export const NANJING_RULE = 'nanjing'
export const NANJING_SUB_RULE = 'nanjing/mil2024'
export const NANJING_VERSION = 'mil-nanjing-2024-v1'
export const NANJING_DESCRIPTION = 'MIL 南京麻将（推广）2024：144张，中发白及八花须补花；不吃牌，四花或非偶然番起和，底分10分、120分封顶，轮庄，含花杠、跟牌、承包和包外。'
const SWITCHES = { tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true }
export function loadNanjingForm(form, config = {}) {
  form.sub_rule = NANJING_SUB_RULE
  for (const [key, fallback] of Object.entries({ game_round: 4, round_timer: 20, step_timer: 5, ...SWITCHES })) form[key] = config[key] ?? fallback
  return form
}
export function buildNanjingRoomPayload(form) {
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
  if (form.sub_rule !== NANJING_SUB_RULE) throw new Error('南京麻将仅支持 MIL 2024')
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  const room_name = String(form.room_name || '').trim()
  if (room_name.length > 128) throw new Error('房间名最多128字')
  return {
    room_rule: NANJING_RULE, password: String(form.password || '').trim(),
    room_config: {
      room_name, sub_rule: NANJING_SUB_RULE,
      game_round: integer('game_round', 1, 4), round_timer: integer('round_timer', 0, 1000), step_timer: integer('step_timer', 0, 100),
      ...Object.fromEntries(Object.entries(SWITCHES).map(([key, fallback]) => [key, boolean(key, fallback)])),
      use_flowers: true, tactical_call: false, open_cuohe: false, claim_protection: false, tian_di_ren_he: false,
      detailed_config: { rule_version: NANJING_VERSION },
    },
  }
}
