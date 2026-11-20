const express = require('express');
const pool = require('../../config/database');
const { grantWallet } = require('../../services/walletGrants');
const router = express.Router();

router.use('/:userId', (req, res, next) => {
  if (!/^[1-9]\d{0,18}$/.test(req.params.userId) || BigInt(req.params.userId) > 9223372036854775807n) {
    return res.status(400).json({ success: false, message: '用户 ID 无效' });
  }
  next();
});

router.get('/:userId', async (req, res) => {
  try {
    const user = await pool.query(`SELECT u.is_tourist,COALESCE(w.balance_minor,0) AS balance_minor
      FROM users u LEFT JOIN user_wallets w USING(user_id) WHERE u.user_id=$1`, [req.params.userId]);
    if (!user.rows.length) return res.status(404).json({ success: false, message: '用户不存在' });
    const ledger = await pool.query(`SELECT entry_id,delta_minor,balance_after,metadata,created_at
      FROM wallet_ledger WHERE user_id=$1 AND source='admin_grant' ORDER BY entry_id DESC LIMIT 20`, [req.params.userId]);
    res.json({ success: true, data: { ...user.rows[0], grants: ledger.rows } });
  } catch (error) {
    console.error('admin wallet read:', error);
    res.status(500).json({ success: false, message: '读取萨拉币失败' });
  }
});

router.post('/:userId/grant', async (req, res) => {
  try {
    const data = await grantWallet(pool, req.params.userId, req.admin.userId, req.body || {});
    res.json({ success: true, data });
  } catch (error) {
    if (!error.status) console.error('admin wallet grant:', error);
    res.status(error.status || 503).json({ success: false,
      message: error.status ? error.message : '发放结果未确认，请使用原请求重试', retryable: !error.status });
  }
});

module.exports = router;
