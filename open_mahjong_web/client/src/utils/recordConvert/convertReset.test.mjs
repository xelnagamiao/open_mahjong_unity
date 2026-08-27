import { salasasaToBotzone } from './botzoneGuobiao.js'
import { salasasaToTziakcha } from './tziakchaGuobiao.js'
import { insertOpeningReset } from './openingReset.js'

const hands = {
  p0_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24],
  p1_tiles: [25, 26, 27, 28, 29, 31, 32, 33, 34, 35, 36, 37],
  p2_tiles: [38, 39, 41, 42, 43, 44, 45, 46, 47, 11, 12, 13],
  p3_tiles: [14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25, 26],
}

const record = {
  game_title: {
    rule: 'guobiao',
    p0_uid: 1, p1_uid: 2, p2_uid: 3, p3_uid: 4,
    p0_name: 'a', p1_name: 'b', p2_name: 'c', p3_name: 'd',
  },
  game_round: {
    round_index_1: {
      round_index: 1,
      current_round: 1,
      seats: [0, 1, 2, 3],
      dealer_index: 0,
      start_player_index: 0,
      ...hands,
      tiles_list: [27, 28, 29],
      action_ticks: [
        ['bh', 51, 1, 'F'],
        ['bd', 27, 1],
        ['reset', 0],
        ['c', 11, 'F'],
        ['end'],
      ],
    },
  },
}

const botzone = salasasaToBotzone(record)
const botzoneText = JSON.stringify(botzone)
if (botzoneText.includes('reset')) {
  throw new Error('Botzone 导出不应包含 reset')
}

const tziakcha = await salasasaToTziakcha(record)
const tzText = JSON.stringify(tziakcha)
if (tzText.includes('"reset"')) {
  throw new Error('雀渣导出不应包含 reset')
}

const ticks = [['c', 11, 'F'], ['end']]
insertOpeningReset(ticks, 0)
if (ticks[0][0] !== 'reset' || ticks[1][0] !== 'c') {
  throw new Error('转换后插入 reset 位置不对')
}

console.log('converter roundtrip tests passed')
