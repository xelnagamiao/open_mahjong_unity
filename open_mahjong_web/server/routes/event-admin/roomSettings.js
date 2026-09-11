const express = require('express');
const { SettingsError } = require('../../utils/eventRoomSettings');

async function callAutoMatchService(eventId, refresh = false) {
  const config = require('../../config/config');
  const base = config.calcServer.baseUrl.replace(/\/$/, '');
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), Math.min(config.calcServer.timeoutMs || 3000, 3000));
  try {
    const query = new URLSearchParams({ event_id: eventId });
    const response = await fetch(`${base}/admin/event/auto-match${refresh ? '/refresh' : `?${query}`}`, {
      method: refresh ? 'POST' : 'GET',
      ...(refresh ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ event_id: eventId }) } : {}),
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`Game service returned ${response.status}`);
    const data = await response.json();
    if (data.success === false || !data.runtime) throw new Error('Invalid auto-match service response');
    return data.runtime;
  } finally {
    clearTimeout(timeout);
  }
}

function createRoomSettingsRouter({
  service,
  membership,
  autoMatchService = callAutoMatchService,
} = {}) {
  const router = express.Router({ mergeParams: true });
  function fail(res, error) {
    if (error instanceof SettingsError) {
      return res.status(error.statusCode).json({ success: false, message: error.message, ...(error.data ? { data: error.data } : {}) });
    }
    console.error('event-admin room settings:', error);
    return res.status(500).json({ success: false, message: '对局设置操作失败，请稍后重试' });
  }

  router.get('/room-settings', membership, async (req, res) => {
    try {
      res.json({ success: true, data: await service.read(req.event.event_id) });
    } catch (error) { fail(res, error); }
  });

  router.get('/auto-match', membership, async (req, res) => {
    try {
      res.json({ success: true, data: await autoMatchService(req.event.event_id) });
    } catch (_) {
      res.status(503).json({ success: false, message: '暂时无法连接自动匹配服务，已保存的设置不会丢失' });
    }
  });

  const save = (kind) => async (req, res) => {
    try {
      const data = await service.update(req.event.event_id, req.eventAdmin.userId, {
        kind, body: req.body || {}, presetId: req.params.presetId,
      });
      let message = '对局设置已保存';
      if (kind === 'auto' || (data.auto_match.enabled && ['manual', 'preset-update'].includes(kind))) {
        try {
          data.runtime = await autoMatchService(req.event.event_id, true);
        } catch (_) {
          // The worker reads the durable configuration on every pass and when it starts.
          message = '设置已保存，游戏服务暂未响应；恢复后会自动生效';
        }
      }
      res.json({ success: true, data, message });
    } catch (error) { fail(res, error); }
  };
  router.put('/room-settings', membership, save('manual'));
  router.put('/auto-match', membership, save('auto'));
  router.post('/room-presets', membership, save('preset-create'));
  router.put('/room-presets/:presetId', membership, save('preset-update'));
  router.delete('/room-presets/:presetId', membership, save('preset-delete'));
  return router;
}

/** Shared handler keeps the legacy events router thin and permits isolated HTTP verification. */
function createSeatHandler({ service, proxyToGameServer, writeAudit, logger = console }) {
  return async (req, res) => {
    try {
      const eventId = req.event.event_id;
      const selected = await service.resolveForSeat(eventId, req.eventAdmin.userId, req.body || {});
      const { status, data } = await proxyToGameServer('/admin/event/rooms/seat', {
        event_id: eventId,
        user_ids: selected.user_ids,
        room_rule: selected.room_rule,
        room_config: selected.room_config,
        created_by: req.eventAdmin.userId,
      });
      if (status >= 400 || data?.success !== true) {
        const responseStatus = status >= 400 && status <= 599 ? status : data?.success === false ? 400 : 502;
        return res.status(responseStatus).json({ success: false, message: data?.detail || data?.message || '组桌失败，请刷新准备名单后重试' });
      }
      let auditWarning = false;
      try {
        await writeAudit({
          adminUserId: req.eventAdmin.userId,
          action: 'event_admin.seat', targetType: 'event', targetId: eventId,
          payload: {
            user_ids: selected.user_ids, room_rule: selected.room_rule, room_config: selected.room_config,
            preset_id: selected.preset_id, preset_name: selected.preset_name, settings_revision: selected.revision,
            room_info: data.room_info || null,
          },
          reason: String(req.body?.reason || '').trim().slice(0, 500),
        });
      } catch (error) {
        // Seating is already committed by the game service. A failed log must never suggest retrying it.
        auditWarning = true;
        logger.error('event-admin seat succeeded but audit failed:', error);
      }
      return res.json({
        success: true,
        data,
        message: auditWarning ? '组桌成功，操作日志暂未写入，请勿重复组桌' : data.message || '组桌成功',
        ...(auditWarning ? { audit_warning: true } : {}),
      });
    } catch (error) {
      if (error instanceof SettingsError) {
        return res.status(error.statusCode).json({ success: false, message: error.message, ...(error.data ? { data: error.data } : {}) });
      }
      logger.error('event-admin seat:', error);
      return res.status(500).json({ success: false, message: '组桌请求未完成，请刷新房间与准备名单后确认状态' });
    }
  };
}

module.exports = { createRoomSettingsRouter, createSeatHandler };
