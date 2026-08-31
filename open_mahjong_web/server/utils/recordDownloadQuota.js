/**
 * 登录玩家每日牌谱下载配额：按局数计，凌晨 4 点换日。
 * 普通账号 200 局，赞助者 1000 局。开发环境或 DEBUG=true 时不扣配额。
 */
const pool = require('../config/database');
const config = require('../config/config');

const DAILY_MAX = 200;
const SPONSOR_DAILY_MAX = 1000;
const FETCH_MAX_GAMES = 100;
const DOWNLOAD_MAX_GAMES = 100;

function isQuotaEnabled() {
  return config.isProduction && !config.isDebug;
}

function currentDayKey(now = new Date()) {
  const d = new Date(now);
  if (d.getHours() < 4) d.setDate(d.getDate() - 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function isActiveSponsor(expiresAt) {
  if (!expiresAt) return false;
  const t = new Date(expiresAt).getTime();
  return Number.isFinite(t) && t > Date.now();
}

async function getDailyMaxForUser(userId, client = pool) {
  const result = await client.query(
    'SELECT sponsor_expires_at FROM users WHERE user_id = $1',
    [userId]
  );
  return isActiveSponsor(result.rows[0]?.sponsor_expires_at) ? SPONSOR_DAILY_MAX : DAILY_MAX;
}

class QuotaExceededError extends Error {
  constructor(snapshot) {
    const max = snapshot?.max || DAILY_MAX;
    super(
      snapshot?.remaining > 0
        ? `今日剩余可下载 ${snapshot.remaining} 局`
        : `今日牌谱下载已达上限（${snapshot?.used || 0}/${max} 局，凌晨 4 点刷新）`
    );
    this.name = 'QuotaExceededError';
    this.code = 'QUOTA_EXCEEDED';
    this.snapshot = snapshot || null;
  }
}

async function ensureRecordDownloadQuotaTable(client = pool) {
  await client.query(`
    CREATE TABLE IF NOT EXISTS record_download_quota (
      user_id BIGINT NOT NULL,
      day_key DATE NOT NULL,
      games_count INT NOT NULL DEFAULT 0,
      PRIMARY KEY (user_id, day_key)
    )
  `);
}

async function countAccountGames(userId, client = pool) {
  const result = await client.query(
    'SELECT COUNT(*)::int AS n FROM game_player_records WHERE user_id = $1',
    [userId]
  );
  return Number(result.rows[0]?.n) || 0;
}

function snapshotOf({ used, dayKey, unlimited, consumed = 0, accountGames = 0, max = DAILY_MAX }) {
  const cap = Number(max) || DAILY_MAX;
  return {
    unlimited: !!unlimited,
    used,
    max: cap,
    remaining: unlimited ? cap : Math.max(0, cap - used),
    day_key: dayKey,
    consumed,
    account_games: accountGames,
  };
}

async function getDownloadQuota(userId) {
  const dayKey = currentDayKey();
  const [accountGames, max] = await Promise.all([
    countAccountGames(userId),
    getDailyMaxForUser(userId),
  ]);
  if (!isQuotaEnabled()) {
    return snapshotOf({ used: 0, dayKey, unlimited: true, accountGames, max });
  }
  await ensureRecordDownloadQuotaTable();
  const row = await pool.query(
    `SELECT games_count FROM record_download_quota
     WHERE user_id = $1 AND day_key = $2::date`,
    [userId, dayKey]
  );
  const used = Number(row.rows[0]?.games_count) || 0;
  return snapshotOf({ used, dayKey, unlimited: false, accountGames, max });
}

/**
 * @param {number} userId
 * @param {number} requestedCount
 * @param {{ allowPartial?: boolean }} [opts]
 * @returns {Promise<object>} snapshot，含 consumed
 */
async function consumeDownloadQuota(userId, requestedCount, opts = {}) {
  const allowPartial = opts.allowPartial !== false;
  const requested = Math.max(0, Math.floor(Number(requestedCount) || 0));
  const dayKey = currentDayKey();
  const max = await getDailyMaxForUser(userId);

  if (!isQuotaEnabled()) {
    const accountGames = await countAccountGames(userId);
    return snapshotOf({
      used: 0,
      dayKey,
      unlimited: true,
      consumed: requested,
      accountGames,
      max,
    });
  }

  const client = await pool.connect();
  let settled = false;
  try {
    await client.query('BEGIN');
    await ensureRecordDownloadQuotaTable(client);
    await client.query(
      `INSERT INTO record_download_quota (user_id, day_key, games_count)
       VALUES ($1, $2::date, 0)
       ON CONFLICT (user_id, day_key) DO NOTHING`,
      [userId, dayKey]
    );
    const row = await client.query(
      `SELECT games_count FROM record_download_quota
       WHERE user_id = $1 AND day_key = $2::date
       FOR UPDATE`,
      [userId, dayKey]
    );
    const used = Number(row.rows[0]?.games_count) || 0;
    const remaining = Math.max(0, max - used);

    if (requested <= 0) {
      await client.query('COMMIT');
      settled = true;
      const accountGames = await countAccountGames(userId);
      return snapshotOf({ used, dayKey, unlimited: false, consumed: 0, accountGames, max });
    }

    if (remaining <= 0 || (!allowPartial && requested > remaining)) {
      await client.query('ROLLBACK');
      settled = true;
      throw new QuotaExceededError({
        used,
        max,
        remaining,
        day_key: dayKey,
        unlimited: false,
      });
    }

    const consumed = Math.min(requested, remaining);
    const nextUsed = used + consumed;
    await client.query(
      `UPDATE record_download_quota SET games_count = $3
       WHERE user_id = $1 AND day_key = $2::date`,
      [userId, dayKey, nextUsed]
    );
    await client.query('COMMIT');
    settled = true;
    const accountGames = await countAccountGames(userId);
    return snapshotOf({
      used: nextUsed,
      dayKey,
      unlimited: false,
      consumed,
      accountGames,
      max,
    });
  } catch (err) {
    if (!settled) {
      try { await client.query('ROLLBACK'); } catch (_) { /* ignore */ }
    }
    throw err;
  } finally {
    client.release();
  }
}

function quotaErrorPayload(err) {
  const snap = err?.snapshot || {};
  return {
    success: false,
    message: err?.message || '今日牌谱下载已达上限',
    data: {
      used: snap.used ?? 0,
      max: snap.max ?? DAILY_MAX,
      remaining: snap.remaining ?? 0,
      unlimited: !!snap.unlimited,
      day_key: snap.day_key || snap.dayKey || currentDayKey(),
    },
  };
}

module.exports = {
  DAILY_MAX,
  SPONSOR_DAILY_MAX,
  FETCH_MAX_GAMES,
  DOWNLOAD_MAX_GAMES,
  isQuotaEnabled,
  currentDayKey,
  QuotaExceededError,
  ensureRecordDownloadQuotaTable,
  getDownloadQuota,
  consumeDownloadQuota,
  quotaErrorPayload,
};
