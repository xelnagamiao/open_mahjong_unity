/**
 * /api/player 与 /api/bot/player 共用的路由 handler
 */
const {
  resolveUserId,
  fetchPlayerInfo,
  fetchPlayerRecords,
  fetchPlayerRankStats,
  fetchPlayerRank,
  fetchPlayerScopeCounts,
  parseRecordQuery,
  parsePagination,
} = require('./playerPublicApi');

async function handlePlayerInfo(req, res) {
  try {
    const userId = await resolveUserId(req.params.key);
    if (userId == null) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const data = await fetchPlayerInfo(userId);
    if (!data) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    res.json({
      success: true,
      message: '获取玩家信息成功',
      data,
    });
  } catch (error) {
    console.error('player info:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
}

async function handlePlayerRecords(req, res) {
  try {
    const userId = await resolveUserId(req.params.key);
    if (userId == null) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const { offset, limit } = parsePagination(req.query);
    const query = parseRecordQuery(req.query);
    const data = await fetchPlayerRecords(userId, query, offset, limit);

    res.json({ success: true, data });
  } catch (error) {
    console.error('player records:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
}

async function handlePlayerRankStats(req, res) {
  try {
    const userId = await resolveUserId(req.params.key);
    if (userId == null) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const query = parseRecordQuery(req.query);
    const data = await fetchPlayerRankStats(userId, query);

    res.json({ success: true, data });
  } catch (error) {
    console.error('player rank-stats:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
}

async function handlePlayerRank(req, res) {
  try {
    const userId = await resolveUserId(req.params.key);
    if (userId == null) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const data = await fetchPlayerRank(userId);
    if (!data) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    res.json({ success: true, data });
  } catch (error) {
    console.error('player rank:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
}

async function handlePlayerScopeCounts(req, res) {
  try {
    const userId = await resolveUserId(req.params.key);
    if (userId == null) return res.status(404).json({ success: false, message: '用户不存在' });
    const { rule, sub_rule, game_type, date_from, date_to } = parseRecordQuery(req.query);
    const data = await fetchPlayerScopeCounts(userId, { rule, sub_rule, game_type, date_from, date_to });
    res.json({ success: true, data });
  } catch (error) {
    console.error('player scope-counts:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
}

module.exports = {
  handlePlayerInfo,
  handlePlayerRecords,
  handlePlayerRankStats,
  handlePlayerRank,
  handlePlayerScopeCounts,
};
