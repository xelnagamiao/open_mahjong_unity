export const GUANGDONG_MIL_SUB_RULE = 'guangdong/mil2023'
export const TUIDAO_SUB_RULE = 'guangdong/tuidao_mil2024'
export const guangdongProfiles = Object.freeze([
  { value: TUIDAO_SUB_RULE, label: '推倒和（MIL 2024，无癞子）', description: '136张，报听可选，头跳，32番封顶，另加2底分。未启用地方附录的万能牌等补则。', book: '推倒和麻将（推广）竞赛规则（试行2024版）.pdf' },
  { value: GUANGDONG_MIL_SUB_RULE, label: '广东麻将（MIL 2023，花鬼）', description: '140张，梅兰竹菊为鬼牌，留手不补花；不吃，不报听，头跳。至少2番，默认同时满足4分起和；自摸翻剩余牌墙前4张奖马，流局退杠分。', book: '广东麻将（推广）竞赛规则（试行2023版）.pdf' },
])

export function loadGuangdongForm(form, config = {}) {
  form.sub_rule = config.sub_rule || form.sub_rule || TUIDAO_SUB_RULE
  form.require_minimum_score = config.detailed_config?.require_minimum_score !== false
  form.tactical_call = config.tactical_call ?? true
  return form
}

export function guangdongDetail(form) {
  if (form.sub_rule === TUIDAO_SUB_RULE) return { edition: 'mil-tuidao-2024-om1', fan_cap: 32, wildcards: false }
  if (form.sub_rule !== GUANGDONG_MIL_SUB_RULE) throw new Error('请选择广东麻将子规则')
  if (typeof form.require_minimum_score !== 'boolean') throw new Error('4分起和开关必须是布尔值')
  return { edition: 'mil-guangdong-2023-om1', wildcards: true, minimum_fan: 2, minimum_score: 4, horse_count: 4, require_minimum_score: form.require_minimum_score }
}

export function buildGuangdongRoomPayload(form) {
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将')
  if (typeof form.tactical_call !== 'boolean') throw new Error('战术鸣牌开关必须是布尔值')
  return {
    room_rule: 'guangdong', password: String(form.password || '').trim(),
    room_config: {
      room_name: String(form.room_name || '').trim(), sub_rule: form.sub_rule, game_round: form.game_round,
      round_timer: form.round_timer, step_timer: form.step_timer, tips: form.tips,
      tourist_limit: form.tourist_limit, allow_spectator: form.allow_spectator,
      tactical_call: form.tactical_call,
      detailed_config: guangdongDetail(form),
    },
  }
}

export function guangdongRows(form) {
  return form.sub_rule === GUANGDONG_MIL_SUB_RULE
    ? [{ label: '规则底本', value: 'MIL 2023 花鬼标准本' }, { label: '起和条件', value: form.require_minimum_score ? '至少2番，同时至少4分' : '至少2番' }, { label: '鬼牌', value: '梅兰竹菊留手，不补花' }, { label: '奖马', value: '自摸翻前4马，不足不补' }, { label: '战术鸣牌', value: form.tactical_call !== false ? '开启（抢断5秒，有机器人时自动关闭）' : '关闭' }]
    : [{ label: '规则底本', value: 'MIL 2024 无癞子标准本' }, { label: '封顶', value: '32番，另加2底分' }, { label: '战术鸣牌', value: form.tactical_call !== false ? '开启（有机器人时自动关闭）' : '关闭' }]
}
