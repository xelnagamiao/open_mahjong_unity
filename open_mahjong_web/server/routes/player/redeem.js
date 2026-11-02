const router = require('express').Router();
const { requirePlayer } = require('../../middleware/requirePlayer');
const { createWindowLimiter, getClientIp } = require('../../middleware/rateLimit');
router.post('/', requirePlayer, createWindowLimiter({
  windowMs: 60000, max: 20, keyFn: req => `${getClientIp(req)}:redeem`,
}), require('../../utils/rewardProxy')('/player/redeem'));
module.exports = router;
