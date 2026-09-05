import { parseHuTick, analyzeRecords } from './recordAnalyzer.js'
import { GUOBIAO_FAN_KEYS, listGuobiaoFanEntries } from '../constants/guobiaoFanDict.js'
import {
  analyzeRecordsAdvanced,
  classifyWinYaku,
  filterWinsByFan,
  shareNodeBeforeWin,
  sortWinsByTimeDesc,
  xunEndBucket,
  COUFAN_KEY,
  MENDUANPING_KEY,
} from './recordAdvancedAnalyzer.js'
import {
  parseRecordShareInput,
  sharePathForWin,
  sharePathForWin3d,
  replaySharePath,
} from './recordShareLink.js'
import { barsFromItems } from './recordBarUnits.js'
import { zipStoreFiles } from './zipStore.js'

function assert(cond, msg) {
  if (!cond) throw new Error(msg)
}

const cou = classifyWinYaku(['喜相逢', '自摸', '花牌*2', '无字'])
assert(cou.isCoufan, '1-2 fan mix without 门断平 should be 凑番')
assert(!cou.isMenduanping, '凑番 is not 门断平')
assert(cou.mainFans.length === 0, '凑番 has no main fan')

const mdp = classifyWinYaku(['门前清', '平和', '自摸', '断幺', '花牌*2'])
assert(mdp.isMenduanping, '门前清+断幺+平和 should be 门断平')
assert(!mdp.isCoufan, '门断平 is not 凑番')

const mdpJiu = classifyWinYaku(['门前清', '平和', '断幺九'])
assert(mdpJiu.isMenduanping, '断幺九 also counts as 断幺')

const noPing = classifyWinYaku(['门前清', '断幺', '自摸'])
assert(noPing.isCoufan && !noPing.isMenduanping, '门前清+断幺 without 平和 is 凑番')

const qing = classifyWinYaku(['清一色', '自摸', '喜相逢*1'])
assert(!qing.isCoufan, '清一色 is not 凑番')
assert(qing.mainFans.some((x) => x.name === '清一色'), '清一色 is 主番')

const wu = classifyWinYaku(['无番和'])
assert(!wu.isCoufan && wu.mainFans[0].name === '无番和', '无番和 is 8-fan 主番')

const mingAnGang = classifyWinYaku(['明暗杠', '自摸'])
assert(!mingAnGang.isCoufan, '明暗杠 5 fan is not 凑番')
assert(mingAnGang.mainFans.some((x) => x.name === '明暗杠'), '明暗杠 is 主番')

const hu = parseHuTick(['hu_self', 0, 24, ['清一色'], [24, -8, -8, -8], 19])
assert(hu.hepaiTile === 19, `hepaiTile expected 19 got ${hu.hepaiTile}`)
assert(hu.fanScore === 24, 'fanScore')

const fuStyle = parseHuTick(['hu_self', 0, 8, ['平胡'], [8, -8, 0, 0], 20, [], 35])
assert(fuStyle.hepaiTile === 35, `fu-style hepai expected 35 got ${fuStyle.hepaiTile}`)

const record = {
  game_id: 'gid-test',
  game_title: { p0_uid: 1, p1_uid: 2, p2_uid: 3, p3_uid: 4, rule: 'guobiao' },
  game_round: {
    round_index_1: {
      round_index: 1,
      current_round: 1,
      seats: [0, 1, 2, 3],
      start_player_index: 0,
      p0_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24],
      p1_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23],
      p2_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23],
      p3_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23],
      action_ticks: [
        ['c', 19, 'F'],
        ['hu_first', 1, 24, ['清一色', '自摸'], [-24, 24, 0, 0], 19],
        ['end'],
      ],
    },
  },
}

