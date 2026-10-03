const express = require('express');
const pool = require('../../config/database');
const config = require('../../config/config');
const service = require('../../services/titles');
const router = express.Router();

async function synchronize(userIds) {
  try {
    const response = await fetch(`${config.calcServer.baseUrl.replace(/\/$/, '')}/admin/titles/refresh`, {
      method: 'POST', headers: {'Content-Type':'application/json'},
      body: JSON.stringify({user_ids:userIds}), signal: AbortSignal.timeout(5000),
    });
    return response.ok;
  } catch (_) { return false; }
}

function route(work) {
  return async (req, res) => {
    try { await work(req, res); }
    catch (error) {
      if (error.code === '23505') return res.status(409).json({success:false,message:'头衔名称已存在'});
      if (error instanceof service.TitleError) return res.status(error.status).json({success:false,message:error.message});
      console.error('admin titles:', error);
      res.status(500).json({success:false,message:'头衔操作失败，请稍后重试'});
    }
  };
}

router.get('/', route(async (req, res) => res.json({success:true,data:await service.listTitles(pool)})));
router.post('/', route(async (req, res) => {
  const result = await service.saveTitle(pool, req.admin.userId, null, req.body || {});
  res.status(201).json({success:true,data:result.title,synced_online:await synchronize(result.userIds)});
}));
router.patch('/:titleId', route(async (req, res) => {
  const result = await service.saveTitle(pool, req.admin.userId, req.params.titleId, req.body || {});
  res.json({success:true,data:result.title,synced_online:await synchronize(result.userIds)});
}));
router.get('/users/:userId', route(async (req, res) => res.json({success:true,data:await service.userTitles(pool,req.params.userId)})));
router.post('/users/:userId/:titleId', route(async (req, res) => {
  const result = await service.changeGrant(pool,req.admin.userId,req.params.userId,req.params.titleId,req.body || {},true);
  res.json({success:true,data:result,synced_online:await synchronize(result.userIds)});
}));
router.delete('/users/:userId/:titleId', route(async (req, res) => {
  const result = await service.changeGrant(pool,req.admin.userId,req.params.userId,req.params.titleId,req.body || {},false);
  res.json({success:true,data:result,synced_online:await synchronize(result.userIds)});
}));
module.exports = router;
