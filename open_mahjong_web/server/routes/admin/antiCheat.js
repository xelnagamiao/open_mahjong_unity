"use strict";
const express = require("express");
const { defaultCenter } = require("../../services/antiCheat/center/service");
function createRouter({ center } = {}) {
  const router = express.Router();
  const service = () => center || defaultCenter();
  router.use((req, res, next) => {
    res.set("Cache-Control", "no-store");
    if (!req.admin?.userId)
      return res
        .status(401)
        .json({ success: false, message: "需要管理员登录" });
    next();
  });
  const wrap = (fn) => async (req, res, next) => {
    try {
      const data = await fn(req);
      res.json({ success: true, data });
    } catch (e) {
      next(e);
    }
  };
  router.get(
    "/overview",
    wrap((req) => service().overview(req.query)),
  );
  router.get(
    "/settings",
    wrap(() => service().getSettings()),
  );
  router.put(
    "/settings",
    wrap((req) => service().saveSettings(req.body, req.admin.userId)),
  );
  router.get(
    "/pairs",
    wrap((req) => service().listPairs(req.query)),
  );
  router.get(
    "/players",
    wrap((req) => service().listPlayers(req.query)),
  );
  router.get(
    "/players/:id",
    wrap((req) => service().player(req.params.id, req.query)),
  );
  router.post("/robert-reviews", wrap((req) => service().startRobertReview(req.body)));
  router.get("/robert-reviews/:id", wrap((req) => service().getRobertReview(req.params.id)));
  router.post("/robert-reviews/:id/cancel", wrap((req) => service().cancelRobertReview(req.params.id)));
  router.get(
    "/pairs/:key",
    wrap((req) => service().pair(req.params.key, req.query)),
  );
  router.patch(
    "/pairs/:key/review",
    wrap((req) => service().review(req.params.key, req.body, req.admin.userId)),
  );
  router.get(
    "/updates",
    wrap((req) => service().updates(req.query)),
  );
  router.post(
    "/updates",
    wrap((req) => service().startUpdate(req.admin.userId)),
  );
  router.get(
    "/updates/:id",
    wrap((req) => service().update(req.params.id)),
  );
  router.post(
    "/updates/:id/cancel",
    wrap((req) => service().cancel(req.params.id, req.admin.userId)),
  );
  router.use((error, req, res, next) => {
    const status = error.status || 500;
    res.status(status).json({
      success: false,
      message:
        status < 500 ? error.message : "数据中心操作失败，请查看本地服务日志",
    });
  });
  return router;
}
module.exports = createRouter();
module.exports.createRouter = createRouter;