const winner = await analyzeRecordsAdvanced([{ game_id: 'gid-test', record }], 2, { tingpai: false })
assert(winner.win_count === 1, `win_count ${winner.win_count}`)
assert(winner.wins[0].isCoufan === false, 'winner not 凑番')
assert(winner.wins[0].round === 1, 'round 1')
assert(winner.wins[0].node === 1, `node ${winner.wins[0].node}`)
assert(winner.wins[0].shareNode === 0, `shareNode ${winner.wins[0].shareNode}`)
assert(winner.wins[0].winTile === 19, `winTile ${winner.wins[0].winTile}`)
assert(winner.wins[0].xunmu === 1, `xunmu ${winner.wins[0].xunmu}`)
assert(sharePathForWin(winner.wins[0]) === '/2d/record/gid-test?round=1&node=0', sharePathForWin(winner.wins[0]))
assert(sharePathForWin3d(winner.wins[0]) === '/game-unity?recordId=gid-test&round=1&node=0', sharePathForWin3d(winner.wins[0]))
assert(filterWinsByFan(winner.wins, 'qingyise').length === 1, 'qingyise filter')
assert(winner.wins[0].yakuText === '清一色 ×1（24 番）、自摸 ×1（1 番）', winner.wins[0].yakuText)
assert(filterWinsByFan(winner.wins, COUFAN_KEY).length === 0, 'coufan filter empty')
assert(winner.fulu_at_win[0] === 1, 'menqing win')

assert(GUOBIAO_FAN_KEYS.length === 82, `guobiao fans ${GUOBIAO_FAN_KEYS.length}`)
const fanList = listGuobiaoFanEntries()
assert(fanList.length === 82, `display fans ${fanList.length}`)
const iSigang = fanList.findIndex((item) => item.key === 'sigang')
const iLian = fanList.findIndex((item) => item.key === 'lianqidui')
const iSangang = fanList.findIndex((item) => item.key === 'sangang')
assert(iSigang < iLian && iLian < iSangang, '三杠 follows 88-fan block in official table order')
assert(fanList[iSangang].value === 32, '三杠 is 32 fan')
assert(fanList[iSangang].label === '三杠（32 番）', fanList[iSangang].label)
assert(fanList[fanList.length - 1].key === 'mingangang', '明暗杠 stays last')

const std = analyzeRecords([{ game_id: 'gid-test', record }], 2)
assert(std.fan_stats.qingyise === 1, 'qingyise fan count')
assert(std.fan_stats.zimo === 1, 'zimo fan count')
assert(!std.fan_stats.dasixi, 'unrelated fan stays empty')

const dealer = await analyzeRecordsAdvanced([{ game_id: 'gid-test', record }], 1, { tingpai: false })
assert(dealer.deal_in_count === 1, `deal_in ${dealer.deal_in_count}`)
assert(dealer.total_deal_in_fan === 24, 'deal in fan')
assert(dealer.win_count === 0, 'dealer did not win')
assert(xunEndBucket(1) === 'le5' && xunEndBucket(5) === 'le5', 'bucket <=5')
assert(xunEndBucket(6) === 'le10' && xunEndBucket(10) === 'le10', 'bucket 6-10')
assert(xunEndBucket(11) === 'le15' && xunEndBucket(15) === 'le15', 'bucket 11-15')
assert(xunEndBucket(16) === 'gt15', 'bucket >=16')
assert(winner.xun_end_count.le5 === 1 && winner.xun_end_score.le5 === 24, 'winner early score')
assert(dealer.xun_end_count.le5 === 1 && dealer.xun_end_score.le5 === -24, 'dealer early score')

const couRecord = {
  game_id: 'gid-cou',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      action_ticks: [
        ['c', 19, 'F'],
        ['d', 25],
        ['c', 25, 'T'],
        ['hu_first', 0, 4, ['喜相逢', '自摸', '无字', '花牌'], [8, -8, 0, 0], 25],
        ['end'],
      ],
    },
  },
}
const couWin = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-cou', record: couRecord, created_at: '2026-08-01T12:00:00Z' }],
  1,
  { tingpai: false },
)
assert(couWin.win_count === 1, 'cou win')
assert(couWin.wins[0].isCoufan, 'should be 凑番')
assert(!couWin.wins[0].isMenduanping, 'should not be 门断平')
assert(couWin.coufan_count === 1, 'coufan_count')
assert(couWin.wins[0].created_at === '2026-08-01T12:00:00Z', 'created_at attached')
assert(filterWinsByFan(couWin.wins, COUFAN_KEY).length === 1, 'coufan query')

