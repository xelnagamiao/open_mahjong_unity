/**
 * 雀渣 ↔ salasasa（国标）
 * 解码对齐 tziakcha.net/record 内联脚本与 other/tziakcha_to_salasasa/convert.py
 */
import {
  seatsFromRoundIndex,
  tzToSalasasa,
  salasasaToTz,
  huClassFromRelative,
  discarderFromHuClass,
  parseJsonInput
} from './tiles.js'

export const FAN_NAMES = [
  '无', '大四喜', '大三元', '绿一色', '九莲宝灯', '四杠', '连七对', '十三幺', '清幺九', '小四喜',
  '小三元', '字一色', '四暗刻', '一色双龙会', '一色四同顺', '一色四节高', '一色四步高', '一色四连环', '三杠', '混幺九',
  '七对', '七星不靠', '全双刻', '清一色', '一色三同顺', '一色三节高', '全大', '全中', '全小', '清龙',
  '三色双龙会', '一色三步高', '一色三连环', '全带五', '三同刻', '三暗刻', '全不靠', '组合龙', '大于五', '小于五',
  '三风刻', '花龙', '推不倒', '三色三同顺', '三色三节高', '无番和', '妙手回春', '海底捞月', '杠上开花', '抢杠和',
  '碰碰和', '混一色', '三色三步高', '五门齐', '全求人', '双暗杠', '双箭刻', '全带幺', '不求人', '双明杠',
  '和绝张', '箭刻', '圈风刻', '门风刻', '门前清', '平和', '四归一', '双同刻', '双暗刻', '暗杠',
  '断幺', '一般高', '喜相逢', '连六', '老少副', '幺九刻', '明杠', '缺一门', '无字', '边张',
  '嵌张', '单钓', '自摸', '花牌', '明暗杠', '天和', '地和', '人和Ⅰ', '人和Ⅱ'
]

const A_NONE = 0
const A_FLOWER = 1
const A_DISCARD = 2
const A_CHI = 3
const A_PENG = 4
const A_GANG = 5
const A_WIN = 6
const A_DRAW = 7

async function inflateZlibBase64(b64) {
  const bin = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0))
  if (typeof DecompressionStream === 'undefined') {
    throw new Error('当前浏览器不支持 DecompressionStream，无法解压雀渣 script')
  }
  const ds = new DecompressionStream('deflate')
  const stream = new Blob([bin]).stream().pipeThrough(ds)
  const buf = await new Response(stream).arrayBuffer()
  return new TextDecoder().decode(buf).replace(/\0/g, '')
}

async function deflateToZlibBase64(text) {
  if (typeof CompressionStream === 'undefined') {
    throw new Error('当前浏览器不支持 CompressionStream，无法压缩雀渣 script')
  }
  const cs = new CompressionStream('deflate')
  const stream = new Blob([text]).stream().pipeThrough(cs)
  const buf = await new Response(stream).arrayBuffer()
  const bytes = new Uint8Array(buf)
  let s = ''
  bytes.forEach((b) => {
    s += String.fromCharCode(b)
  })
  return btoa(s)
}

export async function loadTziakchaRecord(input) {
  const data = typeof input === 'string' ? parseJsonInput(input) : input
  if (data?.step && typeof data.step === 'object') return data
  if (data?.script) {
    const step = JSON.parse(await inflateZlibBase64(data.script))
    return {
      id: data.id,
      belongs: data.belongs,
      next: data.next,
      prev: data.prev,
      step
    }
  }
  if (data?.w && data?.a) return { step: data }
  if (data?.session && Array.isArray(data.records)) return data
  if (data?.game_title && data?.game_round) {
    throw new Error('这是 salasasa 牌谱，请选择 salasasa → 雀渣')
  }
  throw new Error('无法识别雀渣牌谱（需要 script / step / session+records）')
}

function parseWallHex(wallHex) {
  if (typeof wallHex !== 'string' || wallHex.length < 288) {
    throw new Error('step.w 必须是 ≥288 字符 hex')
  }
  const out = []
  for (let i = 0; i < 144; i++) out.push(parseInt(wallHex.slice(i * 2, i * 2 + 2), 16))
  return out
}

