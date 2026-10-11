export const SICHUAN_DUPLICATE_RULES = Object.freeze([
  'sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate',
])
export const isSichuanDuplicate = subRule => SICHUAN_DUPLICATE_RULES.includes(subRule)
export const sichuanDuplicateLabel = subRule => subRule === 'sichuan/doubles_duplicate' ? '血战双打复式' : '血流双打复式'

// Pair by server-issued match id, never by name or adjacent room numbers.
export function groupEventRoomCards(cards) {
  const groups = []
  const paired = new Map()
  for (const card of cards) {
    const id = card.room.duplicate_match_id
    if (!id) {
      groups.push({ id: `room-${card.room.room_id}`, paired: false, cards: [card] })
      continue
    }
    if (!paired.has(id)) {
      const group = { id, paired: true, cards: [] }
      paired.set(id, group)
      groups.push(group)
    }
    paired.get(id).cards.push(card)
  }
  for (const group of paired.values()) group.cards.sort((a, b) =>
    Number(a.room.duplicate_table !== 'open') - Number(b.room.duplicate_table !== 'open'))
  return groups
}
