/**
 * Guobiao (Chinese Official) tingpai check.
 * Ported from Unity GBtingpai.cs (Chinese_Tingpai_Check) for client tip parity.
 */

class PlayerTilesTingpai {
  hand_tiles: number[]
  combination_list: string[]
  complete_step: number

  constructor(tiles_list: number[], combination_list: string[], complete_step: number) {
    this.hand_tiles = [...tiles_list].sort((a, b) => a - b)
    this.combination_list = [...combination_list]
    this.complete_step = complete_step
  }

  deepCopy(): PlayerTilesTingpai {
    return new PlayerTilesTingpai(
      [...this.hand_tiles],
      [...this.combination_list],
      this.complete_step,
    )
  }
}

function removeFirst(arr: number[], value: number): boolean {
  const i = arr.indexOf(value)
  if (i < 0) return false
  arr.splice(i, 1)
  return true
}

class Chinese_Tingpai_Check {
  private static readonly yaojiu = new Set([
    11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47,
  ])
  private static readonly zipai = new Set([41, 42, 43, 44, 45, 46, 47])
  private static readonly hua_tiles = new Set([51, 52, 53, 54, 55, 56, 57, 58])

  private waiting_tiles: Set<number>
  private temp_waiting_tiles: Set<number>
  private debug: boolean

  constructor(debug = false) {
    this.waiting_tiles = new Set()
    this.temp_waiting_tiles = new Set()
    this.debug = debug
  }

  private debugPrint(message: string, ...args: unknown[]): void {
    if (!this.debug) return
    // eslint-disable-next-line no-console
    console.log(message, ...args)
  }

  checkWaitingTiles(player_tiles: PlayerTilesTingpai): Set<number> {
    this.waiting_tiles.clear()
    this.temp_waiting_tiles.clear()

    if (player_tiles.hand_tiles.length === 13) {
      this.GS_check(player_tiles.hand_tiles)
      this.QD_check(player_tiles.hand_tiles)
    }

    if (this.QBK_check(player_tiles)) {
      return this.waiting_tiles
    }

    this.normal_check(player_tiles)
    this.debugPrint('等待牌:', [...this.waiting_tiles].join(', '))
    return this.waiting_tiles
  }

  private GS_check(hand_tiles: number[]): void {
    const GS_step_set = new Set<number>()
    let GS_allowed = true
    for (const tile_id of hand_tiles) {
      if (Chinese_Tingpai_Check.yaojiu.has(tile_id)) {
        GS_step_set.add(tile_id)
      } else {
        GS_allowed = false
      }
    }
    if (!GS_allowed) return
    if (GS_step_set.size === 12) {
      for (const i of Chinese_Tingpai_Check.yaojiu) {
        if (!hand_tiles.includes(i)) this.waiting_tiles.add(i)
      }
    } else if (GS_step_set.size === 13) {
      for (const i of Chinese_Tingpai_Check.yaojiu) this.waiting_tiles.add(i)
    }
  }

  private QD_check(hand_tiles: number[]): void {
    const tile_counts = new Map<number, number>()
    for (const tile_id of hand_tiles) {
      tile_counts.set(tile_id, (tile_counts.get(tile_id) ?? 0) + 1)
    }

    let single = 0
    let waiting_tile: number | null = null
    for (const [tile_id, count] of tile_counts) {
      if (count === 1 || count === 3) {
        single++
        waiting_tile = tile_id
      } else if (single >= 2) {
        return
      }
    }
    if (single === 1 && waiting_tile != null) {
      this.waiting_tiles.add(waiting_tile)
    }
  }