function parseDice(encoded) {
  if (Array.isArray(encoded)) return encoded.map(Number)
  const d = Number(encoded)
  return [d & 15, (d >> 4) & 15, (d >> 8) & 15, (d >> 12) & 15]
}

function decodeAction(tuple) {
  if (tuple && typeof tuple === 'object' && !Array.isArray(tuple)) {
    return {
      player: tuple.p ?? tuple.player,
      type: tuple.a ?? tuple.type,
      data: tuple.d ?? tuple.data,
      time: tuple.t ?? tuple.time ?? 0
    }
  }
  const [combined, data, time] = tuple
  return { player: (combined >> 4) & 3, type: combined & 15, data, time }
}

function rotateWall(wall, dice, dealer = 0) {
  const breakPos = (dealer - (dice[0] + dice[1] - 1) + 12) % 4
  let start = breakPos * 36 + (dice[0] + dice[1] + dice[2] + dice[3]) * 2
  start %= wall.length
  return wall.slice(start).concat(wall.slice(0, start))
}

function dealHands(rotated, dealer = 0) {
  let front = 0
  const hands = [[], [], [], []]
  for (let r = 0; r < 3; r++) {
    for (let o = 0; o < 4; o++) {
      const p = (dealer + o) % 4
      for (let k = 0; k < 4; k++) hands[p].push(rotated[front++])
    }
  }
  for (let o = 0; o < 4; o++) {
    const p = (dealer + o) % 4
    hands[p].push(rotated[front++])
  }
  hands[dealer].push(rotated[front++])
  return { hands, remain: rotated.slice(front) }
}

function formatFanList(yakuEntry) {
  if (!yakuEntry || typeof yakuEntry !== 'object') return []
  const table = yakuEntry.t || {}
  const out = []
  for (const [key, packed] of Object.entries(table)) {
    const idx = Number(key)
    const mul = ((Number(packed) >> 8) & 0xff) + 1
    let base = FAN_NAMES[idx] || `番${idx}`
    if (base.startsWith('独听・')) base = base.replace('独听・', '')
    if (base.startsWith('※ ')) base = base.replace('※ ', '')
    if (base === '花牌' || mul > 1) out.push(`${base}*${mul}`)
    else out.push(base)
  }
  return out
}

function removeExact(hand, tileId) {
  const i = hand.indexOf(tileId)
  if (i >= 0) {
    hand.splice(i, 1)
    return tileId
  }
  const base = tileId & ~3
  const j = hand.findIndex((t) => (t & ~3) === base)
  if (j < 0) return tileId // 容错：反向导出谱可能实例不一致
  return hand.splice(j, 1)[0]
}

function removeByBase(hand, tileBase) {
  const base = tileBase & ~3
  const j = hand.findIndex((t) => (t & ~3) === base)
  if (j < 0) return tileBase // 容错
  return hand.splice(j, 1)[0]
}

