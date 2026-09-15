#!/usr/bin/env node
/**
 * 把长沙旧牌谱升到 hu tick 末段带 bird_tiles[] 的新格式。
 *
 * 独立短生命周期进程：自建 pg Pool，不 require server/config/database.js，
 * 不重启 Node API / 游戏 WS / Python 服。
 *
 * 用法（在 open_mahjong_web 目录）：
 *   node scripts/migrate_changsha_bird_tiles.js --self-test
 *   node scripts/migrate_changsha_bird_tiles.js --local-dir "C:\\...\\LocalRecords" --apply
 *   node scripts/migrate_changsha_bird_tiles.js --db            # 默认 dry-run
 *   node scripts/migrate_changsha_bird_tiles.js --db --apply
 */
'use strict';

const fs = require('fs');
const path = require('path');

const HU_CLASSES = new Set(['hu_self', 'hu_first', 'hu_second', 'hu_third']);
const RANK = { 一: 1, 二: 2, 三: 3, 四: 4, 五: 5, 六: 6, 七: 7, 八: 8, 九: 9 };
const SUIT = { 万: 1, 筒: 2, 条: 3 };

function isChangshaRule(rule) {
  return typeof rule === 'string' && rule.toLowerCase().startsWith('changsha');
}

function isTileId(id) {
  if (!Number.isInteger(id)) return false;
  const suit = Math.floor(id / 10);
  const rank = id % 10;
  return suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9;
}

function parseTileName(text) {
  const item = String(text || '').trim().split('=')[0];
  const n = Number(item);
  if (Number.isInteger(n) && isTileId(n)) return n;
  if ([...item].length !== 2) return null;
  const rank = RANK[item[0]];
  const suit = SUIT[item[1]];
  if (!rank || !suit) return null;
  return suit * 10 + rank;
}

function parseBirdsFromFans(fans) {
  if (!Array.isArray(fans)) return [];
  for (const fan of fans) {
    if (typeof fan !== 'string' || !fan.startsWith('鸟牌:')) continue;
    const payload = fan.slice('鸟牌:'.length).trim();
    if (!payload || payload === '无') return [];
    return payload.split(',').map(parseTileName).filter((id) => id != null);
  }
  return [];
}

function alreadyHasBirds(tick) {
  const last = tick[tick.length - 1];
  return Array.isArray(last) && last.length > 0 && last.every((x) => isTileId(Number(x)));
}

function fansContain(fans, needle) {
  return Array.isArray(fans) && fans.some((fan) => String(fan).includes(needle));
}

function consumeWall(wall, ticksUntilHu) {
  let backward = 'double';
  const remaining = Array.isArray(wall) ? wall.slice() : [];
  for (const tick of ticksUntilHu) {
    if (!Array.isArray(tick) || tick.length === 0) continue;
    const action = tick[0];
    if (action === 'd' || action === 'bd') {
      if (remaining.length) remaining.shift();
    } else if (action === 'gd') {
      if (remaining.length <= 1 || backward === 'single') {
        if (remaining.length) remaining.pop();
      } else if (remaining.length >= 2) {
        remaining.splice(remaining.length - 2, 1);
      }
      backward = backward === 'double' ? 'single' : 'double';
    }
  }
  return remaining;
}

function resolveBirdCount(title) {
  const n = Number(title && title.bird_count);
  if (n === 0 || n === 1 || n === 2 || n === 4) return n;
  return 2;
}

