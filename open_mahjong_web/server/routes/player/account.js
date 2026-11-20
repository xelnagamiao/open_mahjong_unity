const router = require('express').Router();
const { requirePlayer } = require('../../middleware/requirePlayer');
const { createWindowLimiter } = require('../../middleware/rateLimit');
const rewardProxy = require('../../utils/rewardProxy');
const { getAccountProfile } = require('../../services/accountProfile');
const pool = require('../../config/database');
const { withRecordMetadataQuery } = require('../../services/recordMetadataQuery');

router.use(['/mail', '/inventory', '/account'], requirePlayer,
  (_req, res, next) => { res.set('Cache-Control', 'no-store'); next(); },
  createWindowLimiter({ windowMs: 60000, max: 120, keyFn: req => `${req.player.userId}:account` }));
router.use('/mail', rewardProxy('/player/mail'));
router.use('/inventory', rewardProxy('/player/inventory'));
router.get('/account/profile', async (req, res) => {
  try {
    const profile = await withRecordMetadataQuery(pool, db => getAccountProfile(db, req.player.userId, req.query.year));
    if (!profile) return res.status(404).json({ success: false, message: '账户不存在' });
    return res.json({ success: true, data: profile });
  } catch (error) {
    if (error.status === 400) return res.status(400).json({ success: false, message: error.message });
    console.error('account profile:', error.message);
    return res.status(500).json({ success: false, message: '个人资料暂时无法加载，请稍后重试' });
  }
});
module.exports = router;
