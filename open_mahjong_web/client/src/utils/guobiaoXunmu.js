/**
 * 国标巡目按庄家巡计算（当局 player_index 0 为庄），与对局进程 player_index_go_to 一致。
 * 指针回绕且庄家牌河当前非空才 +1；庄家弃牌被鸣走后河空，不加巡。
 */

export function createGuobiaoXunmuClock(startSeat = 0) {
  const seat = ((Number(startSeat) % 4) + 4) % 4
  return {
    currentSeat: seat,
    history: [],
    xunmu: 1,
    eastRiver: 0,
  }
}

export function guobiaoXunmuGoTo(clock, seat) {
  const next = ((Number(seat) % 4) + 4) % 4
  if (
    clock.history.length
    && next !== clock.history[clock.history.length - 1]
    && next < clock.history[clock.history.length - 1]
    && clock.eastRiver > 0
  ) {
    clock.xunmu += 1
  }
  clock.history.push(next)
  clock.currentSeat = next
}

export function guobiaoXunmuOnCut(clock) {
  if (clock.currentSeat === 0) clock.eastRiver += 1
}

export function guobiaoXunmuOnClaim(clock, seat) {
  if (clock.currentSeat === 0 && clock.eastRiver > 0) clock.eastRiver -= 1
  guobiaoXunmuGoTo(clock, seat)
}
