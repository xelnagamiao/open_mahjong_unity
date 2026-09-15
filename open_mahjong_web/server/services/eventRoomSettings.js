const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection, normalizeSeatUserIds, SettingsError } = require('../utils/eventRoomSettings');

function createRoomSettingsService({ pool }) {
  async function read(eventId) {
    const result = await pool.query('SELECT room_settings FROM events WHERE event_id = $1', [eventId]);
    if (!result.rows.length) throw new SettingsError(404, '赛事或基地不存在');
    return readRoomSettings(result.rows[0].room_settings);
  }

  async function update(eventId, userId, change) {
    const client = await pool.connect();
    try {
      await client.query('BEGIN');
      // Lock the current document, never merge against the middleware's earlier snapshot.
      const result = await client.query(
        'SELECT status, room_settings FROM events WHERE event_id = $1 FOR UPDATE', [eventId],
      );
      if (!result.rows.length) throw new SettingsError(404, '赛事或基地不存在');
      const membership = await client.query(
        "SELECT role FROM event_admins WHERE event_id = $1 AND user_id = $2 AND role IN ('owner', 'admin')",
        [eventId, userId],
      );
      if (!membership.rows.length) throw new SettingsError(403, '您已无权修改该赛事或基地');
      const before = readRoomSettings(result.rows[0].room_settings);
      const changed = applyRoomSettingsChange(before, {
        ...change, userId, eventStatus: result.rows[0].status,
      });
      await client.query(
        'UPDATE events SET room_settings = $1::jsonb, updated_at = CURRENT_TIMESTAMP WHERE event_id = $2',
        [JSON.stringify(changed.settings), eventId],
      );
      // The audit and settings commit together; an audit error must not report a failed save after committing it.
      await client.query(
        `INSERT INTO admin_audit_log (admin_user_id, action, target_type, target_id, payload, reason)
         VALUES ($1, $2, 'event', $3, $4::jsonb, $5)`,
        [userId, `event_admin.room_settings.${change.kind}`, eventId,
          JSON.stringify({ before, after: changed.settings, preset_id: changed.savedPresetId || change.presetId || null }),
          String(change.body?.reason || '').trim().slice(0, 500)],
      );
      await client.query('COMMIT');
      return {
        ...changed.settings,
        ...(changed.savedPresetId ? { saved_preset_id: changed.savedPresetId } : {}),
      };
    } catch (error) {
      try { await client.query('ROLLBACK'); } catch (_) { /* Keep the original error. */ }
      throw error;
    } finally {
      client.release();
    }
  }

  async function resolveForSeat(eventId, userId, body = {}) {
    const ids = normalizeSeatUserIds(body.user_ids);
    if (Object.prototype.hasOwnProperty.call(body, 'room_rule') || Object.prototype.hasOwnProperty.call(body, 'room_config')) {
      throw new SettingsError(400, '请从默认配置或预设中选择本桌对局设置');
    }
    // One read snapshot binds selection, revision and permission. No lock survives into the Python request:
    // the game service must acquire its own event-row lock to claim the four waiting players.
    const result = await pool.query(
      `SELECT e.status, e.room_settings, ea.role
       FROM events e LEFT JOIN event_admins ea ON ea.event_id = e.event_id AND ea.user_id = $2
       WHERE e.event_id = $1`, [eventId, userId],
    );
    const event = result.rows[0];
    if (!event) throw new SettingsError(404, '赛事或基地不存在');
    if (!['owner', 'admin'].includes(event.role)) throw new SettingsError(403, '您已无权管理该赛事或基地');
    if (event.status !== 'active') throw new SettingsError(400, '赛事或基地未开启，无法组桌');
    return { ...resolveRoomSelection(event.room_settings, body), user_ids: ids };
  }

  return { read, update, resolveForSeat };
}

module.exports = { createRoomSettingsService };
