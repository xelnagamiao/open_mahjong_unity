import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import { computed, ref, reactive, shallowRef } from "vue";
import { parse, compileScript, compileTemplate } from "@vue/compiler-sfc";
import {
  deliveryColumns,
  numeric,
  percent,
  occurrenceProbability,
  isUnusualOccurrence,
  metricTooltip,
  channelMetric,
  buildRankingQuery,
  directionalChart,
} from "../src/views/admin/centerCharts.js";
const path = (file) => new URL(`../src/views/admin/${file}`, import.meta.url);
function evaluate(file, names, extra = {}) {
  const script = parse(
    fs.readFileSync(path(file), "utf8"),
  ).descriptor.scriptSetup.content.replace(
    /^import[\s\S]*?from\s+['"][^'"]+['"];?/gm,
    "",
  );
  return vm.runInNewContext(`(()=>{${script};return {${names}};})()`, {
    AbortController,
    deliveryColumns,
    shallowRef,
    defineAsyncComponent: (loader) => ({ loader }),
    computed,
    ref,
    reactive,
    numeric,
    percent,
    occurrenceProbability,
    isUnusualOccurrence,
    metricTooltip,
    channelMetric,
    buildRankingQuery,
    directionalChart,
    ...extra,
  });
}
function setup(overrides = {}) {
  const calls = { reads: 0, updates: 0 },
    mounted = [],
    timers = new Map();
  let next = 0;
  const api = {
    overview: async () => ({
      summary: { last_updated: "today" },
      active_update: null,
    }),
    players: async () => {
      calls.reads++;
      return { items: [], total: 0 };
    },
    updates: async () => ({ items: [] }),
    update: async () => {
      calls.updates++;
    },
    ...overrides,
  };
  const page = evaluate(
    "AntiCheat.vue",
    "refreshAll,loadPlayers,updateData,filters,playerRows,playerTotal,expandedKeys,expandPlayer,togglePlayer,search,handleHeaderFilter,resetFilters,error,summaryError,loadSummary,loadQuality,openManagement,managementChanged,quality,qualityError,overview,appliedQuery,changePage,playerLoading",
    {
      antiCheatApi: api,
      onMounted: (f) => mounted.push(f),
      onBeforeUnmount() {},
      ElMessage: { success() {} },
      date: (v) => v,
      ruleLabel: (v) => v,
      setTimeout: (f) => {
        timers.set(++next, f);
        return next;
      },
      clearTimeout: (id) => timers.delete(id),
    },
  );
  return { page, calls, mounted, timers };
}
test("ordinary refresh only reads, without idle polling", async () => {
  const h = setup();
  await h.mounted[0]();
  await h.page.refreshAll();
  assert.equal(h.calls.reads, 2);
  assert.equal(h.calls.updates, 0);
  assert.equal(h.timers.size, 0);
});
test("explicit update guards simultaneous clicks", async () => {
  let finish,
    n = 0;
  const h = setup({
    update: () => {
      n++;
      return new Promise((r) => (finish = r));
    },
  });
  const request = h.page.updateData();
  await h.page.updateData();
  assert.equal(n, 1);
  finish();
  await request;
});
test("channel, window and all-rule filter query full server ranking", async () => {
  let query;
  const h = setup({
    players: async (q) => {
      query = { ...q };
      return { items: [{ user_id: "a" }], total: 83 };
    },
  });
  h.page.filters.channel = "RON";
  h.page.filters.window_days = 30;
  h.page.filters.page = 3;
  await h.page.search();
  assert.equal(query.channel, "RON");
  assert.equal(query.window_days, 30);
  assert.equal(query.rule_key, "__ALL__");
  assert.equal(query.page, 1);
  assert.equal(h.page.playerTotal.value, 83);
  assert.equal("review_status" in query, false);
});
test("stale requests cannot overwrite new results", async () => {
  const r = [];
  const h = setup({ players: () => new Promise((resolve) => r.push(resolve)) });
  const first = h.page.loadPlayers();
  h.page.filters.page = 2;
  const second = h.page.loadPlayers();
  r[1]({ items: [{ user_id: "new" }], total: 1 });
  await second;
  r[0]({ items: [{ user_id: "old" }], total: 1 });
  await first;
  assert.equal(h.page.playerRows.value[0].user_id, "new");
});
test("one unique player expands", () => {
  const h = setup();
  h.page.togglePlayer({ user_id: 1 });
  h.page.togglePlayer({ user_id: 2 });
  assert.deepEqual([...h.page.expandedKeys.value], ["2"]);
});
test("parallel channel charts preserve API ordering and never mix denominators", () => {
  const rows = [
    {
      user_id: "b",
      channels: {
        ALL: {
          feeding: { rate: 0.8, total: 10, count: 8 },
          receiving: { rate: 0.1 },
        },
        RON: {
          feeding: { rate: 0.25, total: 4, count: 1 },
          receiving: { rate: 0.5 },
        },
      },
    },
    {
      user_id: "a",
      channels: { ALL: { feeding: { rate: 1 }, receiving: { rate: 0.2 } } },
    },
  ];
  const all = directionalChart(rows, "ALL"),
    ron = directionalChart(rows, "RON");
  assert.deepEqual(all.yAxis.data, ["b", "a"]);
  assert.equal(all.series[0].data[0].value, 80);
  assert.equal(ron.series[0].data[0].value, 25);
  assert.equal(ron.series[0].data[1].value, null);
  assert.equal(channelMetric(rows[0], "RON", "feeding").total, 4);
  assert.equal(channelMetric(rows[1], "RON", "feeding"), null);
  assert.equal(all.series[0].markLine.data[0].xAxis, 100 / 3);
  assert.match(
    metricTooltip(channelMetric(rows[0], "RON", "feeding")),
    /1 \/ 4/,
  );
});
test("range percentages convert to inclusive decimal query values and omit blank bounds", () => {
  const q = buildRankingQuery({
    sort_by: "rate",
    filter_by: "",
    min_percent: 0,
    max_percent: 100,
    channel: "RON",
    direction: "receiving",
    order: "desc",
    anomaly: "all",
  });
  assert.equal(q.min_value, 0);
  assert.equal(q.max_value, 1);
  assert.equal(q.filter_by, "rate");
  assert.equal(q.direction, "receiving");
  assert.equal("min_percent" in q, false);
  const empty = buildRankingQuery({
    sort_by: "rate",
    min_percent: null,
    max_percent: "",
  });
  assert.equal("min_value" in empty, false);
  assert.equal("max_value" in empty, false);
  assert.equal(
    buildRankingQuery({
      sort_by: "rate",
      filter_by: "occurrence_probability",
      max_percent: 5,
    }).max_value,
    0.05,
  );
});
test("column filters query the selected direction and channel, reset clears them", async () => {
  let query;
  const h = setup({
    players: async (q) => {
      query = q;
      return { items: [], total: 0 };
    },
  });
  h.page.filters.page = 3;
  h.page.filters.q = "PCX";
  await h.page.handleHeaderFilter({
    channel: "RON",
    direction: "feeding",
    sort_by: "occurrence_probability",
    filter_by: "occurrence_probability",
    order: "asc",
    min_percent: 2,
    max_percent: null,
    anomaly: "all",
  });
  assert.equal(query.order, "asc");
  assert.equal(query.min_value, 0.02);
  assert.equal(query.channel, "RON");
  assert.equal(query.direction, "feeding");
  assert.equal(query.q, "PCX");
  assert.equal(query.page, 1);
  await h.page.resetFilters();
  assert.equal(query.sort_by, "rate");
  assert.equal(query.order, "desc");
  assert.equal(query.direction, "any");
  assert.equal("min_value" in query, false);
});
test("missing metrics never become zero scores", () => {
  assert.equal(percent(null), "—");
  assert.equal(occurrenceProbability(null), "—");
  assert.equal(metricTooltip({ count: 0, total: 0 }), "该类事件 0 次");
});
test("detail requests channel and aggregate rule, then selected rule", async () => {
  let query;
  const watches = [];
  const detail = evaluate(
    "CenterPlayerDetail.vue",
    "load,record,ruleKey,changeRule,handleHeaderFilter",
    {
      defineProps: () => ({
        player: { user_id: "42" },
        windowDays: 7,
        channel: "MELD",
        query: {
          q: "PCX",
          direction: "receiving",
          sort_by: "occurrence_probability",
          order: "asc",
          max_value: 0.05,
          anomaly: "probability",
        },
      }),
      watch: (selector, fn) => watches.push(fn),
      onBeforeUnmount() {},
      rulePoolLabel: (v) => v,
      antiCheatApi: {
        player: async (id, q) => {
          query = { id, ...q };
          return { partners: { items: [], total: 0 } };
        },
      },
    },
  );
  await watches[0]();
  assert.equal(query.id, "42");
  assert.equal(query.channel, "MELD");
  assert.equal("q" in query, false);
  assert.equal(query.direction, "receiving");
  assert.equal(query.order, "asc");
  assert.equal(query.max_value, 0.05);
  assert.equal(query.rule_key, "__ALL__");
  detail.ruleKey.value = "rule";
  await detail.changeRule();
  assert.equal(query.rule_key, "rule");
  await detail.handleHeaderFilter({
    channel: "RON",
    direction: "feeding",
    sort_by: "rate",
    filter_by: "rate",
    order: "desc",
    min_percent: 50,
    max_percent: null,
    anomaly: "all",
  });
  assert.equal(query.channel, "RON");
  assert.equal(query.direction, "feeding");
  assert.equal(query.min_value, 0.5);
  assert.equal("max_value" in query, false);
  assert.equal("q" in query, false);
});
for (const file of [
  "AntiCheat.vue",
  "CenterPlayerDetail.vue",
  "CenterAlgorithmGuide.vue",
  "CenterChart.vue",
  "CenterMetricHeader.vue",
  "CenterRobertReview.vue",
])
  test(`${file} compiles`, () => {
    const { descriptor } = parse(fs.readFileSync(path(file), "utf8"));
    const script = compileScript(descriptor, { id: file });
    const result = compileTemplate({
      source: descriptor.template.content,
      id: file,
      filename: file,
      compilerOptions: { bindingMetadata: script.bindings },
    });
    assert.deepEqual(result.errors, []);
  });
