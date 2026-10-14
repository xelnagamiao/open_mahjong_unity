export const FUZHOU_RULE = 'fuzhou'
export const FUZHOU_SUB_RULE = 'fuzhou/standard'
export const FUZHOU_VERSION = 'fuzhou-user-20261014-v1'
export const FUZHOU_DESCRIPTION = '福州十六张：144张，全部字牌及八花逐张补花，补花后开金；十七张和牌，支持抢金、三金倒、听金坎、一游及二游，动态留墩，末四张只摸不打。'
const SWITCHES = { tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true }
export function loadFuzhouForm(form, config = {}) {
  form.sub_rule = FUZHOU_SUB_RULE
  for (const [key, fallback] of Object.entries({ game_round: 1, round_timer: 20, step_timer: 5, ...SWITCHES })) form[key] = config[key] ?? fallback
  form.fuzhou_base_score = config.detailed_config?.base_score ?? 10
  return form
}
export function buildFuzhouRoomPayload(form) {
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
  if (form.sub_rule !== FUZHOU_SUB_RULE) throw new Error('未开放的福州子规则')
  if (String(form.duplicate_key || '').trim()) throw new Error('福州不支持复式牌墙')
  const room_name = String(form.room_name || '').trim()
  if (room_name.length > 128) throw new Error('房间名最多128字')
  const base = form.fuzhou_base_score ?? 10
  if (![10,15,20].includes(base)) throw new Error('底分只能为10、15或20')
  return {
    room_rule: FUZHOU_RULE, password: String(form.password || '').trim(),
    room_config: {
      room_name, sub_rule: FUZHOU_SUB_RULE,
      game_round: integer('game_round', 1, 4), round_timer: integer('round_timer', 0, 1000), step_timer: integer('step_timer', 0, 100),
      ...Object.fromEntries(Object.entries(SWITCHES).map(([key, fallback]) => [key, boolean(key, fallback)])),
      use_flowers: true, tactical_call: false, open_cuohe: false, claim_protection: false, tian_di_ren_he: false,
      detailed_config: { rule_version: FUZHOU_VERSION, base_score: base },
    },
  }
}