export function convertTziakchaStepToSalasasaRound(step, roundOrdinal = 1, recordId = null) {
  const wall = typeof step.w === 'string' ? parseWallHex(step.w) : [...step.w]
  const dice = parseDice(step.d)
  const roundI = Number(step.i || 0)
  const rotated = rotateWall(wall, dice, 0)
  const { hands, remain } = dealHands(rotated, 0)

  const pTiles = {}
  for (let i = 0; i < 4; i++) {
    pTiles[`p${i}_tiles`] = [...hands[i]].map(tzToSalasasa).sort((a, b) => a - b)
  }
  const tilesList = remain.map(tzToSalasasa)

  const players = hands.map((h) => ({ hand: [...h], lastDraw: null }))
  let wallFront = [...remain]
  let lastDiscard = null
  let lastDiscardPlayer = null
  let lastWasGang = false
  let lastWasFlower = false

  const actions = (step.a || []).map(decodeAction)
  const ticks = []
  const g = step.g || {}
  const b = Number(step.b || 0)
  const scoreS = [...(step.s || [0, 0, 0, 0])]
  let yakuList = step.y || [{}, {}, {}, {}]
  if (!Array.isArray(yakuList)) {
    yakuList = [0, 1, 2, 3].map((i) => yakuList[i] || yakuList[String(i)] || {})
  }

  const winnerMask = b & 0x0f
  const discarderMask = (b >> 4) & 0x0f
  const cuoheMask = (b >> 8) & 0x0f

  for (const ac of actions) {
    const typ = ac.type
    const p = ac.player
    const d = ac.data
    const pl = players[p]

    if (typ === A_NONE || typ === 8 || typ === 9) continue

    if (typ === A_FLOWER) {
      const flower = ((d >> 8) & 0x0f) + 136
      const drawn = d & 0xff
      const actualFlower = removeExact(pl.hand, flower)
      const isMo = pl.lastDraw === actualFlower
      ticks.push(['bh', tzToSalasasa(actualFlower), p, isMo ? 'T' : 'F'])
      const wi = wallFront.indexOf(drawn)
      if (wi >= 0) wallFront.splice(wi, 1)
      pl.hand.push(drawn)
      pl.lastDraw = drawn
      ticks.push(['bd', tzToSalasasa(drawn), p])
      lastWasFlower = true
      lastWasGang = false
      continue
    }

    if (typ === A_DRAW) {
      const tile = d & 0xff
      const backward = Boolean(d & 0x0100)
      const wi = wallFront.indexOf(tile)
      if (wi >= 0) {
        if (backward && wallFront[wallFront.length - 1] === tile) wallFront.pop()
        else if (!backward && wallFront[0] === tile) wallFront.shift()
        else wallFront.splice(wi, 1)
      }
      pl.hand.push(tile)
      pl.lastDraw = tile
      const sala = tzToSalasasa(tile)
      if (backward) ticks.push(lastWasFlower ? ['bd', sala, p] : ['gd', sala])
      else ticks.push(['d', sala])
      lastWasGang = false
      lastWasFlower = false
      continue
    }

    if (typ === A_DISCARD) {
      const tile = d & 0xff
      const handPlayed = Boolean((d >> 8) & 1)
      const actual = removeExact(pl.hand, tile)
      ticks.push(['c', tzToSalasasa(actual), handPlayed ? 'F' : 'T'])
      lastDiscard = actual
      lastDiscardPlayer = p
      pl.lastDraw = null
      lastWasGang = false
      lastWasFlower = false
      continue
    }

    if (typ === A_CHI) {
      if (!d) continue
      const tl = (d & 0x3f) << 2
      const offer = (d >> 6) & 3
      const t0 = tl - 4 + ((d >> 10) & 3)
      const t1 = tl + ((d >> 12) & 3)
      const t2 = tl + 4 + ((d >> 14) & 3)
      let code
      let handTiles
      if (offer <= 1) {
        code = 'cl'
        handTiles = [t1, t2]
      } else if (offer === 2) {
        code = 'cm'
        handTiles = [t0, t2]
      } else {
        code = 'cr'
        handTiles = [t0, t1]
      }
      const removed = handTiles.map((t) => removeExact(pl.hand, t))
      if (lastDiscard == null) throw new Error('吃牌时无弃牌')
      ticks.push([
        code,
        tzToSalasasa(lastDiscard),
        p,
        tzToSalasasa(removed[0]),
        tzToSalasasa(removed[1])
      ])
      lastDiscard = null
      pl.lastDraw = null
      continue
    }

    if (typ === A_PENG) {
      if (!d) continue
      const tl = (d & 0x3f) << 2
      const removed = [removeByBase(pl.hand, tl), removeByBase(pl.hand, tl)]
      if (lastDiscard == null) throw new Error('碰牌时无弃牌')
      ticks.push([
        'p',
        tzToSalasasa(lastDiscard),
        p,
        tzToSalasasa(removed[0]),
        tzToSalasasa(removed[1])
      ])
      lastDiscard = null
      pl.lastDraw = null
      continue
    }

    if (typ === A_GANG) {
      if (!d) continue
      const tl = (d & 0x3f) << 2
      const promoted = (d & 0x0300) === 0x0300
      if (promoted) {
        const actual = removeByBase(pl.hand, tl)
        const isMo = pl.lastDraw != null && (pl.lastDraw & ~3) === (actual & ~3)
        ticks.push(['jg', tzToSalasasa(actual), isMo ? 'T' : 'F'])
        lastWasGang = true
        pl.lastDraw = null
        continue
      }
      const offer = (d >> 6) & 3
      if (offer === 0) {
        const ids = [0, 1, 2, 3].map(() => removeByBase(pl.hand, tl))
        const isMo = pl.lastDraw != null && (pl.lastDraw & ~3) === (tl & ~3)
        ticks.push(['ag', tzToSalasasa(ids[0]), isMo ? 'T' : 'F', ...ids.map(tzToSalasasa)])
      } else {
        const removed = [0, 1, 2].map(() => removeByBase(pl.hand, tl))
        if (lastDiscard == null) throw new Error('明杠时无弃牌')
        ticks.push([
          'g',
          tzToSalasasa(lastDiscard),
          p,
          ...removed.map(tzToSalasasa)
        ])
        lastDiscard = null
      }
      lastWasGang = true
      pl.lastDraw = null
      continue
    }

    if (typ === A_WIN) {
      const isCuohe = Boolean(cuoheMask & (1 << p))
      const isWinner = Boolean(winnerMask & (1 << p))
      if (isCuohe && !isWinner) {
        const zShare = Boolean(g.z ?? 1)
        const changes = [0, 0, 0, 0]
        if (zShare) {
          changes[p] = -30
          for (let i = 0; i < 4; i++) if (i !== p) changes[i] = 10
        } else changes[p] = -40
        const fans = formatFanList(yakuList[p])
        if (!fans.includes('错和')) fans.push('错和')
        const fanTotal = Number(yakuList[p]?.f || 0)
        ticks.push([huClassFromRelative(p, lastDiscardPlayer), p, fanTotal, fans, changes])
      }
    }
  }

  if (winnerMask) {
    let discarder = null
    for (let i = 0; i < 4; i++) if (discarderMask & (1 << i)) discarder = i
    for (let i = 0; i < 4; i++) {
      if (!(winnerMask & (1 << i))) continue
      if (cuoheMask & (1 << i)) continue
      ticks.push([
        huClassFromRelative(i, discarder),
        i,
        Number(yakuList[i]?.f || 0),
        formatFanList(yakuList[i]),
        [...scoreS]
      ])
    }
    ticks.push(['end'])
  } else {
    ticks.push(['liuju'])
    ticks.push(['end'])
  }

  const round = {
    round_index: roundOrdinal,
    current_round: roundI + 1,
    seats: seatsFromRoundIndex(roundI),
    dealer_index: 0,
    start_player_index: 0,
    p0_tiles: pTiles.p0_tiles,
    p1_tiles: pTiles.p1_tiles,
    p2_tiles: pTiles.p2_tiles,
    p3_tiles: pTiles.p3_tiles,
    tiles_list: tilesList,
    action_ticks: ticks
  }
  if (recordId) round.tziakcha_record_id = recordId
  return round
}

