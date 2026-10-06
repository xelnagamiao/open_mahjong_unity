const archiver = require('archiver');
const { visibleRecordSql } = require('./duplicateRecordAccess');

const RECORD_BATCH_SIZE = 20;
const DOWNLOAD_CANCELLED = 'RECORD_DOWNLOAD_CANCELLED';

function cancelledError() {
  const error = new Error('牌谱下载连接已关闭');
  error.code = DOWNLOAD_CANCELLED;
  return error;
}

// All waits share one cancellation signal, including a blocked ZIP entry or
// response drain. Register before starting an operation so synchronous events
// and exceptions cannot leave a waiter behind.
function waitForEvent(emitter, event, signal, start = () => {}) {
  return new Promise((resolve, reject) => {
    const cleanup = () => {
      emitter.removeListener(event, done);
      signal.removeEventListener('abort', aborted);
    };
    const done = () => { cleanup(); resolve(); };
    const aborted = () => { cleanup(); reject(signal.reason); };
    if (signal.aborted) return aborted();
    emitter.once(event, done);
    signal.addEventListener('abort', aborted, { once: true });
    try { start(); } catch (error) { cleanup(); reject(error); }
  });
}

async function waitForDrain(archive, res, signal) {
  // Entry completion alone does not mean a slow HTTP client consumed the
  // compressed output. Bound both the response and archive stream buffers.
  if (res.writableNeedDrain) await waitForEvent(res, 'drain', signal);
  if (archive.writableNeedDrain) await waitForEvent(archive, 'drain', signal);
  signal.throwIfAborted();
}

async function streamRecordZip(db, gameIds, userId, res) {
  const controller = new AbortController();
  const { signal } = controller;
  const archive = archiver('zip', { zlib: { level: 5 }, highWaterMark: 64 * 1024 });
  const fail = (error) => controller.abort(error);
  const closed = () => { if (!res.writableFinished) fail(cancelledError()); };
  const responseError = () => fail(cancelledError());
  res.on('close', closed);
  res.on('error', responseError);
  archive.on('error', fail);
  // In-memory sources should never warn. Treat any skipped entry as a failure,
  // rather than returning a successful-looking ZIP with missing content.
  archive.on('warning', fail);
  if (res.destroyed) fail(cancelledError());

  let piped = false;
  try {
    for (let offset = 0; offset < gameIds.length; offset += RECORD_BATCH_SIZE) {
      signal.throwIfAborted();
      await waitForDrain(archive, res, signal);
      const ids = gameIds.slice(offset, offset + RECORD_BATCH_SIZE);
      const result = await db.query(
        `SELECT game_id, record FROM game_records gr
         WHERE game_id = ANY($1::varchar[]) AND ${visibleRecordSql()}`,
        [ids]
      );
      // pool.query releases its connection normally. A disconnect can leave
      // this one bounded query in flight, but never starts the next batch.
      signal.throwIfAborted();
      const byGame = new Map(result.rows.map((row) => [row.game_id, row.record]));
      if (!piped) {
        res.setHeader('Content-Type', 'application/zip');
        res.setHeader('Content-Disposition', `attachment; filename="player_${userId}_records.zip"`);
        archive.pipe(res);
        piped = true;
      }
      for (const gameId of ids) {
        const raw = byGame.get(gameId);
        if (raw === undefined || raw === null) continue;
        const body = typeof raw === 'string' ? raw : JSON.stringify(raw);
        // Only one record enters the compressor at a time; do not enqueue
        // all batches while archiver is still holding their JSON buffers.
        await waitForEvent(archive, 'entry', signal, () => {
          archive.append(body, { name: `${gameId}.json` });
        });
        await waitForDrain(archive, res, signal);
      }
    }
    await waitForEvent(res, 'finish', signal, () => {
      archive.finalize().catch(fail);
    });
  } catch (error) {
    fail(error);
    archive.unpipe(res);
    archive.abort();
    // Let the current bounded entry finish and release zlib/internal pipes
    // even when its original destination has gone away.
    archive.resume();
    if (res.headersSent && !res.destroyed) res.destroy();
    throw error;
  } finally {
    res.removeListener('close', closed);
    res.removeListener('error', responseError);
  }
}

module.exports = { streamRecordZip, RECORD_BATCH_SIZE, DOWNLOAD_CANCELLED };
