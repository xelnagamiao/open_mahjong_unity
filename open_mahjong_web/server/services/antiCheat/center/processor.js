"use strict";
const {
  SCHEMA: S,
  VERSION,
  initialize,
  scan,
  readGame,
  commitFacts,
} = require("./database");
const LOCK = 170114071;
const {
  VERSION: MODEL_VERSION,
  refreshStatistics,
} = require("./populationPipeline");
const INPUT_ERRORS = new Set([
  "metadata_conflict",
  "invalid_json",
  "invalid_record_shape",
  "unsupported_player_count",
  "invalid_player_identity",
  "unknown_timestamp_zone",
  "no_completed_hands",
]);
async function runUpdate(pool, id, options = {}) {
  await initialize(pool);
  const lease = await pool.connect();
  let job,
    ownsLock = false;
  try {
    const acquired = (
      await lease.query("SELECT pg_try_advisory_lock($1) AS acquired", [LOCK])
    ).rows[0].acquired;
    if (!acquired) return null;
    ownsLock = true;
    job = (await pool.query(`SELECT * FROM ${S}.updates WHERE id=$1`, [id]))
      .rows[0];
    if (job?.status === "cancelled") return job;
    if (!job || !["queued", "scanning"].includes(job.status))
      throw new Error("更新状态无效");
    const config = job.settings;
    const progress = async (status, payload) =>
      pool.query(
        `UPDATE ${S}.updates SET status=$2,heartbeat=now(),progress=$3 WHERE id=$1`,
        [id, status, JSON.stringify(payload)],
      );
    const cancelled = async () => {
      if (
        (
          await pool.query(
            `SELECT cancel_requested FROM ${S}.updates WHERE id=$1`,
            [id],
          )
        ).rows[0]?.cancel_requested
      )
        throw Object.assign(new Error("更新已取消，已提交的逐场特征已保留"), {
          cancelled: true,
        });
    };
    await pool.query(
      `UPDATE ${S}.updates SET status='scanning',started_at=now(),heartbeat=now() WHERE id=$1`,
      [id],
    );
    const source = await scan(pool),
      cached = (
        await pool.query(
          `SELECT game_id,source_hash,feature_version,status FROM ${S}.games`,
        )
      ).rows;
    const known = new Map(cached.map((r) => [r.game_id, r])),
      sourceIds = new Set(source.map((r) => r.game_id));
    const added = source.filter((r) => !known.has(r.game_id));
    const changed = source.filter((r) => {
      const old = known.get(r.game_id);
      return (
        old &&
        (old.source_hash !== r.source_hash ||
          old.feature_version !== VERSION ||
          ["failed", "partial"].includes(old.status))
      );
    });
    // Out-of-scope caches remain available as historical evidence; only genuinely
    // deleted source records follow the existing deletion path.
    const allSourceIds=new Set((await pool.query("SELECT game_id FROM public.game_records")).rows.map(r=>r.game_id));
    const deleted = cached.filter((r) => !allSourceIds.has(r.game_id));
    const previousScope=(await pool.query(`SELECT game_id FROM ${S}.scope_game_versions WHERE valid_to IS NULL`)).rows;
    const priorScopeIds=new Set(previousScope.map(r=>r.game_id));
    const scopeChanged=previousScope.some(r=>!sourceIds.has(r.game_id))||source.some(r=>["ready","unsupported"].includes(known.get(r.game_id)?.status)&&!priorScopeIds.has(r.game_id));
    await cancelled();
    if (deleted.length) {
      const c = await pool.connect();
      try {
        await c.query("BEGIN");
        await c.query(`DELETE FROM ${S}.games WHERE game_id=ANY($1::text[])`, [
          deleted.map((r) => r.game_id),
        ]);
        await c.query(
          `UPDATE ${S}.meta SET facts_revision=facts_revision+1 WHERE id=1`,
        );
        await c.query("COMMIT");
      } catch (e) {
        await c.query("ROLLBACK");
        throw e;
      } finally {
        c.release();
      }
    }
    await pool.query(
      `UPDATE ${S}.updates SET source_games=$2,new_games=$3,changed_games=$4,deleted_games=$5,unchanged_games=$6 WHERE id=$1`,
      [
        id,
        source.length,
        added.length,
        changed.length,
        deleted.length,
        source.length - added.length - changed.length,
      ],
    );
    const work = [...added, ...changed].sort(
      (a, b) =>
        String(a.created_at).localeCompare(String(b.created_at)) ||
        a.game_id.localeCompare(b.game_id),
    );
    let processed = 0,
      failed = 0,
      invalid = 0,
      calculations = 0,
      next = 0;
    const extractor = options.extractor || require("./facts").extractGameFacts;
    const concurrency = Math.max(
      1,
      Math.min(
        8,
        Number(
          options.concurrency || process.env.ANTI_CHEAT_ANALYSIS_CONCURRENCY,
        ) || 4,
      ),
    );
    await progress("analyzing", {
      processed,
      total: work.length,
      message: work.length
        ? "正在计算新增或变化的牌谱"
        : "牌谱内容未变化，无需重复计算",
    });
    const workers = await Promise.allSettled(
      Array.from({ length: Math.min(concurrency, work.length) }, async () => {
        while (next < work.length) {
          await cancelled();
          if (next >= work.length) break;
          const item = work[next++];
          const row = await readGame(pool, item.game_id);
          if (!row) continue;
          try {
            const fact = await extractor(row, options.factOptions || {});
            if (
              fact.quality?.calculator_errors?.length ||
              fact.quality?.feature_calculator_errors?.length
            )
              fact.status = "partial";
            await commitFacts(pool, fact, row.source_hash);
            calculations += fact.standard ? 1 : 0;
          } catch (error) {
            const inputError = INPUT_ERRORS.has(error.code);
            if (inputError) invalid += 1;
            else failed += 1;
            await commitFacts(
              pool,
              {
                game_id: row.game_id,
                created_at: null,
                rule: row.rule,
                rule_key: "",
                status: inputError ? "invalid" : "failed",
                players: [],
                quality: {
                  error: String(error.message).slice(0, 300),
                  error_code: error.code || "calculation_failed",
                },
              },
              row.source_hash,
            );
          }
          processed += 1;
          if (processed % 5 === 0 || processed === work.length) {
            await pool.query(
              `UPDATE ${S}.updates SET processed=$2,failed_games=$3,calculator_calls=$4,heartbeat=now(),progress=$5 WHERE id=$1`,
              [
                id,
                processed,
                failed,
                calculations,
                JSON.stringify({
                  processed,
                  total: work.length,
                  invalid_games: invalid,
                  message: `已计算 ${processed} / ${work.length} 场，每场已独立保存`,
                }),
              ],
            );
          }
        }
      }),
    );
    const rejected = workers.find((r) => r.status === "rejected");
    if (rejected) throw rejected.reason;
    await cancelled();
    const meta = (await pool.query(`SELECT * FROM ${S}.meta WHERE id=1`))
      .rows[0];
    const asOf = options.asOf || new Date().toISOString();
    const expired =
      meta.as_of &&
      (
        await pool.query(
          `SELECT EXISTS(SELECT 1 FROM ${S}.games g CROSS JOIN unnest($1::integer[]) w(days) WHERE ${require("./rankedScope").inventoryPredicate("g")} AND days>0 AND g.created_at>$2::timestamptz-days*interval '1 day' AND g.created_at<=$3::timestamptz-days*interval '1 day') AS expired`,
          [config.windows_days, meta.as_of, asOf],
        )
      ).rows[0].expired;
    let statisticsRefreshed=false;
    if (
      !meta.active_generation ||
      String(meta.facts_revision) !== String(meta.stats_facts_revision) ||
      meta.model_revision !== config.revision ||
      meta.model_version !== MODEL_VERSION ||
      expired || scopeChanged
    ) {
      await progress("statistics", {
        processed,
        total: work.length,
        message: "正在更新递送统计与排名",
      });
      await refreshStatistics(pool, id, config, { cancelled, progress, asOf,scopeScanned:true });
      statisticsRefreshed=true;
    }
    await cancelled();
    await pool.query(
      `UPDATE ${S}.updates SET status='completed',finished_at=now(),heartbeat=now(),processed=$2,failed_games=$3,calculator_calls=$4,progress=$5 WHERE id=$1`,
      [
        id,
        processed,
        failed,
        calculations,
        JSON.stringify({
          processed,
          total: work.length,
          invalid_games: invalid,
          message: invalid
            ? `更新完成；${invalid} 场输入无法归因，已保存排除原因`
            : work.length
              ? "更新完成"
              : statisticsRefreshed?"递送统计已更新":"无新增或变化牌谱，已读取现有统计",
        }),
      ],
    );
    return (await pool.query(`SELECT * FROM ${S}.updates WHERE id=$1`, [id]))
      .rows[0];
  } catch (error) {
    await pool.query(
      `UPDATE ${S}.updates SET status=$2,error=$3,finished_at=now(),heartbeat=now() WHERE id=$1`,
      [
        id,
        error.cancelled ? "cancelled" : "failed",
        String(error.message).slice(0, 500),
      ],
    );
    if (options.throwErrors) throw error;
  } finally {
    if (ownsLock)
      await lease
        .query("SELECT pg_advisory_unlock($1)", [LOCK])
        .catch(() => {});
    lease.release();
  }
}
module.exports = { runUpdate, refreshStatistics, LOCK };
