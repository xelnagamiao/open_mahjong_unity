/**
 * Botzone ↔ salasasa（国标）
 * 对齐 Botzone Chinese-Standard-Mahjong / Mahjong-GB 协议字符串。
 */
import {
  salasasaToBotzone as tileToBz,
  botzoneToSalasasa as bzToTile,
  huClassFromRelative,
  parseJsonInput
} from './tiles.js'

function ensureGuobiao(title) {
  if (title?.rule && title.rule !== 'guobiao') {
    throw new Error(`Botzone 互转仅支持国标，当前 rule=${title.rule}`)
  }
}

/** salasasa 整场 → Botzone 风格 JSON（每局一行协议数组） */
export function salasasaToBotzone(input) {
  const data = typeof input === 'string' ? parseJsonInput(input) : input
  if (!data?.game_title || !data?.game_round) throw new Error('需要 salasasa 牌谱')
  ensureGuobiao(data.game_title)

  const rounds = Object.keys(data.game_round)
    .filter((k) => k.startsWith('round_index_'))
    .sort((a, b) => Number(a.split('_').pop()) - Number(b.split('_').pop()))

  const games = rounds.map((key, idx) => {
    const round = data.game_round[key]
    return {
      round_index: idx + 1,
      current_round: round.current_round,
      seats: round.seats,
      lines: convertRoundToBotzoneLines(round, data.game_title, idx)
    }
  })

  return {
    format: 'botzone-guobiao-lines',
    source: 'salasasa',
    title: data.game_title.tziakcha_title || null,
    players: [0, 1, 2, 3].map((i) => ({
      name: data.game_title[`p${i}_name`],
      uid: data.game_title[`p${i}_uid`]
    })),
    games,
    text: games.map((g) => g.lines.join('\n')).join('\n\n# ---- next round ----\n\n')
  }
}

function convertRoundToBotzoneLines(round, title, roundIdx) {
  const lines = []
  // 简化：quan = ((current_round-1)/4)|0，门风以 player 视角导出四份时需分别；这里导出「上帝视角」合并 log
  const quan = Math.floor(((round.current_round || 1) - 1) / 4)
  lines.push(`# round ${roundIdx + 1} quan=${quan}`)
  lines.push(`0 0 ${quan}`)

  // 发牌：四人花数 + 各手牌（去花）+ 花牌列表
  const flowers = [[], [], [], []]
  const hands = [[], [], [], []]
  for (let p = 0; p < 4; p++) {
    for (const t of round[`p${p}_tiles`] || []) {
      if (t >= 51 && t <= 58) flowers[p].push(t)
      else hands[p].push(t)
    }
  }
  const huaCounts = flowers.map((f) => f.length)
  const dealCards = hands[0].slice(0, 13).map(tileToBz)
  const allHua = flowers.flat().map(tileToBz)
  lines.push(`1 ${huaCounts.join(' ')} ${dealCards.join(' ')}${allHua.length ? ` ${allHua.join(' ')}` : ''}`)
  lines.push('# note: Botzone 每人只公开自己的 13 张；此处合并导出，回放/训练请按座位切分')

  const ticks = round.action_ticks || []
  let lastDiscard = null
  let lastDiscardPlayer = null
  let i = 0
  while (i < ticks.length) {
    const t = ticks[i]
    const code = t[0]
    if (code === 'bh') {
      const player = t[2]
      lines.push(`3 ${player} BUHUA ${tileToBz(t[1])}`)
      if (ticks[i + 1]?.[0] === 'bd') {
        // 补花摸牌对他人是 DRAW 隐藏
        lines.push(`3 ${player} DRAW`)
        i++
      }
      i++
      continue
    }
    if (code === 'd') {
      const player = guessPlayerAfter(ticks, i, lastDiscardPlayer)
      lines.push(`2 ${tileToBz(t[1])}`)
      lines.push(`# seat ${player} drew (self view would be "2 CARD")`)
      i++
      continue
    }
    if (code === 'gd' || code === 'bd') {
      const player = t[2] ?? guessPlayerAfter(ticks, i, lastDiscardPlayer)
      lines.push(`3 ${player} DRAW`)
      i++
      continue
    }
    if (code === 'c') {
      const player = guessCutSeat(ticks, i, lastDiscardPlayer)
      lines.push(`3 ${player} PLAY ${tileToBz(t[1])}`)
      lastDiscard = t[1]
      lastDiscardPlayer = player
      i++
      continue
    }
    if (code === 'cl' || code === 'cm' || code === 'cr') {
      const called = t[1]
      const player = t[2]
      const h1 = t[3]
      const h2 = t[4]
      const mid = chiMidTile(code, called, h1, h2)
      // Botzone CHI 后必须带打出牌：合并下一 c
      let playOut = null
      if (ticks[i + 1]?.[0] === 'c') {
        playOut = ticks[i + 1][1]
        i++
      }
      lines.push(
        `3 ${player} CHI ${tileToBz(mid)} ${tileToBz(playOut ?? h1)}`
      )
      lastDiscardPlayer = player
      lastDiscard = playOut
      i++
      continue
    }
    if (code === 'p') {
      const player = t[2]
      let playOut = null
      if (ticks[i + 1]?.[0] === 'c') {
        playOut = ticks[i + 1][1]
        i++
      }
      lines.push(`3 ${player} PENG ${tileToBz(playOut ?? t[1])}`)
      lastDiscardPlayer = player
      lastDiscard = playOut
      i++
      continue
    }
    if (code === 'g') {
      lines.push(`3 ${t[2]} GANG`)
      lastDiscardPlayer = t[2]
      i++
      continue
    }
    if (code === 'ag') {
      const player = guessCutSeat(ticks, i, lastDiscardPlayer)
      lines.push(`3 ${player} GANG`)
      i++
      continue
    }
    if (code === 'jg') {
      const player = guessCutSeat(ticks, i, lastDiscardPlayer)
      lines.push(`3 ${player} BUGANG ${tileToBz(t[1])}`)
      i++
      continue
    }
    if (String(code).startsWith('hu_')) {
      lines.push(`3 ${t[1]} HU`)
      if (Array.isArray(t[3])) lines.push(`# fans ${t[3].join(',')}`)
      if (Array.isArray(t[4])) lines.push(`# score ${t[4].join(',')}`)
      i++
      continue
    }
    if (code === 'liuju') {
      lines.push('3 HUANG')
      i++
      continue
    }
    i++
  }
  return lines
}

