function rewriteRecordSnapshot(value, userId, { oldUsername = null, newUsername }) {
  let changed = false;
  const visit = (node) => {
    if (Array.isArray(node)) return node.forEach(visit);
    if (!node || typeof node !== 'object') return;
    const id = node.user_id ?? node.userId;
    if (Number(id) === userId && (oldUsername === null || node.username === oldUsername)) {
      if (typeof node.username === 'string' && node.username !== newUsername) {
        node.username = newUsername;
        changed = true;
      }
    }
    // 牌谱 game_title 使用 p0_uid / p0_name（而不是玩家对象）保存四家快照。
    for (const key of Object.keys(node)) {
      const match = key.match(/^(p\d+)_uid$/);
      if (!match || Number(node[key]) !== userId) continue;
      const nameKey = `${match[1]}_name`;
      if ((oldUsername === null || node[nameKey] === oldUsername) && typeof node[nameKey] === 'string' && node[nameKey] !== newUsername) {
        node[nameKey] = newUsername;
        changed = true;
      }
    }
    Object.values(node).forEach(visit);
  };
  visit(value);
  return changed;
}

async function rewriteGameRecordSnapshots(client, userId, options) {
  const rows = await client.query(
    `SELECT gr.game_id, gr.record
       FROM game_records gr
      WHERE EXISTS (SELECT 1 FROM game_player_records gpr
                     WHERE gpr.game_id = gr.game_id AND gpr.user_id = $1
                       AND ($2::text IS NULL OR gpr.username = $2))
      FOR UPDATE`,
    [userId, options.oldUsername]
  );
  let count = 0;
  for (const row of rows.rows) {
    const record = typeof row.record === 'string' ? JSON.parse(row.record) : row.record;
    if (rewriteRecordSnapshot(record, userId, options)) {
      await client.query(`UPDATE game_records SET record = $1::jsonb WHERE game_id = $2`, [JSON.stringify(record), row.game_id]);
      count += 1;
    }
  }
  return count;
}

module.exports = { rewriteRecordSnapshot, rewriteGameRecordSnapshots };