  private QBK_check(player_tiles: PlayerTilesTingpai): boolean {
    const hand_kind_set = new Set(player_tiles.hand_tiles).size
    if (hand_kind_set >= 13) {
      const QBK_case_list: Array<Set<number>> = [
        new Set([11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47]),
        new Set([11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47]),
        new Set([21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47]),
        new Set([21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47]),
        new Set([31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47]),
        new Set([31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47]),
      ]
      for (const case_set of QBK_case_list) {
        const QBK_set = new Set<number>()
        for (const i of player_tiles.hand_tiles) {
          if (case_set.has(i)) QBK_set.add(i)
        }
        if (QBK_set.size === 13) {
          for (const i of case_set) {
            if (!player_tiles.hand_tiles.includes(i)) this.waiting_tiles.add(i)
          }
          return true
        }
      }
    } else if (hand_kind_set >= 8) {
      const ZHL_case_list: Array<Set<number>> = [
        new Set([11, 14, 17, 22, 25, 28, 33, 36, 39]),
        new Set([11, 14, 17, 32, 35, 38, 23, 26, 29]),
        new Set([21, 24, 27, 12, 15, 18, 33, 36, 39]),
        new Set([21, 24, 27, 32, 35, 38, 13, 16, 19]),
        new Set([31, 34, 37, 22, 25, 28, 13, 16, 19]),
        new Set([31, 34, 37, 12, 15, 18, 23, 26, 29]),
      ]
      for (const case_set of ZHL_case_list) {
        const ZHL_set = new Set<number>()
        for (const i of player_tiles.hand_tiles) {
          if (case_set.has(i)) ZHL_set.add(i)
        }
        if (ZHL_set.size === 9) {
          player_tiles.complete_step += 9
          player_tiles.combination_list.push(`z${[...case_set].join(',')}`)
          for (const i of case_set) removeFirst(player_tiles.hand_tiles, i)
          return false
        } else if (ZHL_set.size === 8) {
          player_tiles.complete_step += 9
          player_tiles.combination_list.push(`z${[...case_set].join(',')}`)
          for (const i of case_set) {
            if (player_tiles.hand_tiles.includes(i)) {
              removeFirst(player_tiles.hand_tiles, i)
            } else {
              this.temp_waiting_tiles.add(i)
            }
          }
          return false
        }
      }
    }
    return false
  }

  private normal_check(player_tiles: PlayerTilesTingpai): void {
    if (!this.normal_check_block(player_tiles)) return

    this.debugPrint('手牌:', player_tiles.hand_tiles.join(', '))
    const all_list = this.normal_check_traverse_quetou(player_tiles)
    const end_list: PlayerTilesTingpai[] = []
    let count_count = 0
    while (all_list.length > 0) {
      count_count++
      const temp_list = all_list.pop()!
      this.normal_check_traverse_kezi(temp_list, all_list)
      this.normal_check_traverse_dazi(temp_list, all_list)
      if (temp_list.complete_step >= 11) end_list.push(temp_list)
    }
    this.debugPrint('计算次数：', count_count)

    const waiting_tiles_list: number[] = []
    for (const i of end_list) {
      if (i.combination_list.some((comb) => comb.includes('z'))) {
        if (
          i.complete_step === 14 &&
          i.hand_tiles.length === 0 &&
          this.temp_waiting_tiles.size > 0
        ) {
          this.waiting_tiles = new Set(this.temp_waiting_tiles)
          return
        }
        if (this.temp_waiting_tiles.size > 0) continue
      }

      if (i.hand_tiles.length === 1) {
        waiting_tiles_list.push(i.hand_tiles[0])
      } else if (i.hand_tiles.length === 2) {
        const tile1 = i.hand_tiles[0]
        const tile2 = i.hand_tiles[1]
        if (tile1 === tile2) {
          waiting_tiles_list.push(tile1)
        } else if (tile1 <= 39 && tile2 <= 39) {
          if (tile1 === tile2 - 1) {
            const suit1 = Math.floor(tile1 / 10)
            const suit2 = Math.floor(tile2 / 10)
            if (suit1 === suit2) {
              waiting_tiles_list.push(tile1 - 1)
              waiting_tiles_list.push(tile1 + 2)
            }
          } else if (tile1 === tile2 - 2) {
            const suit1 = Math.floor(tile1 / 10)
            const suit2 = Math.floor(tile2 / 10)
            if (suit1 === suit2) waiting_tiles_list.push(tile1 + 1)
          }
        }
      }
    }
    for (const t of waiting_tiles_list) this.waiting_tiles.add(t)
  }

