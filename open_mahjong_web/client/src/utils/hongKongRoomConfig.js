export const hkVersions = [
  { label: 'Wiki版本', value: 'gametower' }, { label: '恋绘色版本', value: 'lianhuise' },
]
export const hkProfiles = [
  { label: '清章十三', value: 'hongkong/qingzhang' },
  { label: '新章十三（Wiki）', value: 'hongkong/new13_gametower' },
  { label: '新章十三（恋绘色）', value: 'hongkong/new13_lianhuise' },
  { label: '新章十三（恋绘色魔改）', value: 'hongkong/qingzhang_lianhuise' },
  { label: '新章十六', value: 'hongkong/new16' },
]
export const hkProfileLabel = form => hkProfiles.find(p => p.value === displayHongKongProfile(form))?.label || form.sub_rule
export const isRemix = form => form.sub_rule === 'hongkong/qingzhang_lianhuise'
export const isGametower = form => form.sub_rule === 'hongkong/new13_gametower' || (form.sub_rule === 'hongkong/new13' && !isLianhuise(form))
export const displayHongKongProfile = form => form.sub_rule === 'hongkong/new13' ? (isLianhuise(form) ? 'hongkong/new13_lianhuise' : 'hongkong/new13_gametower') : form.sub_rule
export const hkChoices = {
  win_claim: [{ label: '截胡', value: 'head_bump' }, { label: '一炮多响', value: 'multiple' }],
  dealer_mode: [{ label: '每局轮庄', value: 'rotate' }, { label: '庄和或流局连庄', value: 'win_or_draw' }],
}
export const hkDefaults = { new13_version: 'gametower', flowers: false, new13_full_shoot: true, self_draw_only: false, win_claim: 'head_bump', dealer_mode: 'rotate', liability_twelve: true, liability_dragons: true, liability_kong: true, liability_limit: true }
export const isLianhuise = form => form.sub_rule === 'hongkong/new13_lianhuise' || (form.sub_rule === 'hongkong/new13' && form.hk_new13_version === 'lianhuise')

export function hongKongDefaults(form) {
  const sixteen = form.sub_rule === 'hongkong/new16', lianhuise = isLianhuise(form)
  return { ...hkDefaults, new13_version: lianhuise ? 'lianhuise' : 'gametower',
    flowers: sixteen || lianhuise || isRemix(form), win_claim: sixteen ? 'multiple' : 'head_bump',
    dealer_mode: isRemix(form) ? 'default' : sixteen || lianhuise ? 'win_or_draw' : 'rotate' }
}

export const hongKongChoices = (form, key) => key === 'dealer_mode' && isRemix(form)
  ? [...hkChoices.dealer_mode, { label: '庄和或流局听牌连庄', value: 'default' }]
  : hkChoices[key]

export function loadHongKongForm(form, detail = {}) {
  form.hk_new13_version = detail?.new13_version ?? 'gametower'
  for (const [key, value] of Object.entries(hongKongDefaults(form)))
    form[`hk_${key}`] = detail?.[key] == null || detail[key] === 'default' ? value : detail[key]
}

export function hongKongDetail(form) {
  const result = Object.fromEntries(Object.entries(hongKongDefaults(form)).map(([key, value]) =>
    [key, form[`hk_${key}`] == null || form[`hk_${key}`] === 'default' ? value : form[`hk_${key}`]]))
  result.new13_version = isLianhuise(form) ? 'lianhuise' : 'gametower'
  result.flowers = form.sub_rule === 'hongkong/new16' || isRemix(form) ||
    ((form.sub_rule === 'hongkong/qingzhang' || isLianhuise(form)) &&
      (typeof result.flowers === 'boolean' ? result.flowers : isLianhuise(form)))
  return result
}

export function hongKongRows(form) {
  const detail = hongKongDetail(form)
  const rows = [{ label: '花牌', value: detail.flowers ? '有花（144张）' : '无花（136张）' }]
  rows.push({ label: '和牌方式', value: detail.self_draw_only ? '只可自摸' : '可点和与自摸' })
  for (const [key, label] of [['win_claim', '多人和同一张牌'], ['dealer_mode', '连庄方式']]) {
    rows.push({ label, value: hongKongChoices(form, key).find(option => option.value === detail[key])?.label || detail[key] })
  }
  const enabled = value => value ? '开启' : '关闭'
  if (form.sub_rule !== 'hongkong/new16') rows.push({ label: '包十二张', value: enabled(detail.liability_twelve) })
  if (form.sub_rule === 'hongkong/qingzhang' || isLianhuise(form)) rows.push({ label: '包大三元', value: enabled(detail.liability_dragons) })
  if (isLianhuise(form)) rows.push({ label: '生章明杠包自摸', value: enabled(detail.liability_kong) })
  if (isRemix(form)) rows.push({ label: '包满贯', value: enabled(detail.liability_limit) })
  if (isGametower(form)) rows.push({ label: '全冲', value: enabled(detail.new13_full_shoot) })
  return rows
}
