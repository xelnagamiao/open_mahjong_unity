const pool = require('../config/database');
const { checkAccountSession } = require('../utils/accountBans');

async function requireActiveAdminAccount(req, res, next) {
  try {
    const { userId } = req.admin || req.eventAdmin;
    if (await checkAccountSession(pool, userId, res)) return next();
  } catch (err) {
    return next(err);
  }
}

module.exports = { requireActiveAdminAccount };
