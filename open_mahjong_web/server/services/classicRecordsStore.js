const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { funDataDir } = require('../utils/runtimeData');
const { parseRecordShareInput, classicReplayLinks, GAME_ID_RE, shareInteger } = require('../utils/recordShareParse');
const { visibleReplayTableSql, LOCKED_MESSAGE } = require('./duplicateRecordAccess');
const { recordPlayers, resolveProtagonist } = require('../utils/classicRecordProtagonist');

const ID_RE = /^cls_[a-z0-9]{12}$/;
const DESCRIPTION_MAX = 80;
const ITEMS_MAX = 100;
const SEED_ID = 'cls_seed_renhe';
const SEED_RECORD = {
  id: SEED_ID,
  game_id: 'V9V0a25tKe',
  round: 5,
  node: 11,
  description: '人和不够番',
  sort: 100,
};

function catalogPath() {
  return path.join(funDataDir(), 'classic-records.json');
}

function writeJsonAtomic(filePath, data) {
  const dir = path.dirname(filePath);
  fs.mkdirSync(dir, { recursive: true });
  const tmp = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  fs.writeFileSync(tmp, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
  try {
    fs.renameSync(tmp, filePath);
  } catch {
    fs.copyFileSync(tmp, filePath);
    fs.unlinkSync(tmp);
  }
}

function readJson(filePath, fallback) {
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch {
    return fallback;
  }
}

function nowIso() {
  return new Date().toISOString();
}

function newId() {
  return `cls_${crypto.randomBytes(6).toString('hex')}`;
}

function emptyCatalog() {
  return { seeded: [], items: [] };
}

function loadCatalog() {
  const data = readJson(catalogPath(), emptyCatalog());
  if (!Array.isArray(data.items)) data.items = [];
  if (!Array.isArray(data.seeded)) data.seeded = [];
  return data;
}

function saveCatalog(catalog) {
  writeJsonAtomic(catalogPath(), {
    seeded: catalog.seeded || [],
    items: catalog.items || [],
  });
}

function clampSort(value, fallback = 0) {
  const n = Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.max(-9999, Math.min(9999, Math.round(n)));
}

function normalizeDescription(value) {
  const text = String(value || '').trim().replace(/\s+/g, ' ');
  if (!text) {
    throw Object.assign(new Error('请填写描述'), { status: 400 });
  }
  if (text.length > DESCRIPTION_MAX) {
    throw Object.assign(new Error(`描述不超过 ${DESCRIPTION_MAX} 字`), { status: 400 });
  }
  return text;
}

function normalizePosition(value, min) {
  if (value == null || value === '') return null;
  const n = Number(value);
  if (!Number.isFinite(n) || n < min) return null;
  return Math.floor(n);
}

function pickPosition(bodyValue, parsedValue, min) {
  if (bodyValue == null || bodyValue === '') return parsedValue ?? null;
  const normalized = normalizePosition(bodyValue, min);
  return normalized == null ? parsedValue ?? null : normalized;
}

function recordKey(item) {
  return `${item.game_id}#${item.table || ''}#${item.cursor || ''}#${item.round == null ? '' : item.round}#${item.node == null ? '' : item.node}`;
}

function decorate(item) {
  if (!item) return item;
  return {
    ...item,
    ...classicReplayLinks(item),
  };
}

function sortItems(items) {
  return [...items].sort((a, b) => {
    const sortDelta = (Number(b.sort) || 0) - (Number(a.sort) || 0);
    if (sortDelta) return sortDelta;
    return String(b.created_at || '').localeCompare(String(a.created_at || ''));
  });
}

function ensureSeed() {
  const catalog = loadCatalog();
  if (catalog.seeded.includes(SEED_ID)) return catalog;
  if (!catalog.items.some((item) => item && item.id === SEED_ID)) {
    catalog.items.push({
      ...SEED_RECORD,
      created_at: nowIso(),
      updated_at: nowIso(),
    });
  }
  catalog.seeded.push(SEED_ID);
  saveCatalog(catalog);
  return catalog;
}

function listAll() {
  const catalog = ensureSeed();
  return sortItems(catalog.items).map(decorate);
}

function listPublic() {
  return listAll();
}

// A catalog entry with a stored perspective needs only public participant metadata.
// Unknown perspectives need the visible action prefix of exactly one table and round.
async function readClassicRoster(db, ids) {
  const { rows } = await db.query(`WITH requested AS MATERIALIZED (
      SELECT req.id,gr.*,link.table_id AS original_side
      FROM unnest($1::varchar[]) req(id)
      LEFT JOIN game_record_tables link ON link.game_id=req.id
      JOIN game_record_metadata gr ON gr.replay_id=COALESCE(link.replay_id,req.id)
    ) SELECT gr.id,gr.game_id,gr.table_id,gr.created_at,gr.original_side,
      ${visibleReplayTableSql('gr')} AS allowed,
      (SELECT jsonb_agg(jsonb_build_object('user_id',p.user_id,'username',p.username,'score',p.score,
          'rank',p.rank,'rule',p.rule,'original_player_index',p.original_player_index) ORDER BY p.rank)
        FROM game_player_records p WHERE p.game_id=gr.game_id) AS players,
      (SELECT jsonb_object_agg(k,v) FROM jsonb_each(gr.record->'game_title') f(k,v)
        WHERE k IN ('rule','player_count') OR k ~ '^p[0-3]_(uid|name)$') AS title
    FROM requested gr`, [ids]);
  const byId = new Map();
  for (const row of rows) {
    if (!byId.has(row.id)) byId.set(row.id, []);
    byId.get(row.id).push(row);
  }
  return byId;
}

async function readClassicPositions(db, requests) {
  if (!requests.length) return new Map();
  // The permission check is repeated in the statement that reads actions.
  const {rows} = await db.query(`WITH permitted AS MATERIALIZED (
      SELECT q.*,gr.replay_id,gr.table_id
      FROM jsonb_to_recordset($1::jsonb) q(idx int, id text, source_id text, rk text, node int, legacy boolean)
      JOIN game_record_metadata gr ON gr.game_id=q.source_id
      LEFT JOIN game_record_metadata link ON link.game_id=q.id
      WHERE ${visibleReplayTableSql('gr')}
        AND (link.game_id IS NULL OR link.game_id=gr.game_id OR ${visibleReplayTableSql('link')})
    ), round_keys AS MATERIALIZED (
      SELECT q.*, COALESCE(
        CASE WHEN r.record #> ARRAY['tables',table_id,'payload','game_round',rk] IS NOT NULL THEN rk END,
        (SELECT key FROM jsonb_object_keys(r.record #> ARRAY['tables',table_id,'payload','game_round']) AS keys(key)
         ORDER BY substring(key FROM 13)::integer LIMIT 1)
      ) AS round_key
      FROM permitted q JOIN game_replays r ON r.game_id=q.replay_id
    ), rounds AS MATERIALIZED (
      SELECT idx,node,legacy,r.record #> ARRAY['tables',table_id,'payload','game_round',round_key] AS rd,
        CASE WHEN legacy THEN r.record #> ARRAY['tables',table_id,'round_meta',round_key,'legacy_node_map'] END AS mapping
      FROM round_keys q JOIN game_replays r ON r.game_id=q.replay_id
    ), positions AS MATERIALIZED (
      SELECT idx,rd->'seats' AS seats,rd->'start_player_index' AS first_actor,rd->'action_ticks' AS ticks,
        CASE WHEN legacy AND jsonb_array_length(mapping)>0
        THEN (mapping->>LEAST(node,jsonb_array_length(mapping)-1))::int ELSE node END AS n
      FROM rounds
    )
    SELECT idx, jsonb_build_object('seats',seats,'start_player_index',first_actor,
      'action_ticks',COALESCE((
        SELECT jsonb_agg(ticks->i ORDER BY i) FROM generate_series(0,LEAST(n,COALESCE(jsonb_array_length(ticks),0))-1) i
      ),'[]'::jsonb)) AS round FROM positions`, [JSON.stringify(requests)]);
  return new Map(rows.map(row => [row.idx,row.round]));
}

async function attachRecordMeta(items, { strict = false, persist = true } = {}) {
  const ids = [...new Set((items || []).map(item=>item.game_id).filter(Boolean))];
  if (!ids.length) return items || [];
  const db = require('../config/database');
  const roster = await readClassicRoster(db, ids);
  const pending = [];
  const entries = items.map((item,idx)=>{
    const choices = roster.get(item.game_id) || [];
    const original = choices.find(row=>row.table_id===row.original_side);
    const side = item.table || original?.table_id;
    const selected = side ? choices.find(row=>row.table_id===side) :
      ['main','open','closed'].map(key=>choices.find(row=>row.table_id===key && row.allowed)).find(Boolean) || choices[0];
    const locked = !!selected && (!selected.allowed || (original && !original.allowed));
    if (strict && locked) throw Object.assign(new Error(LOCKED_MESSAGE),{status:403});
    const indexed = selected?.players || [];
    const players = !selected ? [] : locked ? indexed : recordPlayers({game_title:selected.title},indexed);
    let protagonist = null;
    if (selected && !locked && players.length) {
      if (item.protagonist_user_id!=null || item.protagonist_player_index!=null) {
        try { protagonist = resolveProtagonist(item, {}, players, true); }
        catch (error) { if (strict) throw error; }
      }
      if (!protagonist) pending.push({idx,id:item.game_id,source_id:selected.game_id,rk:'round_index_'+(item.round || 1),
        node:Math.min(2147483647,item.node ?? 0),legacy:!item.cursor && !item.table && selected.game_id===item.game_id});
    }
    return {item,selected,locked,players,protagonist};
  });
  const positions = await readClassicPositions(db,pending);
  for (const req of pending) {
    const entry = entries[req.idx];
    const round = positions.get(req.idx);
    if (!round) {
      if (strict) throw Object.assign(new Error(LOCKED_MESSAGE),{status:403});
      entry.locked=true;
      entry.players=entry.selected.players || [];
      continue;
    }
    const item = {...entry.item,node:round.action_ticks.length};
    entry.protagonist = resolveProtagonist(item,{game_round:{selected:round}},entry.players);
  }
  const decorated = entries.map(({item,selected,locked,players,protagonist})=>{
    const rule = (!locked && selected?.title?.rule) || players[0]?.rule || null;
    return {
      ...decorate({...item,protagonist_user_id:protagonist?.user_id ?? null,
        protagonist_player_index:protagonist?.original_player_index ?? null}),
      protagonist, created_at_game:selected?.created_at || null, rule, show_2d:!rule || rule==='guobiao',
      missing:!selected, locked:Boolean(locked), players:[...players].sort((a,b)=>(a.rank||99)-(b.rank||99)),
    };
  });
  if (persist) {
    // 仅补齐仍未设置主角的同一条目，避免异步查询覆盖管理员刚保存的修改。
    const catalog = loadCatalog();
    let changed = false;
    for (const item of decorated) {
      const stored = catalog.items.find(row => row.id === item.id && recordKey(row) === recordKey(item));
      if (!stored || stored.protagonist_user_id != null || stored.protagonist_player_index != null || !item.protagonist) continue;
      stored.protagonist_user_id = item.protagonist_user_id;
      stored.protagonist_player_index = item.protagonist_player_index;
      changed = true;
    }
    if (changed) saveCatalog(catalog);
  }
  return decorated;
}

function parseClassicInput(body = {}) {
  const parsed = parseRecordShareInput(body.url || body.game_id || body.gameId);
  const gameId = String(parsed?.game_id || body.game_id || body.gameId || '').trim();
  if (!GAME_ID_RE.test(gameId)) {
    throw Object.assign(new Error('请粘贴有效的 2D / 3D 牌谱链接或牌谱 ID'), { status: 400 });
  }
  const round = pickPosition(body.round, parsed?.round, 1);
  const node = pickPosition(body.node, parsed?.node, 0);
  const explicitPlayer = body.protagonist_user_id !== undefined || body.protagonist_player_index !== undefined;
  const userId = explicitPlayer ? body.protagonist_user_id : parsed?.player;
  const playerIndex = explicitPlayer ? body.protagonist_player_index : parsed?.playerIndex;
  if ((userId != null && userId !== '' && shareInteger(userId) == null)
      || (playerIndex != null && playerIndex !== '' && shareInteger(playerIndex, 0, 3) == null)) {
    throw Object.assign(new Error('无效的主角参数'), { status: 400 });
  }
  return {
    game_id: gameId,
    ...((parsed?.table || ['main','open','closed'].includes(body.table)) && { table: parsed?.table || body.table }),
    ...((parsed?.cursor || body.cursor === 'v2') && { cursor: 'v2' }),
    round: round == null ? null : round,
    node: node == null ? null : node,
    protagonist_user_id: shareInteger(userId),
    protagonist_player_index: shareInteger(playerIndex, 0, 3),
    description: normalizeDescription(body.description),
    sort: clampSort(body.sort, 0),
  };
}

function findDuplicate(catalog, fields, exceptId = null) {
  const key = recordKey(fields);
  return catalog.items.find((item) => item && item.id !== exceptId && recordKey(item) === key);
}

function createClassicRecord(body) {
  const catalog = ensureSeed();
  if (catalog.items.length >= ITEMS_MAX) {
    throw Object.assign(new Error(`经典牌谱最多 ${ITEMS_MAX} 条`), { status: 400 });
  }
  const fields = parseClassicInput(body);
  if (findDuplicate(catalog, fields)) {
    throw Object.assign(new Error('这条牌谱已经添加过了'), { status: 409 });
  }
  const now = nowIso();
  const item = {
    id: newId(),
    ...fields,
    created_at: now,
    updated_at: now,
  };
  catalog.items.push(item);
  saveCatalog(catalog);
  return decorate(item);
}

function classicUpdateInput(current, body) {
  const replacedLink = body.url || (body.game_id && body.game_id !== current.game_id) || (body.gameId && body.gameId !== current.game_id);
  const replacedPlayer = body.protagonist_user_id !== undefined || body.protagonist_player_index !== undefined;
  return {
    ...current,
    ...(replacedLink ? { round: undefined, node: undefined, table: undefined, cursor: undefined, protagonist_user_id: undefined, protagonist_player_index: undefined } : {}),
    ...(replacedPlayer ? { protagonist_user_id: undefined, protagonist_player_index: undefined } : {}),
    ...body,
    game_id: body.game_id || body.gameId || current.game_id,
  };
}

function updateClassicRecord(id, body) {
  if (!ID_RE.test(id) && id !== SEED_ID) {
    throw Object.assign(new Error('无效的经典牌谱 ID'), { status: 400 });
  }
  const catalog = ensureSeed();
  const index = catalog.items.findIndex((item) => item && item.id === id);
  if (index < 0) {
    throw Object.assign(new Error('经典牌谱不存在'), { status: 404 });
  }
  const current = catalog.items[index];
  const fields = parseClassicInput(classicUpdateInput(current, body));
  if (findDuplicate(catalog, fields, id)) {
    throw Object.assign(new Error('这条牌谱已经添加过了'), { status: 409 });
  }
  const item = {
    ...current,
    ...fields,
    updated_at: nowIso(),
  };
  catalog.items[index] = item;
  saveCatalog(catalog);
  return decorate(item);
}

function deleteClassicRecord(id) {
  if (!ID_RE.test(id) && id !== SEED_ID) {
    throw Object.assign(new Error('无效的经典牌谱 ID'), { status: 400 });
  }
  const catalog = ensureSeed();
  const next = catalog.items.filter((item) => item && item.id !== id);
  if (next.length === catalog.items.length) {
    throw Object.assign(new Error('经典牌谱不存在'), { status: 404 });
  }
  catalog.items = next;
  saveCatalog(catalog);
}

module.exports = {
  SEED_ID,
  SEED_RECORD,
  DESCRIPTION_MAX,
  ensureSeed,
  listAll,
  listPublic,
  createClassicRecord,
  updateClassicRecord,
  deleteClassicRecord,
  parseClassicInput,
  classicUpdateInput,
  attachRecordMeta,
};