test("no legacy ranking or model settings remain on screen", () => {
  const s =
    fs.readFileSync(path("AntiCheat.vue"), "utf8") +
    fs.readFileSync(path("CenterPlayerDetail.vue"), "utf8");
  assert.doesNotMatch(
    s,
    /suspicionScore|review_status|settingsDraft|pointsChart|scoreBreakdown/,
  );
});

test("header menus keep percent units and never carry another column range", () => {
  let emitted;
  const props = {
    channel: "RON",
    direction: "receiving",
    field: "rate",
    query: {
      channel: "ALL",
      direction: "feeding",
      sort_by: "rate",
      filter_by: "rate",
      order: "desc",
      min_value: 0.9,
      max_value: 1,
      anomaly: "all",
    },
  };
  const menu = evaluate("CenterMetricHeader.vue", "prepare,apply,clear,draft", {
    defineProps: () => props,
    defineEmits: () => (name, value) => {
      emitted = value;
    },
  });
  menu.prepare();
  assert.equal(menu.draft.min, null);
  assert.equal(menu.draft.max, null);
  assert.equal(menu.draft.both, false);
  menu.draft.min = 50;
  menu.apply();
  assert.equal(emitted.channel, "RON");
  assert.equal(emitted.direction, "receiving");
  assert.equal(emitted.sort_by, "rate");
  assert.equal(buildRankingQuery(emitted).min_value, 0.5);
  menu.clear();
  assert.equal("min_value" in buildRankingQuery(emitted), false);
  assert.equal("max_value" in buildRankingQuery(emitted), false);
});

