const express = require('express');
const { requireAdmin } = require('../../middleware/requireAdmin');
const { createWindowLimiter } = require('../../middleware/rateLimit');

const authRoutes = require('./auth');
const dashboardRoutes = require('./dashboard');
const usersRoutes = require('./users');
const rankRoutes = require('./rank');
const gamesRoutes = require('./games');
const gameControlRoutes = require('./game_control');
const auditRoutes = require('./audit');
const messagesRoutes = require('./messages');
const ipBansRoutes = require('./ip_bans');
const statsRoutes = require('./stats');

const router = express.Router();

const adminLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 120,
  keyFn: (req) => `${req.ip || 'unknown'}:admin`,
});

router.use(adminLimiter);

// 登录无需 JWT
router.use('/auth', authRoutes);

// 以下均需管理员 JWT
router.use(requireAdmin);
router.use('/dashboard', dashboardRoutes);
router.use('/users', usersRoutes);
router.use('/rank', rankRoutes);
router.use('/games', gamesRoutes);
router.use('/game-control', gameControlRoutes);
router.use('/audit', auditRoutes);
router.use('/messages', messagesRoutes);
router.use('/ip-bans', ipBansRoutes);
router.use('/stats', statsRoutes);

module.exports = router;
