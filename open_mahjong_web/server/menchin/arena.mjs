import { randomInt, randomUUID } from 'node:crypto'
import { candidates, challengeQuestion, correctAnswer, newQuestion, speedPoints, CHALLENGE_LEVELS, CHALLENGE_ROUNDS, RANKED_ROUNDS, ROUND_SECONDS } from '../../shared/menchin.mjs'

export function createArena({ store, notify, lobbyChanged, scoresChanged, openingMs = 2000, reviewMs = 4000, reconnectMs = 30000 }) {
  const rooms = new Map()
  const byPlayer = new Map()
  const queue = new Map()
  const offline = new Map()

  function lobby() {
    return {
      queued: queue.size,
      rooms: [...rooms.values()].filter(game => game.phase === 'waiting' && game.players.length).map(game => ({
        code: game.code, host: game.players[0].name, count: game.players.length,
        rounds: game.limit, seconds: game.seconds,
      })),
    }
  }

  function state(id) {
    const game = byPlayer.get(id)
    const base = { serverNow: Date.now(), queued: queue.has(id), game: null }
    if (!game) return base
    const reveal = ['review', 'finished'].includes(game.phase)
    return { ...base, game: {
      id: game.id, code: game.code, mode: game.mode, phase: game.phase, limit: game.limit,
      round: game.round, seconds: game.seconds, host: game.players[0].id, you: id,
      level: game.level, requestId: game.requestId, lastPoints: game.lastPoints, endReason: game.endReason,
      hand: game.question?.hand, opensAt: game.opensAt, endsAt: game.endsAt,
      nextAt: game.nextAt, waits: reveal ? game.question?.waits : null,
      players: game.players.map(player => ({
        ...player, submitted: game.answers.has(player.id),
        answer: reveal || player.id === id ? game.answers.get(player.id) : undefined,
      })),
      roundWinner: game.roundWinner, winner: game.winner, forfeit: game.forfeit,
      saving: game.saving, changes: game.changes,
    } }
  }

  const update = id => notify(id, state(id))
  function updateGame(game) {
    for (const p of game.players) if (byPlayer.get(p.id) === game) update(p.id)
  }
  function free(id) {
    if (byPlayer.has(id) || queue.has(id)) throw new Error('请先退出当前对局或匹配')
  }
  function addPlayer(game, actor) {
    game.players.push({
      id: actor.id, name: actor.name, guest: actor.guest,
      wins: 0, points: 0, answered: 0, correct: 0, streak: 0, bestStreak: 0,
    })
    byPlayer.set(actor.id, game)
  }

  function create(mode, actors, limit, seconds) {
    let code
    do { code = String(randomInt(100000, 1000000)) } while (rooms.has(code))
    const game = {
      id: randomUUID(), code, mode, limit, seconds, phase: 'waiting', players: [],
      round: 0, seen: new Set(), answers: new Map(), timer: null,
      roundWinner: null, winner: null, forfeit: null, changes: {}, saving: '',
    }
    rooms.set(code, game)
    actors.forEach(actor => addPlayer(game, actor))
    return game
  }

  function beginRound(game) {
    clearTimeout(game.timer)
    game.round++
    game.phase = 'playing'
    game.answers.clear()
    game.roundWinner = null
    const challenge = game.mode === 'challenge'
    game.question = challenge ? challengeQuestion(game.level, game.seen) : newQuestion(game.round, game.seen)
    game.opensAt = Date.now() + (challenge ? 0 : openingMs)
    game.endsAt = game.opensAt + game.seconds * 1000
    game.nextAt = null
    game.timer = setTimeout(() => closeRound(game, null), game.endsAt - Date.now())
    updateGame(game)
  }

  async function save(game, attempt = 0) {
    try {
      game.changes = await store.settle(game)
      game.saving = 'saved'
      updateGame(game)
      scoresChanged()
    } catch (error) {
      console.error('[menchin] score settlement:', error.message)
      game.saving = 'retry'
      updateGame(game)
      setTimeout(() => save(game, attempt + 1), Math.min(60000, 2000 * (2 ** Math.min(attempt, 5)))).unref()
    }
  }

  function finish(game, winner, forfeit = null) {
    clearTimeout(game.timer)
    game.phase = 'finished'
    game.winner = winner
    game.forfeit = forfeit
    game.finishedAt = Date.now()
    game.nextAt = null
    const record = game.mode === 'challenge' ? !game.players[0].guest : game.mode === 'ranked' && !!winner
    game.saving = record ? 'saving' : ''
    updateGame(game)
    if (record) void save(game)
  }

  function closeRound(game, winner) {
    if (game.phase !== 'playing') return
    clearTimeout(game.timer)
    game.roundWinner = winner
    if (game.mode === 'challenge') {
      const player = game.players[0]
      const answer = game.answers.get(player.id)
      if (!answer && offline.has(player.id)) return leave(player.id)
      player.answered++
      if (!answer?.correct) {
        game.endReason = answer ? 'wrong' : 'timeout'
        game.lastPoints = 0
        return finish(game, null)
      }
      game.lastPoints = speedPoints(game.level, game.endsAt - answer.at)
      player.points += game.lastPoints
      player.correct++
      player.bestStreak = player.correct
      if (game.round < game.limit) return beginRound(game)
      game.endReason = 'complete'
      return finish(game, null)
    } else {
      const player = game.players.find(p => p.id === winner)
      if (player) player.wins++
      if (player?.wins > game.limit / 2) return finish(game, winner)
      if (game.round === game.limit) {
        const [a, b] = game.players
        return finish(game, a.wins === b.wins ? null : (a.wins > b.wins ? a.id : b.id))
      }
      game.nextAt = Date.now() + reviewMs
      game.timer = setTimeout(() => beginRound(game), reviewMs)
    }
    game.phase = 'review'
    updateGame(game)
  }

  function leave(id) {
    const game = byPlayer.get(id)
    if (queue.delete(id)) lobbyChanged(lobby())
    if (!game) return update(id)
    if (game.phase === 'waiting') {
      game.players = game.players.filter(p => p.id !== id)
      lobbyChanged(lobby())
    } else if (game.phase !== 'finished' && game.mode !== 'challenge') {
      const opponent = game.players.find(p => p.id !== id)
      finish(game, offline.has(opponent.id) && offline.has(id) ? null : opponent.id, id)
    }
    byPlayer.delete(id)
    update(id)
    if (game.players.some(p => byPlayer.get(p.id) === game)) updateGame(game)
    else {
      clearTimeout(game.timer)
      rooms.delete(game.code)
    }
  }

  function act(actor, data) {
    const { id } = actor
    const game = byPlayer.get(id)
    switch (data.action) {
      case 'challenge': {
        if (!CHALLENGE_LEVELS.some(option => option.level === data.level) ||
          typeof data.requestId !== 'string' || !/^[a-f0-9]{32}$/.test(data.requestId)) throw new Error('挑战设置无效')
        if (game?.requestId === data.requestId) return update(id)
        if (game?.mode === 'challenge' && game.phase === 'finished') leave(id)
        free(id)
        const run = create('challenge', [actor], CHALLENGE_ROUNDS, ROUND_SECONDS)
        run.level = data.level
        run.requestId = data.requestId
        run.lastPoints = 0
        beginRound(run)
        return
      }
      case 'abandon':
        if (game?.mode === 'challenge' && game.requestId === data.requestId) leave(id)
        return
      case 'create':
      case 'join':
      case 'queue': {
        if (actor.guest) throw new Error('对战需要登录')
        if (actor.matchBanned) throw new Error('账号当前不能参加对战')
        free(id)
        if (data.action === 'create') {
          if (![3, 5].includes(data.rounds) || ![20, 30, 40].includes(data.seconds)) throw new Error('房间设置无效')
          create('custom', [actor], data.rounds, data.seconds)
          update(id)
        } else if (data.action === 'join') {
          const target = rooms.get(data.code)
          if (target?.phase !== 'waiting' || target.players.length >= 2) throw new Error('房间不存在或已满')
          addPlayer(target, actor)
          updateGame(target)
        } else {
          const other = queue.values().next().value
          if (other) {
            queue.delete(other.id)
            beginRound(create('ranked', [other, actor], RANKED_ROUNDS, ROUND_SECONDS))
          } else {
            queue.set(id, actor)
            update(id)
          }
        }
        lobbyChanged(lobby())
        return
      }
      case 'cancel':
        if (queue.delete(id)) lobbyChanged(lobby())
        update(id)
        return
    }
    if (!game || data.gameId !== game.id) throw new Error('对局已变更')
    if (data.action === 'leave') return leave(id)
    if (data.action === 'start') {
      if (game.phase !== 'waiting' || game.players.length !== 2 || game.players[0].id !== id) throw new Error('请等待双方入房后由房主开始')
      beginRound(game)
      lobbyChanged(lobby())
      return
    }
    if (data.round !== game.round) throw new Error('本题已结束')
    if (data.action !== 'answer' || game.phase !== 'playing' || game.answers.has(id)) throw new Error('当前不能提交')
    const at = Date.now()
    if (at < game.opensAt) throw new Error('本题尚未开始')
    if (at >= game.endsAt) {
      closeRound(game, null)
      return
    }
    const tiles = data.tiles
    const options = candidates(game.question.hand)
    const allowNoWait = game.mode === 'challenge' && game.level === 2
    if (!Array.isArray(tiles) || (!tiles.length && !allowNoWait) || tiles.length > 9 || new Set(tiles).size !== tiles.length ||
      !tiles.every(tile => options.includes(tile))) throw new Error('请选择有效的待牌')
    const correct = correctAnswer(tiles, game.question.waits)
    game.answers.set(id, { tiles, at, correct })
    if (correct || game.answers.size === game.players.length) closeRound(game, correct ? id : null)
    else updateGame(game)
  }

  function disconnect(id) {
    if (queue.delete(id)) lobbyChanged(lobby())
    if (!byPlayer.has(id)) return
    clearTimeout(offline.get(id))
    offline.set(id, setTimeout(() => {
      leave(id)
      offline.delete(id)
    }, reconnectMs))
  }

  function reconnect(id) {
    clearTimeout(offline.get(id))
    offline.delete(id)
  }
  return { act, disconnect, reconnect, state, lobby }
}