test("probability header defaults to ascending and restores its own applied range", () => {
  let emitted;
  const props = reactive({
    channel: "ALL",
    direction: "feeding",
    field: "occurrence_probability",
    query: {},
  });
  const menu = evaluate("CenterMetricHeader.vue", "prepare,apply,draft", {
    defineProps: () => props,
    defineEmits: () => (name, value) => {
      emitted = value;
    },
  });
  menu.prepare();
  menu.apply();
  assert.equal(emitted.order, "asc");
  props.query = {
    channel: "ALL",
    direction: "any",
    sort_by: "occurrence_probability",
    filter_by: "occurrence_probability",
    order: "asc",
    max_value: 0.05,
    anomaly: "probability",
  };
  menu.prepare();
  assert.equal(menu.draft.max, 5);
  assert.equal(menu.draft.both, true);
  menu.apply();
  assert.equal(emitted.direction, "any");
  assert.equal(buildRankingQuery(emitted).max_value, 0.05);
});

test("probability preserves definitions and threshold edges", () => {
  const four = {
    total: 4,
    occurrence_probability: 1 / 81,
    nist_lower_95: Math.pow(0.05, 1 / 4),
  };
  const ten = {
    total: 10,
    occurrence_probability: Math.pow(1 / 3, 10),
    nist_lower_95: Math.pow(0.05, 1 / 10),
  };
  assert.equal(occurrenceProbability(four), "1.23457%");
  assert.equal(occurrenceProbability(ten), "0.00169351%");
  assert.equal(
    occurrenceProbability({ total: 1, occurrence_probability: 1 }),
    "100%",
  );
  assert.equal(
    occurrenceProbability({ total: 10000, occurrence_probability: 0 }),
    "<0.0001%",
  );
  assert.equal(
    occurrenceProbability({ total: 100, occurrence_probability: 1e-12 }),
    "<0.0001%",
  );
  assert.equal(
    occurrenceProbability({ total: 0, occurrence_probability: 1 }),
    "—",
  );
  assert.equal(
    isUnusualOccurrence({ total: 5, occurrence_probability: 0.05 }),
    true,
  );
  assert.equal(
    isUnusualOccurrence({ total: 5, occurrence_probability: 0.05000001 }),
    false,
  );
  assert.equal(isUnusualOccurrence(null), false);
});

