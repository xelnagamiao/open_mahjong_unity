const { statisticsSelection } = require('../services/statisticsCatalog');
const { INFO_FAN_DICT } = require('../constants/playerFanDicts');
const express = require('express');
const fs = require('node:fs/promises');
const path = require('node:path');
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
const { getPublicGameRecord } = require('../services/publicGameRecord');
const activityStore = require('../services/activityStore');
const classicRecordsStore = require('../services/classicRecordsStore');
const { getWeeklyScoreboard } = require('../services/weeklyScoreboard');

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
    const def = statisticsSelection(req.query.rule || 'guobiao');
    if (!def) return res.status(400).json({ success: false, message: '此规则没有基础统计' });
    const rule = def.key;
    const roomType = req.query.room_type || 'match';
    if (!['match', 'custom', 'all'].includes(roomType)) {
      return res.status(400).json({ success: false, message: '无效的场次类型' });
    }
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
      querySceneTotals({ asOfDate, rule, eventId: eventId || null, roomType }),
      querySceneTotalsFans({ asOfDate, rule, eventId: eventId || null, roomType }),
      querySceneDailyGames({ dateFrom, dateTo, asOfDate, rule, eventId: eventId || null, roomType }),
      listPlatformEvents(),
    ]);

    res.json({
      success: true,
      data: {
        totals,
        fans,
        daily,
        events,
        fan_dict: def.fan_stats ? INFO_FAN_DICT[def.sub_rule] || {} : null,
        supports_fan_stats: def.fan_stats,
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

/** 最近对局牌谱（可按匹配、自定义或比赛场筛选） */
router.get('/recent-records', async (req, res) => {
  try {
    const matchTier = typeof req.query.match_tier === 'string' ? req.query.match_tier : null;
    const eventId = typeof req.query.event_id === 'string' ? req.query.event_id.trim() : '';
    const roomType = req.query.room_type || 'match';
    if (!['match', 'custom', 'all'].includes(roomType)) {
      return res.status(400).json({ success: false, message: '无效的场次类型' });
    }
    const limit = parseInt(req.query.limit, 10) || 20;
    const offset = parseInt(req.query.offset, 10) || 0;
    const data = await queryRecentLadderRecords({
      rule: req.query.rule || "guobiao",
      matchTier,
      eventId: eventId || null,
      roomType,
      limit,
      offset,
    });
    res.json({ success: true, data });
  } catch (error) {
    console.error('platform recent-records error:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// Read previously completed analyses only. Viewing a replay never calls the provider.
router.get('/record/:gameId/riichi-review', async (req, res) => {
  try {
    const result = await getPublicGameRecord(req.params.gameId);
    if (result.status !== 200) return res.status(result.status).json({ success: false, message: result.message });
    const table = result.data.record.tables.main;
    const title = table?.payload?.game_title;
    const id = table?.table_record_id;
    let review = null;
    if (title?.sub_rule === 'riichi/standard' && /^[a-zA-Z0-9-]+$/.test(id ?? '') && id) {
      const dir = process.env.SATORI_REVIEW_CACHE_DIR ||
        path.resolve(__dirname, '../../../.om_workspace/satori-review/cache');
      try {
        const saved = JSON.parse(await fs.readFile(path.join(dir, id + '.json'), 'utf8'));
        if (saved.schema === 1 && saved.gameId === id && saved.table === 'main') review = saved;
      } catch (error) {
        if (error.code !== 'ENOENT') throw error;
      }
    }
    res.set('Cache-Control', 'no-store').json({ success: true, data: review });
  } catch (error) {
    console.error('riichi review cache error:', error.message);
    res.status(500).json({ success: false, message: '复盘缓存读取失败' });
  }
});

/** 公开牌谱读取，支持正式比赛编号与原桌分享编号。 */
router.get('/record/:gameId', async (req, res) => {
  try {
    const result = await getPublicGameRecord(String(req.params.gameId || ''));
    if (result.status !== 200) {
      return res.status(result.status).json({ success: false, message: result.message });
    }
    res.set('Cache-Control', 'no-store');
    return res.json({ success: true, data: result.data });
  } catch (error) {
    console.error('platform public record error:', error);
    return res.status(500).json({ success: false, message: '牌谱读取失败' });
  }
});

/** 数据站趣味数据：周榜缓存 + 经典牌谱。 */
router.get('/fun-stats', async (_req, res) => {
  try {
    const [week, classics] = await Promise.all([
      getWeeklyScoreboard(),
      classicRecordsStore.attachRecordMeta(classicRecordsStore.listPublic()),
    ]);
    res.set('Cache-Control', 'no-store');
    return res.json({
      success: true,
      data: {
        week: {
          metric: week.metric,
          date_from: week.date_from,
          date_to: week.date_to,
          generated_at: week.generated_at,
          note: week.note,
          missing_pt_records: week.missing_pt_records,
        },
        gainers: week.gainers,
        losers: week.losers,
        classics,
      },
    });
  } catch (error) {
    console.error('platform fun-stats error:', error);
    return res.status(500).json({ success: false, message: '趣味数据读取失败' });
  }
});

module.exports = router;
