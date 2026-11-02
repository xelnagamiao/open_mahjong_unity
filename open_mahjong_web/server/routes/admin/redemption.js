const router = require('express').Router();
router.use(require('../../utils/rewardProxy')('/admin/redemption'));
module.exports = router;