test("a successful guarded query clears a previous range error", async () => {
  let fail = true;
  const h = setup({
    players: async () => {
      if (fail) throw new Error("Invalid range");
      return { items: [], total: 0 };
    },
  });
  await h.page.loadPlayers();
  assert.equal(h.page.error.value, "Invalid range");
  fail = false;
  await h.page.search();
  assert.equal(h.page.error.value, "");
});
test("main ranking starts beside lightweight overview and renders before summary finishes", async () => {
  let finishSummary, finishPlayers;
  const calls = [];
  const h = setup({
    overview: (params) => {
      calls.push(params);
      return new Promise((r) => (finishSummary = r));
    },
    players: () => new Promise((r) => (finishPlayers = r)),
  });
  const pending = h.page.refreshAll();
  assert.equal(calls.length, 1);
  assert.equal(calls[0].detail, "summary");
  assert.equal(typeof finishPlayers, "function");
  finishPlayers({ items: [{ user_id: "ready" }], total: 1 });
  await new Promise((r) => setImmediate(r));
  assert.equal(h.page.playerRows.value[0].user_id, "ready");
  finishSummary({ summary: { last_updated: "v1" }, active_update: null });
  await pending;
});
test("quality fetch is lazy, deduplicated, cached and cannot overwrite light update state", async () => {
  const calls = [];
  let finishQuality;
  const h = setup({
    overview: (params) => {
      calls.push(params);
      if (params)
        return Promise.resolve({
          summary: { last_updated: "v1" },
          active_update: null,
        });
      return new Promise((r) => (finishQuality = r));
    },
  });
  await h.page.refreshAll();
  assert.equal(calls.length, 1);
  const a = h.page.openManagement("quality"),
    b = h.page.managementChanged("quality");
  assert.equal(calls.length, 2);
  assert.equal(calls[1], undefined);
  finishQuality({
    summary: { last_updated: "obsolete" },
    active_update: { id: "obsolete" },
    quality: { cached_games: 12 },
  });
  await Promise.all([a, b]);
  assert.equal(h.page.quality.value.cached_games, 12);
  assert.equal(h.page.overview.value.active_update, null);
  await h.page.openManagement("quality");
  assert.equal(calls.length, 2);
});
test("same-query sorting shares a request while a new page cancels the old GET", async () => {
  const requests = [];
  const h = setup({
    players: (query, config) =>
      new Promise((resolve) =>
        requests.push({ query, signal: config.signal, resolve }),
      ),
  });
  const first = h.page.loadPlayers(),
    duplicate = h.page.loadPlayers();
  assert.equal(requests.length, 1);
  h.page.filters.page = 2;
  const second = h.page.loadPlayers();
  assert.equal(requests.length, 2);
  assert.equal(requests[0].signal.aborted, true);
  requests[1].resolve({ items: [{ user_id: "page2" }], total: 40 });
  await second;
  requests[0].resolve({ items: [{ user_id: "page1" }], total: 40 });
  await Promise.all([first, duplicate]);
  assert.equal(h.page.playerRows.value[0].user_id, "page2");
});
test("update polling reads only summary until completion, then refreshes the current page", async () => {
  let active = true;
  const queries = [];
  const h = setup({
    overview: async () => ({
      summary: { last_updated: active ? "v1" : "v2" },
      active_update: active ? { id: "task" } : null,
    }),
    players: async (q) => {
      queries.push({ ...q });
      return { items: [], total: 100 };
    },
  });
  await h.page.refreshAll();
  await h.page.loadSummary(true);
  assert.equal(queries.length, 1);
  h.page.filters.page = 3;
  await h.page.loadPlayers();
  active = false;
  await h.page.loadSummary(true);
  assert.equal(queries.length, 3);
  assert.equal(queries[2].page, 3);
  assert.equal(h.timers.size, 0);
});
test("summary and ranking failures stay independent", async () => {
  const h = setup({
    overview: async () => {
      throw new Error("Summary failed");
    },
  });
  await h.page.refreshAll();
  assert.equal(h.page.summaryError.value, "Summary failed");
  await h.page.search();
  assert.equal(h.page.summaryError.value, "Summary failed");
});
test("each direction exposes distinct all-event and ron probability columns", () => {
  const probability = deliveryColumns.filter(
    (c) => c.field === "occurrence_probability",
  );
  assert.equal(probability.length, 2);
  assert.deepEqual(
    probability.map((c) => c.channel),
    ["ALL", "RON"],
  );
  assert.equal(probability[0].label, "全口径出现概率");
  assert.equal(probability[1].label, "和牌出现概率");
  assert.equal(deliveryColumns.filter((c) => c.field === "rate").length, 2);
  assert.equal(
    deliveryColumns.filter((c) => c.field === "nist_lower_95").length,
    0,
  );
  assert.equal(
    deliveryColumns.some((c) => c.field === "coplay_rate"),
    true,
  );
});
test("an update does not reuse a summary response requested before its POST", async () => {
  let finishOld;
  let reads = 0;
  const h = setup({
    overview: () => {
      reads++;
      return reads === 1
        ? new Promise((r) => (finishOld = r))
        : Promise.resolve({
            summary: { last_updated: "v1" },
            active_update: { id: "new" },
          });
    },
  });
  const initial = h.page.refreshAll();
  const update = h.page.updateData();
  await new Promise((r) => setImmediate(r));
  finishOld({ summary: { last_updated: "v1" }, active_update: null });
  await Promise.all([initial, update]);
  assert.equal(reads, 2);
  assert.equal(h.page.overview.value.active_update.id, "new");
  assert.equal(h.timers.size, 1);
});