function migrateInnerRecord(record) {
  if (!record || typeof record !== 'object') return { changed: false, record, huUpdated: 0 };
  const title = record.game_title && typeof record.game_title === 'object' ? record.game_title : {};
  const rule = title.rule || '';
  const sub = title.sub_rule || '';
  if (!isChangshaRule(rule) && !isChangshaRule(sub)) {
    return { changed: false, record, huUpdated: 0 };
  }

  let changed = false;
  let huUpdated = 0;
  if (title.bird_count == null) {
    title.bird_count = 2;
    changed = true;
  }
  if (title.dealer_bird == null) {
    title.dealer_bird = true;
    changed = true;
  }
  record.game_title = title;
  const birdCount = resolveBirdCount(title);
  const rounds = record.game_round || {};
  for (const key of Object.keys(rounds)) {
    const round = rounds[key];
    if (!round || typeof round !== 'object') continue;
    const ticks = round.action_ticks;
    if (!Array.isArray(ticks)) continue;
    const wall0 = Array.isArray(round.tiles_list) ? round.tiles_list : [];
    for (let i = 0; i < ticks.length; i++) {
      const tick = ticks[i];
      if (!Array.isArray(tick) || !HU_CLASSES.has(tick[0])) continue;
      if (alreadyHasBirds(tick)) continue;
      if (fansContain(tick[3], '错和')) continue;
      const fans = Array.isArray(tick[3]) ? tick[3] : [];
      let birds = parseBirdsFromFans(fans);
      if (birds.length === 0 && birdCount <= 0) continue;
      if (birds.length === 0 && fansContain(fans, '海底')) {
        const hepai = tick[5];
        if (isTileId(Number(hepai))) birds = [Number(hepai)];
      }
      if (birds.length === 0 && birdCount > 0) {
        const remaining = consumeWall(wall0, ticks.slice(0, i));
        birds = remaining.slice(0, birdCount).filter((id) => isTileId(Number(id))).map(Number);
      }
      if (birds.length === 0) continue;
      ticks[i] = tick.concat([birds]);
      changed = true;
      huUpdated += 1;
    }
  }
  return { changed, record, huUpdated };
}

function migratePayload(json) {
  if (!json || typeof json !== 'object') return { changed: false, payload: json, huUpdated: 0 };
  if (json.record && typeof json.record === 'object' && json.record.game_round) {
    const inner = migrateInnerRecord(json.record);
    json.record = inner.record;
    return { changed: inner.changed, payload: json, huUpdated: inner.huUpdated };
  }
  return (() => {
    const inner = migrateInnerRecord(json);
    return { changed: inner.changed, payload: inner.record, huUpdated: inner.huUpdated };
  })();
}

function parseArgs(argv) {
  const args = {
    apply: false,
    db: false,
    selfTest: false,
    localDir: null,
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--apply') args.apply = true;
    else if (a === '--dry-run') args.apply = false;
    else if (a === '--db') args.db = true;
    else if (a === '--self-test') args.selfTest = true;
    else if (a === '--local-dir') args.localDir = argv[++i];
    else if (a === '--help' || a === '-h') args.help = true;
  }
  return args;
}

function assertEqual(actual, expected, label) {
  const left = JSON.stringify(actual);
  const right = JSON.stringify(expected);
  if (left !== right) {
    throw new Error(`${label}: ${left} !== ${right}`);
  }
}

function runSelfTest() {
  const fromFans = migrateInnerRecord({
    game_title: { rule: 'changsha', sub_rule: 'changsha/classic_double_bird' },
    game_round: {
      round_index_1: {
        tiles_list: [11, 12],
        action_ticks: [
          ['hu_self', 0, 12, ['小胡', '鸟牌:四筒,一条'], [12, -4, -4, -4], 25],
        ],
      },
    },
  });
  assertEqual(fromFans.record.game_round.round_index_1.action_ticks[0][6], [24, 31], 'fans birds');
  assertEqual(fromFans.record.game_title.bird_count, 2, 'title bird_count');

  const fromWall = migrateInnerRecord({
    game_title: { rule: 'changsha', bird_count: 2 },
    game_round: {
      round_index_1: {
        tiles_list: [11, 24, 31, 32],
        action_ticks: [
          ['d', 11],
          ['c', 11, 'F'],
          ['hu_self', 0, 2, ['小胡'], [2, 0, 0, -2], 15],
        ],
      },
    },
  });
  assertEqual(fromWall.record.game_round.round_index_1.action_ticks[2][6], [24, 31], 'wall peek');

  const skipExisting = migrateInnerRecord({
    game_title: { rule: 'changsha', bird_count: 2, dealer_bird: true },
    game_round: {
      round_index_1: {
        tiles_list: [24, 31],
        action_ticks: [['hu_self', 0, 2, ['小胡'], [2, 0, 0, -2], 15, [24, 31]]],
      },
    },
  });
  assertEqual(skipExisting.changed, false, 'skip existing');

  const sea = migrateInnerRecord({
    game_title: { rule: 'changsha', bird_count: 2 },
    game_round: {
      round_index_1: {
        tiles_list: [24, 31],
        action_ticks: [['hu_self', 0, 8, ['碰碰胡', '海底'], [8, -8, 0, 0], 25]],
      },
    },
  });
  assertEqual(sea.record.game_round.round_index_1.action_ticks[0][6], [25], 'sea bottom bird');

  const zeroBird = migrateInnerRecord({
    game_title: { rule: 'changsha', bird_count: 0 },
    game_round: {
      round_index_1: {
        tiles_list: [24, 31],
        action_ticks: [['hu_self', 0, 2, ['小胡'], [2, 0, 0, -2], 15]],
      },
    },
  });
  assertEqual(zeroBird.record.game_round.round_index_1.action_ticks[0].length, 6, 'bird_count 0');
  console.log('self-test ok');
}

