export const TIERS = [
  { key: 'beginner', label: '初级场', color: '#3467ce' },
  { key: 'intermediate', label: '中级场', color: '#37a7a0' },
  { key: 'advanced', label: '高级场', color: '#dc9c39' },
  { key: 'mcrpl', label: 'MCRPL', color: '#b46c85' },
  { key: 'elo', label: 'Elo 场', color: '#8a82c4' },
]
export const tierLabel = key => key === 'total' ? '全部场次' : TIERS.find(t => t.key === key)?.label || key || '自定义'
export const n = value => Number.isFinite(Number(value)) ? Number(value) : 0
export const count = value => value == null ? '—' : n(value).toLocaleString('zh-CN')
export const mean = (a, b, digits = 2) => b > 0 && a != null ? (n(a) / n(b)).toFixed(digits) : '—'
export const percent = (a, b, digits = 1) => b > 0 && a != null ? (n(a) / n(b) * 100).toFixed(digits) + '%' : '—'
export const signed = value => value == null ? '—' : (n(value) > 0 ? '+' : '') + count(value)
export const dateLabel = value => {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', month:'2-digit', day:'2-digit', hour:'2-digit', minute:'2-digit', hour12:false }).format(date)
}
export function selectTotal(rows, tier = 'total') {
  return rows.find(row => row.match_tier === tier) || null
}
export function dailySeries(rows, from, to, tier = 'total') {
  if (!from || !to || from > to) return []
  const totals = new Map()
  for (const row of rows) {
    if (row.match_tier === 'total' || (tier !== 'total' && row.match_tier !== tier)) continue
    totals.set(row.stat_date, (totals.get(row.stat_date) || 0) + n(row.total_games))
  }
  const result = []
  const date = new Date(from + 'T12:00:00Z')
  const end = new Date(to + 'T12:00:00Z')
  while (date <= end && result.length < 366) {
    const key = date.toISOString().slice(0,10)
    result.push({ date: key, label: key.slice(5).replace('-', '/'), value: totals.get(key) || 0 })
    date.setUTCDate(date.getUTCDate()+1)
  }
  return result
}
export function fanEntries(fans, dict, wins) {
  return [...new Set([...Object.keys(dict || {}), ...Object.keys(fans || {})])]
    .map(key => ({ key, label: typeof dict?.[key]==='string' ? dict[key] : key, value:n(fans?.[key]), share:percent(fans?.[key] || 0, wins) }))
    .sort((a,b) => b.value-a.value || a.label.localeCompare(b.label,'zh-CN'))
}
export function platformMetrics(row, unit = '番') {
  return [
    { label:'累计对局', value:count(row?.total_games), unit:'场', detail:'截至聚合统计日，完整对局去重计数' },
    { label:'累计小局', value:count(row?.total_rounds), unit:'局', detail:'桌面小局数，不是参赛玩家的人局合计' },
    { label:'自摸占和牌', value:percent(row?.self_draw_count,row?.win_count), unit:'', detail:'自摸次数 / 和牌次数' },
    { label:'平均和牌值', value:unit?mean(row?.total_fan_score,row?.win_count):'—', unit, detail:unit?'和牌值合计 / 和牌次数；单位随规则变化':'此规则未提供可比较的和牌值' },
  ]
}
export function playerMetrics(row) {
  const ranks = [row?.first_place_count,row?.second_place_count,row?.third_place_count,row?.fourth_place_count].map(n)
  const sample = ranks.reduce((a,b)=>a+b,0)
  const detail = row?.details_available === true
  return [
    {label:'对局样本',value:count(row?.total_games),unit:'场',detail:'当前查询范围内的对局'},
    {label:'平均顺位',value:mean(ranks.reduce((a,v,i)=>a+v*(i+1),0),sample),unit:'',detail:'顺位加权和 / 有结算顺位的场数'},
    {label:'和牌率',value:detail?percent(row?.win_count,row?.total_rounds):'—',unit:'',detail:detail?'本家和牌次数 / 本家小局数':'该范围未完成明细统计'},
    {label:'放铳率',value:detail?percent(row?.deal_in_count,row?.total_rounds):'—',unit:'',detail:detail?'本家放铳次数 / 本家小局数':'该范围未完成明细统计'},
  ]
}
export function sceneName(record) {
  if (record.event_id) return record.event_name || '比赛 / 基地'
  if (record.room_type === 'custom') return '自定义'
  return tierLabel(record.match_tier)
}
export function modeLabel(value, rule = '') {
  if (!value) return '—'
  if (value.startsWith('hands')) return value.replace('hands','')+' 局'
  if(rule.startsWith('hongque'))return String(value).split('/')[0]+' 局'
  return ({ '1':'东风战','2':'半庄战','3':'东西战','4':'全庄战' })[String(value).split('/')[0]] || value
}
export function normalizeRecords(items) {
  return (items || []).map(record => ({ ...record, players:Array.isArray(record.players)?record.players:[] }))
}

