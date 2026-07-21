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
} = require('../services/platformStats');

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

    const [totals, fans, daily] = await Promise.all([
      querySceneTotals({ asOfDate }),
      querySceneTotalsFans({ asOfDate }),
      querySceneDailyGames({ dateFrom, dateTo, asOfDate }),
    ]);

    res.json({
      success: true,
      data: {
        totals,
        fans,
        daily,
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
    const limit = parseInt(req.query.limit, 10) || 20;
    const offset = parseInt(req.query.offset, 10) || 0;
    const data = await queryRecentLadderRecords({
      matchTier,
      limit,
      offset,
    });
    res.json({ success: true, data });
  } catch (error) {
    console.error('platform recent-records error:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
