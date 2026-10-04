export const DUPLICATE_WALL_LABELS = Object.freeze({ manual: '手动牌山', seed: '复现牌山', key: '密钥牌山' })
export const DUPLICATE_ROUND_COUNTS = Object.freeze([1, 4, 8, 12, 16])

export function duplicateRoundSeats(round = 1) {
  const index = Math.max(0, Math.min(15, Number(round) - 1))
  const circleSeats = [[0, 1, 2, 3], [1, 0, 3, 2], [3, 2, 0, 1], [2, 3, 1, 0]]
  return circleSeats[Math.floor(index / 4)].map(seat => (seat - index % 4 + 4) % 4)
}

export function duplicateRoundLabel(round) {
  return `${['东', '南', '西', '北'][Math.floor((round - 1) / 4)]}${['一', '二', '三', '四'][(round - 1) % 4]}局`
}

export function isDuplicateWallRule(rule) {
  return rule === 'guobiao' || rule === 'guobiao/lanshi'
}

export function duplicateWallLabel(value) {
  return DUPLICATE_WALL_LABELS[value] || value || '—'
}

export function duplicateRoomLabel(room = {}) {
  const type = room.duplicate_wall_type || room.wall_type
  if (type) return duplicateWallLabel(type)
  if (room.duplicate_key || room.is_duplicate) return '复式牌墙'
  return room.is_player_set_random_seed ? '场景复现' : '普通对局'
}

export function isDuplicateGameRecord(detail) {
  const title = detail?.record?.game_title || detail?.game_title || {}
  return [detail, title].some(value => value && (value.is_duplicate === true || Boolean(value.duplicate_key) || Boolean(value.duplicate_wall_type) || Boolean(value.duplicate_wall_id)))
}

export function tileCounts(tiles) {
  return tiles.reduce((counts, tile) => {
    counts[tile] = (counts[tile] || 0) + 1
    return counts
  }, {})
}

/** Validate the catalog's full tile multiset, preserving the player's chosen order. */
export function validateManualWall(tiles, rule) {
  if (!rule?.tiles?.length) return '请先选择规则'
  if (!Array.isArray(tiles)) return '牌墙格式错误'
  const limits = Object.fromEntries(rule.tiles.map(item => [item.tile, Number(item.count)]))
  const counts = tileCounts(tiles)
  for (const [tile, count] of Object.entries(counts)) {
    if (!(tile in limits)) return '牌墙包含此规则不支持的牌'
    if (count > limits[tile]) return '同一种牌的数量超过规则限制'
  }
  const total = Number(rule.tile_count) || Object.values(limits).reduce((a, b) => a + b, 0)
  if (tiles.length !== total) return `请补齐完整牌墙（${tiles.length} / ${total} 张）`
  if (total % 4 !== 0) return '牌墙必须能均分为四份'
  return ''
}

export function splitDuplicateWall(tiles, tileCount) {
  const perSeat = Math.floor(Number(tileCount) / 4)
  return Array.from({ length: 4 }, (_, index) => tiles.slice(index * perSeat, (index + 1) * perSeat))
}

export function duplicateRuleWithFlowers(rule, useFlowers = true) {
  if (!rule) return null
  const enabled = rule.rule !== 'guobiao/lanshi' && useFlowers !== false
  const option = rule.flower_options?.find(item => item.use_flowers === enabled)
  const tiles = option?.tiles || rule.tiles.filter(item => enabled || Number(item.tile) < 50)
  return { ...rule, use_flowers: enabled, tiles, tile_count: option?.tile_count || tiles.reduce((total, item) => total + Number(item.count), 0) }
}

export function createDuplicateDraft() {
  return Array.from({ length: 4 }, () => ({ hand: [], wall: [] }))
}

export function duplicateZoneLimit(tileCount, seat, zone, dealerSeat = 0) {
  const handCount = seat === dealerSeat ? 14 : 13
  return zone === 'hand' ? handCount : Number(tileCount) / 4 - handCount
}

export function flattenDuplicateDraft(draft) {
  return draft.flatMap(seat => [...seat.hand, ...seat.wall])
}

export function validateDuplicateDraft(draft, rule, dealerSeat = 0) {
  if (!Array.isArray(draft) || draft.length !== 4) return '请设置四位玩家的手牌与牌山'
  for (let seat = 0; seat < 4; seat++) {
    for (const zone of ['hand', 'wall']) {
      const limit = duplicateZoneLimit(rule?.tile_count, seat, zone, dealerSeat)
      if (!Array.isArray(draft[seat]?.[zone]) || draft[seat][zone].length !== limit) {
        return `请补齐 ${seat + 1} 号玩家${zone === 'hand' ? '手牌' : '牌山'}（${draft[seat]?.[zone]?.length || 0} / ${limit} 张）`
      }
    }
  }
  return validateManualWall(flattenDuplicateDraft(draft), rule)
}

/** Move one existing tile without shifting any other player's partition. */
export function moveDuplicateTile(draft, source, target, tileCount, dealerSeat = 0) {
  const from = draft[source?.seat]?.[source?.zone]
  const to = draft[target?.seat]?.[target?.zone]
  if (!from || !to || !Number.isInteger(source.index) || source.index < 0 || source.index >= from.length) return false
  if (from !== to && to.length >= duplicateZoneLimit(tileCount, target.seat, target.zone, dealerSeat)) return false
  const index = Math.max(0, Math.min(to.length - (from === to ? 1 : 0), target.index ?? to.length))
  const [tile] = from.splice(source.index, 1)
  to.splice(index, 0, tile)
  return true
}

/** Duplicate generation hashes the seed text; never coerce it to a Number. */
export function validateDuplicateSeed(seed) {
  const value = String(seed || '').trim()
  if (!value || value.length > 128) return '种子须为 1–128 个字符的文本'
  return ''
}

export function duplicateDate(value) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString('zh-CN', { hour12: false })
}
