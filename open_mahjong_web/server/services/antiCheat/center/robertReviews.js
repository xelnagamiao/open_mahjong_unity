"use strict";

// Explicit, CPU-serialized Robert review jobs. GETs never launch calculations.
const crypto = require("node:crypto"),
  fs = require("node:fs"),
  path = require("node:path"),
  { fork } = require("node:child_process");
const {
  analyzeRobertDiscards,
  DEFAULT_THRESHOLDS,
  THRESHOLD_LIMITS,
  ROBERT_VERSION,
} = require("./robert");
const { lowerPriority } = require("../pythonWorkerPool");
const { createLoadPacer } = require("./loadPacer");
const ROBERT_LOCK = 170114079;
let cachedPolicyVersion = null;
let running = false;
const workQueue = [];
function drain() {
  if (running) return;
  const run = workQueue.shift();
  if (!run) return;
  running = true;
  Promise.resolve()
    .then(run)
    .catch(() => {})
    .finally(() => {
      running = false;
      drain();
    });
}
function enqueue(run) {
  workQueue.push(run);
  queueMicrotask(drain);
}
function fail(message, status = 400) {
  throw Object.assign(new Error(message), { status });
}
function policyVersion() {
  if (cachedPolicyVersion) return cachedPolicyVersion;
  const ai = path.resolve(
    __dirname,
    "../../../../../open_mahjong_server/server/gamestate/public/ai",
  );
  const calculation = path.resolve(ai, "../../../game_calculation");
  const files = [
    path.join(__dirname, "robert.js"),
    path.join(__dirname, "robertReviews.js"),
    path.join(__dirname, "robertBridge.py"),
    path.join(__dirname, "../replay.js"),
    ...[
      "guobiao_heuristic_logic.py",
      "guobiao_shanten.py",
      "smart_bot_logic.py",
    ].map((f) => path.join(ai, f)),
    ...["guobiao_hepai_check.py", "gb_tingpai_check.py"].map((f) =>
      path.join(calculation, f),
    ),
  ];
  const digest = crypto.createHash("sha256");
  for (const file of files) {
    digest.update(path.basename(file));
    digest.update(fs.readFileSync(file));
  }
  cachedPolicyVersion = `${ROBERT_VERSION}:${digest.digest("hex").slice(0, 24)}`;
  return cachedPolicyVersion;
}

async function ensureRobertReviewTables(pool) {
  await pool.query(
    `CREATE SCHEMA IF NOT EXISTS anti_cheat_center;
     CREATE TABLE IF NOT EXISTS anti_cheat_center.robert_discard_cache (
       cache_key text PRIMARY KEY,game_id text NOT NULL,source_hash text NOT NULL,donor_id text NOT NULL,
       policy_version text NOT NULL,payload jsonb NOT NULL,created_at timestamptz NOT NULL DEFAULT now());
     CREATE TABLE IF NOT EXISTS anti_cheat_center.robert_reviews (
       id uuid PRIMARY KEY,
       signature text NOT NULL,
       status text NOT NULL,
       created_at timestamptz NOT NULL DEFAULT now(),
       started_at timestamptz,
       finished_at timestamptz,
       heartbeat timestamptz,
       cancel_requested boolean NOT NULL DEFAULT false,
       request jsonb NOT NULL,
       snapshot jsonb NOT NULL,
       policy_version text NOT NULL,
       games jsonb NOT NULL DEFAULT '[]',
       progress jsonb NOT NULL DEFAULT '{"done":0,"total":0}',
       result jsonb,
       error text
     );
     CREATE INDEX IF NOT EXISTS ac_robert_reviews_active ON anti_cheat_center.robert_reviews(status, created_at DESC);`,
  );
}

