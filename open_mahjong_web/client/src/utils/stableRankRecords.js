// Only settled ladder games with identifiable rooms/formats are comparable.
// In particular, an analysis-page filter is never evidence of a game's room.
const TIERS = new Set(['beginner', 'intermediate', 'advanced', 'mcrpl'])
const GAME_TYPES = new Set(['dongfeng', 'banzhuang', 'quanzhuang'])
const ROUND_GAME_TYPES = { 1: 'dongfeng', 2: 'banzhuang', 4: 'quanzhuang' }
const EXCLUSION_LABELS = {
  invalid_record: '牌谱格式无效',
  duplicate: '重复牌谱',
  missing_player: '牌谱中没有目标玩家',
  unsupported_rule: '非国标或规则不明',
  non_ladder: '自定义、赛事或非天梯对局',
  unknown_room: '无法确认实际天梯场次',
  unknown_tier: '天梯场次级别不明',
  unknown_format: '局制不明或不支持',
  conflicting_metadata: '牌谱场次或局制信息冲突',
  missing_settlement: '缺少目标玩家的有效结算名次',
  missing_tie_settlement: '缺少完整并列结算信息',
}

function asText(value) {
  return typeof value === 'string' ? value.trim() : ''
}

function parseRecord(item) {
  const raw = item?.record ?? item
  if (typeof raw !== 'string') return raw
  try {
    return JSON.parse(raw)
  } catch (_) {
    return null
  }
}

function firstField(title, item, field) {
  return asText(title?.[field]) || asText(item?.[field])
}

function resolveScene(title, item) {
  const rule = firstField(title, item, 'rule')
  if (rule !== 'guobiao') return { reason: 'unsupported_rule' }

  const roomType = firstField(title, item, 'room_type')
  // Explicit room type always takes precedence over a queue/tier string.
  if (roomType && roomType !== 'match') return { reason: 'non_ladder' }
  const queueName = asText(title.match_queue_type)
  const queue = /^(beginner|intermediate|advanced|mcrpl)_(dongfeng|banzhuang|quanzhuang)$/.exec(queueName)
  if (!roomType && !queue) return { reason: 'unknown_room' }

  const explicitTier = firstField(title, item, 'match_tier')
  if (explicitTier && !TIERS.has(explicitTier)) return { reason: 'unknown_tier' }
  const tier = explicitTier || queue?.[1]
  if (!TIERS.has(tier)) return { reason: 'unknown_tier' }
  if (queue && queue[1] !== tier) return { reason: 'conflicting_metadata' }

  const matchType = firstField(title, item, 'match_type')
  const typeMatch = /^([124])\/4(?:_rank)?$/.exec(matchType)
  const rounds = title.max_round
  const hasRounds = rounds != null && rounds !== ''
  const numericRounds = typeof rounds === 'number' || (typeof rounds === 'string' && /^[124]$/.test(rounds))
  const roundType = hasRounds && numericRounds ? ROUND_GAME_TYPES[Number(rounds)] : null
  if ((hasRounds && !roundType) || (matchType && !typeMatch)) return { reason: 'unknown_format' }
  const types = [queue?.[2], roundType, typeMatch && ROUND_GAME_TYPES[typeMatch[1]]].filter(Boolean)
  if (!types.length || !GAME_TYPES.has(types[0])) return { reason: 'unknown_format' }
  if (new Set(types).size !== 1) return { reason: 'conflicting_metadata' }
  // Unknown queue values must not silently inherit a known tier and format.
  if (queueName && !queue) return { reason: 'unknown_room' }
  return { tier, gameType: types[0] }
}

/**
 * Extract the current target's settled samples from the downloaded records.
 * `settlement_rank` MUST come from this target's current /record-ids response.
 * `settlement_tie_count` is the number of players sharing that competition rank
 * (e.g. a two-way tie at rank 2 occupies places 2 and 3, averaging their PT).
 * Do not use IndexedDB's `rank`: records are keyed by game_id and that cached
 * rank may belong to another player. Without target-bound settlement data we
 * exclude the game, since incomplete round scores cannot prove a final rank.
 * Per-game DB metadata may fill missing historical game_title fields only.
 */
export function collectStableRankSamples(items, userId) {
  const samples = []
  const counts = new Map()
  const seenIds = new Set()
  const seenRecords = new WeakSet()
  const target = Number(userId)
  const exclude = (reason) => counts.set(reason, (counts.get(reason) || 0) + 1)

  for (const item of Array.isArray(items) ? items : []) {
    const record = parseRecord(item)
    const title = record?.game_title
    if (!record || typeof record !== 'object' || !title || typeof title !== 'object' || Array.isArray(title)) {
      exclude('invalid_record')
      continue
    }
    const id = String(item?.game_id || record.game_id || title.game_id || '')
    if ((id && seenIds.has(id)) || seenRecords.has(record)) {
      exclude('duplicate')
      continue
    }
    if (id) seenIds.add(id)
    seenRecords.add(record)

    const hasTarget = Number.isSafeInteger(target) && target > 0
      && [0, 1, 2, 3].some((seat) => Number(title[`p${seat}_uid`]) === target)
    if (!hasTarget) {
      exclude('missing_player')
      continue
    }
    const scene = resolveScene(title, item)
    if (scene.reason) {
      exclude(scene.reason)
      continue
    }
    const rawRank = item?.settlement_rank
    const rank = typeof rawRank === 'number' || (typeof rawRank === 'string' && /^[1-4]$/.test(rawRank))
      ? Number(rawRank) : null
    if (!Number.isInteger(rank) || rank < 1 || rank > 4
      || (item?.settlement_user_id != null && Number(item.settlement_user_id) !== target)) {
      exclude('missing_settlement')
      continue
    }
    const rawTies = item?.settlement_tie_count
    const tieCount = typeof rawTies === 'number' || (typeof rawTies === 'string' && /^[1-4]$/.test(rawTies))
      ? Number(rawTies) : null
    if (!Number.isInteger(tieCount) || tieCount < 1 || rank + tieCount - 1 > 4) {
      exclude('missing_tie_settlement')
      continue
    }
    const sample = { tier: scene.tier, gameType: scene.gameType, rank }
    if (tieCount > 1) {
      sample.rankWeights = [1, 2, 3, 4].map((place) => place >= rank && place < rank + tieCount ? 1 / tieCount : 0)
    }
    samples.push(sample)
  }

  const exclusions = [...counts].map(([reason, count]) => ({ reason, label: EXCLUSION_LABELS[reason], count }))
  return {
    samples,
    excludedCount: exclusions.reduce((sum, row) => sum + row.count, 0),
    exclusions,
  }
}
