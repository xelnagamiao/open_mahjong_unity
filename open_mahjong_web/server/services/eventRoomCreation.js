const { ROUND_COUNTS } = require('./duplicateWalls');

const ROOM_COUNTS = Object.freeze([1, 3, 5, 8, 10, 20]);
const DUPLICATE_SUB_RULES = new Set(['guobiao/standard', 'guobiao/xiaolin', 'guobiao/kshen', 'guobiao/lanshi']);
function fail(status, message) { throw Object.assign(new Error(message), { status }); }

function createEventRoomCreationService({ duplicateWalls, proxyToGameServer, writeAudit }) {
  async function create(event, userId, body = {}, { platformAdmin = false } = {}) {
    if (event.status !== 'active') fail(400, event.status === 'registered' ? '赛事尚未开启，无法创建房间' : '赛事已关闭，无法创建房间');
    const count = body.room_count ?? 1;
    if (!ROOM_COUNTS.includes(count)) fail(400, '房间数量请选择 1、3、5、8、10 或 20 个');
    const rule = String(body.room_rule || '').trim();
    if (!rule) fail(400, '请选择房间规则');
    const reason = String(body.reason || '').trim();
    if (platformAdmin && !reason) fail(400, '请填写操作原因');
    if (body.room_config != null && (typeof body.room_config !== 'object' || Array.isArray(body.room_config))) fail(400, '房间配置必须为对象');
    if (body.auto_duplicate !== undefined && typeof body.auto_duplicate !== 'boolean') fail(400, '自动生成复式密钥开关必须为布尔值');
    if (body.duplicate_enabled !== undefined && typeof body.duplicate_enabled !== 'boolean') fail(400, '复式开关必须为布尔值');
    const roomConfig = { ...(body.room_config || {}) };
    if (body.auto_duplicate && body.duplicate_enabled !== true) fail(400, '请先开启复式');
    if (body.duplicate_enabled === false && roomConfig.duplicate_key) fail(400, '请先开启复式');
    if (body.duplicate_enabled) {
      if (rule !== 'guobiao') fail(400, '复式房间仅支持国标麻将');
      if (!body.auto_duplicate && !String(roomConfig.duplicate_key || '').trim()) fail(400, '请输入复式密钥或开启自动生成复式密钥');
    }
    let generated = { walls: [], quota: null };
    if (body.auto_duplicate) {
      if (event.kind !== 'event' || rule !== 'guobiao') fail(400, '自动生成复式密钥仅支持赛事国标房间');
      if (!DUPLICATE_SUB_RULES.has(roomConfig.sub_rule || 'guobiao/standard')) fail(400, '此国标子规则不支持复式密钥');
      if (roomConfig.duplicate_key || roomConfig.random_seed) fail(400, '自动生成密钥不能同时使用已有密钥或复现种子');
      const roundCount = body.duplicate_round_count ?? 16;
      if (!ROUND_COUNTS.includes(roundCount)) fail(400, '复式局数请选择 1、4、8、12 或 16 局');
      generated = await duplicateWalls.createBatch(userId, {
        scope: 'event', event_id: event.event_id, wall_type: 'key',
        rule: roomConfig.sub_rule === 'guobiao/lanshi' ? 'guobiao/lanshi' : 'guobiao',
        name: String(roomConfig.room_name || '').trim().slice(0, 80),
        round_count: roundCount, use_flowers: roomConfig.use_flowers,
      }, count, { platformAdmin });
    }

    const items = [];
    const result = () => ({
      ...(items.length === 1 ? items[0] : {}), items,
      requested_count: count, created_count: items.length,
      generated_key_count: generated.walls.length, duplicate_quota: generated.quota,
    });
    for (let index = 0; index < count; index += 1) {
      const config = { ...roomConfig };
      if (generated.walls[index]) config.duplicate_key = generated.walls[index].key;
      try {
        const { status, data } = await proxyToGameServer('/admin/event/rooms/create', {
          event_id: event.event_id, room_rule: rule, room_config: config,
          password: body.password || '', created_by: userId,
        });
        if (status >= 400 || data?.success === false) fail(status >= 400 ? status : 400, data?.detail || data?.message || '创建房间失败');
        // Count the room before auditing so an audit failure still reports the room that exists.
        items.push(data);
        await writeAudit({
          adminUserId: userId, action: platformAdmin ? 'event.room.create' : 'event_admin.room.create',
          targetType: 'event', targetId: event.event_id,
          payload: { room_rule: rule, room_config: config, room_info: data?.room_info || null }, reason,
        });
      } catch (error) {
        error.data = result();
        throw error;
      }
    }
    return result();
  }
  return { create };
}

module.exports = { ROOM_COUNTS, createEventRoomCreationService };
