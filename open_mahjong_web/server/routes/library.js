const express = require('express');
const pool = require('../config/database');
const { requirePlayer } = require('../middleware/requirePlayer');
const { createWindowLimiter, getClientIp } = require('../middleware/rateLimit');

const router = express.Router();

const ALLOWED_RULE_KEYS = new Set([
  'guobiao',
  'riichi',
  'qingque',
  'classical',
  'sichuan',
  'changsha',
  'jiandan',
  'shiyangjin',
  'guobiao-kobayashi',
  'guobiao-kshen',
]);

const postWriteLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 10,
  keyFn: (req) => `${getClientIp(req)}:library-write`,
});

function normalizeRuleKey(raw) {
  const key = String(raw || '').trim().toLowerCase();
  if (!ALLOWED_RULE_KEYS.has(key)) return { error: '未知的规则条目' };
  return { value: key };
}

function normalizeTitle(title) {
  const text = String(title || '').trim();
  if (!text) return { error: '请填写标题' };
  if (text.length > 200) return { error: '标题过长（最多 200 字）' };
  return { value: text };
}

function normalizeBody(body, { max = 10000, label = '内容' } = {}) {
  const text = String(body || '').trim();
  if (!text) return { error: `请填写${label}` };
  if (text.length > max) return { error: `${label}过长（最多 ${max} 字）` };
  return { value: text };
}

function mapPost(row) {
  return {
    post_id: row.post_id,
    rule_key: row.rule_key,
    title: row.title,
    body: row.body,
    author_user_id: row.author_user_id,
    author_username: row.author_username || '',
    reply_count: Number(row.reply_count || 0),
    created_at: row.created_at,
    updated_at: row.updated_at,
  };
}

function mapReply(row) {
  return {
    reply_id: row.reply_id,
    post_id: row.post_id,
    body: row.body,
    author_user_id: row.author_user_id,
    author_username: row.author_username || '',
    created_at: row.created_at,
    updated_at: row.updated_at,
  };
}

router.get('/rules/:ruleKey/posts', async (req, res) => {
  try {
    const keyParsed = normalizeRuleKey(req.params.ruleKey);
    if (keyParsed.error) {
      return res.status(404).json({ success: false, message: keyParsed.error });
    }

    const limit = Math.min(Math.max(parseInt(req.query.limit, 10) || 50, 1), 100);
    const offset = Math.max(parseInt(req.query.offset, 10) || 0, 0);

    const result = await pool.query(
      `SELECT p.post_id, p.rule_key, p.title, p.body, p.author_user_id,
              p.created_at, p.updated_at,
              u.username AS author_username,
              COALESCE(r.cnt, 0)::int AS reply_count
       FROM library_posts p
       LEFT JOIN users u ON u.user_id = p.author_user_id
       LEFT JOIN (
         SELECT post_id, COUNT(*)::int AS cnt
         FROM library_replies
         GROUP BY post_id
       ) r ON r.post_id = p.post_id
       WHERE p.rule_key = $1
       ORDER BY p.updated_at DESC, p.post_id DESC
       LIMIT $2 OFFSET $3`,
      [keyParsed.value, limit, offset]
    );

    res.json({ success: true, data: { items: result.rows.map(mapPost) } });
  } catch (err) {
    console.error('library posts list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/posts/:postId', async (req, res) => {
  try {
    const postId = Number(req.params.postId);
    if (!Number.isFinite(postId) || postId <= 0) {
      return res.status(400).json({ success: false, message: '无效的帖子 ID' });
    }

    const postResult = await pool.query(
      `SELECT p.post_id, p.rule_key, p.title, p.body, p.author_user_id,
              p.created_at, p.updated_at,
              u.username AS author_username,
              COALESCE(r.cnt, 0)::int AS reply_count
       FROM library_posts p
       LEFT JOIN users u ON u.user_id = p.author_user_id
       LEFT JOIN (
         SELECT post_id, COUNT(*)::int AS cnt
         FROM library_replies
         GROUP BY post_id
       ) r ON r.post_id = p.post_id
       WHERE p.post_id = $1`,
      [postId]
    );
    if (!postResult.rows[0]) {
      return res.status(404).json({ success: false, message: '帖子不存在' });
    }

    const repliesResult = await pool.query(
      `SELECT r.reply_id, r.post_id, r.body, r.author_user_id,
              r.created_at, r.updated_at,
              u.username AS author_username
       FROM library_replies r
       LEFT JOIN users u ON u.user_id = r.author_user_id
       WHERE r.post_id = $1
       ORDER BY r.created_at ASC, r.reply_id ASC`,
      [postId]
    );

    res.json({
      success: true,
      data: {
        post: mapPost(postResult.rows[0]),
        replies: repliesResult.rows.map(mapReply),
      },
    });
  } catch (err) {
    console.error('library post detail:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/rules/:ruleKey/posts', postWriteLimiter, requirePlayer, async (req, res) => {
  try {
    const keyParsed = normalizeRuleKey(req.params.ruleKey);
    if (keyParsed.error) {
      return res.status(404).json({ success: false, message: keyParsed.error });
    }
    const titleParsed = normalizeTitle(req.body?.title);
    if (titleParsed.error) {
      return res.status(400).json({ success: false, message: titleParsed.error });
    }
    const bodyParsed = normalizeBody(req.body?.body, { label: '正文' });
    if (bodyParsed.error) {
      return res.status(400).json({ success: false, message: bodyParsed.error });
    }

    const result = await pool.query(
      `INSERT INTO library_posts (rule_key, title, body, author_user_id)
       VALUES ($1, $2, $3, $4)
       RETURNING post_id, rule_key, title, body, author_user_id, created_at, updated_at`,
      [keyParsed.value, titleParsed.value, bodyParsed.value, req.player.userId]
    );

    const row = result.rows[0];
    res.status(201).json({
      success: true,
      data: {
        post: mapPost({
          ...row,
          author_username: req.player.username || '',
          reply_count: 0,
        }),
      },
    });
  } catch (err) {
    console.error('library create post:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/posts/:postId/replies', postWriteLimiter, requirePlayer, async (req, res) => {
  const client = await pool.connect();
  try {
    const postId = Number(req.params.postId);
    if (!Number.isFinite(postId) || postId <= 0) {
      return res.status(400).json({ success: false, message: '无效的帖子 ID' });
    }
    const bodyParsed = normalizeBody(req.body?.body, { max: 5000, label: '回复' });
    if (bodyParsed.error) {
      return res.status(400).json({ success: false, message: bodyParsed.error });
    }

    await client.query('BEGIN');
    const postCheck = await client.query(
      `SELECT post_id FROM library_posts WHERE post_id = $1 FOR UPDATE`,
      [postId]
    );
    if (!postCheck.rows[0]) {
      await client.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '帖子不存在' });
    }

    const replyResult = await client.query(
      `INSERT INTO library_replies (post_id, body, author_user_id)
       VALUES ($1, $2, $3)
       RETURNING reply_id, post_id, body, author_user_id, created_at, updated_at`,
      [postId, bodyParsed.value, req.player.userId]
    );

    await client.query(
      `UPDATE library_posts SET updated_at = CURRENT_TIMESTAMP WHERE post_id = $1`,
      [postId]
    );
    await client.query('COMMIT');

    res.status(201).json({
      success: true,
      data: {
        reply: mapReply({
          ...replyResult.rows[0],
          author_username: req.player.username || '',
        }),
      },
    });
  } catch (err) {
    try {
      await client.query('ROLLBACK');
    } catch {
      /* ignore */
    }
    console.error('library create reply:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    client.release();
  }
});

module.exports = router;
