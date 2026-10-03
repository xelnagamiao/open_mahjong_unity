import { mjaiToSalasasaRecord, salasasaToMjaiRecord } from './mjaiRiichi.js'

function assert(condition, message) {
  if (!condition) throw new Error(message)
}

const hands = [
  ['1m', '2m', '3m', '4m', '5m', '6m', '7m', '8m', '9m', '1p', '2p', '3p', '4p'],
  ['1s', '2s', '3s', '4s', '5s', '6s', '7s', '8s', '9s', 'E', 'S', 'W', 'N'],
  ['P', 'P', 'P', 'F', 'F', 'F', 'C', 'C', 'C', '1m', '2m', '3m', '4m'],
  ['4p', '5p', '6p', '7p', '8p', '9p', '1s', '2s', '3s', '4s', '5s', '6s', '7s']
]

const mjai = [
  { type: 'start_game', names: ['a', 'b', 'c', 'd'], aka_flag: true },
  {
    type: 'start_kyoku',
    bakaze: 'E',
    kyoku: 1,
    honba: 0,
    kyotaku: 0,
    oya: 0,
    scores: [25000, 25000, 25000, 25000],
    dora_marker: '4m',
    tehais: hands
  },
  { type: 'tsumo', actor: 0, pai: '4p' },
  { type: 'reach', actor: 0 },
  { type: 'dahai', actor: 0, pai: '4p', tsumogiri: true },
  { type: 'reach_accepted', actor: 0, deltas: [-1000, 0, 0, 0], scores: [24000, 25000, 25000, 25000] },
  { type: 'end_game', scores: [24000, 25000, 25000, 25000] }
]

const record = mjaiToSalasasaRecord(mjai)
const round = record.game_round.round_index_1
assert(round.p0_tiles.length === 13, '庄家开局摸牌不应追加到 p0_tiles')
assert(round.action_ticks[0][0] === 'd', '首个动作应是庄家开局摸牌')
assert(!round.action_ticks.some((tick) => tick[0] === 'dora'), '首张 dora 不应重复写入动作')
assert(round.action_ticks.some((tick) => tick[0] === 'c' && tick[3] === 'H'), 'reach 后首张弃牌应保留横置标记')
assert(round.current_round === 1, '东一局应使用 salasasa round 1')

const roundTrip = salasasaToMjaiRecord(record).events
assert(roundTrip.filter((event) => event.type === 'tsumo').length === 1, '往返不应重复庄家开局摸牌')
assert(roundTrip.filter((event) => event.type === 'dora').length === 0, '往返不应额外产生首张 dora 事件')
assert(roundTrip.find((event) => event.type === 'start_kyoku').dora_marker === '4m', '应保留首张 dora 指示牌')
assert(roundTrip.find((event) => event.type === 'start_kyoku').kyoku === 1, 'MJAI 东一局应使用 kyoku 1')
assert(roundTrip.some((event) => event.type === 'end_kyoku'), '每局应输出 end_kyoku')

const tile = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24]
const actorRecord = {
  game_title: { rule: 'riichi', starting_score: 25000 },
  game_round: {
    round_index_1: {
      current_round: 1,
      dealer_index: 0,
      p0_tiles: tile,
      p1_tiles: tile,
      p2_tiles: tile,
      p3_tiles: tile,
      tiles_list: [],
      action_ticks: [['d', 31], ['c', 11, 'T'], ['d', 32], ['c', 12, 'T'], ['d', 33], ['c', 13, 'T'], ['end']]
    }
  }
}
const actors = salasasaToMjaiRecord(actorRecord).events
  .filter((event) => event.type === 'dahai')
  .map((event) => event.actor)
assert(JSON.stringify(actors) === JSON.stringify([0, 1, 2]), '连续摸切应按座位顺序推进')

const hiddenInput = [
  { type: 'start_game', names: ['a', 'b', 'c', 'd'] },
  {
    type: 'start_kyoku',
    bakaze: 'E',
    kyoku: 1,
    oya: 0,
    scores: [25000, 25000, 25000, 25000],
    dora_marker: '1m',
    tehais: Array.from({ length: 4 }, () => Array(13).fill('?'))
  },
  { type: 'tsumo', actor: 0, pai: '?' },
  { type: 'dahai', actor: 0, pai: '1m', tsumogiri: false },
  { type: 'hora', actor: 0, target: 0, pai: '?', fan: 1, fu: 30, deltas: [1000, -1000, 0, 0], uradora_markers: ['2m'] },
  { type: 'end_kyoku' },
  { type: 'end_game' }
]
const hiddenRecord = mjaiToSalasasaRecord(hiddenInput)
const hiddenRound = hiddenRecord.game_round.round_index_1
assert(hiddenRound.action_ticks[0][0] === 'c', '隐藏摸牌不应写入无效牌 ID')
assert(hiddenRound.action_ticks.some((tick) => tick[0] === 'hu_riichi' && tick[8][0] === 12), '应保留 uradora_markers')
const hiddenOutput = salasasaToMjaiRecord(hiddenRecord).events
assert(hiddenOutput.some((event) => event.type === 'end_kyoku'), '隐藏牌谱仍应可反向转换')
assert(hiddenOutput.some((event) => event.type === 'hora' && event.uradora_markers?.[0] === '2m'), '应输出标准 uradora_markers')

console.log('mjai riichi converter tests passed')
