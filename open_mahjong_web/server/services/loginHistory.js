// Reuse the game server's existing history table. No GeoIP lookup, HTTP call or visit tracking.
async function recordSuccessfulWebLogin(pool, userId, ip, logger = console) {
  if (!Number.isSafeInteger(Number(userId)) || Number(userId) <= 10000000) return false;
  try {
    await pool.query({
      text: `WITH inserted AS (
        INSERT INTO user_login_ips (user_id, ip_address) VALUES ($1, $2) RETURNING id
      ) DELETE FROM user_login_ips WHERE user_id = $1 AND id IN (
        SELECT id FROM user_login_ips WHERE user_id = $1 ORDER BY logged_at DESC, id DESC OFFSET 19
      )`,
      // Use the same database timestamp default as Python so both services order logins consistently.
      values: [userId, String(ip || 'unknown').trim().slice(0, 45)],
      query_timeout: 2000,
    });
    return true;
  } catch {
    logger.warn('Could not save successful web login history');
    return false;
  }
}
module.exports = { recordSuccessfulWebLogin };