function buildTitleFromTziakcha(session, records, sourceUrl) {
  const first = records[0]?.step || {}
  const g = first.g || {}
  const players = first.p || session?.players || []
  const title = {
    rule: 'guobiao',
    room_type: 'custom',
    sub_rule: 'guobiao/standard',
    commitment_hex: '0'.repeat(64),
    salt: '0'.repeat(32),
    max_round: Math.max(1, Math.ceil(Number(session?.periods || records.length) / 4)),
    hepai_limit: Number(g.l || g.b || 8),
    open_cuohe: true,
    tips: false,
    is_player_set_random_seed: false,
    show_moqie_hint: false,
    tziakcha_session_id: session?.id || null,
    tziakcha_title: g.t || session?.title || null,
    tziakcha_cfg: g
  }
  if (sourceUrl) title.source_url = sourceUrl
  const entry = []
  for (let i = 0; i < 4; i++) {
    const src = players[i] || {}
    const name = src.n || src.name || `P${i}`
    const pid = src.i || src.id || name
    const uid = stableUid(pid)
    entry.push(uid)
    title[`p${i}_uid`] = uid
    title[`p${i}_name`] = name
    title[`p${i}_tziakcha_id`] = pid
  }
  title.player_entry_order = entry
  return title
}

function stableUid(pid) {
  if (typeof pid === 'number') return pid
  let h = 0
  for (const ch of String(pid)) h = (h * 131 + ch.charCodeAt(0)) & 0x7fffffff
  return 900000000 + (h % 99999999)
}

