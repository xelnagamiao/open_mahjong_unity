export const ratio = (n, d, suffix = '%') =>
  (!d || d <= 0 ? '0.00' + suffix : ((n / d) * 100).toFixed(2) + suffix);

/** 有顺位的对局数（1–4 位次数之和），顺位相关比率的分母 */
export const rankedGames = (s) =>
  (s?.first_place_count || 0) + (s?.second_place_count || 0)
  + (s?.third_place_count || 0) + (s?.fourth_place_count || 0);

/** 顺位占比（分母 = rankedGames，与饼图中心一致） */
export const rankRate = (count, s) => ratio(count, rankedGames(s));

/** 饼图图例百分比（1 位小数，与统计表同分母） */
export const rankRatePieLabel = (count, s) => {
  const d = rankedGames(s);
  if (!d || d <= 0) return '0.0%';
  return `${(Number(count) / d * 100).toFixed(1)}%`;
};

/** 玩家个人副露率：副露小局数 / 该玩家总小局数 */
export const playerFuluRate = (fuluCount, totalRounds) =>
  ratio(fuluCount, totalRounds);

/** 平台全站副露率：四人席累加后除以总小局数×4 */
export const platformFuluRate = (fuluCount, totalRounds) =>
  ratio(fuluCount, (Number(totalRounds) || 0) * 4);

export const avg = (n, d) =>
  (d === undefined || !d || d <= 0 ? '0.00' : (n / d).toFixed(2));

export const avgRank = (s) => {
  const games = rankedGames(s);
  if (!games) return '0.00';
  const weighted = (s.first_place_count || 0) * 1
    + (s.second_place_count || 0) * 2
    + (s.third_place_count || 0) * 3
    + (s.fourth_place_count || 0) * 4;
  return (weighted / games).toFixed(2);
};

function buildStatsRowsBase(s, fuluRateFn) {
  return [
    { label: '总对局', value: String(s.total_games || 0) },
    { label: '总回合', value: String(s.total_rounds || 0) },
    { label: '平均顺位', value: avgRank(s) },
    { label: '局均点', value: avg(s.total_round_score, s.total_games) },
    { label: '一位率', value: rankRate(s.first_place_count, s) },
    { label: '二位率', value: rankRate(s.second_place_count, s) },
    { label: '三位率', value: rankRate(s.third_place_count, s) },
    { label: '四位率', value: rankRate(s.fourth_place_count, s) },
    { label: '和牌率', value: ratio(s.win_count, s.total_rounds) },
    { label: '自摸率', value: ratio(s.self_draw_count, s.win_count) },
    { label: '放铳率', value: ratio(s.deal_in_count, s.total_rounds) },
    { label: '错和率', value: ratio(s.cuohe_count, s.total_rounds) },
    { label: '副露率', value: fuluRateFn(s.fulu_round_count, s.total_rounds) },
    { label: '平均和番', value: avg(s.total_fan_score, s.win_count) },
    { label: '平均和巡', value: avg(s.total_win_turn, s.win_count) },
    { label: '平均铳番', value: avg(s.total_fangchong_score, s.deal_in_count) },
  ];
}

/** 玩家个人统计（PlayerData） */
export const buildPlayerStatsRows = (s) => buildStatsRowsBase(s, playerFuluRate);

/** 平台全站聚合统计（管理后台 / 平台数据页） */
export const buildPlatformStatsRows = (s) => buildStatsRowsBase(s, platformFuluRate);

/** 场次“总计”页签需要累加的原始计数字段（比率类指标由 buildPlatformStatsRows 基于合计重新计算） */
export const SCENE_TOTAL_KEYS = [
  'total_games', 'total_rounds', 'win_count', 'self_draw_count', 'deal_in_count',
  'total_fan_score', 'total_win_turn', 'total_fangchong_score',
  'first_place_count', 'second_place_count', 'third_place_count', 'fourth_place_count',
  'fulu_round_count', 'cuohe_count', 'total_round_score',
];

/** 把多个场次的原始统计行累加为一行总计（后端已返回 total 行时前端不会走到这里） */
export const sumSceneTotals = (rows) => {
  const out = { match_tier: 'total' };
  for (const k of SCENE_TOTAL_KEYS) {
    out[k] = rows.reduce((sum, r) => sum + (Number(r?.[k]) || 0), 0);
  }
  return out;
};

/** 把多个场次的番种计数累加为一个总计对象 */
export const sumTierFans = (fansByTier, tiers) => {
  const out = {};
  for (const t of tiers || []) {
    const fans = fansByTier?.[t];
    if (!fans) continue;
    for (const [key, value] of Object.entries(fans)) {
      out[key] = (out[key] || 0) + (Number(value) || 0);
    }
  }
  return out;
};

