const { ROUND_COUNTS } = require('./duplicateWalls');

function fail(status, message) { throw Object.assign(new Error(message), { status }); }

function createEventDuplicateWallService({ duplicateWalls, writeAudit }) {
  async function create(event, userId, body = {}, { platformAdmin = false } = {}) {
    if (!event) fail(404, '赛事或基地不存在');
    if (!['event', 'base'].includes(event.kind)) fail(400, '复式密钥只能属于赛事或基地');
    if (!['registered', 'active'].includes(event.status)) fail(409, '只有申办完成且尚未结束的赛事或基地可以创建牌墙');
    if (!body || typeof body !== 'object' || Array.isArray(body)) fail(400, '请求必须为对象');
    if (body.wall_type !== 'key' || !['sichuan/sbr', 'sichuan/xueliu'].includes(body.rule)) fail(400, '此入口仅支持生成川麻随机密钥牌山');
    if (!ROUND_COUNTS.includes(body.round_count)) fail(400, '比赛副数请选择 1、4、8、12 或 16 副');
    if (body.use_flowers !== undefined && body.use_flowers !== false) fail(400, '川麻复式不能启用花牌');
    const reason = String(body.reason || '').trim();
    if (platformAdmin && !reason) fail(400, '请填写操作原因');
    const result = await duplicateWalls.createBatch(userId, {
      scope: 'event', event_id: event.event_id, wall_type: 'key', rule: body.rule,
      round_count: body.round_count, use_flowers: false, name: body.name || '',
    }, 1, {
      platformAdmin,
      beforeCommit: async ({ walls }, db) => writeAudit({
        adminUserId: userId,
        action: platformAdmin ? 'event.duplicate_wall.create' : 'event_admin.duplicate_wall.create',
        targetType: 'event', targetId: event.event_id,
        payload: { wall_id: walls[0].id, rule: walls[0].rule, wall_type: 'key', round_count: walls[0].round_count },
        reason,
      }, db),
    });
    return { wall: result.walls[0], quota: result.quota };
  }
  return { create };
}

module.exports = { createEventDuplicateWallService };
