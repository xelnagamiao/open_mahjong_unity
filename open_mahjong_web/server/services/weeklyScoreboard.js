const fs = require('fs');
const path = require('path');
const pool = require('../config/database');
const { funDataDir } = require('../utils/runtimeData');
const {
  weekRange,
  cacheMatchesWeek,
  msUntilNextShanghaiHour,
} = require('../utils/funStatsTime');
const { pickPtLeaders } = require('../utils/funStatsLeaders');

// game_records.created_at 是北京时间的 TIMESTAMP（不带时区），直接按 04:00 切日。
const STAT_DATE_EXPR = "(gr.created_at - interval '4 hours')::date";
const TOP_N = 10;
const BOT_USER_ID_MAX = 10;
const METRIC = 'pt_change';

function cachePath() {
  return path.join(funDataDir(), 'weekly-scoreboard.json');
}

function writeJsonAtomic(filePath, data) {
  const dir = path.dirname(filePath);
  fs.mkdirSync(dir, { recursive: true });
  const tmp = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  fs.writeFileSync(tmp, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
  try {
    fs.renameSync(tmp, filePath);
  } catch {
    fs.copyFileSync(tmp, filePath);
    fs.unlinkSync(tmp);
  }
}

function readJson(filePath) {
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch {
    return null;
  }
}

async function queryWeeklyPtChanges({ dateFrom, dateTo }, db = pool) {
  const result = await db.query(
    `SELECT gpr.user_id,
            COALESCE(MAX(u.username), MAX(gpr.username)) AS username,
            SUM(gpr.pt_change) AS total_pt_change,
            COUNT(gpr.pt_change)::int AS games,
            COUNT(*) FILTER (WHERE gpr.pt_change IS NULL)::int AS missing_pt_records
       FROM game_player_records gpr
       INNER JOIN game_records gr ON gr.game_id = gpr.game_id
       LEFT JOIN users u ON u.user_id = gpr.user_id
      WHERE gpr.room_type = 'match'
        AND gpr.rule = 'guobiao'
        AND gpr.user_id > $3
        AND ${STAT_DATE_EXPR} BETWEEN $1::date AND $2::date
      GROUP BY gpr.user_id`,
    [dateFrom, dateTo, BOT_USER_ID_MAX]
  );
  return result.rows;
}

function snapshotNote(dateFrom, dateTo) {
  return `统计最近 ${dateFrom} 至 ${dateTo} 七个完整统计日的段位 PT 净变更（北京时间 04:00 切日，仅计已保存的结算 PT）`;
}

async function computeWeeklyScoreboard(now = new Date()) {
  const { date_from: dateFrom, date_to: dateTo } = weekRange(now);
  const rows = await queryWeeklyPtChanges({ dateFrom, dateTo });
  const { gainers, losers } = pickPtLeaders(rows, TOP_N);
  return {
    metric: METRIC,
    date_from: dateFrom,
    date_to: dateTo,
    generated_at: now.toISOString(),
    note: snapshotNote(dateFrom, dateTo),
    missing_pt_records: rows.reduce((total, row) => total + Number(row.missing_pt_records || 0), 0),
    gainers,
    losers,
  };
}

function readCachedScoreboard(now = new Date()) {
  const cache = readJson(cachePath());
  // 同一统计周的旧「终局得分」缓存也必须重新计算。
  if (cache?.metric !== METRIC) return null;
  if (!cacheMatchesWeek(cache, now)) return null;
  if (!Array.isArray(cache.gainers) || !Array.isArray(cache.losers)) return null;
  return cache;
}

async function refreshWeeklyScoreboard(now = new Date()) {
  const data = await computeWeeklyScoreboard(now);
  writeJsonAtomic(cachePath(), data);
  return data;
}

async function getWeeklyScoreboard(now = new Date()) {
  const cached = readCachedScoreboard(now);
  if (cached) return { ...cached, from_cache: true };
  const fresh = await refreshWeeklyScoreboard(now);
  return { ...fresh, from_cache: false };
}

function startWeeklyScoreboardScheduler() {
  const run = async () => {
    try {
      await refreshWeeklyScoreboard();
      console.log('趣味数据周榜已按 04:00 统计日刷新');
    } catch (err) {
      console.error('趣味数据周榜刷新失败:', err);
    }
  };
  const scheduleNext = () => {
    setTimeout(async () => {
      await run();
      scheduleNext();
    }, msUntilNextShanghaiHour(4));
  };
  scheduleNext();
}

module.exports = {
  TOP_N,
  pickPtLeaders,
  queryWeeklyPtChanges,
  computeWeeklyScoreboard,
  getWeeklyScoreboard,
  refreshWeeklyScoreboard,
  startWeeklyScoreboardScheduler,
};
