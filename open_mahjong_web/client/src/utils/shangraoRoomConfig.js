export const SHANGRAO_RULE = 'shangrao'
export const SHANGRAO_SUB_RULE = 'shangrao/standard'
export const SHANGRAO_VERSION = 'shangrao-v0.2'
export const SHANGRAO_DESCRIPTION = "上饶麻将是流行于江西上饶及周边的玩法，使用136张麻将牌（无花牌），只能自摸。除了基本的屁和牌型，还有碰碰胡、七对、九幺和十三烂特色牌型。宝牌（即“癞子”）为核心特色，单宝仅可作将，多余宝才能作万能牌，打出宝牌（“飞宝”）和牌可翻倍，因此又称为“飞宝麻将”。特色牌型和翻倍机制配合只能自摸的规则，使牌局兼具趣味性和策略性。"
export const SHANGRAO_OPTIONS = {qingyise:'清一色', ziyise:'字一色', allow_special_bao:'九幺允许宝牌', four_bao_reward:'四宝奖励（局末2分）', flying_seven_stars:'飞宝七星'}

export function loadShangraoForm(form, config = {}) {
  form.sub_rule = SHANGRAO_SUB_RULE
  for (const [key, fallback] of Object.entries({
    game_round: 4, round_timer: 20, step_timer: 5, tips: true,
    count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true,
  })) form[key] = config[key] ?? fallback
  for (const key of Object.keys(SHANGRAO_OPTIONS)) form["shangrao_"+key] = config.detailed_config?.[key] ?? false
  return form
}

export function buildShangraoRoomPayload(form) {
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
  if (form.sub_rule !== SHANGRAO_SUB_RULE) throw new Error('请选择上饶麻将标准规则')
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  const room_name = String(form.room_name || '').trim()
  return {
    room_rule: SHANGRAO_RULE, password: String(form.password || '').trim(),
    room_config: {
      ...(room_name ? { room_name } : {}), sub_rule: SHANGRAO_SUB_RULE,
      game_round: integer('game_round', 1, 4, '副数组'),
      round_timer: integer('round_timer', 0, 1000, '局时'), step_timer: integer('step_timer', 0, 100, '步时'),
      tips: boolean('tips', true), count_tips: boolean('count_tips', false), pointer_tips: boolean('pointer_tips', true),
      tourist_limit: boolean('tourist_limit', false), allow_spectator: boolean('allow_spectator', true),
      use_flowers: false, open_cuohe: false, tactical_call: false, claim_protection: false, tian_di_ren_he: false,
      detailed_config: { rule_version: SHANGRAO_VERSION, ...Object.fromEntries(Object.keys(SHANGRAO_OPTIONS).map(key=>[key,boolean("shangrao_"+key,false)])) },
    },
  }
}
