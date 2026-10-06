export const ratio = (n, d, suffix = '%') =>
  (!d || d <= 0 ? '0.00' + suffix : (((Number(n) || 0) / d) * 100).toFixed(2) + suffix);

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
  (d === undefined || !d || d <= 0 ? '0.00' : ((Number(n) || 0) / d).toFixed(2));

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
    // 国标平均和巡按庄家巡，与对局进程 player_index_go_to 一致
    { label: '平均和巡', value: avg(s.total_win_turn, s.win_count) },
    { label: '平均铳番', value: avg(s.total_fangchong_score, s.deal_in_count) },
  ];
}

/** history_stats 只有全历史数据，无法按日期或具体场次拆分。 */
export const canUsePrestoredPlayerStats = ({ scene, dateRange, recordsOnly = false }) =>
  !recordsOnly && (scene === 'rank' || scene === 'custom')
  && !(Array.isArray(dateRange) && dateRange[0] && dateRange[1]);

/** 对局数、净得分和顺位必须来自同一批筛选记录，不与全历史分子混用。 */
export const mergePlayerRankStats = (base, rankRow) => {
  if (!rankRow) return base;
  return {
    ...base,
    total_games: rankRow.total_games,
    total_round_score: rankRow.total_round_score ?? null,
    first_place_count: rankRow.first_place_count,
    second_place_count: rankRow.second_place_count,
    third_place_count: rankRow.third_place_count,
    fourth_place_count: rankRow.fourth_place_count,
  };
};

const PLAYER_SETTLEMENT_LABELS = new Set(['总对局', '平均顺位', '局均点', '一位率', '二位率', '三位率', '四位率']);

/** 玩家个人统计（PlayerData）；未提供的明细不能显示为 0 或沿用全历史值。 */
export const buildPlayerStatsRows = (s, { detailed = true, rule = s?.rule } = {}) => {
  if (rule === 'riichi') return buildRiichiStatsRows(s, detailed);
  return buildStatsRowsBase(s, playerFuluRate).map((row) => {
    if ((!detailed && !PLAYER_SETTLEMENT_LABELS.has(row.label))
      || (row.label === '局均点' && s.total_round_score == null)) {
      return { ...row, value: '—' };
    }
    return row;
  });
};

function buildRiichiStatsRows(s, detailed) {
  const d = s.riichi_details || {};
  const rows = buildStatsRowsBase(s, playerFuluRate)
    .filter(row => !['错和率', '平均铳番', '平均和巡', '平均和番'].includes(row.label))
    .map(row => row.label === '局均点' ? { ...row, tip: '本批对局净得点合计 / 玩家参赛场数；包含立直棒和流局收支。' } : row);
  rows.push(
    { label: '被飞率', value: ratio(d.busted_count || 0, d.final_score_count), tip: '终局点数小于 0 的场数 / 有结算点数的场数。' },
    { label: '立直率', value: ratio(d.riichi_round_count || 0, s.total_rounds), tip: '成功支付立直棒的局数 / 已结束小局数；宣言牌被荣和不算成立。' },
    { label: '平均和了打点', value: avg(d.total_win_points || 0, s.win_count), tip: '和了时实际获得点数的平均，含本场和供托。' },
    { label: '平均和了巡目', value: avg(s.total_win_turn, s.win_count), tip: '本家待和巡目：历史弃牌次数加一，自摸和荣和均按此计算；被鸣走的弃牌计入，杠后补牌不另加巡。' },
  );
  return rows;
}

