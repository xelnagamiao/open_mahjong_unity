export function roomSeatItems(room) {
  const capacity = room.duplicate_lobby ? 8 : Math.min(8, Math.max(1, Number(room.max_player) || 4))
  return Array.from({ length: capacity }, (_, index) => {
    const player = (room.players || []).find(player => !player.waiting && player.seat_index === index)
    return { index, player, label: room.duplicate_lobby ? `${index < 4 ? '闭' : '开'}${'东南西北'[index % 4]}` : `${index + 1}` }
  })
}

export function roomStateLabel(room) {
  if (room.event_seating_pending) return '组桌中'
  if (room.is_game_running) return '对局中'
  return ({ ready: '等待对室', interrupted: '待赛务处理', completed: '已结束', cancelled: '已结束' })[room.duplicate_status] || '等待中'
}
