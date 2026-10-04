const crypto = require('node:crypto');
const ROUND_COUNTS = Object.freeze([1, 4, 8, 12, 16]);

const QUOTAS = Object.freeze({
  personal: { daily_limit: 5, storage_limit: 25 },
  event: { daily_limit: 20, storage_limit: 100 },
});
const LABELS = { 41: '东', 42: '南', 43: '西', 44: '北', 45: '中', 46: '白', 47: '发', 51: '春', 52: '夏', 53: '秋', 54: '冬', 55: '梅', 56: '兰', 57: '竹', 58: '菊' };
const NUMBER_TILES = [1, 2, 3].flatMap((suit) => Array.from({ length: 9 }, (_, i) => suit * 10 + i + 1));
const HONORS = [41, 42, 43, 44, 45, 46, 47];
const FLOWERS = [51, 52, 53, 54, 55, 56, 57, 58];
const RULES = {
  guobiao: { label: '国标麻将', base: [...NUMBER_TILES, ...HONORS], flowers: FLOWERS },
  'guobiao/lanshi': { label: '国标蓝十改（无花）', base: [...NUMBER_TILES, ...HONORS] },
};

function fail(status, message) { throw Object.assign(new Error(message), { status }); }
function tileLabel(tile) { return LABELS[tile] || `${tile % 10}${({ 1: '万', 2: '筒', 3: '条' })[Math.floor(tile / 10)]}`; }
function normalizeUseFlowers(rule, value) {
  if (!Object.hasOwn(RULES, rule)) fail(400, '新复式仅支持国标麻将');
  if (value !== undefined && typeof value !== 'boolean') fail(400, 'use_flowers 必须为布尔值');
  return rule === 'guobiao/lanshi' ? false : value ?? true;
}
function tileSpec(rule, useFlowers) {
  const spec = Object.hasOwn(RULES, rule) ? RULES[rule] : null;
  if (!spec) fail(400, '新复式仅支持国标麻将');
  const flowers = normalizeUseFlowers(rule, useFlowers) ? FLOWERS : [];
  return [...spec.base.map((tile) => ({ tile, label: tileLabel(tile), count: 4 })),
    ...flowers.map((tile) => ({ tile, label: tileLabel(tile), count: 1 }))];
}
function fullWall(rule, useFlowers) { return tileSpec(rule, useFlowers).flatMap(({ tile, count }) => Array(count).fill(tile)); }
function catalog() {
  return { rules: Object.entries(RULES).map(([rule, spec]) => ({
    rule, label: spec.label, tiles: tileSpec(rule), tile_count: fullWall(rule).length,
    use_flowers: normalizeUseFlowers(rule), supports_flower_toggle: rule === 'guobiao',
    flower_options: (rule === 'guobiao' ? [true, false] : [false]).map((useFlowers) => ({
      use_flowers: useFlowers, tiles: tileSpec(rule, useFlowers), tile_count: fullWall(rule, useFlowers).length,
    })),
  })), quotas: QUOTAS, round_counts: ROUND_COUNTS };
}
function validateTiles(rule, tiles, useFlowers) {
  const expected = fullWall(rule, useFlowers);
  if (!Array.isArray(tiles) || tiles.length !== expected.length || tiles.some((tile) => !Number.isInteger(tile))) {
    fail(400, `牌墙必须包含 ${expected.length} 张合法整数牌值`);
  }
  const counts = new Map();
  for (const tile of tiles) counts.set(tile, (counts.get(tile) || 0) + 1);
  for (const { tile, count } of tileSpec(rule, useFlowers)) {
    if (counts.get(tile) !== count) fail(400, `${tileLabel(tile)}必须恰好有 ${count} 张`);
    counts.delete(tile);
  }
  if (counts.size) fail(400, '牌墙包含此规则不允许的牌');
  return [...tiles];
}