export async function tziakchaToSalasasa(input, options = {}) {
  const loaded = await loadTziakchaRecord(input)
  let session
  let records
  if (loaded.session && Array.isArray(loaded.records)) {
    session = loaded.session
    records = loaded.records
  } else {
    records = [loaded]
    session = {
      id: loaded.belongs,
      title: loaded.step?.g?.t,
      periods: 1,
      players: loaded.step?.p || []
    }
  }
  const title = buildTitleFromTziakcha(session, records, options.sourceUrl)
  const gameRound = {}
  records.forEach((rec, idx) => {
    const step = { ...rec.step }
    if (Array.isArray(step.w)) {
      step.w = step.w.map((t) => t.toString(16).padStart(2, '0')).join('')
    }
    gameRound[`round_index_${idx + 1}`] = convertTziakchaStepToSalasasaRound(
      step,
      idx + 1,
      rec.id || null
    )
  })
  return { game_title: title, game_round: gameRound }
}

// -------------------- salasasa → 雀渣（解码态 step + 可选 script） --------------------

function buildYakuTable(fanList, fanTotal) {
  const t = {}
  for (const name of fanList || []) {
    const m = String(name).match(/^(.*?)(?:\*(\d+))?$/)
    const base = m[1]
    const mul = Number(m[2] || 1)
    let idx = FAN_NAMES.indexOf(base)
    if (idx < 0 && ['边张', '嵌张', '单钓'].includes(base)) {
      idx = FAN_NAMES.indexOf(base)
    }
    if (idx < 0) continue
    // lo=番分粗略：花牌用 1；其余用 FAN 表常见分不好拿，用 1 占位，总番走 f
    const lo = base === '花牌' ? 1 : 1
    t[String(idx)] = lo | ((mul - 1) << 8)
  }
  return { f: fanTotal || 0, t, h: { p: [], s: [] } }
}

function encodeDiscardData(tzTile, handPlayed) {
  return (tzTile & 0xff) | (handPlayed ? 0x100 : 0)
}

function encodeDrawData(tzTile, backward) {
  return (tzTile & 0xff) | (backward ? 0x100 : 0)
}

function encodeFlowerData(flowerTz, drawnTz, auto = false) {
  const flowerIdx = flowerTz - 136
  return (drawnTz & 0xff) | ((flowerIdx & 0x0f) << 8) | (auto ? 0x1000 : 0)
}

function findChiOffer(calledTz, h1, h2) {
  const tiles = [calledTz, h1, h2].sort((a, b) => (a & ~3) - (b & ~3))
  const bases = tiles.map((t) => t >> 2)
  // 中间牌型
  const midBase = bases[1]
  const tl = midBase << 2
  const offsets = tiles.map((t, i) => {
    const expected = tl + (i - 1) * 4
    return t - expected
  })
  // offer: called 是最低/中/高
  const calledBase = calledTz >> 2
  let offer
  if (calledBase === bases[0]) offer = 0
  else if (calledBase === bases[1]) offer = 2
  else offer = 3
  const data =
    (midBase & 0x3f) |
    ((offer & 3) << 6) |
    ((offsets[0] & 3) << 10) |
    ((offsets[1] & 3) << 12) |
    ((offsets[2] & 3) << 14)
  // pack_get_tile 用 (data&0x3f)<<2，所以低 6 位是 midBase
  return data
}

function encodePengData(calledTz, fromDir = 1) {
  const base = (calledTz >> 2) & 0x3f
  const offset = calledTz & 3
  return base | ((fromDir & 3) << 6) | ((offset & 3) << 10) | (1 << 8)
}

