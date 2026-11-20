export const deliveryColumns = [
  {
    channel: "ALL",
    field: "rate",
    label: "全口径递送率",
    context: "全口径递送率",
    width: 108,
  },
  {
    channel: "ALL",
    field: "coplay_rate",
    label: "同桌率",
    context: "同桌率",
    width: 78,
  },
  {
    channel: "ALL",
    field: "occurrence_probability",
    label: "全口径出现概率",
    context: "全口径出现概率",
    width: 88,
  },
  {
    channel: "RON",
    field: "rate",
    label: "和牌递送率",
    context: "和牌递送率",
    width: 108,
  },
  {
    channel: "RON",
    field: "occurrence_probability",
    label: "和牌出现概率",
    context: "和牌出现概率",
    width: 88,
  },
];

export function numeric(value) {
  if (value == null || typeof value === "boolean" || value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}
export function ratio(value) {
  return value && typeof value === "object"
    ? numeric(value.value)
    : numeric(value);
}
export function percent(value) {
  const n = ratio(value);
  return n == null ? "—" : `${(n * 100).toFixed(1)}%`;
}
export function occurrenceProbability(metric) {
  const value = numeric(metric?.occurrence_probability);
  if (value === null || numeric(metric?.total) === 0) return "—";
  if (value * 100 < 0.0001) return "<0.0001%";
  return `${Number((value * 100).toPrecision(6))}%`;
}
export function isUnusualOccurrence(metric) {
  const value = numeric(metric?.occurrence_probability);
  return numeric(metric?.total) !== 0 && value !== null && value <= 0.05;
}
export function metricTooltip(metric) {
  if (numeric(metric?.total) === null || metric.total === 0)
    return "该类事件 0 次";
  return `${metric.count} / ${metric.total} 次 · 同桌 ${metric.shared_games ?? "—"} / ${metric.games ?? "—"} 场 · 出现概率 ${occurrenceProbability(metric)}`;
}
export function channelMetric(row, channel, direction) {
  return row?.channels?.[channel]?.[direction] ?? null;
}
export function buildRankingQuery(filters = {}) {
  const { min_percent, max_percent, ...query } = filters;
  query.filter_by = filters.filter_by || filters.sort_by || "rate";
  const min = numeric(min_percent),
    max = numeric(max_percent);
  if (min !== null) query.min_value = min / 100;
  if (max !== null) query.max_value = max / 100;
  return query;
}
export function directionalChart(rows = [], channel = "ALL") {
  const ordered = rows;
  return {
    animation: false,
    textStyle: {
      fontFamily: "Arial, Microsoft YaHei, sans-serif",
      fontWeight: 400,
      fontSize: 11,
    },
    grid: { left: 100, right: 35, top: 35, bottom: 30 },
    legend: { top: 0, data: ["喂牌", "收牌"], textStyle: { fontWeight: 400 } },
    xAxis: {
      type: "value",
      min: 0,
      max: 100,
      axisLabel: { formatter: "{value}%" },
    },
    yAxis: {
      type: "category",
      inverse: true,
      data: ordered.map((r) => r.name || r.user_id),
      axisLabel: { width: 90, overflow: "truncate" },
    },
    tooltip: {
      trigger: "item",
      renderMode: "richText",
      confine: true,
      formatter: (p) =>
        `${p.name} · ${p.seriesName} ${percent(p.data?.metric?.rate)}\n${metricTooltip(p.data?.metric)}`,
    },
    series: ["feeding", "receiving"].map((key, index) => ({
      name: index ? "收牌" : "喂牌",
      type: "bar",
      barMaxWidth: 10,
      itemStyle: { color: index ? "#53a899" : "#668ae4" },
      data: ordered.map((r) => ({
        value:
          numeric(channelMetric(r, channel, key)?.rate) === null
            ? null
            : channelMetric(r, channel, key).rate * 100,
        metric: channelMetric(r, channel, key),
      })),
      ...(index
        ? {}
        : {
            markLine: {
              silent: true,
              symbol: "none",
              lineStyle: { type: "dashed", color: "#9da8b6" },
              label: { formatter: "33.3%", fontWeight: 400 },
              data: [{ xAxis: 100 / 3 }],
            },
          }),
    })),
  };
}
