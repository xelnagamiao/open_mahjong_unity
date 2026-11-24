import elo from '../utils/guessFanElo.js'

const { INITIAL_RATING, K, expectedScore } = elo
const fields = 'points, answered, correct, best_streak AS "bestStreak", rating, matches, wins'
const emptyStats = { points: 0, answered: 0, correct: 0, bestStreak: 0, rating: INITIAL_RATING, matches: 0, wins: 0 }

export function menchinStore(pool) {
  async function initialize() {
    await pool.query(`
      CREATE TABLE IF NOT EXISTS menchin_players (
        user_id BIGINT PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
        points INTEGER NOT NULL DEFAULT 0, answered INTEGER NOT NULL DEFAULT 0,
        correct INTEGER NOT NULL DEFAULT 0, best_streak INTEGER NOT NULL DEFAULT 0,
        rating INTEGER NOT NULL DEFAULT ${INITIAL_RATING}, matches INTEGER NOT NULL DEFAULT 0,
        wins INTEGER NOT NULL DEFAULT 0
      );
      CREATE TABLE IF NOT EXISTS menchin_results (
        game_id UUID PRIMARY KEY, finished_at TIMESTAMPTZ NOT NULL,
        solo_player BIGINT REFERENCES users(user_id) ON DELETE SET NULL,
        solo_score INTEGER, rating_changes JSONB NOT NULL DEFAULT '{}'
      );
      ALTER TABLE menchin_results ADD COLUMN IF NOT EXISTS challenge_level SMALLINT;
      CREATE INDEX IF NOT EXISTS menchin_daily_scores ON menchin_results (finished_at, solo_player)
        WHERE solo_player IS NOT NULL;
      CREATE INDEX IF NOT EXISTS menchin_ranked_ratings ON menchin_players (rating DESC, wins DESC)
        WHERE matches > 0;
    `)
  }

  async function stats(id) {
    const { rows } = await pool.query(`SELECT ${fields} FROM menchin_players WHERE user_id = $1`, [id])
    return rows[0] || { ...emptyStats }
  }

  async function challengeStandings() {
    const { rows } = await pool.query(`
      SELECT best.id, u.username AS name, best.score, best.level
      FROM (
        SELECT DISTINCT ON (solo_player) solo_player AS id, solo_score AS score, challenge_level AS level
        FROM menchin_results
        WHERE challenge_level IS NOT NULL AND solo_player IS NOT NULL
          AND finished_at >= (date_trunc('day', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Shanghai') AT TIME ZONE 'Asia/Shanghai')
        ORDER BY solo_player, solo_score DESC, finished_at, game_id
      ) best JOIN users u ON u.user_id = best.id
      ORDER BY best.score DESC, best.id LIMIT 20`)
    return rows
  }

  async function duelStandings() {
    const { rows } = await pool.query(`
      SELECT p.user_id AS id, u.username AS name, p.rating AS score, p.wins, p.matches
      FROM menchin_players p JOIN users u USING (user_id) WHERE p.matches > 0
      ORDER BY p.rating DESC, p.wins DESC, p.matches, p.user_id LIMIT 20`)
    return rows
  }

  async function boards() {
    const [challenge, duel] = await Promise.all([challengeStandings(), duelStandings()])
    return { challenge, duel }
  }

  async function settle(game) {
    const solo = game.mode === 'challenge' ? game.players[0] : null
    const client = await pool.connect()
    try {
      await client.query('BEGIN')
      const inserted = await client.query(`
        INSERT INTO menchin_results (game_id, finished_at, solo_player, solo_score, challenge_level)
        VALUES ($1,$2,$3,$4,$5) ON CONFLICT DO NOTHING`,
      [game.id, new Date(game.finishedAt), solo?.id, solo?.points, solo ? game.level : null])
      let changes = {}
      if (!inserted.rowCount) {
        const { rows } = await client.query('SELECT rating_changes FROM menchin_results WHERE game_id=$1', [game.id])
        changes = rows[0].rating_changes
      } else if (solo) {
        await client.query(`
          INSERT INTO menchin_players (user_id, points, answered, correct, best_streak)
          VALUES ($1,$2,$3,$4,$5) ON CONFLICT (user_id) DO UPDATE SET
          points = menchin_players.points + EXCLUDED.points, answered = menchin_players.answered + EXCLUDED.answered,
          correct = menchin_players.correct + EXCLUDED.correct, best_streak = GREATEST(menchin_players.best_streak, EXCLUDED.best_streak)`,
        [solo.id, solo.points, solo.answered, solo.correct, solo.bestStreak])
      } else {
        const ids = game.players.map(p => p.id)
        await client.query(`INSERT INTO menchin_players (user_id)
          SELECT unnest($1::bigint[]) ORDER BY 1 ON CONFLICT DO NOTHING`, [ids])
        const { rows } = await client.query(`SELECT user_id, rating FROM menchin_players
          WHERE user_id = ANY($1::bigint[]) ORDER BY user_id FOR UPDATE`, [ids])
        const firstWins = String(rows[0].user_id) === game.winner
        const delta = Math.round(K * (Number(firstWins) - expectedScore(rows[0].rating, rows[1].rating)))
        for (const [i, row] of rows.entries()) {
          const id = String(row.user_id)
          const rating = Math.max(0, row.rating + (i === 0 ? delta : -delta))
          changes[id] = { rating, delta: rating - row.rating }
          await client.query(`UPDATE menchin_players SET rating=$2, matches=matches+1, wins=wins+$3
            WHERE user_id=$1`, [id, rating, Number(id === game.winner)])
        }
        await client.query('UPDATE menchin_results SET rating_changes=$2 WHERE game_id=$1', [game.id, changes])
      }
      await client.query('COMMIT')
      return changes
    } catch (error) {
      await client.query('ROLLBACK')
      throw error
    } finally {
      client.release()
    }
  }
  return { initialize, stats, boards, duelStandings, settle }
}