function encodeMingGangData(calledTz, fromDir = 1) {
  const base = (calledTz >> 2) & 0x3f
  const offset = calledTz & 3
  return base | ((fromDir & 3) << 6) | ((offset & 3) << 10)
}

function encodeAnGangData(tileTz) {
  const base = (tileTz >> 2) & 0x3f
  return base // offer 0
}

function encodeJiaGangData(tileTz) {
  const base = (tileTz >> 2) & 0x3f
  const offset = tileTz & 3
  return base | ((offset & 3) << 10) | 0x0300
}

export function convertSalasasaRoundToTziakchaStep(round, title = {}, roundOrdinal = 1) {
  const pool = new Map()
  const toTz = (t) => salasasaToTz(t, pool)

  // 重建完整 144 墙：手牌 + tiles_list（发牌后剩余）
  // 注意：开局补花后手牌会变，这里用 p*_tiles（补花前）+ tiles_list
  const dealt = []
  for (let i = 0; i < 4; i++) {
    const tiles = round[`p${i}_tiles`] || []
    for (const t of tiles) dealt.push(toTz(t))
  }
  const remain = (round.tiles_list || []).map(toTz)
  // 雀渣发牌顺序：3轮×4人×4 + 每人1 + 庄再1。我们把 dealt 按发牌顺序不好还原，
  // 简化：墙 = 已发（按座位交错近似）+ remain。对回放主要看 step.a，墙用于展示。
  const wallTz = [...dealt, ...remain]
  while (wallTz.length < 144) wallTz.push(0)
  const wallHex = wallTz
    .slice(0, 144)
    .map((t) => t.toString(16).padStart(2, '0'))
    .join('')

  const actions = []
  let time = 0
  let lastDiscardPlayer = null
  const pushAct = (player, type, data) => {
    time += 1000
    actions.push([(player << 4) | type, data, time])
  }

  // 开始出牌
  pushAct(0, A_NONE, 0)

  const ticks = round.action_ticks || []
  let i = 0
  while (i < ticks.length) {
    const tick = ticks[i]
    const code = tick[0]
    if (code === 'end' || code === 'liuju') {
      i++
      continue
    }
    if (code === 'bh') {
      const flower = toTz(tick[1])
      const player = tick[2]
      // 下一 tick 应为 bd
      const next = ticks[i + 1]
      let drawn = 0
      if (next && next[0] === 'bd') {
        drawn = toTz(next[1])
        i++
      }
      pushAct(player, A_FLOWER, encodeFlowerData(flower, drawn, false))
      i++
      continue
    }
    if (code === 'bd') {
      pushAct(tick[2] ?? 0, A_DRAW, encodeDrawData(toTz(tick[1]), true))
      i++
      continue
    }
    if (code === 'd') {
      // 摸牌玩家需从巡目推断：上一动作后的下家；简化用「上次切牌者下家」或庄
      const player = (lastDiscardPlayer == null ? 0 : (lastDiscardPlayer + 1) % 4)
      // 若前面是鸣牌，摸牌者是鸣牌者——由后续切牌修正困难；用启发式：
      // 查看下一切牌者
      let guess = player
      for (let j = i + 1; j < ticks.length; j++) {
        if (ticks[j][0] === 'c') {
          // 切牌者即本手摸牌者（常规）
          // 但吃碰后也是切牌者摸过？吃碰不摸。所以若中间无 d，则 c 的玩家刚鸣过。
          break
        }
      }
      // 更稳：找紧随其后的 c / ag / jg / hu 的玩家
      for (let j = i + 1; j < Math.min(i + 6, ticks.length); j++) {
        const t = ticks[j]
        if (t[0] === 'c') {
          guess = lastDiscardPlayer == null ? 0 : guess
          // 无法从 tick 直接得摸牌座位；用「上一次行动座位」链
          break
        }
        if (['ag', 'jg', 'hu_self', 'bh'].includes(t[0])) break
      }
      // 使用动作链 currentPlayer
      pushAct(guessDrawPlayer(ticks, i, lastDiscardPlayer), A_DRAW, encodeDrawData(toTz(tick[1]), false))
      i++
      continue
    }
    if (code === 'gd') {
      pushAct(guessDrawPlayer(ticks, i, lastDiscardPlayer), A_DRAW, encodeDrawData(toTz(tick[1]), true))
      i++
      continue
    }
    if (code === 'c') {
      const tile = toTz(tick[1])
      const handPlayed = tick[2] !== 'T'
      const player = guessCutPlayer(ticks, i, lastDiscardPlayer)
      pushAct(player, A_DISCARD, encodeDiscardData(tile, handPlayed))
      lastDiscardPlayer = player
      i++
      continue
    }
    if (code === 'cl' || code === 'cm' || code === 'cr') {
      const called = toTz(tick[1])
      const player = tick[2]
      const h1 = toTz(tick[3])
      const h2 = toTz(tick[4])
      pushAct(player, A_CHI, findChiOffer(called, h1, h2))
      lastDiscardPlayer = player
      i++
      continue
    }
    if (code === 'p') {
      const called = toTz(tick[1])
      const player = tick[2]
      const from = lastDiscardPlayer == null ? 1 : (lastDiscardPlayer - player + 4) % 4 || 1
      pushAct(player, A_PENG, encodePengData(called, from))
      lastDiscardPlayer = player
      i++
      continue
    }
    if (code === 'g') {
      const called = toTz(tick[1])
      const player = tick[2]
      const from = lastDiscardPlayer == null ? 1 : (lastDiscardPlayer - player + 4) % 4 || 1
      pushAct(player, A_GANG, encodeMingGangData(called, from))
      lastDiscardPlayer = player
      i++
      continue
    }
    if (code === 'ag') {
      const tile = toTz(tick[1])
      const player = guessCutPlayer(ticks, i, lastDiscardPlayer)
      pushAct(player, A_GANG, encodeAnGangData(tile))
      i++
      continue
    }
    if (code === 'jg') {
      const tile = toTz(tick[1])
      const player = guessCutPlayer(ticks, i, lastDiscardPlayer)
      pushAct(player, A_GANG, encodeJiaGangData(tile))
      i++
      continue
    }
    if (String(code).startsWith('hu_')) {
      const player = tick[1]
      pushAct(player, A_WIN, (Number(tick[2]) || 0) << 1)
      i++
      continue
    }
    i++
  }

  // 终局 b / s / y
  let bMask = 0
  let score = [0, 0, 0, 0]
  const yaku = [{}, {}, {}, {}]
  for (let k = ticks.length - 1; k >= 0; k--) {
    const t = ticks[k]
    if (String(t[0]).startsWith('hu_')) {
      const winner = t[1]
      const huClass = t[0]
      const fan = t[2]
      const fans = t[3] || []
      score = [...(t[4] || score)]
      bMask |= 1 << winner
      const disc = discarderFromHuClass(winner, huClass)
      if (disc != null) bMask |= 1 << (disc + 4)
      yaku[winner] = buildYakuTable(fans, fan)
      if (fans.includes('错和')) bMask |= 1 << (winner + 8)
      break
    }
    if (t[0] === 'liuju') break
  }

  const roundI = Math.max(0, (round.current_round || roundOrdinal) - 1)
  const names = [0, 1, 2, 3].map((i) => ({
    i: title[`p${i}_tziakcha_id`] || `p${i}`,
    n: title[`p${i}_name`] || `P${i}`,
    e: title[`p${i}_elo`] || 2000,
    a: 0,
    s: 0,
    r: ''
  }))

  return {
    v: 0,
    r: title.master_seed_hex || '',
    g: title.tziakcha_cfg || {
      t: title.tziakcha_title || 'salasasa导出',
      n: Object.keys(title).length,
      l: title.hepai_limit || 8,
      b: title.hepai_limit || 8,
      z: 1,
      d: 0,
      s: 1,
      o: 1,
      a: 0,
      r: 1,
      bl: 1
    },
    p: names,
    s: score,
    b: bMask,
    i: roundI,
    t: Date.now(),
    w: wallHex,
    d: 0x1111, // 占位骰子
    a: actions,
    y: yaku
  }
}

