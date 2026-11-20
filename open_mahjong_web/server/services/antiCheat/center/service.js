"use strict";
const crypto = require("crypto"),
  path = require("path"),
  { fork } = require("child_process");
const {
  SCHEMA: S,
  DEFAULTS,
  settings: validate,
  fail,
  initialize,
} = require("./database");
const { LOCK } = require("./processor");
const { withRobertComparison } = require("./robertComparison");
const {inventoryPredicate,SCOPE_VERSION}=require("./rankedScope");
function pagination(q = {}) {
  const page = Number(q.page || 1),
    size = Number(q.page_size || 20);
  if (
    !Number.isInteger(page) ||
    page < 1 ||
    !Number.isInteger(size) ||
    size < 1 ||
    size > 100
  )
    fail("分页参数无效");
  return { page, page_size: size, offset: (page - 1) * size };
}
function windowDays(q = {}) {
  const n = Number(q.window_days ?? 0);
  if (!Number.isInteger(n) || n < 0 || n > 3650) fail("窗口无效");
  return n;
}
function createCenter({ pool, launch, clock = () => new Date() } = {}) {
  let init;
  const ready = () =>
    (init ||= initialize(pool).catch((e) => {
      init = null;
      throw e;
    }));
  async function reconcile() {
    await ready();
    const c = await pool.connect();
    let free = false;
    try {
      free = (await c.query("SELECT pg_try_advisory_lock($1) AS free", [LOCK]))
        .rows[0].free;
      if (free)
        await c.query(
          `UPDATE ${S}.updates SET status=CASE WHEN cancel_requested THEN 'cancelled' ELSE 'interrupted' END,finished_at=now(),error=CASE WHEN cancel_requested THEN '更新已取消，已提交的逐场特征已保留' ELSE '进程已结束，点击更新可从逐场缓存继续' END WHERE status IN ('queued','scanning','analyzing','statistics') AND COALESCE(heartbeat,created_at)<now()-interval '60 seconds'`,
        );
    } finally {
      if (free) await c.query("SELECT pg_advisory_unlock($1)", [LOCK]);
      c.release();
    }
  }
  async function meta() {
    await ready();
    return (await pool.query(`SELECT * FROM ${S}.meta WHERE id=1`)).rows[0];
  }
  async function getSettings() {
    return (await meta()).settings;
  }
  async function saveSettings(input, userId) {
    await ready();
    const value = validate(input),
      c = await pool.connect();
    try {
      await c.query("BEGIN");
      const current = (
        await c.query(`SELECT settings FROM ${S}.meta WHERE id=1 FOR UPDATE`)
      ).rows[0].settings;
      if (current.revision !== value.revision)
        fail("配置已被更新，请重新读取后保存", 409);
      value.revision += 1;
      await c.query(`UPDATE ${S}.meta SET settings=$1 WHERE id=1`, [
        JSON.stringify(value),
      ]);
      await c.query(
        `INSERT INTO ${S}.audit(actor,action,detail) VALUES($1,'settings.changed',$2)`,
        [userId, JSON.stringify({ before: current, after: value })],
      );
      await c.query("COMMIT");
      return value;
    } catch (e) {
      await c.query("ROLLBACK");
      throw e;
    } finally {
      c.release();
    }
  }
  async function updates(q = {}) {
    await ready();
    const p = pagination(q),
      rows = (
        await pool.query(
          `SELECT *,count(*) OVER()::int AS full_count FROM ${S}.updates ORDER BY created_at DESC LIMIT $1 OFFSET $2`,
          [p.page_size, p.offset],
        )
      ).rows;
    return {
      items: rows.map(({ full_count, ...r }) => r),
      total: rows[0]?.full_count || 0,
      page: p.page,
      page_size: p.page_size,
    };
  }
  async function update(id) {
    await ready();
    const row = (
      await pool.query(`SELECT * FROM ${S}.updates WHERE id=$1`, [id])
    ).rows[0];
    if (!row) fail("更新记录不存在", 404);
    return row;
  }
  async function startUpdate(userId) {
    await ready();
    const c = await pool.connect();
    let id;
    try {
      await c.query("BEGIN");
      await c.query("SELECT pg_advisory_xact_lock($1)", [LOCK + 1]);
      const rows = (
        await c.query(
          `SELECT * FROM ${S}.updates WHERE status IN ('queued','scanning','analyzing','statistics') ORDER BY created_at DESC`,
        )
      ).rows;
      if (rows.length) {
        const running = (
          await c.query("SELECT pg_try_advisory_xact_lock($1) AS available", [
            LOCK,
          ])
        ).rows[0];
        if (
          !running.available ||
          Date.now() - new Date(rows[0].created_at).getTime() < 60000
        ) {
          await c.query("COMMIT");
          return rows[0];
        }
        await c.query(
          `UPDATE ${S}.updates SET status='interrupted',finished_at=now(),error='上次进程已结束，逐场缓存已保留' WHERE status IN ('queued','scanning','analyzing','statistics')`,
        );
      }
      id = crypto.randomUUID();
      const cfg = (await c.query(`SELECT settings FROM ${S}.meta WHERE id=1`))
        .rows[0].settings;
      await c.query(
        `INSERT INTO ${S}.updates(id,status,requested_by,settings,progress) VALUES($1,'queued',$2,$3,$4)`,
        [
          id,
          userId,
          JSON.stringify(cfg),
          JSON.stringify({
            message: "等待增量检查",
            processed: 0,
            total: null,
          }),
        ],
      );
      await c.query(
        `INSERT INTO ${S}.audit(actor,action,detail) VALUES($1,'update.requested',$2)`,
        [userId, JSON.stringify({ update_id: id })],
      );
      await c.query("COMMIT");
    } catch (e) {
      await c.query("ROLLBACK");
      throw e;
    } finally {
      c.release();
    }
    if (launch) launch(id);
    else {
      const child = fork(path.join(__dirname, "worker.js"), [id], {
        stdio: "ignore",
        detached: true,
        windowsHide: true,
        env: process.env,
      });
      child.unref();
    }
    return update(id);
  }
  async function cancel(id, userId) {
    const row = await update(id);
    if (!["queued", "scanning", "analyzing", "statistics"].includes(row.status))
      fail("更新已结束", 409);
    await pool.query(
      `UPDATE ${S}.updates SET cancel_requested=true WHERE id=$1`,
      [id],
    );
    await pool.query(
      `INSERT INTO ${S}.audit(actor,action,detail) VALUES($1,'update.cancel',$2)`,
      [userId, JSON.stringify({ update_id: id })],
    );
    return update(id);
  }
  async function overview(q = {}) {
    await reconcile();
    const m = await meta();
    const source = Number(
      (await pool.query(`SELECT count(*)::int AS n FROM ${S}.scope_source_inventory`))
        .rows[0].n,
    );
    const active =
      (
        await pool.query(
          `SELECT * FROM ${S}.updates WHERE status IN ('queued','scanning','analyzing','statistics') ORDER BY created_at DESC LIMIT 1`,
        )
      ).rows[0] || null;
    if (q.detail === "summary") return {
      detail: "summary",
      scope_version:SCOPE_VERSION,
      model_ready: m.model_version === require("./populationPipeline").VERSION,
      summary: { source_games: source, source_count_basis:"last_explicit_scope_scan", last_updated: m.as_of },
      active_update: active,
      settings: m.settings,
      model_revision: m.model_revision,
      settings_pending: !!m.active_generation &&
        (m.model_revision !== m.settings.revision || m.model_version !== require("./populationPipeline").VERSION),
    };
    const summary = (
      await pool.query(`SELECT count(*)::int AS cached_games,count(*) FILTER(WHERE status IN ('ready','unsupported'))::int AS analyzed_games,count(*) FILTER(WHERE status='failed')::int AS failed_games,count(*) FILTER(WHERE status='partial')::int AS partial_games,count(*) FILTER(WHERE status='invalid')::int AS invalid_records,
      count(*) FILTER(WHERE status='unsupported')::int AS unsupported_games,
      COALESCE(sum(COALESCE((payload#>>'{quality,completed_hands}')::int,0)),0)::int AS completed_hands,
      COALESCE(sum(COALESCE((payload#>>'{quality,reconstructed_hands}')::int,0)),0)::int AS reconstructed_hands,
      COALESCE(sum(COALESCE((payload#>>'{quality,opportunity_verified_hands}')::int,0)),0)::int AS opportunity_verified_hands,
      COALESCE(sum(COALESCE((payload#>>'{quality,scored_hands}')::int,0)),0)::int AS scored_hands FROM ${S}.games g WHERE ${inventoryPredicate("g")}`)
    ).rows[0];
    const pairSummary = m.active_generation && m.model_version === require("./populationPipeline").VERSION
      ? (
          await pool.query(
            `SELECT count(*)::int AS pair_count,count(*) FILTER(WHERE flag_all OR flag_meld OR flag_ron)::int AS finding_count,
      count(*) FILTER(WHERE flag_ron)::int AS ron_findings,count(*) FILTER(WHERE flag_all)::int AS all_findings,count(*) FILTER(WHERE flag_meld)::int AS meld_findings FROM ${S}.published_pairs WHERE generation=$1 AND window_days=0 AND rule_key='__ALL__'`,
            [m.active_generation],
          )
        ).rows[0]
      : {};
    const players = m.active_generation && m.model_version === require("./populationPipeline").VERSION
      ? Number(
          (
            await pool.query(
              `SELECT count(DISTINCT user_id)::int AS n FROM ${S}.published_players WHERE generation=$1 AND window_days=0 AND rule_key='__ALL__'`,
              [m.active_generation],
            )
          ).rows[0].n,
        )
      : 0;
    const issues = (
      await pool.query(
        `SELECT DISTINCT g.game_id,e->>'code' AS code,e->>'message' AS message FROM ${S}.games g CROSS JOIN LATERAL jsonb_array_elements(COALESCE(g.payload#>'{quality,issues}','[]'::jsonb)) e WHERE ${inventoryPredicate("g")} ORDER BY code,game_id LIMIT 50`,
      )
    ).rows;
    const failures = (
      await pool.query(
        `SELECT game_id,COALESCE(payload#>>'{quality,error_code}',status) AS code,payload#>>'{quality,error}' AS message FROM ${S}.games g WHERE ${inventoryPredicate("g")} AND status IN ('failed','partial','invalid') ORDER BY processed_at DESC LIMIT 20`,
      )
    ).rows;
    const modelReady = m.model_version === require("./populationPipeline").VERSION;
    return {
      scope_version:SCOPE_VERSION,
      model_ready: modelReady,
      model_description: {
        version: m.model_version, baseline: 1 / 3, probability_threshold: 0.05,
        method: "exact_binomial_upper_tail",
        method_description: "仅国标四人标准段位场；出现概率为固定 1/3 基准下达到当前次数或更多的二项上尾概率。",
      },
      summary: {
        ...summary,
        ...pairSummary,
        source_games: source,
        source_count_basis:"last_explicit_scope_scan",
        pending_games: Math.max(
          0,
          source - summary.analyzed_games - summary.invalid_records,
        ),
        player_count: players,
        last_updated: m.as_of,
      },
      quality: {
        ...summary,
        issues: [...failures, ...issues].slice(0, 50),
        issues_limit: 50,
      },
      settings: m.settings,
      active_update: active,
      model_revision: m.model_revision,
      settings_pending:
        !!m.active_generation &&
        (m.model_revision !== m.settings.revision || !modelReady),
      statistical_notes: [
        "出现概率直接显示二项上尾概率，不是作弊概率，未作多重检验校正。",
        "同桌率=共同场数÷当前玩家场数，单独显示。",
      ],
    };
  }
  async function listPairs(q = {}) {
    const m = await meta(), p = pagination(q), days = windowDays(q);
    const channel = require("./playerView").channelOf(q);
    if (!m.active_generation || m.model_version !== require("./populationPipeline").VERSION)
      return { items: [], total: 0, page: p.page, page_size: p.page_size };
    const args = [m.active_generation, days, q.rule_key || "__ALL__", channel];
    const where = ["generation=$1", "window_days=$2", "rule_key=$3", "$4::text IS NOT NULL"];
    if (q.q) {
      args.push("%" + String(q.q).trim().slice(0, 120) + "%");
      where.push(`(donor_id ILIKE $${args.length} OR recipient_id ILIKE $${args.length} OR donor_name ILIKE $${args.length} OR recipient_name ILIKE $${args.length})`);
    }
    if (q.anomalies === "true")
      where.push("((payload->'feeding'->$4->>'flag')::boolean OR (payload->'receiving'->$4->>'flag')::boolean)");
    const condition = where.join(" AND ");
    const total = Number((await pool.query(`SELECT count(*)::int n FROM ${S}.published_pairs WHERE ${condition}`, args)).rows[0].n);
    const rows = (await pool.query(`SELECT payload FROM ${S}.published_pairs WHERE ${condition}
      ORDER BY GREATEST((payload->'feeding'->$4->>'rate')::float,(payload->'receiving'->$4->>'rate')::float) DESC NULLS LAST,shared_games DESC,pair_id
      LIMIT $${args.length + 1} OFFSET $${args.length + 2}`, [...args, p.page_size, p.offset])).rows;
    return { items: rows.map(r => r.payload), total, page: p.page, page_size: p.page_size };
  }
  const { listPlayers, player } = require("./playerView").createPlayerView({
    pool,
    meta,
    pagination,
    windowDays,
  });
  let robertReviews;
  const reviewsForDiscards = () => robertReviews ||= require("./robertReviews").createRobertReviews({
    pool, meta, ...require("./robertSource").createRobertSource(pool),
  });
  async function pair(key, q = {}) {
    const m = await meta();
    if (!m.active_generation || m.model_version !== require("./populationPipeline").VERSION) fail("统计范围尚未更新",409);
    if (typeof key !== "string" || key.length > 120) fail("玩家对标识无效");
    const row = (
      await pool.query(
        `SELECT * FROM ${S}.published_pairs WHERE generation=$1 AND pair_key=$2`,
        [m.active_generation, key],
      )
    ).rows[0];
    if (!row) fail("玩家对不存在", 404);
    const p = pagination({
      page: q.evidence_page || 1,
      page_size: q.evidence_page_size || 20,
    });
    const cutoff =
      row.window_days === 0
        ? null
        : new Date(
            new Date(m.as_of).getTime() - row.window_days * 86400000,
          ).toISOString();
    const params = [row.donor_id, row.recipient_id, row.rule_key, cutoff, m.as_of,m.active_generation];
    const cond = `e.payload->>'donor_id'=$1 AND e.payload->>'recipient_id'=$2
      AND EXISTS(SELECT 1 FROM ${S}.published_scope_games scoped WHERE scoped.generation=$6::uuid AND scoped.game_id=g.game_id AND scoped.source_hash=g.source_hash) AND ${inventoryPredicate("g")} AND ($3='__ALL__' OR g.rule_key=$3) AND ($4::timestamptz IS NULL OR e.created_at>=$4) AND e.created_at<=$5`;
    const from = `${S}.evidence e JOIN ${S}.games g ON g.game_id=e.game_id`;
    const n = Number((await pool.query(`SELECT count(*)::int n FROM ${from} WHERE ${cond}`, params)).rows[0].n);
    const ev = (await pool.query(`SELECT e.payload FROM ${from} WHERE ${cond}
      ORDER BY e.created_at DESC,e.game_id,e.ordinal LIMIT $7 OFFSET $8`, [...params,p.page_size,p.offset])).rows.map(r=>r.payload);
    const review = (await pool.query(`SELECT status,note,updated_at,updated_by FROM ${S}.reviews WHERE pair_key=$1`, [key])).rows[0] || null;
    const latestFacts = (
      await pool.query(`SELECT facts_revision FROM ${S}.meta WHERE id=1`)
    ).rows[0].facts_revision;
    const evidenceConsistent =
      String(m.stats_facts_revision) === String(m.facts_revision) &&
      String(latestFacts) === String(m.facts_revision);
    return {
      ...row.payload,
      evidence: {
        items: evidenceConsistent ? ev : [],
        total: evidenceConsistent ? n : 0,
        page: p.page,
        page_size: p.page_size,
        status: evidenceConsistent ? "ready" : "pending_update",
        message: evidenceConsistent
          ? null
          : "逐场事实正在更新，发布完成后显示与本版统计一致的证据",
      },
      review,
    };
  }
  async function review(key, input, userId) {
    await pair(key, { evidence_page_size: 1 });
    if (
      !["observe", "review", "explained"].includes(input.status) ||
      typeof input.note !== "string" ||
      input.note.length > 2000
    )
      fail("复核内容格式错误");
    return (
      await pool.query(
        `INSERT INTO ${S}.reviews(pair_key,status,note,updated_by) VALUES($1,$2,$3,$4) ON CONFLICT(pair_key) DO UPDATE SET status=excluded.status,note=excluded.note,updated_by=excluded.updated_by,updated_at=now() RETURNING *`,
        [key, input.status, input.note, userId],
      )
    ).rows[0];
  }
  return {
    ready,
    overview,
    getSettings,
    saveSettings,
    startUpdate,
    updates,
    update,
    cancel,
    listPairs,
    listPlayers,
    player,
    startRobertReview: (body) => reviewsForDiscards().start(body).then(withRobertComparison),
    getRobertReview: (id) => reviewsForDiscards().get(id).then(withRobertComparison),
    cancelRobertReview: (id) => reviewsForDiscards().cancel(id).then(withRobertComparison),
    pair,
    review,
  };
}
let instance;
function defaultCenter() {
  return (instance ||= createCenter({
    pool: require("../../../config/database"),
  }));
}
module.exports = { createCenter, defaultCenter, pagination, windowDays };