  private normal_check_block(player_tiles: PlayerTilesTingpai): boolean {
    if (player_tiles.hand_tiles.length === 0) return false
    let block_count = player_tiles.combination_list.length
    let tile_id_pointer = player_tiles.hand_tiles[0]
    for (const tile_id of player_tiles.hand_tiles) {
      if (!(tile_id === tile_id_pointer || tile_id === tile_id_pointer + 1)) {
        block_count++
      }
      tile_id_pointer = tile_id
    }
    return block_count <= 6
  }

  private normal_check_traverse_quetou(player_tiles: PlayerTilesTingpai): PlayerTilesTingpai[] {
    const all_list: PlayerTilesTingpai[] = []
    let quetou_id_pointer = 0
    for (const tile_id of player_tiles.hand_tiles) {
      const count = player_tiles.hand_tiles.filter((x) => x === tile_id).length
      if (count >= 2 && tile_id !== quetou_id_pointer) {
        const temp_list = player_tiles.deepCopy()
        removeFirst(temp_list.hand_tiles, tile_id)
        removeFirst(temp_list.hand_tiles, tile_id)
        temp_list.complete_step += 2
        temp_list.combination_list.push(`q${tile_id}`)
        all_list.push(temp_list)
        quetou_id_pointer = tile_id
      }
    }
    all_list.push(player_tiles.deepCopy())
    return all_list
  }

  private normal_check_traverse_kezi(
    player_tiles: PlayerTilesTingpai,
    all_list: PlayerTilesTingpai[],
  ): void {
    let same_tile_id = 0
    for (const tile_id of player_tiles.hand_tiles) {
      const count = player_tiles.hand_tiles.filter((x) => x === tile_id).length
      if (count >= 3 && tile_id !== same_tile_id) {
        const temp_list = player_tiles.deepCopy()
        removeFirst(temp_list.hand_tiles, tile_id)
        removeFirst(temp_list.hand_tiles, tile_id)
        removeFirst(temp_list.hand_tiles, tile_id)
        temp_list.complete_step += 3
        temp_list.combination_list.push(`k${tile_id}`)
        all_list.push(temp_list)
        same_tile_id = tile_id
      }
    }
  }

  private normal_check_traverse_dazi(
    player_tiles: PlayerTilesTingpai,
    all_list: PlayerTilesTingpai[],
  ): void {
    let same_tile_id = 0
    for (const tile_id of player_tiles.hand_tiles) {
      if (tile_id <= 37 && tile_id % 10 <= 7) {
        if (
          player_tiles.hand_tiles.includes(tile_id + 1) &&
          player_tiles.hand_tiles.includes(tile_id + 2) &&
          tile_id !== same_tile_id
        ) {
          const temp_list = player_tiles.deepCopy()
          removeFirst(temp_list.hand_tiles, tile_id)
          removeFirst(temp_list.hand_tiles, tile_id + 1)
          removeFirst(temp_list.hand_tiles, tile_id + 2)
          temp_list.complete_step += 3
          temp_list.combination_list.push(`s${tile_id + 1}`)
          all_list.push(temp_list)
          same_tile_id = tile_id
        }
      }
    }
  }

  tingpaiCheck(hand_tile_list: number[], combination_list: string[]): Set<number> {
    if (hand_tile_list.some((tile) => Chinese_Tingpai_Check.hua_tiles.has(tile))) {
      return new Set()
    }
    const test_tiles = new PlayerTilesTingpai(
      hand_tile_list,
      combination_list,
      combination_list.length * 3,
    )
    this.checkWaitingTiles(test_tiles)
    const exclude_set = new Set([10, 20, 30, 40])
    for (const x of exclude_set) this.waiting_tiles.delete(x)
    return new Set(this.waiting_tiles)
  }
}

/** Sorted unique waiting tiles (tingpai). */
export function tingpaiCheck(
  hand: number[],
  combinations: string[],
  debug = false,
): number[] {
  const checker = new Chinese_Tingpai_Check(debug)
  const waiting = checker.tingpaiCheck(hand, combinations)
  return [...waiting].sort((a, b) => a - b)
}