function guessDrawPlayer(ticks, idx, lastDiscardPlayer) {
  // 向后找第一个 c/ag/jg/hu/bh 推断座位
  for (let j = idx + 1; j < ticks.length; j++) {
    const t = ticks[j]
    if (t[0] === 'c') {
      // 若中间没有鸣牌，切牌者=摸牌者
      let melded = false
      for (let k = idx + 1; k < j; k++) {
        if (['cl', 'cm', 'cr', 'p', 'g'].includes(ticks[k][0])) melded = true
      }
      if (!melded) return inferPlayerForCut(ticks, j, lastDiscardPlayer)
      return ticks[j - 1]?.[2] ?? 0
    }
    if (t[0] === 'ag' || t[0] === 'jg') return inferPlayerForCut(ticks, j, lastDiscardPlayer)
    if (String(t[0]).startsWith('hu_')) return t[1]
    if (t[0] === 'bh') return t[2]
    if (['cl', 'cm', 'cr', 'p', 'g'].includes(t[0])) return t[2]
  }
  return lastDiscardPlayer == null ? 0 : (lastDiscardPlayer + 1) % 4
}

function guessCutPlayer(ticks, idx, lastDiscardPlayer) {
  return inferPlayerForCut(ticks, idx, lastDiscardPlayer)
}

