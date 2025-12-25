const express = require('express');
const router = express.Router();
const pool = require('../config/database');

// 番种英文到中文的翻译字典
const fanTranslationDict = {
  "dasixi": "大四喜",
  "dasanyuan": "大三元",
  "lvyise": "绿一色",
  "jiulianbaodeng": "九莲宝灯",
  "sigang": "四杠",
  "sangang": "三杠",
  "lianqidui": "连七对",
  "shisanyao": "十三幺",
  "qingyaojiu": "清幺九",
  "xiaosixi": "小四喜",
  "xiaosanyuan": "小三元",
  "ziyise": "字一色",
  "sianke": "四暗刻",
  "yiseshuanglonghui": "一色双龙会",
  "yisesitongshun": "一色四同顺",
  "yisesijiegao": "一色四节高",
  "yisesibugao": "一色四步高",
  "hunyaojiu": "混幺九",
  "qiduizi": "七对子",
  "qixingbukao": "七星不靠",
  "quanshuangke": "全双刻",
  "qingyise": "清一色",
  "yisesantongshun": "一色三同顺",
  "yisesanjiegao": "一色三节高",
  "quanda": "全大",
  "quanzhong": "全中",
  "quanxiao": "全小",
  "qinglong": "清龙",
  "sanseshuanglonghui": "三色双龙会",
  "yisesanbugao": "一色三步高",
  "quandaiwu": "全带五",
  "santongke": "三同刻",
  "sananke": "三暗刻",
  "quanbukao": "全不靠",
  "zuhelong": "组合龙",
  "dayuwu": "大于五",
  "xiaoyuwu": "小于五",
  "sanfengke": "三风刻",
  "hualong": "花龙",
  "tuibudao": "推不倒",
  "sansesantongshun": "三色三同顺",
  "sansesanjiegao": "三色三节高",
  "wufanhe": "无番和",
  "miaoshouhuichun": "妙手回春",
  "haidilaoyue": "海底捞月",
  "gangshangkaihua": "杠上开花",
  "qiangganghe": "抢杠和",
  "pengpenghe": "碰碰和",
  "hunyise": "混一色",
  "sansesanbugao": "三色三步高",
  "wumenqi": "五门齐",
  "quanqiuren": "全求人",
  "shuangangang": "双暗杠",
  "shuangjianke": "双箭刻",
  "quandaiyao": "全带幺",
  "buqiuren": "不求人",
  "shuangminggang": "双明杠",
  "hejuezhang": "和绝张",
  "jianke": "箭刻",
  "quanfengke": "圈风刻",
  "menfengke": "门风刻",
  "menqianqing": "门前清",
  "pinghe": "平和",
  "siguiyi": "四归一",
  "shuangtongke": "双同刻",
  "shuanganke": "双暗刻",
  "angang": "暗杠",
  "duanyao": "断幺",
  "yibangao": "一般高",
  "xixiangfeng": "喜相逢",
  "lianliu": "连六",
  "laoshaofu": "老少副",
  "yaojiuke": "幺九刻",
  "minggang": "明杠",
  "queyimen": "缺一门",
  "wuzi": "无字",
  "bianzhang": "边张",
  "qianzhang": "嵌张",
  "dandiaojiang": "单钓将",
  "zimo": "自摸",
  "huapai": "花牌",
  "mingangang": "明暗杠"
};