test("pagination duplicate events reuse the same request without forcing a reload", async () => {
  const requests = [];
  const h = setup({
    players: () => new Promise((resolve) => requests.push(resolve)),
  });
  const a = h.page.changePage(2),
    b = h.page.changePage(2);
  assert.equal(requests.length, 1);
  requests[0]({ items: [], total: 40 });
  await Promise.all([a, b]);
  assert.equal(h.page.filters.page, 2);
});
test("quick table queries never show a mask; slower queries clear it on completion", async () => {
  const quick = setup();
  await quick.page.loadPlayers();
  assert.equal(quick.page.playerLoading.value, false);
  assert.equal(quick.timers.size, 0);
  let finish;
  const slow = setup({
    players: () => new Promise((resolve) => (finish = resolve)),
  });
  const pending = slow.page.loadPlayers();
  assert.equal(slow.page.playerLoading.value, false);
  [...slow.timers.values()][0]();
  assert.equal(slow.page.playerLoading.value, true);
  finish({ items: [], total: 0 });
  await pending;
  assert.equal(slow.page.playerLoading.value, false);
  assert.equal(slow.timers.size, 0);
});
test("probability anomaly shortcut uses custom inclusive ceiling on the exact column", () => {
  for (const channel of ["ALL", "RON"])
    for (const direction of ["feeding", "receiving"]) {
      let emitted;
      const header = evaluate(
        "CenterMetricHeader.vue",
        "prepare,draft,applyAnomaly",
        {
          defineProps: () => ({
            channel,
            direction,
            field: "occurrence_probability",
            query: {},
          }),
          defineEmits: () => (_event, value) => (emitted = value),
        },
      );
      header.prepare();
      assert.equal(header.draft.threshold, 5);
      header.draft.threshold = 12.5;
      header.applyAnomaly();
      const q = buildRankingQuery(emitted);
      assert.equal(q.channel, channel);
      assert.equal(q.direction, direction);
      assert.equal(q.max_value, 0.125);
      assert.equal(q.min_value, undefined);
      assert.equal(q.anomaly, "all");
      assert.equal(q.order, "asc");
    }
});
test("active columns and help contain no retired confidence lower limit", () => {
  for (const file of [
    "AntiCheat.vue",
    "CenterPlayerDetail.vue",
    "CenterMetricHeader.vue",
    "CenterAlgorithmGuide.vue",
  ])
    assert.doesNotMatch(
      fs.readFileSync(path(file), "utf8"),
      /NIST|nist_lower_95|置信下限/,
    );
});

