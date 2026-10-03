const pool = require('../config/database')
const { INITIAL_RATING, K, MIGRATION_ID, expectedScore } = require('./guessFanElo')

async function ensureGuessFanTables() {
  const client = await pool.connect()
  try {
    await client.query('BEGIN')
    // Serialize startup/migration across Node workers before reading the version marker.
    await client.query("SELECT pg_advisory_xact_lock(hashtext('guess_fan_ratings_schema'))")
    await client.query(`
    CREATE TABLE IF NOT EXISTS guess_fan_ratings (
      user_id       BIGINT NOT NULL,
      rule_set      VARCHAR(16) NOT NULL DEFAULT 'mixed',
      username      VARCHAR(64) NOT NULL,
      wins          INTEGER NOT NULL DEFAULT 0,
      matches       INTEGER NOT NULL DEFAULT 0,
      rating        INTEGER NOT NULL DEFAULT 1000,
      streak        INTEGER NOT NULL DEFAULT 0,
      best_streak   INTEGER NOT NULL DEFAULT 0,
      updated_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
      PRIMARY KEY (user_id, rule_set)
    );
  `)
  // 旧表迁移：补充 rule_set 列并把主键改为 (user_id, rule_set)
  const column = await client.query(
    `SELECT 1 FROM information_schema.columns
     WHERE table_schema = current_schema() AND table_name = 'guess_fan_ratings' AND column_name = 'rule_set'`
  )
  if (column.rowCount === 0) {
    await client.query(`ALTER TABLE guess_fan_ratings ADD COLUMN rule_set VARCHAR(16) NOT NULL DEFAULT 'mixed'`)
    await client.query(`DROP INDEX IF EXISTS idx_guess_fan_ratings_rating`)
    await client.query(`ALTER TABLE guess_fan_ratings DROP CONSTRAINT IF EXISTS guess_fan_ratings_pkey`)
    await client.query(`ALTER TABLE guess_fan_ratings ADD PRIMARY KEY (user_id, rule_set)`)
  }
  await client.query(`
    CREATE INDEX IF NOT EXISTS idx_guess_fan_ratings_rating
      ON guess_fan_ratings (rule_set, rating DESC, wins DESC, matches ASC);
  `)
    await client.query(`CREATE TABLE IF NOT EXISTS guess_fan_rating_migrations (
      version TEXT PRIMARY KEY,
      migrated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
      previous_rows JSONB NOT NULL
    )`)
    const applied = await client.query('SELECT 1 FROM guess_fan_rating_migrations WHERE version = $1', [MIGRATION_ID])
    if (!applied.rowCount) {
      // Keep the exact original rows for recovery. A failure rolls back both rows and marker.
      await client.query('LOCK TABLE guess_fan_ratings IN ACCESS EXCLUSIVE MODE')
      await client.query(`INSERT INTO guess_fan_rating_migrations (version, previous_rows)
        SELECT $1, COALESCE(jsonb_agg(to_jsonb(r) ORDER BY rule_set, user_id), '[]'::jsonb)
        FROM guess_fan_ratings r`, [MIGRATION_ID])
      await client.query('UPDATE guess_fan_ratings SET rating = 1500 + (rating - 1000) * 5')
    }
    await client.query(`ALTER TABLE guess_fan_ratings ALTER COLUMN rating SET DEFAULT ${INITIAL_RATING}`)
    await client.query('COMMIT')
  } catch (err) {
    await client.query('ROLLBACK')
    throw err
  } finally {
    client.release()
  }
}

/**
 * @param {number} limit
 * @param {string} [ruleSet] 排行榜规则集：mixed=国标+立直，riichi=立直
 * @returns {Promise<Array<{userId:string,username:string,wins:number,matches:number,rating:number,streak:number,bestStreak:number,losses:number,winRate:number}>>}
 */
