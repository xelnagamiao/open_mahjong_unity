const ROOM_TYPE_LABELS = {
  custom: '自定义房',
  match: '排位匹配',
}

const RULE_LABELS = {
  guobiao: '国标',
  riichi: '立直',
  qingque: '青雀',
  classical: '古典',
  sichuan: '四川',
}

const SUB_RULE_LABELS = {
  'guobiao/standard': '国标麻将(标准)',
  'guobiao/xiaolin': '国标麻将(小林改)',
  'guobiao/kshen': 'K神麻将',
  'guobiao/lanshi': '国标麻将(蓝十改)',
  'qingque/standard': '青雀',
  'classical/standard': '古典麻雀',
  'sichuan/standard': '四川麻将(血战到底)',
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