function robertSetup(direction = "feeding", overrides = {}) {
  const timers = new Map(),
    watches = [],
    calls = [];
  let next = 0;
  const props = {
    modelValue: true,
    selection: {
      player: { user_id: "A" },
      partner: { user_id: "B" },
      direction,
      window_days: 30,
      rule_key: "GUOBIAO",
      channel: "RON",
    },
  };
  const state = evaluate(
    "CenterRobertReview.vue",
    "start,cancel,poll,job,channel,error,busy,thresholds,differenceText,probabilityText",
    {
      defineProps: () => props,
      defineEmits: () => () => {},
      watch: (fn, handler) => watches.push(handler),
      onBeforeUnmount() {},
      setTimeout: (f) => {
        timers.set(++next, f);
        return next;
      },
      clearTimeout: (id) => timers.delete(id),
      antiCheatApi: {
        startRobertReview: async (body) => {
          calls.push(body);
          return {
            id: "r1",
            status: "running",
            progress: { done: 0, total: 4 },
          };
        },
        robertReview: async () => ({
          id: "r1",
          status: "completed",
          result: {
            feeding: { numerator: 1, denominator: 4, rate: 0.25 },
            normal: { numerator: 9, denominator: 10, rate: 0.9 },
            regular_deviation: { numerator: 1, denominator: 10, rate: 0.1 },
            comparison: { difference_points: 15, p_greater: 46 / 91 },
          },
        }),
        cancelRobertReview: async () => ({ id: "r1", status: "cancelled" }),
        ...overrides,
      },
    },
  );
  watches[0](props.selection);
  return { state, calls, timers, props };
}
test("Robert review does not run on opening and submits exact selected direction only on start", async () => {
  for (const direction of ["feeding", "receiving"]) {
    const h = robertSetup(direction);
    assert.equal(h.calls.length, 0);
    assert.equal(h.timers.size, 0);
    await h.state.start();
    assert.equal(h.calls.length, 1);
    assert.equal(h.calls[0].donor_id, direction === "feeding" ? "A" : "B");
    assert.equal(h.calls[0].recipient_id, direction === "feeding" ? "B" : "A");
    assert.equal(h.calls[0].channel, "RON");
    assert.equal(h.calls[0].window_days, 30);
    assert.equal(h.calls[0].thresholds.shanten, 1);
    assert.equal(h.calls[0].thresholds.reshape_progress, 24);
    await h.state.poll();
    assert.equal(h.state.job.value.result.feeding.denominator, 4);
    assert.equal(h.state.job.value.result.regular_deviation.rate, 0.1);
    assert.equal(h.state.job.value.result.comparison.p_greater, 46 / 91);
    assert.equal(h.timers.size, 0);
  }
});
test("Robert review guards double starts and cancels its active job", async () => {
  const h = robertSetup();
  await h.state.start();
  await h.state.start();
  assert.equal(h.calls.length, 1);
  await h.state.cancel();
  assert.equal(h.state.job.value.status, "cancelled");
  assert.equal(h.timers.size, 0);
});
test("Robert errors and unavailable ratios are never shown as zero agreement", async () => {
  const h = robertSetup("feeding", {
    startRobertReview: async () => ({
      id: "r1",
      status: "failed",
      error: "牌谱不支持",
    }),
  });
  await h.state.start();
  assert.equal(h.state.error.value, "牌谱不支持");
  assert.equal(h.timers.size, 0);
  assert.equal(percent(null), "—");
});

