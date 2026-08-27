const express = require('express');
const router = express.Router();
const { guobiaoFanDict } = require('../constants/guobiaoFanDict');
const {
  getAsOfStatDate,
  querySceneTotals,
  querySceneTotalsFans,
  querySceneDailyGames,
  queryHomeHierarchyStats,
  queryRecentLadderRecords,
  listPlatformEvents,
} = require('../services/platformStats');
const { getPublicQueueStatus } = require('../services/matchQueueStatus');
const { getPublicGameRecord, getPublicUnityGameRecord } = require('../services/publicGameRecord');
const activityStore = require('../services/activityStore');

function defaultDateRange(asOfDate, days = 30) {
  const to = asOfDate ? new Date(`${asOfDate}T12:00:00`) : new Date();
  const from = new Date(to);
  from.setDate(from.getDate() - (days - 1));
  const fmt = (d) => {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  };
  return { date_from: fmt(from), date_to: fmt(to) };
}

/** 通知页活动列表：每次从目录现读，避免静态 index.json 被缓存成空列表。 */
router.get('/activities', (_req, res) => {
  try {
    res.set('Cache-Control', 'no-store');
    return res.json({ success: true, data: activityStore.getPublicIndex() });
  } catch (error) {
    console.error('platform activities error:', error);
    return res.status(500).json({ success: false, message: '活动列表读取失败' });
  }
});

/** 2D 大厅公开只读匹配人数；加入队列等操作仍必须登录游戏服。 */
router.get('/queue-status', async (_req, res) => {
  try {
    const data = await getPublicQueueStatus();
    res.set('Cache-Control', 'public, max-age=2, stale-while-revalidate=10');
    res.json({ success: true, data });
  } catch (error) {
    console.error('platform queue-status error:', error);
    res.status(502).json({ success: false, message: '暂时无法读取匹配人数' });
  }
});

router.get('/stats', async (req, res) => {
  try {
    const asOfDate = await getAsOfStatDate();
    const days = Math.min(365, Math.max(7, parseInt(req.query.days, 10) || 30));
    let dateFrom = typeof req.query.date_from === 'string' ? req.query.date_from.slice(0, 10) : null;
    let dateTo = typeof req.query.date_to === 'string' ? req.query.date_to.slice(0, 10) : null;
    if (!dateFrom || !dateTo) {
      const defaults = defaultDateRange(asOfDate, days);
      dateFrom = dateFrom || defaults.date_from;
      dateTo = dateTo || defaults.date_to;
    }
    if (asOfDate && dateTo > asOfDate) dateTo = asOfDate;

    const eventId = typeof req.query.event_id === 'string' ? req.query.event_id.trim() : '';
    const [totals, fans, daily, events] = await Promise.all([
      querySceneTotals({ asOfDate, eventId: eventId || null }),
      querySceneTotalsFans({ asOfDate, eventId: eventId || null }),
      querySceneDailyGames({ dateFrom, dateTo, asOfDate, eventId: eventId || null }),
      listPlatformEvents(),
    ]);

    res.json({
      success: true,
      data: {
        totals,
        fans,
        daily,
        events,
        fan_dict: guobiaoFanDict,
      },
      meta: {
        as_of_date: asOfDate,
        date_from: dateFrom,
        date_to: dateTo,
        note: '统计日按北京时间 04:00 切日；平台数据截止至最近已完成聚合的统计日',
      },
    });
  } catch (error) {
    console.error('platform stats error:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

/** 首页：规则 → 匹配/自定义 → 局制 → 等级场 */
router.get('/home-stats', async (req, res) => {
  try {
    const asOfDate = await getAsOfStatDate();
    const data = await queryHomeHierarchyStats();
    res.json({
      success: true,
      data,
      meta: {
        as_of_date: asOfDate,
        ...(data.meta || {}),
      },
    });
  } catch (error) {
    console.error('platform home-stats error:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

/** 最近天梯对局牌谱（可按场次筛选） */
router.get('/recent-records', async (req, res) => {
  try {
    const matchTier = typeof req.query.match_tier === 'string' ? req.query.match_tier : null;
    const eventId = typeof req.query.event_id === 'string' ? req.query.event_id.trim() : '';
    const limit = parseInt(req.query.limit, 10) || 20;
    const offset = parseInt(req.query.offset, 10) || 0;
    const data = await queryRecentLadderRecords({
      matchTier,
      eventId: eventId || null,
      limit,
      offset,
    });
    res.json({ success: true, data });
  } catch (error) {
    console.error('platform recent-records error:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

/** 可分享的国标 2D 牌谱；只公开牌桌回放所需的对局和玩家公开资料。 */
router.get('/record/:gameId', async (req, res) => {
  try {
    const result = await getPublicGameRecord(String(req.params.gameId || ''));
    if (result.status !== 200) {
      return res.status(result.status).json({ success: false, message: result.message });
    }
    res.set('Cache-Control', 'public, max-age=300, stale-while-revalidate=3600');
    return res.json({ success: true, data: result.data });
  } catch (error) {
    console.error('platform public record error:', error);
    return res.status(500).json({ success: false, message: '牌谱读取失败' });
  }
});

/** Public read-only record payload for the Unity 3D replay viewer (all supported rules). */
router.get('/unity-record/:gameId', async (req, res) => {
  try {
    const result = await getPublicUnityGameRecord(String(req.params.gameId || ''));
    if (result.status !== 200) {
      return res.status(result.status).json({ success: false, message: result.message });
    }
    res.set('Cache-Control', 'public, max-age=300, stale-while-revalidate=3600');
    return res.json({ success: true, data: result.data });
  } catch (error) {
    console.error('platform public Unity record error:', error);
    return res.status(500).json({ success: false, message: '牌谱读取失败' });
  }
});

module.exports = router;
