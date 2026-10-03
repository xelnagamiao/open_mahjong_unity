export const SHANXI_SUB_RULE = 'shanxi/mil2023'
export const SHANXI_VERSION = 'mil-shanxi-2023-om1'

export function shanxiRoomPayload(form) {
  const room_name = String(form.room_name || '').trim()
  if (room_name.length > 128) throw new Error('房间名不能超过128字')
  for (const [key, min, max] of [['game_round', 1, 4], ['round_timer', 0, 1000], ['step_timer', 0, 100]]) {
    if (!Number.isInteger(form[key]) || form[key] < min || form[key] > max) throw new Error(`无效设置：${key}`)
  }
  for (const key of ['tips', 'tourist_limit', 'allow_spectator']) if (typeof form[key] !== 'boolean') throw new Error(`无效开关：${key}`)
  return { room_rule: 'shanxi', password: String(form.password || '').trim(), room_config: {
    room_name, sub_rule: SHANXI_SUB_RULE, detailed_config: { rule_version: SHANXI_VERSION },
    game_round: form.game_round, round_timer: form.round_timer, step_timer: form.step_timer,
    tips: form.tips, tourist_limit: form.tourist_limit, allow_spectator: form.allow_spectator,
  } }
}
