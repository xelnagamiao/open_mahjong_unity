/** Three-player tables leave the opposite side empty from every perspective. */
export function relativeSeat(seat, viewer, count = 4) {
  const offset = (seat - viewer + count) % count
  return count === 3 && offset === 2 ? 3 : offset
}

export function absoluteSeat(direction, viewer, count = 4) {
  if (count === 3 && direction === 2) return -1
  return ((count === 3 && direction === 3 ? 2 : direction) + viewer) % count
}