async function fetchLeaderboardTop(limit = 20, ruleSet = 'mixed') {
  const result = await pool.query(
    `SELECT user_id, username, wins, matches, rating, streak, best_streak
     FROM guess_fan_ratings
     WHERE rule_set = $1
     ORDER BY rating DESC, wins DESC, matches ASC
     LIMIT $2`,
    [ruleSet, limit]
  )
  return result.rows.map((row) => {
    const wins = Number(row.wins) || 0
    const matches = Number(row.matches) || 0
    return {
      userId: String(row.user_id),
      username: row.username,
      wins,
      matches,
      rating: row.rating == null ? INITIAL_RATING : Number(row.rating),
      streak: Number(row.streak) || 0,
      bestStreak: Number(row.best_streak) || 0,
      losses: matches - wins,
      winRate: matches ? Math.round((wins / matches) * 100) : 0,
    }
  })
}

/**
 * 事务内读取双方当前评分、计算 Elo、写回。
 * @param {{
 *   winnerUserId: number|string,
 *   players: Array<{ userId: number|string, username: string }>
 * }} opts
 */
async function applyMatchRating({ winnerUserId, players, ruleSet = 'mixed' }) {
  if (!Array.isArray(players) || players.length !== 2) return null
  const rule = String(ruleSet || 'mixed').slice(0, 16)
  const ids = players.map((p) => Number(p.userId))
  if (ids.some((id) => !Number.isFinite(id))) {
    throw new Error('无效的用户 ID')
  }
  const winnerId = Number(winnerUserId)
  if (!Number.isFinite(winnerId)) throw new Error('无效的胜者 ID')

  const client = await pool.connect()
  try {
    await client.query('BEGIN')
    const locked = await client.query(
      `SELECT user_id, username, wins, matches, rating, streak, best_streak
       FROM guess_fan_ratings
       WHERE user_id = ANY($1::bigint[])
         AND rule_set = $2
       FOR UPDATE`,
      [ids, rule]
    )
    const byId = new Map(locked.rows.map((r) => [Number(r.user_id), r]))

    const rows = players.map((p) => {
      const uid = Number(p.userId)
      const existing = byId.get(uid)
      return {
        userId: uid,
        username: String(p.username || '').trim() || existing?.username || String(uid),
        wins: existing ? Number(existing.wins) : 0,
        matches: existing ? Number(existing.matches) : 0,
        rating: existing ? Number(existing.rating) : INITIAL_RATING,
        streak: existing ? Number(existing.streak) : 0,
        bestStreak: existing ? Number(existing.best_streak) : 0,
      }
    })

    const [a, b] = rows
    const expectedA = expectedScore(a.rating, b.rating)
    const scoreA = a.userId === winnerId ? 1 : 0
    const delta = Math.round(K * (scoreA - expectedA))

    for (const row of rows) {
      const won = row.userId === winnerId
      row.matches += 1
      if (won) {
        row.wins += 1
        row.streak += 1
        row.bestStreak = Math.max(row.bestStreak, row.streak)
      } else {
        row.streak = 0
      }
      const d = row === a ? delta : -delta
      row.rating = Math.max(0, row.rating + d)
      await client.query(
        `INSERT INTO guess_fan_ratings
           (user_id, rule_set, username, wins, matches, rating, streak, best_streak, updated_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7, $8, CURRENT_TIMESTAMP)
         ON CONFLICT (user_id, rule_set) DO UPDATE SET
           username = EXCLUDED.username,
           wins = EXCLUDED.wins,
           matches = EXCLUDED.matches,
           rating = EXCLUDED.rating,
           streak = EXCLUDED.streak,
           best_streak = EXCLUDED.best_streak,
           updated_at = CURRENT_TIMESTAMP`,
        [row.userId, rule, row.username, row.wins, row.matches, row.rating, row.streak, row.bestStreak]
      )
    }

    await client.query('COMMIT')
    return rows
  } catch (err) {
    try {
      await client.query('ROLLBACK')
    } catch (_) {
      /* ignore */
    }
    throw err
  } finally {
    client.release()
  }
}

module.exports = {
  ensureGuessFanTables,
  fetchLeaderboardTop,
  applyMatchRating,
}