function chiMidTile(code, called, h1, h2) {
  const nums = [called, h1, h2].map((t) => (t >= 100 ? Math.floor(t / 100) * 10 + 5 : t))
  nums.sort((a, b) => a - b)
  return nums[1]
}

function guessPlayerAfter(ticks, idx, lastDiscardPlayer) {
  for (let j = idx + 1; j < ticks.length; j++) {
    const t = ticks[j]
    if (t[0] === 'c') return guessCutSeat(ticks, j, lastDiscardPlayer)
    if (['cl', 'cm', 'cr', 'p', 'g', 'bh'].includes(t[0])) return t[2]
    if (String(t[0]).startsWith('hu_')) return t[1]
  }
  return lastDiscardPlayer == null ? 0 : (lastDiscardPlayer + 1) % 4
}

function guessCutSeat(ticks, idx, lastDiscardPlayer) {
  for (let j = idx - 1; j >= 0; j--) {
    const t = ticks[j]
    if (['cl', 'cm', 'cr', 'p', 'g', 'bh'].includes(t[0])) return t[2]
    if (t[0] === 'c') return (guessCutSeat(ticks, j, lastDiscardPlayer) + 1) % 4
  }
  return lastDiscardPlayer == null ? 0 : (lastDiscardPlayer + 1) % 4
}