// Versioned, deterministic Fisher–Yates; store the resulting order, not a runtime PRNG dependency.
function generateTiles(rule, seed, useFlowers) {
  const tiles = fullWall(rule, useFlowers);
  let counter = 0;
  for (let i = tiles.length - 1; i > 0; i -= 1) {
    const bound = i + 1;
    const ceiling = 0x100000000 - (0x100000000 % bound);
    let value;
    do {
      value = crypto.createHash('sha256').update(`duplicate-wall-v1\0${rule}\0${seed}\0${counter++}`).digest().readUInt32BE(0);
    } while (value >= ceiling);
    const j = value % bound;
    [tiles[i], tiles[j]] = [tiles[j], tiles[i]];
  }
  return tiles;
}
function normalizeInput(body = {}) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) fail(400, '请求必须为对象');
  const scope = body.scope || 'personal';
  if (!Object.hasOwn(QUOTAS, scope)) fail(400, '请选择个人或比赛复式');
  const eventId = scope === 'event' ? String(body.event_id || '').trim() : null;
  if (scope === 'event' && (!eventId || eventId.length > 32)) fail(400, '请选择所属比赛');
  const wallType = body.wall_type;
  if (!['manual', 'seed', 'key'].includes(wallType)) fail(400, '请选择手动牌墙、复现牌墙或密钥牌墙');
  const rule = body.rule || 'guobiao';
  const useFlowers = normalizeUseFlowers(rule, body.use_flowers);
  const name = String(body.name || '').trim();
  if (name.length > 80) fail(400, '名称最多 80 字');
  let seed = null;
  if (wallType === 'seed') {
    if (typeof body.seed !== 'string' && !Number.isSafeInteger(body.seed)) fail(400, '请输入有效的随机种子');
    seed = String(body.seed).trim();
    if (!seed || seed.length > 128) fail(400, '随机种子须为 1 至 128 个字符');
  } else if (wallType === 'key') seed = crypto.randomBytes(32).toString('hex');
  const roundCount = body.round_count ?? 1;
  if (!ROUND_COUNTS.includes(roundCount)) fail(400, '局数请选择 1、4、8、12 或 16 局');
  let roundTiles;
  if (wallType === 'manual') {
    const drafts = body.round_tiles ?? (roundCount === 1 ? [body.tiles] : null);
    if (!Array.isArray(drafts) || drafts.length !== roundCount) fail(400, `请提供完整的 ${roundCount} 局牌山`);
    roundTiles = drafts.map((tiles, index) => {
      try { return validateTiles(rule, tiles, useFlowers); }
      catch (error) { fail(400, `第 ${index + 1} 局：${error.message}`); }
    });
  } else {
    roundTiles = Array.from({ length: roundCount }, (_, index) => {
      const roundSeed = index === 0 ? seed : crypto.createHash('sha256')
        .update(`duplicate-series-v1\0${seed}\0${index + 1}`).digest('hex');
      return generateTiles(rule, roundSeed, useFlowers);
    });
  }
  return { scope, event_id: eventId, wall_type: wallType, rule, use_flowers: useFlowers, name, seed,
    tiles: roundTiles[0], round_count: roundCount, round_tiles: roundTiles };
}
function wallDto(row, ownerView = false) {
  const dto = {};
  for (const key of ['id', 'key', 'name', 'scope', 'event_id', 'wall_type', 'rule', 'is_unlocked', 'created_at', 'unlocked_at', 'ended_at', 'deleted_at']) dto[key] = row[key] ?? null;
  dto.owner_user_id = row.scope === 'personal' ? row.owner_user_id ?? null : null;
  dto.owner_username = row.scope === 'personal' ? row.owner_username ?? null : null;
  dto.event_name = row.scope === 'event' ? row.event_name ?? null : null;
  dto.is_deleted = row.deleted_at != null;
  dto.round_count = row.round_count ?? 1;
  dto.use_flowers = typeof row.use_flowers === 'boolean' ? row.use_flowers
    : Array.isArray(row.tiles) ? row.tiles.some((tile) => FLOWERS.includes(tile)) : null;
  // Historical walls remain manageable even when their rule is no longer creatable.
  // Never regenerate their count (or disclose a key wall) through the current catalog.
  dto.tile_count = row.tile_count == null ? (Array.isArray(row.tiles) ? row.tiles.length : null) : Number(row.tile_count);
  if (ownerView && row.wall_type !== 'key') {
    dto.tiles = row.tiles;
    dto.round_tiles = row.round_tiles ?? [row.tiles];
    dto.seed = row.seed;
  }
  return dto;
}
function quotaDto(scope, row = {}) {
  const limits = QUOTAS[scope];
  const createdToday = Number(row.created_today) || 0;
  const stored = Number(row.stored) || 0;
  return { ...limits, created_today: createdToday, stored, remaining_today: Math.max(0, limits.daily_limit - createdToday), remaining_storage: Math.max(0, limits.storage_limit - stored), timezone: 'Asia/Shanghai' };
}