// 获取玩家信息（包含统计数据和用户设置）
router.get('/info/:userid', async (req, res) => {
  try {
    const userId = parseInt(req.params.userid);
    
    if (isNaN(userId)) {
      return res.status(400).json({
        success: false,
        message: '无效的用户ID'
      });
    }

    // 获取用户设置信息（包含 username）
    const userSettingsResult = await pool.query(`
      SELECT 
        us.user_id,
        us.title_id,
        us.profile_image_id,
        us.character_id,
        us.voice_id,
        u.username
      FROM user_settings us
      INNER JOIN users u ON us.user_id = u.user_id
      WHERE us.user_id = $1
    `, [userId]);

    if (userSettingsResult.rows.length === 0) {
      return res.status(404).json({
        success: false,
        message: '用户不存在'
      });
    }

    const userSettings = userSettingsResult.rows[0];

    // 获取 GB 统计数据
    const gbStatsResult = await pool.query(`
      SELECT * FROM gb_record_stats
      WHERE user_id = $1
      ORDER BY rule, mode
    `, [userId]);

    // 获取 JP 统计数据
    const jpStatsResult = await pool.query(`
      SELECT * FROM jp_record_stats
      WHERE user_id = $1
      ORDER BY rule, mode
    `, [userId]);

    // 处理 GB 统计数据
    const gbStats = gbStatsResult.rows.map(row => {
      const baseFields = {
        rule: row.rule,
        mode: row.mode,
        total_games: row.total_games,
        total_rounds: row.total_rounds,
        win_count: row.win_count,
        self_draw_count: row.self_draw_count,
        deal_in_count: row.deal_in_count,
        total_fan_score: row.total_fan_score,
        total_win_turn: row.total_win_turn,
        total_fangchong_score: row.total_fangchong_score,
        first_place_count: row.first_place_count,
        second_place_count: row.second_place_count,
        third_place_count: row.third_place_count,
        fourth_place_count: row.fourth_place_count
      };

      // 提取番种字段
      const excludedFields = new Set(['user_id', 'rule', 'mode', 'total_games', 'total_rounds', 
        'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
        'total_win_turn', 'total_fangchong_score', 'first_place_count',
        'second_place_count', 'third_place_count', 'fourth_place_count',
        'created_at', 'updated_at']);
      
      const fanStats = {};
      for (const [key, value] of Object.entries(row)) {
        if (!excludedFields.has(key) && value !== null && value !== 0) {
          fanStats[key] = value;
        }
      }

      return {
        ...baseFields,
        fan_stats: Object.keys(fanStats).length > 0 ? fanStats : null
      };
    });

    // 处理 JP 统计数据（类似处理）
    const jpStats = jpStatsResult.rows.map(row => {
      const baseFields = {
        rule: row.rule,
        mode: row.mode,
        total_games: row.total_games,
        total_rounds: row.total_rounds,
        win_count: row.win_count,
        self_draw_count: row.self_draw_count,
        deal_in_count: row.deal_in_count,
        total_fan_score: row.total_fan_score,
        total_win_turn: row.total_win_turn,
        total_fangchong_score: row.total_fangchong_score,
        first_place_count: row.first_place_count,
        second_place_count: row.second_place_count,
        third_place_count: row.third_place_count,
        fourth_place_count: row.fourth_place_count
      };

      const excludedFields = new Set(['user_id', 'rule', 'mode', 'total_games', 'total_rounds', 
        'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
        'total_win_turn', 'total_fangchong_score', 'first_place_count',
        'second_place_count', 'third_place_count', 'fourth_place_count',
        'created_at', 'updated_at']);
      
      const fanStats = {};
      for (const [key, value] of Object.entries(row)) {
        if (!excludedFields.has(key) && value !== null && value !== 0) {
          fanStats[key] = value;
        }
      }

      return {
        ...baseFields,
        fan_stats: Object.keys(fanStats).length > 0 ? fanStats : null
      };
    });

    res.json({
      success: true,
      message: '获取玩家信息成功',
      data: {
        user_id: userId,
        user_settings: {
          user_id: userSettings.user_id,
          username: userSettings.username,
          title_id: userSettings.title_id,
          profile_image_id: userSettings.profile_image_id,
          character_id: userSettings.character_id,
          voice_id: userSettings.voice_id
        },
        gb_stats: gbStats,
        jp_stats: jpStats
      }
    });

  } catch (error) {
    console.error('获取玩家信息错误:', error);
    res.status(500).json({
      success: false,
      message: '服务器内部错误'
    });
  }
});

// 获取玩家对局记录列表
router.get('/records/:userid', async (req, res) => {
  try {
    const userId = parseInt(req.params.userid);
    const limit = parseInt(req.query.limit) || 20;
    
    if (isNaN(userId)) {
      return res.status(400).json({
        success: false,
        message: '无效的用户ID'
      });
    }

    // 获取用户参与的最近N个游戏的 game_id（去重）
    const gameIdsResult = await pool.query(`
      SELECT DISTINCT game_id
      FROM game_player_records
      WHERE user_id = $1
      ORDER BY game_id DESC
      LIMIT $2
    `, [userId, limit]);

    if (gameIdsResult.rows.length === 0) {
      return res.json({
        success: true,
        data: []
      });
    }

    const gameIds = gameIdsResult.rows.map(row => row.game_id);

    // 获取每个游戏的完整信息
    const records = [];
    for (const gameId of gameIds) {
      // 获取游戏记录
      const gameRecordResult = await pool.query(`
        SELECT game_id, record, created_at
        FROM game_records
        WHERE game_id = $1
      `, [gameId]);

      if (gameRecordResult.rows.length === 0) continue;

      const gameRecord = gameRecordResult.rows[0];

      // 获取该游戏的4个玩家信息
      const playersResult = await pool.query(`
        SELECT 
          user_id,
          username,
          score,
          rank,
          rule,
          title_used,
          character_used,
          profile_used,
          voice_used
        FROM game_player_records
        WHERE game_id = $1
        ORDER BY rank
      `, [gameId]);

      const players = playersResult.rows.map(row => ({
        user_id: row.user_id,
        username: row.username,
        score: row.score,
        rank: row.rank,
        title_used: row.title_used,
        character_used: row.character_used,
        profile_used: row.profile_used,
        voice_used: row.voice_used
      }));

      // 从 record JSONB 中提取 rule
      let rule = 'GB';
      try {
        const recordData = typeof gameRecord.record === 'string' 
          ? JSON.parse(gameRecord.record) 
          : gameRecord.record;
        rule = recordData.rule || players[0]?.rule || 'GB';
      } catch (e) {
        rule = players[0]?.rule || 'GB';
      }

      records.push({
        game_id: gameRecord.game_id,
        rule: rule,
        record: gameRecord.record,
        created_at: gameRecord.created_at,
        players: players
      });
    }

    res.json({
      success: true,
      message: `获取到 ${records.length} 局游戏记录`,
      data: records
    });

  } catch (error) {
    console.error('获取对局记录错误:', error);
    res.status(500).json({
      success: false,
      message: '服务器内部错误'
    });
  }
});

module.exports = router;

