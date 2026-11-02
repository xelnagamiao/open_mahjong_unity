import options from '../../../shared/hongqueRoomOptions.json' with { type: 'json' }
import { HONGQUE_W_SUB_RULE } from '../constants/hongqueRulebooks.js'

export const hongqueProfiles = options.profiles
export const isHongqueW = form => form.sub_rule === HONGQUE_W_SUB_RULE

export function loadHongqueForm(form, config = {}) {
  for (const [key, value] of Object.entries({ ...options.defaults, ...options.wDefaults })) form[key] = config[key] ?? value
  return form
}

export function buildHongqueRoomPayload(form) {
  if (!hongqueProfiles.some(profile => profile.value === form.sub_rule)) throw new Error('请选择虹雀子规则')
  if (String(form.duplicate_key || '').trim()) throw new Error('虹雀不支持复式牌山')
  if (typeof form.room_name !== 'string' || form.room_name.trim().length > 128) throw new Error('房间名必须是最多128字的文本')
  if (![4, 8, 12, 16].includes(form.game_round)) throw new Error('局数须为4、8、12或16')
  if (!['multi_ron', 'head_bump'].includes(form.hepai_way)) throw new Error('请选择多响或截和')
  const config = { ...options.defaults, room_name: form.room_name.trim(), sub_rule: form.sub_rule, game_round: form.game_round, hepai_way: form.hepai_way }
  for (const [key, max] of [['round_timer', 1000], ['step_timer', 100], ...(isHongqueW(form) ? [['hongque_min_fan', 100], ['hongque_min_points', 10000]] : [])]) {
    if (!Number.isSafeInteger(form[key]) || form[key] < 0 || form[key] > max) throw new Error(`${key}须为0–${max}的整数`)
    config[key] = form[key]
  }
  for (const key of ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator', ...(isHongqueW(form) ? ['hongque_colourless'] : [])]) {
    if (typeof form[key] !== 'boolean') throw new Error(`${key}须为开关值`)
    config[key] = form[key]
  }
  return { room_rule: 'hongque', password: String(form.password || '').trim(), room_config: config }
}

export function hongqueRoomRows(form) {
  return [
    { label: '点和方式', value: form.hepai_way === 'head_bump' ? '截和' : '一炮多响' },
    ...(isHongqueW(form) ? [
      { label: '加入无色牌', value: form.hongque_colourless ? '开启（144张）' : '关闭（126张）' },
      { label: '起和番数', value: `${form.hongque_min_fan}番` },
      { label: '起和点数', value: `${form.hongque_min_points}点` },
    ] : []),
    { label: '剩余张数提示', value: form.count_tips ? '开启' : '关闭' },
    { label: '指针提示', value: form.pointer_tips ? '开启' : '关闭' },
  ]
}
