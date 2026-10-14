const { ROOM_COUNTS } = require('./eventRoomCreation');

function fail(status, message) { throw Object.assign(new Error(message), { status }); }
function checkResult(result) {
  if (result.status >= 400 || result.data?.success === false) {
    fail(result.status >= 400 ? result.status : 400, result.data?.detail || result.data?.message || '房间操作失败');
  }
  return result.data;
}

function createAdminRoomService({ pool, proxyToGameServer, writeAudit, eventRoomCreation }) {
  async function list() {
    const data = checkResult(await proxyToGameServer('/admin/rooms', null, 'GET'));
    const ids = [...new Set(data.items.map(room => room.event_id).filter(Boolean))];
    const venues = ids.length ? (await pool.query('SELECT event_id, name, kind FROM events WHERE event_id = ANY($1)', [ids])).rows : [];
    const byId = new Map(venues.map(venue => [venue.event_id, venue]));
    return { items: data.items.map(room => ({ ...room,
      event_name: byId.get(room.event_id)?.name || '', venue_kind: byId.get(room.event_id)?.kind || '',
    })) };
  }

  async function create(userId, body) {
    const reason = String(body.reason || '').trim();
    if (!reason) fail(400, '请填写操作原因');
    if (body.event_id) {
      const event = (await pool.query('SELECT event_id, status, kind FROM events WHERE event_id = $1', [body.event_id])).rows[0];
      if (!event) fail(404, '赛事或基地不存在');
      return eventRoomCreation.create(event, userId, body, { platformAdmin: true });
    }
    const count = body.room_count ?? 1;
    if (!ROOM_COUNTS.includes(count)) fail(400, '房间数量请选择 1、3、5、8、10 或 20 个');
    const rule = typeof body.room_rule === 'string' ? body.room_rule.trim() : '';
    if (!rule) fail(400, '请选择房间规则');
    if (body.room_config != null && (typeof body.room_config !== 'object' || Array.isArray(body.room_config))) fail(400, '房间配置必须为对象');
    if (body.auto_duplicate) fail(400, '自动生成复式密钥请选择赛事');
    const items = [];
    try {
      for (let i = 0; i < count; i += 1) {
        const data = checkResult(await proxyToGameServer('/admin/rooms/create', {
          room_rule: rule, room_config: body.room_config || {}, password: body.password || '', created_by: userId,
        }));
        items.push(data);
        await writeAudit({ adminUserId: userId, action: 'room.create', targetType: 'room', targetId: data.room_id,
          payload: { room_rule: rule, sub_rule: body.room_config?.sub_rule }, reason });
      }
    } catch (error) {
      error.data = { items, requested_count: count, created_count: items.length };
      throw error;
    }
    return { items, requested_count: count, created_count: items.length };
  }

  async function close(userId, roomId, body) {
    const reason = String(body.reason || '').trim();
    if (!reason) fail(400, '请填写关闭原因');
    if (typeof body.instance_id !== 'string' || !body.instance_id) fail(400, '请刷新房间列表后重试');
    const data = checkResult(await proxyToGameServer(`/admin/rooms/${encodeURIComponent(roomId)}/close`, { instance_id: body.instance_id }));
    await writeAudit({ adminUserId: userId, action: 'room.close', targetType: 'room', targetId: roomId,
      payload: { instance_id: body.instance_id, closed_room_ids: data.closed_room_ids }, reason });
    return data;
  }
  return { list, create, close };
}

module.exports = { createAdminRoomService };