const mdpRecord = {
  game_id: 'gid-mdp',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      action_ticks: [
        ['c', 19, 'F'],
        ['d', 25],
        ['c', 25, 'T'],
        ['hu_first', 0, 8, ['门前清', '平和', '自摸', '断幺'], [8, -8, 0, 0], 25],
        ['end'],
      ],
    },
  },
}
const mdpWin = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-mdp', record: mdpRecord, created_at: '2026-08-31T12:00:00Z' }],
  1,
  { tingpai: false },
)
assert(mdpWin.wins[0].isMenduanping, 'record 门断平')
assert(!mdpWin.wins[0].isCoufan, 'record 门断平 is not 凑番')
assert(mdpWin.menduanping_count === 1, 'menduanping_count')
assert(mdpWin.coufan_count === 0, '门断平 not counted as 凑番')
assert(filterWinsByFan(mdpWin.wins, MENDUANPING_KEY).length === 1, '门断平 query')
assert(filterWinsByFan(mdpWin.wins, COUFAN_KEY).length === 0, '凑番 query excludes 门断平')

assert(winner.first_place_count === 1, 'advanced includes standard rank stats')
assert(winner.fan_stats.qingyise === 1, 'advanced includes standard fan_stats')
assert(winner.second_place_count === 0, 'winner is first')

const tsumoRecord = {
  game_id: 'gid-tsumo',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      action_ticks: [
        ['hu_self', 1, 24, ['清一色'], [-8, 24, -8, -8], 19],
        ['end'],
      ],
    },
  },
}
const paidTsumo = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-tsumo', record: tsumoRecord }],
  1,
  { tingpai: false },
)
assert(paidTsumo.tsumo_against_count === 1, '被摸 once')
assert(paidTsumo.win_count === 0, 'payer did not win')
const tsumoWinner = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-tsumo', record: tsumoRecord }],
  2,
  { tingpai: false },
)
assert(tsumoWinner.tsumo_against_count === 0, 'winner is not 被摸')
assert(tsumoWinner.self_draw_count === 1, 'winner self-draw')

const liujuRecord = {
  game_id: 'gid-liuju',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      action_ticks: [['liuju'], ['end']],
    },
  },
}
const liujuStats = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-liuju', record: liujuRecord }],
  1,
  { tingpai: false },
)
assert(liujuStats.liuju_count === 1, '流局 counted')
assert(liujuStats.total_rounds === 1, 'one round')
assert(liujuStats.win_count === 0, '流局 no win')

const tenpaiDealRecord = {
  game_id: 'gid-deal-tenpai',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      p0_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 21, 21, 22, 31],
      action_ticks: [
        ['c', 31, 'T'],
        ['hu_first', 1, 8, ['平和'], [-8, 8, 0, 0], 31],
        ['end'],
      ],
    },
  },
}
const fakeTingpai = (hand) => (hand.includes(22) ? [22] : [])
const dealTenpai = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-deal-tenpai', record: tenpaiDealRecord }],
  1,
  { tingpaiCheck: fakeTingpai },
)
assert(dealTenpai.deal_in_count === 1, 'deal in once')
assert(dealTenpai.deal_in_tenpai_count === 1, '点炮时听牌')
assert(dealTenpai.deal_ins[0].tenpai === true, 'deal-in marked tenpai')
assert(dealTenpai.tenpai_round_count === 1, '听过一次即计听牌局')

const dealNoten = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-deal-tenpai', record: tenpaiDealRecord }],
  1,
  { tingpaiCheck: () => [] },
)
assert(dealNoten.deal_in_count === 1, 'still deal in')
assert(dealNoten.deal_in_tenpai_count === 0, '点炮时没听')