/**
 * 番种条目：count 为达成数量；传入 winCount 时额外计算 percent（占和牌次数的达成率）；
 * 传入 fanValues 时附加 value（番数）；sortBy 支持 'count'（从多到少）/ 'default'（番种表顺序）
 */
export function buildAllFanEntries(fans, fanDict, winCount, fanValues, sortBy = 'count') {
  const dict = fanDict || {};
  const dictKeys = Object.keys(dict);
  const keys = fanValues
    ? [
      ...Object.keys(fanValues).filter((key) => Object.prototype.hasOwnProperty.call(dict, key)),
      ...dictKeys.filter((key) => !Object.prototype.hasOwnProperty.call(fanValues, key)),
    ]
    : dictKeys;
  const entries = keys.map((key) => {
    const count = Number(fans?.[key]) || 0;
    const entry = { key, label: dict[key] || key, count };
    if (winCount !== undefined && winCount !== null) {
      entry.percent = ratio(count, winCount);
    }
    if (fanValues) {
      entry.value = Number(fanValues[key]) || 0;
    }
    return entry;
  });
  if (sortBy === 'default') return entries;
  return entries.sort((a, b) => b.count - a.count || a.label.localeCompare(b.label, 'zh-CN'));
}

export const TIER_CHART_COLORS = {
  beginner: '#409eff',
  intermediate: '#67c23a',
  advanced: '#e6a23c',
  mcrpl: '#f56c6c',
};
const EVENT_CHART_PALETTE = ['#9b59b6', '#1abc9c', '#e67e22', '#2ecc71', '#3498db', '#e74c3c'];

function colorForTier(tier, index) {
  return TIER_CHART_COLORS[tier] || EVENT_CHART_PALETTE[index % EVENT_CHART_PALETTE.length];
}

function parseLocalDate(str) {
  if (!str) return null;
  const d = new Date(`${String(str).slice(0, 10)}T12:00:00`);
  return Number.isNaN(d.getTime()) ? null : d;
}

function formatLocalDate(d) {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/** 闭区间内每一天，避免 0 局日期从曲线横轴消失 */
export function enumerateDates(dateFrom, dateTo) {
  const from = parseLocalDate(dateFrom);
  const to = parseLocalDate(dateTo);
  if (!from || !to || from > to) return [];
  const dates = [];
  const cur = new Date(from);
  while (cur <= to) {
    dates.push(formatLocalDate(cur));
    cur.setDate(cur.getDate() + 1);
  }
  return dates;
}

function resolveDailyDates(rowDates, dateFrom, dateTo) {
  if (dateFrom && dateTo) return enumerateDates(dateFrom, dateTo);
  if (!rowDates.length) return [];
  return enumerateDates(rowDates[0], rowDates[rowDates.length - 1]);
}

export function buildSceneDailyChartOption(rows, { tierOptions, tierLabel, selectedTier = null, dateFrom = null, dateTo = null } = {}) {
  const byDate = {};
  for (const row of rows) {
    const d = row.stat_date;
    const t = row.match_tier;
    if (!byDate[d]) byDate[d] = {};
    byDate[d][t] = (byDate[d][t] || 0) + (Number(row.total_games) || 0);
  }
  const dates = resolveDailyDates(Object.keys(byDate).sort(), dateFrom, dateTo);
  const tiers = selectedTier ? [selectedTier] : tierOptions.map((t) => t.value);
  const chartBottom = dates.length > 14 ? 88 : 72;
  return {
    tooltip: { trigger: 'axis' },
    legend: {
      data: tiers.map((t) => tierLabel[t] || t),
      bottom: 4,
      itemGap: 16,
    },
    grid: { left: 48, right: 24, top: 28, bottom: chartBottom, containLabel: true },
    xAxis: {
      type: 'category',
      data: dates,
      axisLabel: { rotate: dates.length > 14 ? 35 : 0, margin: 16 },
      axisTick: { alignWithLabel: true },
    },
    yAxis: { type: 'value', minInterval: 1 },
    series: tiers.map((t, i) => ({
      name: tierLabel[t] || t,
      type: 'line',
      smooth: true,
      data: dates.map((d) => byDate[d]?.[t] || 0),
      itemStyle: { color: colorForTier(t, i) },
    })),
  };
}

export function buildSceneDailyTable(rows, tierOptions, tierLabel) {
  const byDate = {};
  for (const row of rows) {
    const d = row.stat_date;
    if (!byDate[d]) {
      byDate[d] = { stat_date: d };
      for (const t of tierOptions) byDate[d][t.value] = 0;
    }
    byDate[d][row.match_tier] = (byDate[d][row.match_tier] || 0) + (Number(row.total_games) || 0);
  }
  return Object.values(byDate).sort((a, b) => (a.stat_date < b.stat_date ? 1 : -1));
}
