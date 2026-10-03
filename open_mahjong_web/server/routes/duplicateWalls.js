const express = require('express');
const { requirePlayer } = require('../middleware/requirePlayer');
const { createWindowLimiter, getClientIp } = require('../middleware/rateLimit');
const { catalog, createDuplicateWallService } = require('../services/duplicateWalls');

function createRouter(service, auth = requirePlayer) {
  const router = express.Router();
  router.use((_req, res, next) => { res.set('Cache-Control', 'no-store'); next(); });
  router.use(createWindowLimiter({ windowMs: 60_000, max: 120, keyFn: (req) => `${getClientIp(req)}:duplicate-walls` }));
  const handle = (fn, status = 200) => async (req, res) => {
    try { res.status(status).json({ success: true, data: await fn(req) }); }
    catch (error) {
      if (!error.status) console.error('duplicate walls:', error);
      res.status(error.status || 500).json({ success: false, message: error.status ? error.message : '复式服务暂时不可用' });
    }
  };
  router.get('/catalog', handle(() => catalog()));
  router.get('/public', handle((req) => service.publicList(req.query.key)));
  router.get('/public/:key', handle((req) => service.publicDetail(req.params.key)));
  router.get('/mine', auth, handle((req) => service.mine(req.player.userId, req.query.scope, req.query.event_id)));
  router.post('/', auth, handle((req) => service.create(req.player.userId, req.body), 201));
  router.patch('/:id', auth, handle((req) => service.manage(req.player.userId, req.params.id, { is_unlocked: req.body?.is_unlocked })));
  router.delete('/:id', auth, handle((req) => service.manage(req.player.userId, req.params.id, { remove: true })));
  return router;
}

module.exports = createRouter(createDuplicateWallService(require('../config/database')));
module.exports.createRouter = createRouter;