/** 日麻条件统计仅供数据站高级分析，样本数和分母随分组一起展示。 */
export function buildRiichiAdvancedStatsGroups(s) {
  if (!s) return [];
  const d = s.riichi_details;
  const available = d != null && s.details_available !== false;
  const rate = (n, count) => available && count > 0 ? ratio(n, count) : '—';
  const mean = (n, count) => available && count > 0 ? avg(n, count) : '—';
  const sample = (n, unit) => available ? `${n || 0} ${unit}` : '样本不可用';
  return [
    { title: '立直分析', sample: sample(d?.riichi_round_count, '次成立立直'), rows: [
      { label: '平均立直巡目', value: mean(d?.total_riichi_turn, d?.riichi_round_count), tip: '本家立直弃牌序号的平均，包含被鸣走的弃牌。' },
      { label: '立直后和牌率', value: rate(d?.riichi_win_count, d?.riichi_round_count), tip: '立直后和牌局数 / 成立立直局数。' },
      { label: '立直后放铳率', value: rate(d?.riichi_deal_in_count, d?.riichi_round_count), tip: '立直后放铳局数 / 成立立直局数。' },
    ] },
    { title: '副露与门清', sample: `${sample(s.fulu_round_count, '个副露小局')} · ${sample(s.win_count, '次和牌')}`, rows: [
      { label: '副露后和牌率', value: rate(d?.fulu_win_count, s.fulu_round_count), tip: '有明副露且和牌的局数 / 有明副露的局数；暗杠保持门清。' },
      { label: '默听和牌占比', value: rate(d?.damaten_win_count, s.win_count), tip: '门清且未成立立直的和牌次数 / 全部和牌次数（含门清自摸），不是默听后的和牌率。' },
    ] },
    { title: '流局与失点', sample: `${sample(d?.exhaustive_draw_count, '次荒牌流局')} · ${sample(s.deal_in_count, '次放铳')}`, rows: [
      { label: '流局率', value: rate(d?.draw_round_count, s.total_rounds), tip: '荒牌或途中流局的小局数 / 已结束小局数。' },
      { label: '荒牌流局听牌率', value: rate(d?.tenpai_draw_count, d?.exhaustive_draw_count), tip: '荒牌流局时本家听牌局数 / 荒牌流局局数；途中流局不计。' },
      { label: '被自摸率', value: rate(d?.tsumo_loss_count, s.total_rounds), tip: '对手自摸且本家实际失点的小局数 / 已结束小局数。' },
      { label: '平均放铳失点', value: mean(d?.total_deal_in_points, s.deal_in_count), tip: '荣和放铳时实际支付点数的平均；一炮多响计一局并合计失点。' },
    ] },
    { title: '点数与和牌', sample: `${sample(d?.final_score_count, '场终局')} · ${sample(s.win_count, '次和牌')}`, rows: [
      { label: '平均终局点数', value: mean(d?.final_score_total, d?.final_score_count), tip: '实际终局点数的平均，与净得点分开。' },
      { label: '平均和了番数', value: mean(s.total_fan_score, s.win_count), tip: '和牌番数合计 / 和牌次数。' },
    ] },
  ];
}

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

/** 含今天在内的近 N 个日历日，格式 YYYY-MM-DD */
export function makePastDateRange(days) {
  const end = new Date();
  const start = new Date();
  start.setDate(end.getDate() - (Math.max(1, Number(days) || 1) - 1));
  return [formatLocalDate(start), formatLocalDate(end)];
}

export const STATS_DATE_SHORTCUTS = [
  { text: '近7天', value: () => makePastDateRange(7) },
  { text: '近30天', value: () => makePastDateRange(30) },
  { text: '近90天', value: () => makePastDateRange(90) },
];

/**
 * 将 YYYY-MM-DD 闭区间转为 API 查询参数。
 * date_to 为次日 00:00（开区间），与 rank-stats / event player-stats 口径一致。
 */
export function dateRangeToQueryParams(dateRange) {
  if (!Array.isArray(dateRange) || dateRange.length < 2 || !dateRange[0] || !dateRange[1]) {
    return {};
  }
  const from = String(dateRange[0]).slice(0, 10);
  const to = parseLocalDate(dateRange[1]);
  if (!parseLocalDate(from) || !to) return {};
  to.setDate(to.getDate() + 1);
  return {
    date_from: `${from}T00:00:00`,
    date_to: `${formatLocalDate(to)}T00:00:00`,
  };
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
