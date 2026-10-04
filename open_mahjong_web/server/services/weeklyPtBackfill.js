const { execFile } = require('node:child_process');
const path = require('node:path');
const { promisify } = require('node:util');

const runFile = promisify(execFile);
let pending;

function ptBackfillEnabled() {
  return Boolean(process.env.WEEKLY_PT_BASELINE && process.env.WEEKLY_PT_PYTHON);
}

// Optional migration bridge for a running game server that predates PT persistence.
// It uses the server's settlement rules in a separate short-lived process.
async function backfillWeeklyPt() {
  if (!ptBackfillEnabled()) return;
  if (pending) return pending;
  const serverRoot = path.resolve(__dirname, '../../../open_mahjong_server');
  pending = runFile(process.env.WEEKLY_PT_PYTHON, [
    path.join(serverRoot, 'server/database/guobiao/backfill_pt_changes.py'),
    '--baseline', process.env.WEEKLY_PT_BASELINE,
    '--config', path.join(serverRoot, 'server/local_config.py'), '--apply',
  ], {
    timeout: 60000, maxBuffer: 256 * 1024,
    env: { ...process.env, PYTHONIOENCODING: 'utf-8', PYTHONDONTWRITEBYTECODE: '1' },
  }).then(({ stdout }) => {
    console.log('周榜 PT 补算:', stdout.trim());
  }).finally(() => { pending = null; });
  return pending;
}

module.exports = { backfillWeeklyPt, ptBackfillEnabled };
