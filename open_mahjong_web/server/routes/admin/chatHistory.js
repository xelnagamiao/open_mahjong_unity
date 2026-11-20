const express = require('express');
const pool = require('../../config/database');

const router = express.Router();
const PAGE_SIZE = 40;
const CONTEXT_SIZE = 20;

function messageId(value) {
  return typeof value === 'string' && /^[1-9]\d{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n;
}

function validText(value, max) {
  return typeof value === 'string' && value.length <= max && !value.includes('\0');
}

router.use((_req, res, next) => {
  res.set('Cache-Control', 'no-store');
  next();
});

router.get('/', async (req, res) => {
  const { kind = 'all', sender = '', keyword = '', before } = req.query;
  if (!['all', 'dm', 'venue'].includes(kind) || !validText(sender, 255) || !validText(keyword, 500)
      || (before !== undefined && !messageId(before))) {
    return res.status(400).json({ success: false, message: '聊天记录查询条件无效' });
  }
  const conditions = [];
  const params = [];
  if (kind !== 'all') conditions.push(`c.kind = $${params.push(kind)}`);
  if (sender.trim()) {
    const param = '$' + params.push(sender.trim());
    conditions.push(`(m.sender_user_id::text = ${param} OR strpos(lower(s.username), lower(${param})) > 0)`);
  }
  if (keyword.trim()) conditions.push(`strpos(lower(m.content), lower($${params.push(keyword.trim())})) > 0`);
  if (before) conditions.push(`m.message_id < $${params.push(before)}`);

  try {
    const { rows } = await pool.query(
      `SELECT m.message_id, m.conversation_key, m.created_at, m.sender_user_id, s.username AS sender_name,
              m.content, c.kind, c.peer_low, c.peer_high, a.username AS low_name, b.username AS high_name,
              c.event_id, e.name AS venue_name, e.kind AS venue_kind
       FROM chat_messages m
       JOIN chat_conversations c USING (conversation_key)
       JOIN users s ON s.user_id = m.sender_user_id
       LEFT JOIN users a ON a.user_id = c.peer_low
       LEFT JOIN users b ON b.user_id = c.peer_high
       LEFT JOIN events e ON e.event_id = c.event_id
       ${conditions.length ? 'WHERE ' + conditions.join(' AND ') : ''}
       ORDER BY m.message_id DESC LIMIT $${params.push(PAGE_SIZE + 1)}`, params
    );
    res.json({ success: true, data: { items: rows.slice(0, PAGE_SIZE), has_more: rows.length > PAGE_SIZE } });
  } catch (error) {
    console.error('admin chat history:', error.code);
    res.status(500).json({ success: false, message: '读取聊天记录失败' });
  }
});

// Chronological slices use the same conversation/message index as the game client.
router.get('/:key', async (req, res) => {
  const { key } = req.params;
  const { anchor, direction = 'around' } = req.query;
  if (!/^(?:dm:[1-9]\d{0,18}:[1-9]\d{0,18}|venue:[A-Za-z0-9_-]{1,32})$/.test(key)
      || !messageId(anchor) || !['around', 'older', 'newer'].includes(direction)) {
    return res.status(400).json({ success: false, message: '会话或消息位置无效' });
  }

  async function readSide(older) {
    const operator = older ? (direction === 'around' ? '<=' : '<') : '>';
    const { rows } = await pool.query(
      `SELECT m.message_id, m.created_at, m.sender_user_id, u.username AS sender_name, m.content
       FROM chat_messages m JOIN users u ON u.user_id = m.sender_user_id
       WHERE m.conversation_key = $1 AND m.message_id ${operator} $2
       ORDER BY m.message_id ${older ? 'DESC' : 'ASC'} LIMIT $3`, [key, anchor, CONTEXT_SIZE + 1]
    );
    const items = rows.slice(0, CONTEXT_SIZE);
    return { items: older ? items.reverse() : items, more: rows.length > CONTEXT_SIZE };
  }
  try {
    const [older, newer] = await Promise.all([
      direction === 'newer' ? null : readSide(true),
      direction === 'older' ? null : readSide(false),
    ]);
    res.json({ success: true, data: {
      items: [...(older?.items || []), ...(newer?.items || [])],
      has_older: older?.more, has_newer: newer?.more,
    } });
  } catch (error) {
    console.error('admin chat context:', error.code);
    res.status(500).json({ success: false, message: '读取会话失败' });
  }
});

module.exports = router;