function migrateLocalDir(dir, apply) {
  const abs = path.resolve(dir);
  if (!fs.existsSync(abs) || !fs.statSync(abs).isDirectory()) {
    throw new Error(`本地目录不存在: ${abs}`);
  }
  const names = fs.readdirSync(abs).filter((name) => name.toLowerCase().endsWith('.json') && name !== 'index.json');
  let scanned = 0;
  let changedFiles = 0;
  let huUpdated = 0;
  for (const name of names) {
    const filePath = path.join(abs, name);
    let json;
    try {
      json = JSON.parse(fs.readFileSync(filePath, 'utf8'));
    } catch (err) {
      console.warn(`跳过无法解析的文件 ${name}: ${err.message}`);
      continue;
    }
    scanned += 1;
    const result = migratePayload(json);
    if (!result.changed) continue;
    changedFiles += 1;
    huUpdated += result.huUpdated;
    console.log(`${apply ? '写入' : '将改'} ${name}  hu+=${result.huUpdated}`);
    if (apply) {
      fs.writeFileSync(filePath, JSON.stringify(result.payload), 'utf8');
    }
  }
  console.log(`本地扫描 ${scanned} 个 json，${apply ? '已改' : '将改'} ${changedFiles} 个文件，hu tick ${huUpdated} 条`);
}

async function migrateDatabase(apply) {
  require('dotenv').config({ path: path.join(__dirname, '..', '.env') });
  const { Pool } = require('pg');
  const pool = new Pool({
    host: process.env.DB_HOST || 'localhost',
    user: process.env.DB_USER || 'postgres',
    password: process.env.DB_PASSWORD || 'qwe123',
    database: process.env.DB_NAME || 'open_mahjong',
    port: parseInt(process.env.DB_PORT, 10) || 5432,
    max: 2,
    idleTimeoutMillis: 5000,
    connectionTimeoutMillis: 8000,
  });
  try {
    const selectSql = `
      SELECT gr.game_id, gr.record
      FROM game_records gr
      WHERE COALESCE(gr.record #>> '{game_title,rule}', '') ILIKE 'changsha%'
         OR COALESCE(gr.record #>> '{game_title,sub_rule}', '') ILIKE 'changsha%'
         OR EXISTS (
              SELECT 1 FROM game_player_records gpr
              WHERE gpr.game_id = gr.game_id
                AND COALESCE(gpr.rule, '') ILIKE 'changsha%'
            )
    `;
    const { rows } = await pool.query(selectSql);
    let changedGames = 0;
    let huUpdated = 0;
    for (const row of rows) {
      const result = migratePayload(row.record);
      if (!result.changed) continue;
      changedGames += 1;
      huUpdated += result.huUpdated;
      console.log(`${apply ? '写入' : '将改'} ${row.game_id}  hu+=${result.huUpdated}`);
      if (apply) {
        await pool.query(
          'UPDATE game_records SET record = $1::jsonb WHERE game_id = $2',
          [JSON.stringify(result.payload), row.game_id]
        );
      }
    }
    console.log(`数据库扫描长沙 ${rows.length} 局，${apply ? '已改' : '将改'} ${changedGames} 局，hu tick ${huUpdated} 条`);
  } finally {
    await pool.end();
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help || (!args.selfTest && !args.localDir && !args.db)) {
    console.log(`用法:
  node scripts/migrate_changsha_bird_tiles.js --self-test
  node scripts/migrate_changsha_bird_tiles.js --local-dir <dir> [--apply]
  node scripts/migrate_changsha_bird_tiles.js --db [--apply]
默认 dry-run；加 --apply 才写盘/UPDATE。不重启任何服务器。`);
    process.exit(args.help ? 0 : 1);
  }
  if (args.selfTest) runSelfTest();
  if (args.localDir) migrateLocalDir(args.localDir, args.apply);
  if (args.db) await migrateDatabase(args.apply);
}

if (require.main === module) {
  main().catch((err) => {
    console.error(err);
    process.exit(1);
  });
}

module.exports = {
  migrateInnerRecord,
  migratePayload,
};