function inferPlayerForCut(ticks, idx, lastDiscardPlayer) {
  // 向前找最近有明确座位的动作
  for (let j = idx - 1; j >= 0; j--) {
    const t = ticks[j]
    if (['cl', 'cm', 'cr', 'p', 'g', 'bh'].includes(t[0])) return t[2]
    if (String(t[0]).startsWith('hu_')) return t[1]
    if (t[0] === 'd' || t[0] === 'gd' || t[0] === 'bd') {
      // 继续向前
      continue
    }
    if (t[0] === 'c') {
      return (inferPlayerForCut(ticks, j, lastDiscardPlayer) + 1) % 4
    }
  }
  return lastDiscardPlayer == null ? 0 : (lastDiscardPlayer + 1) % 4
}

export async function salasasaToTziakcha(input, options = {}) {
  const data = typeof input === 'string' ? parseJsonInput(input) : input
  if (!data?.game_title || !data?.game_round) {
    throw new Error('需要 salasasa 牌谱（game_title + game_round）')
  }
  if (data.game_title.rule && data.game_title.rule !== 'guobiao') {
    throw new Error(`当前为 ${data.game_title.rule}，雀渣互转仅支持国标`)
  }
  const rounds = Object.keys(data.game_round)
    .filter((k) => k.startsWith('round_index_'))
    .sort((a, b) => Number(a.split('_').pop()) - Number(b.split('_').pop()))

  const records = []
  for (let n = 0; n < rounds.length; n++) {
    const step = convertSalasasaRoundToTziakchaStep(
      data.game_round[rounds[n]],
      data.game_title,
      n + 1
    )
    const rec = {
      id: data.game_round[rounds[n]].tziakcha_record_id || `export${n + 1}`,
      belongs: data.game_title.tziakcha_session_id || 'salasasa',
      step
    }
    if (options.compress) {
      rec.script = await deflateToZlibBase64(JSON.stringify(step))
      // 保留解码便于核对
      rec.step_decoded = step
      delete rec.step
      rec.script_note = '已 zlib+base64；可用 step_decoded 查看'
    }
    records.push(rec)
  }

  return {
    session: {
      id: data.game_title.tziakcha_session_id || 'salasasa',
      title: data.game_title.tziakcha_title || 'salasasa导出',
      periods: records.length,
      players: [0, 1, 2, 3].map((i) => ({
        n: data.game_title[`p${i}_name`],
        i: data.game_title[`p${i}_tziakcha_id`]
      }))
    },
    records,
    note: 'salasasa→雀渣为近似重建：骰子/实例 id/摸牌座位启发式，可用于分析，不宜当作权威原始谱'
  }
}
