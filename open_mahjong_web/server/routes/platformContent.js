const express = require('express');
const { defaultStore, publicDocument } = require('../services/platformContentStore');
function createRouter(store = defaultStore()) {
  const router = express.Router();
  router.get('/', (_req, res, next) => {
    try {
      const data = publicDocument(store.read());
      res.set('Cache-Control', 'no-cache').set('ETag', `"platform-content-${data.revision}"`).json(data);
    } catch (error) { next(error); }
  });
  router.get('/assets/:filename', (req, res) => {
    try {
      res.set('X-Content-Type-Options', 'nosniff').set('Cache-Control', 'public, max-age=86400').sendFile(store.assetPath(req.params.filename));
    } catch (error) { res.status(error.status || 500).end(); }
  });
  return router;
}
module.exports = createRouter();
module.exports.createRouter = createRouter;
