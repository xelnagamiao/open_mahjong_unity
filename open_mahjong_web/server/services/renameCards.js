const crypto = require('node:crypto');

// Shared with Python inventory.py. Always lock catalog, then user, then inventory.
const CATALOG_LOCK = 220922041;
const RENAME_CARD_ASSET = 'item.rename_credit';

// Both account and administration queries use the unaliased users table.
const RENAME_CARD_COLUMNS = `
  (SELECT COALESCE(SUM(i.quantity), 0) FROM user_inventory i
     JOIN item_definitions d USING (item_id)
    WHERE i.user_id = users.user_id AND d.asset_key = '${RENAME_CARD_ASSET}') AS rename_card_count,
  (SELECT COALESCE(SUM(i.quantity), 0) FROM user_inventory i
     JOIN item_definitions d USING (item_id)
    WHERE i.user_id = users.user_id AND d.asset_key = '${RENAME_CARD_ASSET}'
      AND d.use_enabled) AS rename_card_usable_count`;

async function consumeRenameCard(client, userId, oldUsername, newUsername) {
  // Caller holds the shared catalog lock and the user's FOR UPDATE row lock.
  const available = await client.query(
    `SELECT i.item_id, i.quantity FROM user_inventory i
       JOIN item_definitions d USING (item_id)
      WHERE i.user_id = $1 AND i.quantity > 0 AND d.asset_key = $2 AND d.use_enabled
      ORDER BY CASE WHEN d.code = 'rename_coupon' THEN 0 ELSE 1 END, d.sort_order, d.item_id
      LIMIT 1 FOR UPDATE OF i`,
    [userId, RENAME_CARD_ASSET]
  );
  if (!available.rows.length) {
    throw Object.assign(new Error('改名卡不足或暂不可用'), { status: 400 });
  }
  const card = available.rows[0];
  const operationId = crypto.randomUUID();
  const result = {
    old_username: oldUsername, new_username: newUsername,
    changes: [{ item_id: card.item_id, delta: -1, quantity_after: card.quantity - 1 }],
  };
  const hash = crypto.createHash('sha256').update(JSON.stringify({ userId, ...result })).digest('hex');
  await client.query(
    `INSERT INTO inventory_operations
       (operation_id, user_id, request_id, request_hash, action, reason, result)
     VALUES ($1, $2, $3, $4, 'rename', '账号自助改名', $5::jsonb)`,
    [operationId, userId, `rename_${operationId}`, hash, JSON.stringify(result)]
  );
  await client.query(
    `UPDATE user_inventory SET quantity = quantity - 1, updated_at = CURRENT_TIMESTAMP
      WHERE user_id = $1 AND item_id = $2`,
    [userId, card.item_id]
  );
  await client.query(
    `INSERT INTO inventory_ledger (operation_id, user_id, item_id, delta, quantity_after)
     VALUES ($1, $2, $3, -1, $4)`,
    [operationId, userId, card.item_id, card.quantity - 1]
  );
  return operationId;
}

module.exports = { CATALOG_LOCK, RENAME_CARD_COLUMNS, consumeRenameCard };
