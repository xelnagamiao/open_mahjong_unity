const express = require('express');
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');
const { proxyToGameServer } = require('../../services/gameServerProxy');
const { createEventRoomCreationService } = require('../../services/eventRoomCreation');
const { createDuplicateWallService } = require('../../services/duplicateWalls');
const { createAdminRoomService } = require('../../services/adminRooms');

const router = express.Router();
const duplicateWalls = createDuplicateWallService(pool);
const eventRoomCreation = createEventRoomCreationService({ duplicateWalls, proxyToGameServer, writeAudit });
const service = createAdminRoomService({ pool, proxyToGameServer, writeAudit, eventRoomCreation });

function route(action) {
  return async (req, res) => {
    try {
      const data = await action(req);
      res.json({ success: true, data, message: data.message });
    } catch (error) {
      console.error('admin rooms:', error.message);
      res.status(error.status || 502).json({ success: false, message: error.status ? error.message : '游戏服暂时不可用，请刷新列表确认操作结果', data: error.data });
    }
  };
}

router.get('/', route(() => service.list()));
router.get('/venues', route(async () => ({ items: (await pool.query(
  "SELECT event_id, name, kind FROM events WHERE status = 'active' ORDER BY kind, name, event_id"
)).rows })));
router.post('/create', route(req => service.create(req.admin.userId, req.body || {})));
router.post('/:roomId/close', route(req => service.close(req.admin.userId, req.params.roomId, req.body || {})));
module.exports = router;
