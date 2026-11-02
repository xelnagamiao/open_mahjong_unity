export { DUOCAI_DESCRIPTION } from '../constants/duocaiRulebooks.js'

export const DUOCAI_VERSION = 'duocai-v1'
export const DUOCAI_PROFILES = [
  { value: 'duocai/guobiao', label: '国标', game_round: 4, roundLabel: '全庄战（轮庄制）', description: '8番起和，底分100点；金银赤宝牌及纯赖子杠奖励不计入起和番。' },
  { value: 'duocai/riichi', label: '立直', game_round: 2, roundLabel: '东南战', description: '有食断、后付和食替，头跳、切上满贯；无听牌终局、无和牌终局。' },
]

const defaults = {
  round_timer: 20, step_timer: 5, tips: true, count_tips: false,
  pointer_tips: true, tourist_limit: false, allow_spectator: true,
}

export function duocaiProfile(subRule) {
  return DUOCAI_PROFILES.find(profile => profile.value === subRule)
}

export function changeDuocaiProfile(form, subRule) {
  const profile = duocaiProfile(subRule)
  if (!profile) throw new Error('请选择夺彩麻将的国标或立直子规则')
  form.sub_rule = profile.value
  form.game_round = profile.game_round
  form.starting_score = 40000
}

export function loadDuocaiForm(form, config = {}) {
  form.sub_rule = config.sub_rule || DUOCAI_PROFILES[0].value
  form.game_round = config.game_round ?? duocaiProfile(form.sub_rule)?.game_round ?? 4
  form.starting_score = 40000
  for (const [key, value] of Object.entries(defaults)) form[key] = config[key] ?? value
  return form
}

export function buildDuocaiRoomPayload(form) {
  const profile = duocaiProfile(form.sub_rule)
  if (!profile) throw new Error('请选择夺彩麻将的国标或立直子规则')
  if (String(form.duplicate_key || '').trim()) throw new Error('夺彩麻将不支持复式牌山')
  if (form.game_round !== profile.game_round) throw new Error(`夺彩${profile.label}采用${profile.roundLabel}`)
  const integer = (key, low, high, label) => {
    if (!Number.isInteger(form[key]) || form[key] < low || form[key] > high) throw new Error(`${label}须为 ${low}–${high} 之间的整数`)
    return form[key]
  }
  const boolean = key => {
    const value = form[key] ?? defaults[key]
    if (typeof value !== 'boolean') throw new Error(`${key}须为开关值`)
    return value
  }
  const roomName = String(form.room_name || '').trim()
  return {
    room_rule: 'duocai', password: String(form.password || '').trim(),
    room_config: {
      ...(roomName ? { room_name: roomName } : {}), sub_rule: profile.value,
      game_round: profile.game_round, starting_score: 40000,
      round_timer: integer('round_timer', 0, 1000, '局时储备'), step_timer: integer('step_timer', 0, 100, '步时'),
      tips: boolean('tips'), count_tips: boolean('count_tips'), pointer_tips: boolean('pointer_tips'),
      tourist_limit: boolean('tourist_limit'), allow_spectator: boolean('allow_spectator'),
      use_flowers: false, open_cuohe: false, tactical_call: false, claim_protection: false,
      detailed_config: { rule_version: DUOCAI_VERSION },
    },
  }
}
