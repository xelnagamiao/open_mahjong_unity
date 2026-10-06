export const HANGZHOU_RULE = 'hangzhou'
export const HANGZHOU_SUB_RULE = 'hangzhou/mil2025'
export const HANGZHOU_VERSION = 'mil-hangzhou-2025-om1'
export const HANGZHOU_DESCRIPTION = 'MIL 杭州麻将（推广）2025：136张，白板财神，十三张手牌，仅自摸；爆头、财飘、七对、十风，4番封顶，老庄2/4/8倍，三吃承包，墙尾20张流局。'
const SWITCHES = { tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true, tactical_call: true }
export function loadHangzhouForm(form, config = {}) {
  form.sub_rule = HANGZHOU_SUB_RULE
  for (const [key, fallback] of Object.entries({ game_round: 4, round_timer: 20, step_timer: 5, ...SWITCHES })) form[key] = config[key] ?? fallback
  return form
}
export function buildHangzhouRoomPayload(form) {
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
  if (form.sub_rule !== HANGZHOU_SUB_RULE) throw new Error('杭州麻将仅支持 MIL 2025')
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  const room_name = String(form.room_name || '').trim()
  if (room_name.length > 128) throw new Error('房间名最多128字')
  return {
    room_rule: HANGZHOU_RULE, password: String(form.password || '').trim(),
    room_config: {
      room_name, sub_rule: HANGZHOU_SUB_RULE,
      game_round: integer('game_round', 1, 4), round_timer: integer('round_timer', 0, 1000), step_timer: integer('step_timer', 0, 100),
      ...Object.fromEntries(Object.entries(SWITCHES).map(([key, fallback]) => [key, boolean(key, fallback)])),
      use_flowers: false, open_cuohe: false, claim_protection: false, tian_di_ren_he: false,
      detailed_config: { rule_version: HANGZHOU_VERSION },
    },
  }
}