function formatDbReview(row) {
  return {
    id: row.id,
    status: row.status,
    created_at:
      row.created_at instanceof Date
        ? row.created_at.toISOString()
        : row.created_at,
    finished_at:
      row.finished_at instanceof Date
        ? row.finished_at.toISOString()
        : row.finished_at ?? undefined,
    request: row.request,
    progress: row.progress,
    result: row.result ?? undefined,
    error: row.error ?? undefined,
  };
}
function validate(body) {
  if (!body || typeof body !== "object" || Array.isArray(body))
    fail("复核请求格式错误");
  const out = {};
  for (const key of ["donor_id", "recipient_id"]) {
    if (!/^[1-9]\d{0,19}$/.test(String(body[key] ?? "")))
      fail("请输入有效玩家编号");
    out[key] = String(body[key]);
  }
  if (out.donor_id === out.recipient_id) fail("出牌者和目标伙伴必须不同");
  out.channel = body.channel || "RON";
  if (!["ALL", "MELD", "RON"].includes(out.channel)) fail("复核口径无效");
  out.window_days = Number(body.window_days ?? 0);
  if (
    !Number.isInteger(out.window_days) ||
    out.window_days < 0 ||
    out.window_days > 3650
  )
    fail("复核窗口无效");
  if (
    typeof body.rule_key !== "string" ||
    !body.rule_key ||
    body.rule_key.length > 1000
  )
    fail("规则池无效");
  out.rule_key = body.rule_key;
  out.thresholds = { ...DEFAULT_THRESHOLDS };
  if (body.thresholds !== undefined) {
    if (
      !body.thresholds ||
      typeof body.thresholds !== "object" ||
      Array.isArray(body.thresholds)
    )
      fail("差距阈值格式错误");
    for (const [key, value] of Object.entries(body.thresholds)) {
      const limit = THRESHOLD_LIMITS[key];
      if (
        !limit ||
        typeof value !== "number" ||
        !Number.isFinite(value) ||
        value < limit.min ||
        value > limit.max ||
        (limit.integer && !Number.isInteger(value))
      )
        fail("差距阈值超出允许范围");
      out.thresholds[key] = value;
    }
  }
  return out;
}
const metricKey = {
  effective_shanten: "shanten",
  structural_shanten: "shanten",
  legal_ukeire: "legal_ukeire",
  ukeire: "ukeire",
  fan: "fan",
  ron_ukeire: "ron_ukeire",
  reshape_progress: "reshape_progress",
};
function classify(row, thresholds) {
  if (
    row.status !== "available" ||
    row.rate_comparable === false ||
    row.is_recommended
  )
    return false;
  const key = metricKey[row.gap?.deciding_metric],
    value = row.gap?.value;
  return (
    !!key &&
    typeof value === "number" &&
    Number.isFinite(value) &&
    value >= thresholds[key]
  );
}
function compact(result) {
  return {
    status: result.status,
    version: result.version,
    policy_source_sha256: result.policy_source_sha256,
    eligible_decisions: result.eligible_decisions,
    analyzed_decisions: result.analyzed_decisions,
    policy_evaluated_decisions: result.policy_evaluated_decisions,
    calculator_unavailable_decisions: result.calculator_unavailable_decisions,
    unavailable_decisions: result.unavailable_decisions || 0,
    truncated: !!result.truncated,
    skipped_unreconstructed_hands: result.skipped_unreconstructed_hands || 0,
    decisions: (result.decisions || []).map((d) => ({
      id: d.id,
      status: d.status,
      reason: d.reason,
      game_id: d.game_id,
      hand_index: d.hand_index,
      tick_index: d.tick_index,
      donor_id: d.donor_id,
      actual_tile: d.actual_tile,
      events: d.events || [],
      recommended_tile: d.recommended_tile,
      acceptable_tiles: d.acceptable_tiles,
      is_recommended: d.is_recommended,
      comparison_status: d.comparison_status,
      rate_comparable: d.rate_comparable,
      gap: d.gap,
    })),
  };
}
function createRobertReviews({
  pool,
  meta,
  findGames,
  readRecord,
  analyzer = analyzeRobertDiscards,
  cache,
  clock = () => new Date(),
  version = policyVersion,
  maxQueued = 8,
  maxTasks = 100,
  background,
  launch,
} = {}) {
  if (
    typeof meta !== "function" ||
    typeof findGames !== "function" ||
    typeof readRecord !== "function"
  )
    throw new TypeError("Robert review requires local source callbacks");
  const useBackgroundWorker =
    background !== undefined
      ? Boolean(background)
      : Boolean(pool && analyzer === analyzeRobertDiscards && !cache);
  const tasks = new Map();
  let planning = Promise.resolve(),
    closed = false,
    cacheReady;
  const persistent = cache || {
    async ready() {
      if (!pool) throw new Error("缺少本地基准缓存连接");
      return (cacheReady ||= ensureRobertReviewTables(pool).catch((e) => {
        cacheReady = null;
        throw e;
      }));
    },
    async get(key) {
      await this.ready();
      return (
        (
          await pool.query(
            "SELECT payload FROM anti_cheat_center.robert_discard_cache WHERE cache_key=$1",
            [key],
          )
        ).rows[0]?.payload || null
      );
    },
    async put(key, data, entry, donor, policy) {
      await this.ready();
      await pool.query(
        `INSERT INTO anti_cheat_center.robert_discard_cache(cache_key,game_id,source_hash,donor_id,policy_version,payload)
      VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(cache_key) DO UPDATE SET payload=excluded.payload`,
        [
          key,
          entry.game_id,
          entry.source_hash,
          donor,
          policy,
          JSON.stringify(data),
        ],
      );
    },
  };

  function spawnBackgroundWorker() {
    if (launch) {
      launch();
      return;
    }
    const child = fork(path.join(__dirname, "robertReviewWorker.js"), [], {
      stdio: "ignore",
      detached: true,
      windowsHide: true,
      env: process.env,
    });
    lowerPriority(child.pid);
    child.unref();
  }

  async function reconcilePersistent() {
    await persistent.ready();
    const c = await pool.connect();
    let free = false;
    try {
      free = (
        await c.query("SELECT pg_try_advisory_lock($1) AS free", [ROBERT_LOCK])
      ).rows[0].free;
      if (free) {
        await c.query(
          `UPDATE anti_cheat_center.robert_reviews
             SET status = CASE WHEN cancel_requested THEN 'cancelled' ELSE 'failed' END,
                 finished_at = now(),
                 error = CASE WHEN cancel_requested THEN '复核已取消' ELSE '后台复核进程已中断，请重新发起复核' END
           WHERE status = 'running'
             AND COALESCE(heartbeat, started_at, created_at) < now() - interval '60 seconds'`,
        );
      }
    } finally {
      if (free) await c.query("SELECT pg_advisory_unlock($1)", [ROBERT_LOCK]);
      c.release();
    }
  }
  const publicTask = (t) =>
    JSON.parse(
      JSON.stringify({
        id: t.id,
        status: t.status,
        created_at: t.created_at,
        finished_at: t.finished_at,
        request: t.request,
        progress: t.progress,
        result: t.result,
        error: t.error,
      }),
    );
  const cancelled = (task) => {
    if (task.controller.signal.aborted || closed)
      throw Object.assign(new Error("复核已取消"), { name: "AbortError" });
  };
  async function execute(task) {
    if (task.status === "cancelled") return;
    task.status = "running";
    const result = {
      feeding: { numerator: 0, denominator: 0, rate: null },
      normal: { numerator: 0, denominator: 0, rate: null },
      unavailable_decisions: 0,
      unavailable_games: 0,
      partial_games: 0,
      skipped_unreconstructed_hands: 0,
      cache_hits: 0,
      computed_games: 0,
      channel: task.request.channel,
      policy_version: task.policy,
      feeding_definition:
        "目标伙伴指定口径中明显差于罗伯特推荐的实际弃牌 / 目标伙伴该口径全部可比弃牌",
      normal_definition:
        "未向目标伙伴实际递送的其余可比弃牌中，无明显差距的次数 / 该组全部可比弃牌",
    };
    let validGames = 0;
    try {
      for (const entry of task.games) {
        cancelled(task);
        // The source callback must check current local fingerprints before any
        // cache reuse, so a changed record cannot borrow an old comparison.
        const record = await readRecord(entry, task.snapshot);
        cancelled(task);
        if (
          !record ||
          (record.source_hash != null &&
            record.source_hash !== entry.source_hash)
        )
          throw new Error("本地牌谱已变化，请重新发起复核");
        const key = crypto
          .createHash("sha256")
          .update(
            JSON.stringify([
              entry.game_id,
              entry.source_hash,
              task.request.donor_id,
              task.policy,
            ]),
          )
          .digest("hex");
        let comparison = await persistent.get(key);
        cancelled(task);
        if (comparison) result.cache_hits++;
        else {
          try {
            comparison = compact(
              await analyzer(record, {
                donor_id: task.request.donor_id,
                thresholds: DEFAULT_THRESHOLDS,
                signal: task.controller.signal,
              }),
            );
          } catch (error) {
            if (error.name === "AbortError") throw error;
            comparison = {
              status: "unavailable",
              reason: "机器人基准计算失败",
              decisions: [],
              unavailable_decisions: 0,
            };
          }
          result.computed_games++;
          cancelled(task);
          // Partial reconstruction and individual errors are deliberately not
          // cached as complete normal evidence. A later explicit retry can recover.
          if (
            comparison.status === "available" &&
            (comparison.policy_evaluated_decisions ??
              comparison.analyzed_decisions) > 0 &&
            !comparison.truncated &&
            !(
              comparison.calculator_unavailable_decisions ??
              comparison.unavailable_decisions
            ) &&
            !comparison.skipped_unreconstructed_hands
          )
            await persistent.put(
              key,
              comparison,
              entry,
              task.request.donor_id,
              task.policy,
            );
        }
        cancelled(task);
        result.unavailable_decisions += comparison.unavailable_decisions || 0;
        result.skipped_unreconstructed_hands +=
          comparison.skipped_unreconstructed_hands || 0;
        if (
          comparison.unavailable_decisions ||
          comparison.skipped_unreconstructed_hands ||
          comparison.truncated
        )
          result.partial_games++;
        const rows = (comparison.decisions || []).filter(
          (d) => d.status === "available" && d.rate_comparable !== false,
        );
        if (comparison.status !== "available" || !rows.length) {
          result.unavailable_games++;
        } else {
          validGames++;
          for (const row of rows) {
            const events = row.events || [],
              toTarget = events.filter(
                (e) => String(e.recipient_id) === task.request.recipient_id,
              ),
              channelTarget = toTarget.some((e) =>
                task.request.channel === "ALL"
                  ? ["MELD", "RON"].includes(e.family)
                  : e.family === task.request.channel,
              );
            const worse = classify(row, task.request.thresholds);
            if (channelTarget) {
              result.feeding.denominator++;
              if (worse) result.feeding.numerator++;
            }
            // A MELD to the target never becomes a RON control, or vice versa.
            if (!toTarget.length) {
              result.normal.denominator++;
              if (!worse) result.normal.numerator++;
            }
          }
        }
        task.progress.done++;
      }
      cancelled(task);
      for (const group of [result.feeding, result.normal])
        group.rate = group.denominator
          ? group.numerator / group.denominator
          : null;
      task.result = result;
      if (task.games.length && !validGames) {
        task.status = "failed";
        task.error = "本批牌谱没有可比的罗伯特弃牌基准，未生成正常率。";
      } else task.status = "completed";
    } catch (error) {
      task.status = error.name === "AbortError" ? "cancelled" : "failed";
      task.error =
        error.name === "AbortError"
          ? "复核已取消"
          : String(error.message || "复核失败").slice(0, 300);
    } finally {
      task.finished_at = clock().toISOString();
      task.games = [];
      task.snapshot = null;
    }
  }
  async function startImpl(body) {
    if (closed) fail("复核服务已停止", 503);
    const request = validate(body),
      snapshot = await meta(),
      policy = typeof version === "function" ? version() : String(version);
    const signature = JSON.stringify([
      request,
      snapshot?.active_generation ?? snapshot?.generation ?? null,
      snapshot?.as_of ?? null,
      policy,
    ]);
    if (useBackgroundWorker) {
      await reconcilePersistent();
      const dupRes = await pool.query(
        `SELECT * FROM anti_cheat_center.robert_reviews
          WHERE signature=$1 AND status IN ('queued','running')
          ORDER BY created_at DESC LIMIT 1`,
        [signature],
      );
      if (dupRes.rowCount) {
        spawnBackgroundWorker();
        return formatDbReview(dupRes.rows[0]);
      }
      const activeCount = Number(
        (
          await pool.query(
            `SELECT count(*)::int AS n FROM anti_cheat_center.robert_reviews WHERE status IN ('queued','running')`,
          )
        ).rows[0].n,
      );
      if (activeCount >= maxQueued)
        fail("已有多项复核等待处理，请稍后再试", 429);
    } else {
      const duplicate = [...tasks.values()].find(
        (t) =>
          t.signature === signature && ["queued", "running"].includes(t.status),
      );
      if (duplicate) return publicTask(duplicate);
      if (
        [...tasks.values()].filter((t) =>
          ["queued", "running"].includes(t.status),
        ).length >= maxQueued
      )
        fail("已有多项复核等待处理，请稍后再试", 429);
    }
    const selected = await findGames(
      {
        donor_id: request.donor_id,
        recipient_id: request.recipient_id,
        window_days: request.window_days,
        rule_key: request.rule_key,
      },
      snapshot,
    );
    if (!Array.isArray(selected)) throw new Error("本地复核场次查询失败");
    const seen = new Map(),
      games = [];
    for (const entry of selected) {
      if (
        !entry ||
        typeof entry.game_id !== "string" ||
        typeof entry.source_hash !== "string" ||
        !entry.source_hash
      )
        throw new Error("本地牌谱指纹缺失");
      if (
        seen.has(entry.game_id) &&
        seen.get(entry.game_id) !== entry.source_hash
      )
        throw new Error("同场本地牌谱指纹冲突，请重新发起复核");
      if (!seen.has(entry.game_id)) {
        seen.set(entry.game_id, entry.source_hash);
        games.push({ game_id: entry.game_id, source_hash: entry.source_hash });
      }
    }
    if (useBackgroundWorker) {
      const id = crypto.randomUUID();
      const progress = { done: 0, total: games.length };
      const inserted = (
        await pool.query(
          `INSERT INTO anti_cheat_center.robert_reviews(id,signature,status,created_at,request,snapshot,policy_version,games,progress)
           VALUES($1,$2,'queued',$3,$4,$5,$6,$7,$8) RETURNING *`,
          [
            id,
            signature,
            clock().toISOString(),
            JSON.stringify(request),
            JSON.stringify(snapshot || {}),
            policy,
            JSON.stringify(games),
            JSON.stringify(progress),
          ],
        )
      ).rows[0];
      spawnBackgroundWorker();
      return formatDbReview(inserted);
    }
    while (tasks.size >= maxTasks) {
      const old = [...tasks.values()].find(
        (t) => !["queued", "running"].includes(t.status),
      );
      if (!old) break;
      tasks.delete(old.id);
    }
    const task = {
      id: crypto.randomUUID(),
      signature,
      request,
      snapshot,
      policy,
      games,
      status: "queued",
      created_at: clock().toISOString(),
      controller: new AbortController(),
      progress: { done: 0, total: games.length },
    };
    tasks.set(task.id, task);
    enqueue(() => execute(task));
    return publicTask(task);
  }
  function start(body) {
    const result = planning.then(() => startImpl(body));
    planning = result.catch(() => {});
    return result;
  }
  async function get(id) {
    if (useBackgroundWorker) {
      await reconcilePersistent();
      const row = (
        await pool.query(
          `SELECT * FROM anti_cheat_center.robert_reviews WHERE id=$1`,
          [id],
        )
      ).rows[0];
      if (!row) fail("复核任务不存在，请重新复核", 404);
      if (row.status === "queued") spawnBackgroundWorker();
      return formatDbReview(row);
    }
    const task = tasks.get(id);
    if (!task) fail("复核任务不存在或进程已重启，请重新复核", 404);
    return publicTask(task);
  }
  async function cancel(id) {
    if (useBackgroundWorker) {
      await reconcilePersistent();
      const row = (
        await pool.query(
          `SELECT * FROM anti_cheat_center.robert_reviews WHERE id=$1`,
          [id],
        )
      ).rows[0];
      if (!row) fail("复核任务不存在，请重新复核", 404);
      if (!["queued", "running"].includes(row.status))
        return formatDbReview(row);
      const updated = (
        await pool.query(
          `UPDATE anti_cheat_center.robert_reviews
              SET cancel_requested = true,
                  status = CASE WHEN status = 'queued' THEN 'cancelled' ELSE status END,
                  finished_at = CASE WHEN status = 'queued' THEN now() ELSE finished_at END,
                  error = CASE WHEN status = 'queued' THEN '复核已取消' ELSE error END
            WHERE id = $1
            RETURNING *`,
          [id],
        )
      ).rows[0];
      return formatDbReview(updated);
    }
    const task = tasks.get(id);
    if (!task) fail("复核任务不存在或进程已重启，请重新复核", 404);
    if (!["queued", "running"].includes(task.status)) return publicTask(task);
    task.controller.abort();
    if (task.status === "queued") {
      task.status = "cancelled";
      task.finished_at = clock().toISOString();
    }
    return publicTask(task);
  }
  async function close() {
    closed = true;
    for (const task of tasks.values())
      if (["queued", "running"].includes(task.status)) await cancel(task.id);
  }
  return { start, get, cancel, close };
}