function ticksUntilXun(targetXun, huTick) {
  const ticks = [['c', 11, 'F']]
  const wraps = Math.max(0, targetXun - 1)
  for (let i = 0; i < wraps * 4; i += 1) {
    ticks.push(['d', 12], ['c', 12, 'T'])
  }
  ticks.push(huTick, ['end'])
  return ticks
}

const xunScoreRecord = {
  game_id: 'gid-xun-score',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      action_ticks: ticksUntilXun(5, ['hu_self', 0, 8, ['自摸'], [8, -8, 0, 0], 12]),
    },
    round_index_2: {
      ...record.game_round.round_index_1,
      round_index: 2,
      current_round: 2,
      action_ticks: ticksUntilXun(8, ['hu_first', 1, 16, ['平和'], [-16, 16, 0, 0], 12]),
    },
    round_index_3: {
      ...record.game_round.round_index_1,
      round_index: 3,
      current_round: 3,
      action_ticks: ticksUntilXun(12, ['hu_self', 0, 4, ['自摸'], [4, -4, 0, 0], 12]),
    },
    round_index_4: {
      ...record.game_round.round_index_1,
      round_index: 4,
      current_round: 4,
      action_ticks: ticksUntilXun(16, ['liuju']),
    },
  },
}
const xunScore = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-xun-score', record: xunScoreRecord }],
  1,
  { tingpai: false },
)
assert(xunScore.xun_end_count.le5 === 1 && xunScore.xun_end_score.le5 === 8, `le5 ${xunScore.xun_end_score.le5}`)
assert(xunScore.xun_end_count.le10 === 1 && xunScore.xun_end_score.le10 === -16, `le10 ${xunScore.xun_end_score.le10}`)
assert(xunScore.xun_end_count.le15 === 1 && xunScore.xun_end_score.le15 === 4, `le15 ${xunScore.xun_end_score.le15}`)
assert(xunScore.xun_end_count.gt15 === 1 && xunScore.xun_end_score.gt15 === 0, `gt15 ${xunScore.xun_end_score.gt15}`)

const funRecord = {
  game_id: 'gid-fun',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      start_player_index: 0,
      p0_tiles: [51, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23],
      action_ticks: [
        ['bh', 51, 0, 'F'],
        ['bd', 24, 0],
        ['reset', 0],
        ['c', 24, 'T'],
        ['end'],
      ],
    },
  },
}
let openingHand = null
const funStats = await analyzeRecordsAdvanced(
  [{ game_id: 'gid-fun', record: funRecord }],
  1,
  {
    tingpai: false,
    shantenOf: (hand) => {
      openingHand = hand
      return 3
    },
  },
)
assert(funStats.deal_draw_tile_count === 14, `deal+draw ${funStats.deal_draw_tile_count}`)
assert(funStats.flower_tile_count === 1, `flowers ${funStats.flower_tile_count}`)
assert(funStats.opening_shanten_count === 1 && funStats.opening_shanten_total === 3, 'dog power')
assert(Array.isArray(openingHand) && openingHand.length === 13, `opening hand ${openingHand?.length}`)
assert(!openingHand.includes(51), 'opening hand dropped flower')
assert(openingHand.includes(24), 'opening hand has replacement')

const sorted = sortWinsByTimeDesc([...mdpWin.wins, ...couWin.wins])
assert(sorted[0].game_id === 'gid-mdp', 'newer game first')
assert(sorted[1].game_id === 'gid-cou', 'older game second')

const items = Array.from({ length: 100 }, (_, i) => ({ game_id: `g${i}` }))
const local = new Set(items.slice(0, 50).map((row) => row.game_id))
const bars = barsFromItems(items, local)
assert(bars.length === 1 && bars[0].size === 100, 'one hundred bar')
assert(bars[0].downloadedCount === 50, 'downloadedCount 50')
assert(Math.abs(bars[0].fillRatio - 0.5) < 1e-9, `fillRatio ${bars[0].fillRatio}`)
assert(bars[0].downloaded === false, 'partial bar not fully downloaded')

