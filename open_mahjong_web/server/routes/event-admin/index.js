const express = require('express');
const { requireEventAdmin, requireEventMembership } = require('../../middleware/requireEventAdmin');
const { createWindowLimiter } = require('../../middleware/rateLimit');

const authRoutes = require('./auth');
const eventsRoutes = require('./events');
const eventExtrasRoutes = require('./announcements');
const { createRoomSettingsRouter } = require('./roomSettings');
const { createRoomSettingsService } = require('../../services/eventRoomSettings');
const pool = require('../../config/database');
const roomSettingsRoutes = createRoomSettingsRouter({
  service: createRoomSettingsService({ pool }),
  membership: requireEventMembership,
});

const router = express.Router();

const eventAdminLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 120,
  keyFn: (req) => `${req.ip || 'unknown'}:event-admin`,
});

router.use(eventAdminLimiter);

// 登录无需 JWT
router.use('/auth', authRoutes);

// 以下均需赛事主/子管理员 JWT
router.use(requireEventAdmin);
router.use('/events', eventsRoutes);
router.use('/events/:eventId', roomSettingsRoutes);
router.use('/events/:eventId', eventExtrasRoutes);

module.exports = router;
