function fail(status, message) { throw Object.assign(new Error(message), { status }); }

function createEventRoomBotService({ proxyToGameServer, writeAudit }) {
  async function add(event, userId, roomId, body = {}, { platformAdmin = false } = {}) {
    if (event.status !== 'active') fail(400, '赛事或基地未开启，不能添加机器人');
    if (!['auto', 'smart', 'heuristic'].includes(body.bot_type)) fail(400, '请选择机器人类型');
    if (!Number.isInteger(body.seat_index) || body.seat_index < 0 || body.seat_index > 3) fail(400, '请选择有效空位');
    if (typeof body.instance_id !== 'string' || !body.instance_id.trim()) fail(400, '请刷新房间列表后重试');
    const reason = String(body.reason || '').trim();
    if (platformAdmin && !reason) fail(400, '请填写操作原因');
    const payload = { event_id: event.event_id, instance_id: body.instance_id,
      seat_index: body.seat_index, bot_type: body.bot_type };
    const { status, data } = await proxyToGameServer(`/admin/rooms/${encodeURIComponent(roomId)}/bots`, payload);
    if (status >= 400 || data?.success === false) fail(status >= 400 ? status : 400, data?.detail || data?.message || '添加机器人失败');
    await writeAudit({ adminUserId: userId, action: platformAdmin ? 'event.room.add_bot' : 'event_admin.room.add_bot',
      targetType: 'event', targetId: event.event_id, payload: { room_id: roomId, ...payload }, reason });
    return data;
  }
  return { add };
}

module.exports = { createEventRoomBotService };
