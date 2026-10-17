const RIICHI_ABORT_CAPTIONS = {
  four_wind_abort: '四风连打',
  four_kan_abort: '四杠散了',
  four_riichi_abort: '四人立直',
  three_ron_abort: '三家和流局',
}

export function specialDrawCaption(action, rule = '') {
  if (action === 'jiuzhongjiupai') {
    return String(rule).split('/')[0] === 'classical' ? '九老峰回' : '九种九牌'
  }
  return RIICHI_ABORT_CAPTIONS[action] || null
}