/** Botzone 文本 / JSON → salasasa */
export function botzoneToSalasasa(input) {
  let text
  let meta = {}
  if (typeof input === 'string') {
    const trimmed = input.trim()
    if (trimmed.startsWith('{')) {
      const obj = JSON.parse(trimmed)
      if (obj.text) text = obj.text
      else if (Array.isArray(obj.games)) {
        text = obj.games.map((g) => (g.lines || []).join('\n')).join('\n\n')
        meta = obj
      } else if (Array.isArray(obj.lines)) text = obj.lines.join('\n')
      else throw new Error('无法识别 Botzone JSON')
    } else text = input
  } else if (input?.text) {
    text = input.text
    meta = input
  } else if (Array.isArray(input?.games)) {
    text = input.games.map((g) => (g.lines || []).join('\n')).join('\n\n')
    meta = input
  } else throw new Error('请输入 Botzone 协议文本或 {games/lines/text} JSON')

  const blocks = text.split(/#\s*----\s*next round\s*----/i)
  const gameRound = {}
  blocks.forEach((block, idx) => {
    const round = parseBotzoneBlock(block, idx + 1)
    if (round) gameRound[`round_index_${idx + 1}`] = round
  })
  if (!Object.keys(gameRound).length) throw new Error('未解析到有效 Botzone 回合')

  const title = {
    rule: 'guobiao',
    room_type: 'custom',
    sub_rule: 'guobiao/standard',
    commitment_hex: '0'.repeat(64),
    salt: '0'.repeat(32),
    max_round: Math.max(1, Math.ceil(Object.keys(gameRound).length / 4)),
    hepai_limit: 8,
    open_cuohe: false,
    tips: false,
    is_player_set_random_seed: false,
    player_entry_order: [1, 2, 3, 4],
    p0_uid: 1,
    p0_name: meta.players?.[0]?.name || 'P0',
    p1_uid: 2,
    p1_name: meta.players?.[1]?.name || 'P1',
    p2_uid: 3,
    p2_name: meta.players?.[2]?.name || 'P2',
    p3_uid: 4,
    p3_name: meta.players?.[3]?.name || 'P3',
    source_format: 'botzone'
  }

  return { game_title: title, game_round: gameRound }
}

function parseBotzoneBlock(block, roundIndex) {
  const rawLines = block
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith('#'))
  if (!rawLines.length) return null

  const ticks = []
  let seats = [0, 1, 2, 3]
  let pTiles = [[], [], [], []]
  let tilesList = []
  let quan = 0
  let lastPlay = null
  let lastPlayPlayer = null

  for (const line of rawLines) {
    const parts = line.split(/\s+/)
    const head = parts[0]
    if (head === '0') {
      quan = Number(parts[2] || 0)
      continue
    }
    if (head === '1') {
      // 1 hua0..3 cards... flowers...
      const hua = parts.slice(1, 5).map(Number)
      const rest = parts.slice(5)
      const handCards = []
      const flowerCards = []
      for (const c of rest) {
        if (/^H/i.test(c)) flowerCards.push(bzToTile(c))
        else handCards.push(bzToTile(c))
      }
      // 仅一份公开手牌：放到 seat0，其余未知用空；花按计数轮转塞
      pTiles[0] = [...handCards.slice(0, 13)]
      let fi = 0
      for (let p = 0; p < 4; p++) {
        for (let k = 0; k < (hua[p] || 0); k++) {
          if (fi < flowerCards.length) pTiles[p].push(flowerCards[fi++])
        }
      }
      continue
    }
    if (head === '2') {
      // 自己摸牌
      ticks.push(['d', bzToTile(parts[1])])
      continue
    }
    if (head === '3') {
      const player = Number(parts[1])
      const act = parts[2]
      if (act === 'DRAW') {
        // 他人摸牌不可见
        continue
      }
      if (act === 'BUHUA') {
        ticks.push(['bh', bzToTile(parts[3]), player, 'F'])
        continue
      }
      if (act === 'PLAY') {
        const tile = bzToTile(parts[3])
        ticks.push(['c', tile, 'F'])
        lastPlay = tile
        lastPlayPlayer = player
        continue
      }
      if (act === 'PENG') {
        const out = bzToTile(parts[3])
        ticks.push(['p', lastPlay ?? out, player, out, out])
        ticks.push(['c', out, 'F'])
        lastPlay = out
        lastPlayPlayer = player
        continue
      }
      if (act === 'CHI') {
        const mid = bzToTile(parts[3])
        const out = bzToTile(parts[4])
        const called = lastPlay ?? mid
        // 粗略：用 mid 推断 cl/cm/cr
        const code = chiCodeFrom(called, mid)
        const others = chiOthers(called, mid)
        ticks.push([code, called, player, others[0], others[1]])
        ticks.push(['c', out, 'F'])
        lastPlay = out
        lastPlayPlayer = player
        continue
      }
      if (act === 'GANG') {
        ticks.push(['g', lastPlay ?? 11, player, 11, 11, 11])
        continue
      }
      if (act === 'BUGANG') {
        ticks.push(['jg', bzToTile(parts[3]), 'F'])
        continue
      }
      if (act === 'HU') {
        const hclass = huClassFromRelative(player, lastPlayPlayer)
        ticks.push([hclass, player, 8, [], [0, 0, 0, 0]])
        continue
      }
      if (act === 'HUANG') {
        ticks.push(['liuju'])
        continue
      }
    }
  }
  ticks.push(['end'])

  return {
    round_index: roundIndex,
    current_round: quan * 4 + 1,
    seats,
    dealer_index: 0,
    start_player_index: 0,
    p0_tiles: pTiles[0],
    p1_tiles: pTiles[1],
    p2_tiles: pTiles[2],
    p3_tiles: pTiles[3],
    tiles_list: tilesList,
    action_ticks: ticks,
    note: 'Botzone→salasasa 为协议近似重建，隐藏信息（他人手牌/摸牌）无法完整还原'
  }
}

function chiCodeFrom(called, mid) {
  const cn = called % 10
  const mn = mid % 10
  if (cn === mn) return 'cm'
  if (cn < mn) return 'cl'
  return 'cr'
}

function chiOthers(called, mid) {
  const suit = Math.floor(called / 10) * 10
  const mn = mid % 10
  const all = [suit + (mn - 1), suit + mn, suit + (mn + 1)]
  return all.filter((t) => t !== called)
}
