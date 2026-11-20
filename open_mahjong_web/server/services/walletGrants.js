const MAX_GRANT_MINOR = 100000000;

function failure(message, status = 400) {
  return Object.assign(new Error(message), { status });
}

function validateGrant(body) {
  const { amount_minor: amount, request_id: requestId } = body;
  const reason = typeof body.reason === 'string' ? body.reason.trim() : '';
  if (!Number.isSafeInteger(amount) || amount < 1 || amount > MAX_GRANT_MINOR) {
    throw failure('发放金额须为 0.01–1,000,000 萨拉币，最多两位小数');
  }
  if (!reason || reason.length > 500) throw failure('请填写发放原因（最多 500 字）');
  if (typeof requestId !== 'string' || !/^[A-Za-z0-9_-]{8,64}$/.test(requestId)) {
    throw failure('发放请求编号无效');
  }
  return { amount, reason, requestId };
}

async function grantWallet(pool, userId, adminUserId, body) {
  const { amount, reason, requestId } = validateGrant(body);
  const db = await pool.connect();
  try {
    await db.query('BEGIN');
    // Same lock order as game rewards and purchases: user, then wallet.
    const user = await db.query('SELECT is_tourist FROM users WHERE user_id=$1 FOR UPDATE', [userId]);
    if (!user.rows.length) throw failure('用户不存在', 404);
    if (user.rows[0].is_tourist !== false) throw failure('仅可向注册用户发放萨拉币');
    const sourceId = `${adminUserId}:${requestId}`;
    const previous = await db.query(
      "SELECT delta_minor,balance_after,metadata FROM wallet_ledger WHERE user_id=$1 AND source='admin_grant' AND source_id=$2",
      [userId, sourceId]);
    if (previous.rows.length) {
      const old = previous.rows[0];
      if (String(old.delta_minor) !== String(amount) || old.metadata.reason !== reason) {
        throw failure('此请求编号已用于其他发放，请核对记录', 409);
      }
      await db.query('COMMIT');
      return { duplicate: true, balance_after: old.balance_after, amount_minor: amount };
    }
    await db.query('INSERT INTO user_wallets(user_id) VALUES($1) ON CONFLICT DO NOTHING', [userId]);
    const wallet = await db.query(`UPDATE user_wallets SET balance_minor=balance_minor+$2,
      earned_minor=earned_minor+$2,version=version+1,updated_at=CURRENT_TIMESTAMP
      WHERE user_id=$1 RETURNING balance_minor`, [userId, amount]);
    const balance = wallet.rows[0].balance_minor;
    await db.query(`INSERT INTO wallet_ledger
      (user_id,source,source_id,delta_minor,balance_after,metadata)
      VALUES($1,'admin_grant',$2,$3,$4,$5::jsonb)`,
    [userId, sourceId, amount, balance, JSON.stringify({ admin_user_id: adminUserId, reason })]);
    await db.query(`INSERT INTO admin_audit_log(admin_user_id,action,target_type,target_id,payload,reason)
      VALUES($1,'wallet.grant','user',$2,$3::jsonb,$4)`,
    [adminUserId, String(userId), JSON.stringify({ request_id: requestId, amount_minor: amount, balance_after: balance }), reason]);
    await db.query('COMMIT');
    return { duplicate: false, balance_after: balance, amount_minor: amount };
  } catch (error) {
    await db.query('ROLLBACK');
    throw error;
  } finally {
    db.release();
  }
}

module.exports = { grantWallet, validateGrant };