test("a review lost on server restart stops polling and permits a new explicit request", async () => {
  const h = robertSetup("feeding", {
    robertReview: async () => {
      throw Object.assign(new Error("复核任务不存在"), { response: { status: 404 } });
    },
  });
  await h.state.start();
  await h.state.poll();
  assert.equal(h.state.busy.value, false);
  assert.equal(h.timers.size, 0);
  assert.equal(h.state.job.value.status, "failed");
  await h.state.start();
  assert.equal(h.calls.length, 2);
});

test("Robert comparisons retain the sign of differences and the precision of small probabilities", () => {
  const { differenceText, probabilityText } = robertSetup().state;
  assert.equal(differenceText(22.4441583), "+22.44 个百分点");
  assert.equal(differenceText(-2.5), "-2.5 个百分点");
  assert.equal(differenceText(0), "0 个百分点");
  assert.equal(differenceText(null), "—");
  assert.equal(probabilityText(1.42059651706084e-8), "0.000001421%");
  assert.equal(probabilityText(1e-15), "<0.00000001%");
  assert.equal(probabilityText(0.9999999), ">99.99%");
  assert.equal(probabilityText(1), "100%");
  assert.equal(probabilityText(null), "—");
});

test("Robert threshold controls match server limits and compare both deviation rates", () => {
  const state = evaluate("CenterRobertReview.vue", "thresholdFields", {
    defineProps: () => ({}),
    defineEmits: () => () => {},
    watch() {},
    onBeforeUnmount() {},
    setTimeout() {},
    clearTimeout() {},
    antiCheatApi: {},
  });
  assert.deepEqual(
    Array.from(state.thresholdFields, (f) => [f.key, f.max]),
    [
      ["shanten", 8],
      ["legal_ukeire", 136],
      ["ukeire", 136],
      ["fan", 1000],
      ["ron_ukeire", 136],
      ["reshape_progress", 20000],
    ],
  );
  const source = fs.readFileSync(path("CenterRobertReview.vue"), "utf8");
  assert.match(source, /常规出牌偏离率/);
  assert.match(source, /喂牌偏高的出现概率/);
  assert.doesNotMatch(source, /常规出牌(一致率|正常率)/);
  assert.match(source, /:max="field.max"/);
});
