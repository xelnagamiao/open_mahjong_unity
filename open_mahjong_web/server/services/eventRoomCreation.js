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
    const paired = ['sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate'].includes(roomConfig.sub_rule);
    if (rule === 'sichuan' && !paired && (roomConfig.duplicate_key || body.duplicate_enabled || body.auto_duplicate)) {
      fail(400, '川麻仅支持血战或血流双打复式，请选择对应的双打复式子规则');
    }
    if (paired) {
      if (roomConfig.duplicate_sync_mode !== undefined && typeof roomConfig.duplicate_sync_mode !== 'boolean') fail(400, '复式同步模式必须为布尔值');
      roomConfig.duplicate_sync_mode ??= false;
      if (rule !== 'sichuan' || !['event', 'base'].includes(event.kind)) fail(400, '复式双打只能在赛事或基地中创建');
      if (body.auto_duplicate || body.duplicate_enabled || roomConfig.random_seed) fail(400, '复式双打请选择自动生成或输入已有密钥');
      if (roomConfig.duplicate_key !== undefined) {
        if (typeof roomConfig.duplicate_key !== 'string') fail(400, '复式密钥格式不正确');
        roomConfig.duplicate_key = roomConfig.duplicate_key.trim();
        if (roomConfig.duplicate_key && !/^[A-Za-z0-9_-]{8,40}$/.test(roomConfig.duplicate_key)) fail(400, '复式密钥格式不正确');
      }
      if (roomConfig.duplicate_key && count !== 1) fail(400, '一个已有牌山只能绑定一组开闭室');
      const rounds = roomConfig.duplicate_round_count;
      if (!ROUND_COUNTS.includes(rounds)) fail(400, '比赛副数请选择 1、4、8、12 或 16 副');
    }
    if (body.auto_duplicate && body.duplicate_enabled !== true) fail(400, '请先开启复式');
    if (body.duplicate_enabled === false && roomConfig.duplicate_key) fail(400, '请先开启复式');
    if (body.duplicate_enabled) {
      if (rule !== 'guobiao') fail(400, '此复式开关仅支持国标麻将；川麻请选择双打复式子规则');
      if (!body.auto_duplicate && !String(roomConfig.duplicate_key || '').trim()) fail(400, '请输入复式密钥或开启自动生成复式密钥');
    }
    let generated = { walls: [], quota: null };
    if (body.auto_duplicate) {
      if (!['event', 'base'].includes(event.kind) || rule !== 'guobiao') fail(400, '此自动生成复式密钥开关仅支持赛事或基地的国标房间');
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
      ...(paired ? { paired: true, created_pair_count: items.length, created_room_count: items.length * 2 } : {}),
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
        if (status >= 400 || data?.success === false) {
          const message = data?.detail || data?.message || '创建房间失败';
          if (paired && event.kind === 'base' && message === '复式双打只能在比赛场中创建') {
            fail(409, '游戏服务尚未加载基地双打复式支持，请在计划内重启后重试');
          }
          fail(status >= 400 ? status : 400, message);
        }
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
