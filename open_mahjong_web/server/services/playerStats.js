/**
 * 与 routes/player.js 一致的规则统计查询，供公开 API 与管理后台复用
 */
const pool = require('../config/database');

const ruleConfig = {
  guobiao: { historyTable: 'guobiao_history_stats', fanTable: 'guobiao_fan_stats' },
  riichi: { historyTable: 'riichi_history_stats', fanTable: null },
  qingque: { historyTable: 'qingque_history_stats', fanTable: 'qingque_fan_stats' },
  classical: { historyTable: 'classical_history_stats', fanTable: 'classical_fan_stats' },
};

const HISTORY_FIELDS = new Set([
  'user_id', 'rule', 'mode', 'total_games', 'total_rounds', 'win_count',
  'self_draw_count', 'deal_in_count', 'total_fan_score', 'total_win_turn',
  'total_fangchong_score', 'first_place_count', 'second_place_count',
  'third_place_count', 'fourth_place_count', 'fulu_round_count',
  'created_at', 'updated_at',
]);

function extractFanStats(fanRow) {
  if (!fanRow) return null;
  const fanStats = {};
  for (const [key, value] of Object.entries(fanRow)) {
    if (HISTORY_FIELDS.has(key)) continue;
    if (value !== null && value !== 0) fanStats[key] = value;
  }
  return Object.keys(fanStats).length > 0 ? fanStats : null;
}

async function queryRuleStats(userId, rule) {
  const cfg = ruleConfig[rule];
  if (!cfg) return [];

  const historyResult = await pool.query(
    `SELECT * FROM ${cfg.historyTable} WHERE user_id = $1 ORDER BY rule, mode`,
    [userId]
  );

  let fanByKey = new Map();
  if (cfg.fanTable) {
    const fanResult = await pool.query(
      `SELECT * FROM ${cfg.fanTable} WHERE user_id = $1`,
      [userId]
    );
    for (const row of fanResult.rows) {
      fanByKey.set(`${row.rule}|${row.mode}`, row);
    }
  }

  return historyResult.rows.map((row) => ({
    rule: row.rule,
    mode: row.mode,
    total_games: row.total_games,
    total_rounds: row.total_rounds,
    win_count: row.win_count,
    self_draw_count: row.self_draw_count,
    deal_in_count: row.deal_in_count,
    total_fan_score: row.total_fan_score,
    total_win_turn: row.total_win_turn,
    total_fangchong_score: row.total_fangchong_score,
    first_place_count: row.first_place_count,
    second_place_count: row.second_place_count,
    third_place_count: row.third_place_count,
    fourth_place_count: row.fourth_place_count,
    fulu_round_count: row.fulu_round_count,
    fan_stats: extractFanStats(fanByKey.get(`${row.rule}|${row.mode}`)),
  }));
}

async function fetchUserStatsBundle(userId) {
  const [guobiao, riichi, qingque, classical] = await Promise.all([
    queryRuleStats(userId, 'guobiao'),
    queryRuleStats(userId, 'riichi'),
    queryRuleStats(userId, 'qingque'),
    queryRuleStats(userId, 'classical'),
  ]);
  return { guobiao_stats: guobiao, riichi_stats: riichi, qingque_stats: qingque, classical_stats: classical };
}

module.exports = { ruleConfig, HISTORY_FIELDS, queryRuleStats, fetchUserStatsBundle, extractFanStats };
