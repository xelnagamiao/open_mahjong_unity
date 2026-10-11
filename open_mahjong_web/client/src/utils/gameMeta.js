const ROOM_TYPE_LABELS = {
  custom: '自定义房',
  match: '排位匹配',
}

const RULE_LABELS = {
  shenyang: '沈阳麻将',
  nanchang: '南昌麻将',
  wuhan: '武汉麻将',
  jiujiang: '九江麻将',
  guobiao: '国标',
  riichi: '立直',
  qingque: '青雀',
  classical: '古典',
  shanghai: '上海麻将',
  sichuan: '四川',
}

const SUB_RULE_LABELS = {
  'shenyang/mil2025': '沈阳麻将（MIL 2025）',
  'nanchang/mil2026': '南昌麻将（MIL 2026 四人单打）',
  'wuhan/mil2025': '武汉麻将（MIL 2025）',
  'jiujiang/standard': '九江麻将',
  'shanghai/lajihe': '上海垃圾和',
  'guobiao/standard': '国标麻将(标准)',
  'guobiao/xiaolin': '国标麻将(小林改)',
  'guobiao/kshen': 'K神麻将',
  'guobiao/lanshi': '国标麻将(蓝十改)',
  'guobiao/transparent': '透明国标麻将',
  'qingque/standard': '青雀',
  'classical/standard': '古典麻雀',
  'sichuan/standard': '四川麻将(血战到底)',
  'sichuan/doubles': '血战双打·2V2', 'sichuan/xueliu_doubles': '血流双打·2V2',
  'riichi/standard': '立直麻将(标准)',
}

export function formatRoomType(roomType) {
  if (!roomType) return '-'
  return ROOM_TYPE_LABELS[roomType] || roomType
}

export function formatRule(rule) {
  if (!rule) return '-'
  return RULE_LABELS[rule] || rule
}

export function formatSubRule(subRule) {
  if (!subRule) return '-'
  return SUB_RULE_LABELS[subRule] || subRule
}

export function pickGameMeta(players = []) {
  const first = players[0] || {}
  return {
    room_type: first.room_type || '',
    rule: first.rule || '',
    sub_rule: first.sub_rule || '',
    match_type: first.match_type || '',
  }
}
