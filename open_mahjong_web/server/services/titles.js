const { writeAudit } = require('../utils/audit');

class TitleError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

function id(value, minimum = 2) {
  if (!/^\d+$/.test(String(value)) || !Number.isSafeInteger(Number(value)) || Number(value) < minimum || Number(value) > 2147483647) {
    throw new TitleError(400, '无效的 ID');
  }
  return Number(value);
}

function text(value, label, max, required = false) {
  if (typeof value !== 'string') throw new TitleError(400, `${label}必须是文本`);
  const result = value.trim();
  if ((required && !result) || [...result].length > max || /[<>\x00-\x1f\x7f]/.test(result)) {
    throw new TitleError(400, `${label}${required ? '不能为空，' : ''}最多 ${max} 字，不能包含标签或控制字符`);
  }
  return result;
}

function definition(body) {
  const result = {
    name: text(body.name, '头衔名称', 24, true),
    description: text(body.description ?? '', '说明', 200),
    is_enabled: body.is_enabled ?? true,
    sort_order: body.sort_order ?? 0,
  };
  if (typeof result.is_enabled !== 'boolean') throw new TitleError(400, '启用状态必须是布尔值');
  if (!Number.isInteger(result.sort_order) || Math.abs(result.sort_order) > 1000000) throw new TitleError(400, '排序须为 -1000000 至 1000000 的整数');
  return result;
}

async function transaction(db, work) {
  const client = await db.connect();
  try {
    await client.query('BEGIN');
    const result = await work(client);
    await client.query('COMMIT');
    return result;
  } catch (error) {
    await client.query('ROLLBACK');
    throw error;
  } finally { client.release(); }
}

async function listTitles(db) {
  const result = await db.query(`SELECT t.*, (SELECT COUNT(*)::int FROM user_titles ut WHERE ut.title_id = t.title_id) AS owner_count
    FROM titles t ORDER BY t.sort_order, t.title_id`);
  return result.rows;
}

async function userTitles(db, userId) {
  userId = id(userId, 11);
  const user = await db.query('SELECT user_id FROM users WHERE user_id = $1', [userId]);
  if (!user.rowCount) throw new TitleError(404, '用户不存在');
  const result = await db.query(`SELECT t.*, ut.granted_at, ut.granted_by, ut.grant_reason,
      COALESCE(us.title_id = t.title_id AND t.is_enabled, FALSE) AS equipped
    FROM user_titles ut JOIN titles t USING (title_id)
    LEFT JOIN user_settings us ON us.user_id = ut.user_id
    WHERE ut.user_id = $1 ORDER BY t.sort_order, t.title_id`, [userId]);
  return result.rows;
}

async function saveTitle(db, adminUserId, titleId, body) {
  const value = definition(body);
  const reason = text(body.reason, '变更原因', 500, true);
  if (titleId != null) titleId = id(titleId);
  return transaction(db, async (client) => {
    let before = null;
    let userIds = [];
    if (titleId != null) {
      const found = await client.query('SELECT * FROM titles WHERE title_id = $1 FOR UPDATE', [titleId]);
      if (!found.rowCount) throw new TitleError(404, '头衔不存在');
      before = found.rows[0];
      const owners = await client.query('SELECT user_id FROM user_titles WHERE title_id = $1', [titleId]);
      userIds = owners.rows.map(row => Number(row.user_id));
    }
    const result = before
      ? await client.query(`UPDATE titles SET name=$2, description=$3, is_enabled=$4, sort_order=$5, updated_at=CURRENT_TIMESTAMP
          WHERE title_id=$1 RETURNING *`, [titleId, value.name, value.description, value.is_enabled, value.sort_order])
      : await client.query(`INSERT INTO titles (name,description,is_enabled,sort_order) VALUES ($1,$2,$3,$4) RETURNING *`,
          [value.name, value.description, value.is_enabled, value.sort_order]);
    const after = result.rows[0];
    if (!after.is_enabled) {
      await client.query('UPDATE user_settings SET title_id=1, updated_at=CURRENT_TIMESTAMP WHERE title_id=$1', [after.title_id]);
    }
    await writeAudit({adminUserId, action: before ? 'title.update' : 'title.create', targetType: 'title', targetId: after.title_id,
      payload: {before, after}, reason}, client);
    return {title: after, userIds};
  });
}

async function changeGrant(db, adminUserId, userId, titleId, body, grant) {
  userId = id(userId, 11);
  titleId = id(titleId);
  const reason = text(body.reason, '变更原因', 500, true);
  return transaction(db, async (client) => {
    const title = await client.query('SELECT * FROM titles WHERE title_id=$1 FOR SHARE', [titleId]);
    if (!title.rowCount) throw new TitleError(404, '头衔不存在');
    if (grant && !title.rows[0].is_enabled) throw new TitleError(409, '已停用的头衔不能授予');
    const user = await client.query('SELECT user_id FROM users WHERE user_id=$1 FOR UPDATE', [userId]);
    if (!user.rowCount) throw new TitleError(404, '用户不存在');
    let result;
    if (grant) {
      result = await client.query(`INSERT INTO user_titles (user_id,title_id,granted_by,grant_reason) VALUES ($1,$2,$3,$4)
        ON CONFLICT (user_id,title_id) DO NOTHING RETURNING *`, [userId,titleId,adminUserId,reason]);
    } else {
      result = await client.query('DELETE FROM user_titles WHERE user_id=$1 AND title_id=$2 RETURNING *', [userId,titleId]);
      await client.query('UPDATE user_settings SET title_id=1, updated_at=CURRENT_TIMESTAMP WHERE user_id=$1 AND title_id=$2', [userId,titleId]);
    }
    await writeAudit({adminUserId, action: grant ? 'title.grant' : 'title.revoke', targetType: 'user', targetId: userId,
      payload: {title_id:titleId, changed:result.rowCount > 0, grant:result.rows[0] || null}, reason}, client);
    return {userIds:[userId], changed:result.rowCount > 0};
  });
}

module.exports = {TitleError, id, definition, listTitles, userTitles, saveTitle, changeGrant};