async function drainPersistentRobertReviews(
  pool,
  { readRecord, analyzer = analyzeRobertDiscards, clock = () => new Date() } = {},
) {
  await ensureRobertReviewTables(pool);
  const source = readRecord
    ? { readRecord }
    : require("./robertSource").createRobertSource(pool);
  const lease = await pool.connect();
  let ownsLock = false;
  try {
    ownsLock = (
      await lease.query("SELECT pg_try_advisory_lock($1) AS acquired", [
        ROBERT_LOCK,
      ])
    ).rows[0].acquired;
    if (!ownsLock) return 0;

    let processedJobs = 0;
    while (true) {
      const next = (
        await pool.query(
          `SELECT * FROM anti_cheat_center.robert_reviews
            WHERE status = 'queued' AND NOT cancel_requested
            ORDER BY created_at ASC LIMIT 1`,
        )
      ).rows[0];
      if (!next) break;
      processedJobs += 1;

      await pool.query(
        `UPDATE anti_cheat_center.robert_reviews
            SET status = 'running', started_at = now(), heartbeat = now()
          WHERE id = $1`,
        [next.id],
      );

      const controller = new AbortController();
      const cancelPoll = setInterval(async () => {
        try {
          const row = (
            await pool.query(
              `SELECT cancel_requested FROM anti_cheat_center.robert_reviews WHERE id=$1`,
              [next.id],
            )
          ).rows[0];
          if (row?.cancel_requested) controller.abort();
          else
            await pool.query(
              `UPDATE anti_cheat_center.robert_reviews SET heartbeat = now() WHERE id=$1`,
              [next.id],
            );
        } catch {
          // ignore transient poll errors
        }
      }, 2000);
      cancelPoll.unref();

      const task = {
        id: next.id,
        request: next.request,
        snapshot: next.snapshot,
        policy: next.policy_version,
        games: Array.isArray(next.games) ? next.games : [],
        status: "running",
        controller,
        progress: { done: 0, total: Array.isArray(next.games) ? next.games.length : 0 },
        result: null,
        error: null,
      };

      const checkCancelled = () => {
        if (controller.signal.aborted)
          throw Object.assign(new Error("复核已取消"), { name: "AbortError" });
      };

      const result = {
        feeding: { numerator: 0, denominator: 0, rate: null },
        normal: { numerator: 0, denominator: 0, rate: null },
        unavailable_decisions: 0,
        unavailable_games: 0,
        partial_games: 0,
        skipped_unreconstructed_hands: 0,
        cache_hits: 0,
        computed_games: 0,
        channel: task.request.channel,
        policy_version: task.policy,
        feeding_definition:
          "目标伙伴指定口径中明显差于罗伯特推荐的实际弃牌 / 目标伙伴该口径全部可比弃牌",
        normal_definition:
          "未向目标伙伴实际递送的其余可比弃牌中，无明显差距的次数 / 该组全部可比弃牌",
      };
      let validGames = 0;

      const pacer = createLoadPacer();
      try {
        for (const entry of task.games) {
          checkCancelled();
          await pacer.waitBeforeItem({
            activeIndex: 0,
            signal: controller.signal,
            onYield: async () => {
              checkCancelled();
              await pool.query(
                `UPDATE anti_cheat_center.robert_reviews SET heartbeat=now() WHERE id=$1`,
                [next.id],
              );
            },
          });
          const record = await source.readRecord(entry, task.snapshot);
          checkCancelled();
          if (
            !record ||
            (record.source_hash != null &&
              record.source_hash !== entry.source_hash)
          )
            throw new Error("本地牌谱已变化，请重新发起复核");
          const key = crypto
            .createHash("sha256")
            .update(
              JSON.stringify([
                entry.game_id,
                entry.source_hash,
                task.request.donor_id,
                task.policy,
              ]),
            )
            .digest("hex");
          let comparison =
            (
              await pool.query(
                "SELECT payload FROM anti_cheat_center.robert_discard_cache WHERE cache_key=$1",
                [key],
              )
            ).rows[0]?.payload || null;
          checkCancelled();
          if (comparison) result.cache_hits++;
          else {
            try {
              comparison = compact(
                await analyzer(record, {
                  donor_id: task.request.donor_id,
                  thresholds: DEFAULT_THRESHOLDS,
                  signal: controller.signal,
                }),
              );
            } catch (error) {
              if (error.name === "AbortError") throw error;
              comparison = {
                status: "unavailable",
                reason: "机器人基准计算失败",
                decisions: [],
                unavailable_decisions: 0,
              };
            }
            result.computed_games++;
            checkCancelled();
            if (
              comparison.status === "available" &&
              (comparison.policy_evaluated_decisions ??
                comparison.analyzed_decisions) > 0 &&
              !comparison.truncated &&
              !(
                comparison.calculator_unavailable_decisions ??
                comparison.unavailable_decisions
              ) &&
              !comparison.skipped_unreconstructed_hands
            ) {
              await pool.query(
                `INSERT INTO anti_cheat_center.robert_discard_cache(cache_key,game_id,source_hash,donor_id,policy_version,payload)
                 VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(cache_key) DO UPDATE SET payload=excluded.payload`,
                [
                  key,
                  entry.game_id,
                  entry.source_hash,
                  task.request.donor_id,
                  task.policy,
                  JSON.stringify(comparison),
                ],
              );
            }
          }
          checkCancelled();
          result.unavailable_decisions += comparison.unavailable_decisions || 0;
          result.skipped_unreconstructed_hands +=
            comparison.skipped_unreconstructed_hands || 0;
          if (
            comparison.unavailable_decisions ||
            comparison.skipped_unreconstructed_hands ||
            comparison.truncated
          )
            result.partial_games++;
          const rows = (comparison.decisions || []).filter(
            (d) => d.status === "available" && d.rate_comparable !== false,
          );
          if (comparison.status !== "available" || !rows.length) {
            result.unavailable_games++;
          } else {
            validGames++;
            for (const row of rows) {
              const events = row.events || [],
                toTarget = events.filter(
                  (e) => String(e.recipient_id) === task.request.recipient_id,
                ),
                channelTarget = toTarget.some((e) =>
                  task.request.channel === "ALL"
                    ? ["MELD", "RON"].includes(e.family)
                    : e.family === task.request.channel,
                );
              const worse = classify(row, task.request.thresholds);
              if (channelTarget) {
                result.feeding.denominator++;
                if (worse) result.feeding.numerator++;
              }
              if (!toTarget.length) {
                result.normal.denominator++;
                if (!worse) result.normal.numerator++;
              }
            }
          }
          task.progress.done++;
          await pool.query(
            `UPDATE anti_cheat_center.robert_reviews SET progress=$2, heartbeat=now() WHERE id=$1`,
            [next.id, JSON.stringify(task.progress)],
          );
        }
        checkCancelled();
        for (const group of [result.feeding, result.normal])
          group.rate = group.denominator
            ? group.numerator / group.denominator
            : null;
        if (task.games.length && !validGames) {
          task.status = "failed";
          task.error = "本批牌谱没有可比的罗伯特弃牌基准，未生成正常率。";
        } else {
          task.status = "completed";
        }
        task.result = result;
      } catch (error) {
        task.status = error.name === "AbortError" ? "cancelled" : "failed";
        task.error =
          error.name === "AbortError"
            ? "复核已取消"
            : String(error.message || "复核失败").slice(0, 300);
      } finally {
        clearInterval(cancelPoll);
      }

      await pool.query(
        `UPDATE anti_cheat_center.robert_reviews
            SET status=$2, finished_at=$3, heartbeat=now(), progress=$4, result=$5, error=$6
          WHERE id=$1`,
        [
          next.id,
          task.status,
          clock().toISOString(),
          JSON.stringify(task.progress),
          task.result ? JSON.stringify(task.result) : null,
          task.error,
        ],
      );
    }
    return processedJobs;
  } finally {
    if (ownsLock)
      await lease
        .query("SELECT pg_advisory_unlock($1)", [ROBERT_LOCK])
        .catch(() => {});
    lease.release();
  }
}

module.exports = {
  createRobertReviews,
  drainPersistentRobertReviews,
  ensureRobertReviewTables,
  ROBERT_LOCK,
  policyVersion,
  validate,
  compact,
  classify,
};