function createDuplicateWallService(pool) {
  async function authorizeScope(db, userId, scope, eventId, forCreate = false) {
    if (!Object.hasOwn(QUOTAS, scope)) fail(400, '无效的复式范围');
    if (scope !== 'event') return;
    const result = await db.query(
      `SELECT e.status FROM events e JOIN event_admins ea ON ea.event_id = e.event_id
       WHERE e.event_id = $1 AND ea.user_id = $2 AND e.kind = 'event'`, [eventId, userId]);
    if (!result.rows.length) fail(403, '只有该比赛的管理员可以管理比赛复式牌墙');
    if (forCreate && !['registered', 'active'].includes(result.rows[0].status)) fail(409, '只有申办完成且尚未结束的比赛可以创建牌墙');
  }
  function scopeWhere(userId, scope, eventId) {
    return scope === 'event' ? { sql: "scope = 'event' AND event_id = $1", params: [eventId] }
      : { sql: "scope = 'personal' AND owner_user_id = $1", params: [userId] };
  }
  async function quota(db, userId, scope, eventId) {
    const where = scopeWhere(userId, scope, eventId);
    const result = await db.query(
      `SELECT COUNT(*) FILTER (WHERE (created_at AT TIME ZONE 'Asia/Shanghai')::date = (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Shanghai')::date)::int AS created_today,
              COUNT(*) FILTER (WHERE deleted_at IS NULL)::int AS stored
       FROM duplicate_walls WHERE ${where.sql}`, where.params);
    return quotaDto(scope, result.rows[0]);
  }
  async function create(userId, body) {
    const input = normalizeInput(body);
    const client = await pool.connect();
    try {
      await client.query('BEGIN');
      // One scope-wide lock serializes both daily and stored counts. Deleted rows remain in daily counts.
      const lockKey = input.scope === 'event' ? `duplicate:event:${input.event_id}` : `duplicate:personal:${userId}`;
      await client.query('SELECT pg_advisory_xact_lock(hashtextextended($1, 0))', [lockKey]);
      await authorizeScope(client, userId, input.scope, input.event_id, true);
      const before = await quota(client, userId, input.scope, input.event_id);
      if (!before.remaining_today) fail(429, `今天最多创建 ${before.daily_limit} 个复式牌墙（北京时间零点刷新）`);
      if (!before.remaining_storage) fail(409, `最多同时保存 ${before.storage_limit} 个复式牌墙，请先删除不再使用的牌墙`);
      const key = `dup_${crypto.randomBytes(16).toString('hex')}`;
      const result = await client.query(
        `INSERT INTO duplicate_walls (key, owner_user_id, event_id, scope, wall_type, rule, name, tiles, seed, use_flowers, round_count, round_tiles)
         VALUES ($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9,$10,$11,$12::jsonb) RETURNING *`,
        [key, userId, input.event_id, input.scope, input.wall_type, input.rule, input.name, JSON.stringify(input.tiles), input.seed, input.use_flowers, input.round_count, JSON.stringify(input.round_tiles)]);
      const after = await quota(client, userId, input.scope, input.event_id);
      await client.query('COMMIT');
      return { wall: wallDto(result.rows[0], true), quota: after };
    } catch (error) {
      await client.query('ROLLBACK');
      throw error;
    } finally { client.release(); }
  }
  async function mine(userId, scope = 'personal', eventId = null) {
    await authorizeScope(pool, userId, scope, eventId);
    const where = scopeWhere(userId, scope, eventId);
    const items = await pool.query(`SELECT * FROM duplicate_walls WHERE ${where.sql} AND deleted_at IS NULL ORDER BY created_at DESC, id DESC`, where.params);
    return { items: items.rows.map((row) => wallDto(row, true)), quota: await quota(pool, userId, scope, eventId) };
  }
  async function manage(userId, id, change) {
    if (!/^\d+$/.test(String(id))) fail(400, '无效的牌墙编号');
    if (!change.remove && typeof change.is_unlocked !== 'boolean') fail(400, '解除锁定状态必须为布尔值');
    const client = await pool.connect();
    try {
      await client.query('BEGIN');
      const result = await client.query('SELECT * FROM duplicate_walls WHERE id = $1 AND deleted_at IS NULL FOR UPDATE', [id]);
      const row = result.rows[0];
      if (!row) fail(404, '牌墙不存在');
      if (row.scope === 'event') await authorizeScope(client, userId, row.scope, row.event_id);
      else if (String(row.owner_user_id) !== String(userId)) fail(403, '只有创建者可以管理该密钥');
      if (!change.remove) {
        if (row.is_unlocked && !change.is_unlocked) fail(409, '密钥已解除锁定，不能再次锁定');
        if (row.is_unlocked === change.is_unlocked) {
          await client.query('COMMIT');
          return { wall: wallDto(row, true) };
        }
      }
      if (change.remove && !row.is_unlocked) {
        fail(409, '锁定的密钥不能删除，请先解除锁定');
      }
      const updated = change.remove
        ? await client.query('UPDATE duplicate_walls SET deleted_at = CURRENT_TIMESTAMP WHERE id = $1 RETURNING *', [id])
        : await client.query(`UPDATE duplicate_walls SET is_unlocked = $2,
            unlocked_at = COALESCE(unlocked_at, CURRENT_TIMESTAMP) WHERE id = $1 RETURNING *`, [id, change.is_unlocked]);
      await client.query('COMMIT');
      return { wall: wallDto(updated.rows[0], true) };
    } catch (error) { await client.query('ROLLBACK'); throw error; }
    finally { client.release(); }
  }
  async function publicList(key = '') {
    const needle = String(key).trim();
    if (needle.length > 40) fail(400, '无效的密钥');
    const result = await pool.query(
      `SELECT dw.*, u.username AS owner_username, e.name AS event_name, jsonb_array_length(dw.tiles) AS tile_count
       FROM duplicate_walls dw
       LEFT JOIN users u ON u.user_id = dw.owner_user_id
       LEFT JOIN events e ON e.event_id = dw.event_id
       WHERE dw.is_unlocked = TRUE AND ($1 = '' OR dw.key = $1) ORDER BY dw.unlocked_at DESC, dw.id DESC LIMIT 100`, [needle]);
    return { items: result.rows.map((row) => wallDto(row)) };
  }
  async function publicDetail(key) {
    if (!/^dup_[a-f0-9]{32}$/.test(String(key))) fail(404, '密钥不存在或尚未解除锁定');
    const result = await pool.query(
      `SELECT dw.*, u.username AS owner_username, e.name AS event_name
       FROM duplicate_walls dw
       LEFT JOIN users u ON u.user_id = dw.owner_user_id
       LEFT JOIN events e ON e.event_id = dw.event_id
       WHERE dw.key = $1 AND dw.is_unlocked = TRUE`, [key]);
    if (!result.rows.length) fail(404, '密钥不存在或尚未解除锁定');
    const wall = result.rows[0];
    const games = await pool.query(
      `WITH matched_games AS (
        SELECT game_id, ended_at FROM duplicate_games WHERE wall_id = $1
        UNION ALL
        SELECT gr.game_id, NULL::timestamptz FROM game_records gr
        WHERE gr.record #>> '{game_title,duplicate_key}' = $2
          AND NOT EXISTS (SELECT 1 FROM duplicate_games dg WHERE dg.game_id = gr.game_id)
      )
       SELECT dg.game_id, dg.ended_at, gr.created_at, dw.rule, dw.event_id, e.name AS event_name,
        COALESCE((SELECT jsonb_agg(jsonb_build_object('user_id', p.user_id, 'username', p.username,
          'score', p.score, 'rank', p.rank, 'original_player_index', p.original_player_index) ORDER BY p.original_player_index)
          FROM game_player_records p WHERE p.game_id = dg.game_id), '[]'::jsonb) AS players
       FROM matched_games dg JOIN duplicate_walls dw ON dw.id = $1
       LEFT JOIN events e ON e.event_id = dw.event_id
       LEFT JOIN game_records gr ON gr.game_id = dg.game_id
       ORDER BY dg.ended_at DESC NULLS FIRST, dg.game_id`, [wall.id, wall.key]);
    return { wall: wallDto(wall), games: games.rows };
  }
  return { create, mine, manage, publicList, publicDetail };
}

module.exports = { QUOTAS, RULES, catalog, fullWall, validateTiles, generateTiles, normalizeInput, normalizeUseFlowers, wallDto, quotaDto, createDuplicateWallService };
