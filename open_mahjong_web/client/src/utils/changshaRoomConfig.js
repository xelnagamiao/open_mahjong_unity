import options from '../../../shared/changshaRoomOptions.json' with { type: 'json' }

import exchangeOptions from '../../../shared/changshaExchangeSixOptions.json' with { type: 'json' }
export const changshaOptions = options
export const exchangeSixRule = 'changsha/exchange_six'
const common = { game_round: 4, round_timer: 20, step_timer: 5, tips: false, tourist_limit: false, allow_spectator: true }
export function loadChangshaForm(form, config = {}) {
  form.sub_rule = config.sub_rule || 'changsha/classic_double_bird'
  form.exchange_selection_seconds = config.exchange_selection_seconds ?? 30
  for (const [key, fallback] of Object.entries(common)) form[key] = config[key] ?? fallback
  for (const [key, option] of Object.entries(options)) form[key] = config[key] ?? option.default
  if ([1, 3].includes(form.open_kong_replacement_count)) form.open_kong_replacement_count = 2
  return form
}

export function changshaRows(form) {
  const values = form.sub_rule === exchangeSixRule ? { ...form, ...exchangeOptions } : form
  const rows = form.sub_rule === exchangeSixRule ? [{ label: '换牌时间', value: `${form.exchange_selection_seconds}秒` }] : []
  return rows.concat(Object.entries(options).filter(([key]) => key !== 'score_cap_value' || values.score_cap_type !== 'none').map(([key, option]) => ({
    label: option.label,
    value: typeof values[key] === 'boolean' ? (values[key] ? '开启' : '关闭')
      : option.choices?.[option.values.indexOf(values[key])] ?? String(values[key]),
  })))
}

export function buildChangshaRoomPayload(form) {
  if (String(form.duplicate_key || '').trim()) throw new Error('复式房间仅支持国标麻将和川麻血流')
  if (!['changsha/classic_double_bird', exchangeSixRule].includes(form.sub_rule)) throw new Error('不支持的长沙麻将子规则')
  form = form.sub_rule === exchangeSixRule ? { ...form, ...exchangeOptions } : form
  const config = { room_name: String(form.room_name || '').trim(), sub_rule: form.sub_rule }
  if (form.sub_rule === exchangeSixRule) {
    if (!Number.isInteger(form.exchange_selection_seconds) || form.exchange_selection_seconds < 10 || form.exchange_selection_seconds > 60) throw new Error('换牌时间必须为10–60秒整数')
    config.exchange_selection_seconds = form.exchange_selection_seconds
  }
  if (config.room_name.length > 128) throw new Error('房间名最多128字')
  for (const [key, option] of Object.entries(options)) {
    const value = key === 'score_cap_value' && form.score_cap_type === 'none' ? 0 : form[key]
    if (key !== 'score_cap_value' || form.score_cap_type !== 'none') {
      if (option.values ? !option.values.includes(value)
        : typeof option.default === 'boolean' ? typeof value !== 'boolean'
        : !Number.isSafeInteger(value) || value < option.min || value > option.max) throw new Error(`无效的长沙设置：${option.label}`)
    }
    config[key] = value
  }
  if (![1,2,4].includes(form.game_round)) throw new Error('长沙麻将圈数必须为1、2或4')
  for (const [key, max] of [['round_timer',1000],['step_timer',100]]) {
    if (!Number.isInteger(form[key]) || form[key] < 0 || form[key] > max) throw new Error('无效的计时设置')
  }
  for (const [key, fallback] of Object.entries(common)) {
    if (typeof fallback === 'boolean' && typeof form[key] !== 'boolean') throw new Error(`${key}须为开关值`)
    config[key] = form[key]
  }
  return { room_rule: 'changsha', room_config: config, password: String(form.password || '').trim() }
}
