#!/usr/bin/env node
/**
 * 冒烟测试 Bot API 全部 player 端点。
 *
 * 用法（open_mahjong_web 目录）：
 *   node scripts/test-botapi.js [user_id]
 *
 * 环境：.env 中 BOT_API_JWT_SECRET；可选 TEST_USER_ID（默认 10000001）
 */
require('dotenv').config({ path: require('path').join(__dirname, '..', '.env') });

const { signToken } = require('../server/utils/jwt');

const BASE = process.env.TEST_BASE_URL || 'http://localhost:3000/api/bot/player';
const USER_ID = process.argv[2] || process.env.TEST_USER_ID || '10000001';
const SECRET = process.env.BOT_API_JWT_SECRET;

if (!SECRET) {
  console.error('缺少 BOT_API_JWT_SECRET');
  process.exit(1);
}

const token = signToken(
  { aud: 'botapi', bot_name: 'test-bot', iat: Math.floor(Date.now() / 1000) },
  String(SECRET).trim(),
  null
);

const headers = { Authorization: `Bearer ${token}` };
const GRADE_RULES = ['guobiao', 'riichi', 'riichi_sanma'];
const ELO_RULES = ['qingque', 'sichuan', 'sichuan_xueliu_exchange'];

const cases = [
  { name: 'info', path: `/info/${USER_ID}` },
  { name: 'records', path: `/records/${USER_ID}?limit=5` },
  { name: 'rank-stats', path: `/rank-stats/${USER_ID}?tier=rank` },
  { name: 'rank', path: `/rank/${USER_ID}` },
  { name: 'elo-records', path: `/records/${USER_ID}?rule=qingque&tier=elo&limit=5` },
  { name: 'nanque-records', path: `/records/${USER_ID}?rule=zhongyong&sub_rule=zhongyong/nanque&limit=5` },
  { name: 'sanma-east', path: `/records/${USER_ID}?rule=riichi&sub_rule=riichi/sanma&tier=rank&game_type=dongfeng&limit=5`, rule: 'riichi', subRule: 'riichi/sanma', matchType: '1/4_sanma_rank' },
  { name: 'sanma-south', path: `/records/${USER_ID}?rule=riichi&sub_rule=riichi/sanma&tier=rank&game_type=banzhuang&limit=5`, rule: 'riichi', subRule: 'riichi/sanma', matchType: '2/4_sanma_rank' },
  { name: 'exchange-records', path: `/records/${USER_ID}?rule=sichuan&sub_rule=sichuan/xueliu_exchange&tier=elo&limit=5`, rule: 'sichuan', subRule: 'sichuan/xueliu_exchange', matchType: '4/4_rank' },
  { name: 'scope-counts', path: `/scope-counts/${USER_ID}?rule=qingque` },
  { name: 'no-auth', path: `/info/${USER_ID}`, skipAuth: true, expectStatus: 401 },
];

async function runCase(c) {
  const url = `${BASE}${c.path}`;
  const h = c.skipAuth ? {} : headers;
  const res = await fetch(url, { headers: h });
  const body = await res.json().catch(() => ({}));
  let ok = c.expectStatus ? res.status === c.expectStatus : res.ok && body.success === true;
  if (ok && ['info', 'rank'].includes(c.name)) {
    const ratings = body.data?.ratings;
    ok = GRADE_RULES.every(rule => {
      const rating = ratings?.[rule];
      return rating?.rule === rule && rating.system === 'grade' && !('elo' in rating)
        && typeof rating.rank_name === 'string' && Number.isFinite(rating.rank_score)
        && Number.isInteger(rating.games) && rating.games >= 0 && !!rating.bounds && !!rating.progress;
    }) && ELO_RULES.every(rule => {
      const rating = ratings?.[rule];
      return rating?.rule === rule && rating.system === 'elo' && Number.isFinite(rating.elo)
        && Number.isInteger(rating.games) && rating.games >= 0 && rating.bounds === null && rating.progress === null;
    });
    if (c.name === 'info') ok &&= Array.isArray(body.data.nanque_stats) && !!body.data.fan_dict.nanque
      && [...GRADE_RULES, ...ELO_RULES].every(rule => Array.isArray(body.data.ranked_stats?.[rule]));
  }
  if (ok && c.name === 'elo-records') ok = body.data.items.every(item => item.room_type === 'match' && item.match_tier === 'elo');
  if (ok && c.name === 'nanque-records') ok = body.data.items.every(item => item.rule === 'zhongyong' && item.sub_rule === 'zhongyong/nanque');
  if (ok && c.matchType) ok = body.data.items.every(item => item.rule === c.rule && item.sub_rule === c.subRule
    && item.room_type === 'match' && item.match_type === c.matchType);
  if (ok && c.name === 'scope-counts') ok = Number.isInteger(body.data.elo);
  return {
    name: c.name,
    ok,
    status: res.status,
    message: body.message || (body.data ? 'ok' : JSON.stringify(body).slice(0, 80)),
  };
}

(async () => {
  console.log(`Base: ${BASE}`);
  console.log(`User: ${USER_ID}\n`);

  let failed = 0;
  for (const c of cases) {
    try {
      const r = await runCase(c);
      const mark = r.ok ? 'PASS' : 'FAIL';
      if (!r.ok) failed += 1;
      console.log(`[${mark}] ${r.name}  HTTP ${r.status}  ${r.message}`);
    } catch (e) {
      failed += 1;
      console.log(`[FAIL] ${c.name}  ${e.message}`);
    }
  }

  console.log(failed ? `\n${failed} 项失败` : '\n全部通过');
  process.exitCode = failed ? 1 : 0;
})();
