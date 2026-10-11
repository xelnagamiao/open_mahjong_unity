export const NANCHANG_RULE = 'nanchang'
export const NANCHANG_SUB_RULE = 'nanchang/mil2026'
export const NANCHANG_VERSION = 'mil-nanchang-2026'
export const NANCHANG_DESCRIPTION = "MIL《南昌麻将（推广）竞赛规则（试行2026版）》四人单打：136张，正副精万能牌，字牌可吃，支持十三烂、七对、精吊、德国及冲关、霸王精。1番起和、6番封顶，精分、杠分、和牌分分别结算。"

export function loadNanchangForm(form, config = {}) {
  form.sub_rule = NANCHANG_SUB_RULE
  for (const [key, fallback] of Object.entries({
    game_round: 4, round_timer: 20, step_timer: 5, tips: true,
    count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true,
  })) form[key] = config[key] ?? fallback
  return form
}

export function buildNanchangRoomPayload(form) {
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
  if (form.sub_rule !== NANCHANG_SUB_RULE) throw new Error('南昌麻将仅支持 MIL 2026')
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  const room_name = String(form.room_name || '').trim()
  return {
    room_rule: NANCHANG_RULE, password: String(form.password || '').trim(),
    room_config: {
      ...(room_name ? { room_name } : {}), sub_rule: NANCHANG_SUB_RULE,
      game_round: integer('game_round', 1, 4, '圈数'),
      round_timer: integer('round_timer', 0, 1000, '局时'), step_timer: integer('step_timer', 0, 100, '步时'),
      tips: boolean('tips', true), count_tips: boolean('count_tips', false), pointer_tips: boolean('pointer_tips', true),
      tourist_limit: boolean('tourist_limit', false), allow_spectator: boolean('allow_spectator', true),
      use_flowers: false, open_cuohe: false, tactical_call: false, claim_protection: false, tian_di_ren_he: false,
      detailed_config: { rule_version: NANCHANG_VERSION },
    },
  }
}
