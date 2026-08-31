export const BAR_UNIT_STYLES = {
  100: { height: 100, width: 14, color: '#409eff', compactHeight: 22, compactWidth: 7 },
  10: { height: 75, width: 10, color: '#67c23a', compactHeight: 17, compactWidth: 5 },
  1: { height: 50, width: 6, color: '#e6a23c', compactHeight: 11, compactWidth: 3 },
}

export function countToDenoms(n) {
  const total = Math.max(0, Math.floor(Number(n) || 0))
  return {
    total,
    hundreds: Math.floor(total / 100),
    tens: Math.floor((total % 100) / 10),
    ones: total % 10,
  }
}

function pushCountBars(bars, count, size) {
  for (let i = 0; i < count; i += 1) {
      bars.push({
        key: `${size}-${i}`,
        size,
        ids: [],
        missingIds: [],
        downloadedCount: size,
        fillRatio: 1,
        downloaded: true,
      })
  }
}

/** 仅数量、全部视为实色（本账号总量小条） */
export function barsFromCount(n) {
  const { hundreds, tens, ones } = countToDenoms(n)
  const bars = []
  pushCountBars(bars, hundreds, 100)
  pushCountBars(bars, tens, 10)
  pushCountBars(bars, ones, 1)
  return bars
}

/**
 * 按筛选后的牌谱列表切成 100/10/1 块。
 * @param {Array<{ game_id: string, rank?: number|null }>} items
 * @param {Set<string>|string[]} localIds
 */
export function barsFromItems(items, localIds) {
  const list = Array.isArray(items) ? items : []
  const local = localIds instanceof Set ? localIds : new Set(localIds || [])
  const { hundreds, tens, ones } = countToDenoms(list.length)
  const bars = []
  let offset = 0

  const pushSized = (count, size) => {
    for (let i = 0; i < count; i += 1) {
      const slice = list.slice(offset, offset + size)
      offset += size
      const ids = slice.map((row) => String(row.game_id))
      const missingIds = ids.filter((id) => !local.has(id))
      const downloadedCount = ids.length - missingIds.length
      bars.push({
        key: `${size}-${offset}`,
        size,
        ids,
        missingIds,
        downloadedCount,
        fillRatio: ids.length ? downloadedCount / ids.length : 0,
        downloaded: ids.length > 0 && missingIds.length === 0,
      })
    }
  }

  pushSized(hundreds, 100)
  pushSized(tens, 10)
  pushSized(ones, 1)
  return bars
}