assert(shareNodeBeforeWin([
  ['c', 19, 'F'],
  ['ask_other', { 1: ['hu'] }, 19],
  ['hu_first', 1, 24, ['清一色'], [-24, 24, 0, 0], 19],
], 2) === 0, 'ron share skips ask_other and lands on discard')
assert(shareNodeBeforeWin([
  ['d', 25],
  ['ask_hand', []],
  ['hu_self', 0, 8, ['自摸'], [8, -8, 0, 0], 25],
], 2) === 0, 'tsumo share lands on draw')
assert(shareNodeBeforeWin([
  ['jg', 19],
  ['ask_other', { 1: ['hu'] }, 19],
  ['hu_first', 1, 8, ['抢杠和'], [-8, 8, 0, 0], 19],
], 2) === 0, 'chankan share lands on added kong')
assert(shareNodeBeforeWin([
  ['c', 19, 'F'],
  ['hu_first', 1, 8, ['错和'], [-8, 8, 0, 0], 19],
  ['hu_first', 2, 8, ['平和'], [0, -8, 8, 0], 19],
], 2) === 0, 'share skips 错和 and still lands on discard')

assert(parseRecordShareInput('Ab12Cd34').gameId === 'Ab12Cd34', 'bare id')
assert(parseRecordShareInput('/2d/record/Ab12Cd34?round=2&node=40').round === 2, '2d round')
assert(parseRecordShareInput('/2d/record/Ab12Cd34?round=2&node=40').node === 40, '2d node')
assert(parseRecordShareInput('https://salasasa.cn/game-unity?recordId=Ab12Cd34&round=3&node=9').gameId === 'Ab12Cd34', '3d id')
assert(parseRecordShareInput('https://salasasa.cn/game-unity?recordId=Ab12Cd34&round=3&node=9').round === 3, '3d round')
assert(parseRecordShareInput('https://salasasa.cn/game-unity?recordId=Ab12Cd34&round=3&node=9').node === 9, '3d node')
assert(parseRecordShareInput('not a record') == null, 'reject junk')
assert(replaySharePath('2d', 'Ab12Cd34') === '/2d/record/Ab12Cd34', 'replay 2d')
assert(replaySharePath('2d-node', 'Ab12Cd34', 2, 40) === '/2d/record/Ab12Cd34?round=2&node=40', 'replay 2d node')
assert(replaySharePath('3d', 'Ab12Cd34') === '/game-unity?recordId=Ab12Cd34', 'replay 3d')
assert(replaySharePath('3d-node', 'Ab12Cd34', 3, 9) === '/game-unity?recordId=Ab12Cd34&round=3&node=9', 'replay 3d node')

const claimedDealer = {
  game_id: 'gid-xun',
  game_title: record.game_title,
  game_round: {
    round_index_1: {
      ...record.game_round.round_index_1,
      p0_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25],
      action_ticks: [
        ['reset', 0],
        ['c', 11, 'F'],
        ['p', 11, 2],
        ['c', 12, 'F'],
        ['d', 13],
        ['c', 13, 'T'],
        ['d', 14],
        ['c', 14, 'T'],
        ['d', 14],
        ['hu_self', 0, 8, ['平胡'], [8, 0, 0, 0]],
        ['end'],
      ],
    },
  },
}
const claimedStats = await analyzeRecordsAdvanced([{ game_id: 'gid-xun', record: claimedDealer }], 1, { tingpai: false })
assert(claimedStats.wins[0].xunmu === 1, `claimed dealer first discard stays 巡1, got ${claimedStats.wins[0].xunmu}`)
assert(claimedStats.total_win_turn === 1, `total_win_turn ${claimedStats.total_win_turn}`)

const blob = zipStoreFiles([{ name: 'a.json', data: '{"ok":1}' }])
const buf = new Uint8Array(await blob.arrayBuffer())
assert(buf[0] === 0x50 && buf[1] === 0x4b, 'zip starts with PK')
assert(blob.size > 30, 'zip has payload')

console.log('record advanced analyzer checks ok')
