const express = require('express');
const router = express.Router();
const pool = require('../config/database');

// 国标麻将番种英文->中文翻译字典
const guobiaoFanDict = {
  dasixi: '大四喜', dasanyuan: '大三元', lvyise: '绿一色', jiulianbaodeng: '九莲宝灯',
  sigang: '四杠', sangang: '三杠', lianqidui: '连七对', shisanyao: '十三幺',
  qingyaojiu: '清幺九', xiaosixi: '小四喜', xiaosanyuan: '小三元', ziyise: '字一色',
  sianke: '四暗刻', yiseshuanglonghui: '一色双龙会', yisesitongshun: '一色四同顺',
  yisesijiegao: '一色四节高', yisesibugao: '一色四步高', hunyaojiu: '混幺九',
  qiduizi: '七对', qixingbukao: '七星不靠', quanshuangke: '全双刻', qingyise: '清一色',
  yisesantongshun: '一色三同顺', yisesanjiegao: '一色三节高', quanda: '全大',
  quanzhong: '全中', quanxiao: '全小', qinglong: '清龙', sanseshuanglonghui: '三色双龙会',
  yisesanbugao: '一色三步高', quandaiwu: '全带五', santongke: '三同刻', sananke: '三暗刻',
  quanbukao: '全不靠', zuhelong: '组合龙', dayuwu: '大于五', xiaoyuwu: '小于五',
  sanfengke: '三风刻', hualong: '花龙', tuibudao: '推不倒', sansesantongshun: '三色三同顺',
  sansesanjiegao: '三色三节高', wufanhe: '无番和', miaoshouhuichun: '妙手回春',
  haidilaoyue: '海底捞月', gangshangkaihua: '杠上开花', qiangganghe: '抢杠和',
  pengpenghe: '碰碰和', hunyise: '混一色', sansesanbugao: '三色三步高', wumenqi: '五门齐',
  quanqiuren: '全求人', shuangangang: '双暗杠', shuangjianke: '双箭刻', quandaiyao: '全带幺',
  buqiuren: '不求人', shuangminggang: '双明杠', hejuezhang: '和绝张', jianke: '箭刻',
  quanfengke: '圈风刻', menfengke: '门风刻', menqianqing: '门前清', pinghe: '平和',
  siguiyi: '四归一', shuangtongke: '双同刻', shuanganke: '双暗刻', angang: '暗杠',
  duanyao: '断幺', yibangao: '一般高', xixiangfeng: '喜相逢', lianliu: '连六',
  laoshaofu: '老少副', yaojiuke: '幺九刻', minggang: '明杠', queyimen: '缺一门',
  wuzi: '无字', bianzhang: '边张', qianzhang: '嵌张', dandiaojiang: '单钓将',
  zimo: '自摸', huapai: '花牌', mingangang: '明暗杠'
};

// 青雀麻将番种英文->中文翻译字典
const qingqueFanDict = {
  hepai: '和牌', tianhe: '天和', dihe: '地和', lingshangkaihua: '岭上开花',
  haidilaoyue: '海底捞月', hedilaoyue: '河底捞鱼', qianggang: '抢杠', qidui: '七对',
  menqianqing: '门前清', siangang: '四暗杠', sanangang: '三暗杠', shuangangang: '双暗杠',
  angang: '暗杠', sigang: '四杠', sangang: '三杠', shuanggang: '双杠',
  sianke: '四暗刻', sananke: '三暗刻', duiduihe: '对对和', shiergui: '十二鬼',
  bagui: '八鬼', sandiedui: '三叠对', erdiedui: '二叠对', diedui: '叠对',
  ziyise: '字一色', dasixi: '大四喜', xiaosixi: '小四喜', sixidui: '四喜对',
  fengpaisanke: '风牌三刻', fengpaiqidui: '风牌七对', fengpailiudui: '风牌六对',
  fengpaiwudui: '风牌五对', fengpaisidui: '风牌四对', dasanyuan: '大三元',
  xiaosanyuan: '小三元', sanyuanliudui: '三元六对', sanyuandui: '三元对',
  fanpaisike: '番牌四刻', fanpaisanke: '番牌三刻', fanpaierke: '番牌二刻',
  fanpaike: '番牌刻', fanpaiqidui: '番牌七对', fanpailiudui: '番牌六对',
  fanpaiwudui: '番牌五对', fanpaisifu: '番牌四副', fanpaisanfu: '番牌三副',
  fanpaierfu: '番牌二副', fanpai: '番牌', qingyaojiu: '清幺九', hunyaojiu: '混幺九',
  qingdaiyao: '清带幺', hundaiyao: '混带幺', jiulianbaodeng: '九莲宝灯',
  qingyise: '清一色', hunyise: '混一色', wumenqi: '五门齐', hunyishu: '混一数',
  ershu: '二数', erju: '二局', sanju: '三局', siju: '四局', lianshu: '连数',
  jianshu: '间数', jingshu: '镜数', yingshu: '映数', mantingfang: '满厅芳',
  sitongshun: '四同顺', santongshun: '三同顺', erbangao: '二般高', yibangao: '一般高',
  silianke: '四连刻', sanlianke: '三连刻', sibugao: '四步高', sanbugao: '三步高',
  silianhuan: '四连环', sanlianhuan: '三连环', yiqiguantong: '一气贯通',
  qiliandui: '七连对', liuliandui: '六连对', wuliandui: '五连对', siliandui: '四连对',
  sansetongke: '三色同刻', sansetongshun: '三色同顺', sanseedui: '三色二对',
  sansetongdui: '三色同对', sanselianke: '三色连刻', sanseguantong: '三色贯通',
  jingtong: '镜同', jingtongsandui: '镜同三对', jingtongerdui: '镜同二对',
  shuanglonghui: '双龙会'
};

// 古典麻将番种英文->中文翻译字典
const classicalFanDict = {
  zimo: '自摸', hunyise: '混一色', xiaosanyuan: '小三元', qingyise: '清一色',
  ziyise: '字一色', luanfengheming: '乱风和鸣', lingshangkaihua: '岭上开花',
  haidilaoyue: '海底捞月', jinjidoushi: '金鸡独立', dasanyuan: '大三元',
  dasixi: '大四喜', xiaosixi: '小四喜', tianhe: '天和', dihe: '地和',
  jiulianbaodeng: '九莲宝灯', guoshiwushuang: '国士无双'
};

const riichiFanDict = {
  riichi: '立直', menzen_tsumo: '门前清自摸和', pinfu: '平和', tanyao: '断幺九',
  iipeikou: '一杯口', yakuhai_haku: '役牌·白', yakuhai_hatsu: '役牌·发', yakuhai_chun: '役牌·中',
  jikaze_ton: '自风·东', jikaze_nan: '自风·南', jikaze_sha: '自风·西', jikaze_pe: '自风·北',
  bakaze_ton: '场风·东', bakaze_nan: '场风·南', bakaze_sha: '场风·西', bakaze_pe: '场风·北',
  rinshan: '岭上开花', chankan: '枪杠', haitei: '海底捞月', houtei: '河底捞鱼',
  ippatsu: '一发', dora: '宝牌', akadora: '赤宝牌', uradora: '里宝牌',
  daburi_riichi: '双立直', sanshoku_doukou: '三色同刻', san_kantsu: '三杠子', toitoi: '对对和',
  sanankou: '三暗刻', shousangen: '小三元', honroutou: '混老头', chiitoitsu: '七对子',
  chanta: '混全带幺九', ittsu: '一气通贯', sanshoku_doujun: '三色同顺',
  ittsu_menzen: '一气通贯（门清）', ittsu_shitachi: '一气通贯（食下）',
  sanshoku_doujun_menzen: '三色同顺（门清）', sanshoku_doujun_shitachi: '三色同顺（食下）',
  chanta_menzen: '混全带幺九（门清）', chanta_shitachi: '混全带幺九（食下）',
  junchan_menzen: '纯全带幺九（门清）', junchan_shitachi: '纯全带幺九（食下）',
  honitsu_menzen: '混一色（门清）', honitsu_shitachi: '混一色（食下）',
  chinitsu_menzen: '清一色（门清）', chinitsu_shitachi: '清一色（食下）',
  ryanpeikou: '二杯口', junchan: '纯全带幺九', honitsu: '混一色', chinitsu: '清一色',
  tenhou: '天和', chiihou: '地和', daisangen: '大三元', suuankou: '四暗刻',
  tsuuiisou: '字一色', ryuuiisou: '绿一色', chinroutou: '清老头', kokushi: '国士无双',
  shousuushii: '小四喜', suukantsu: '四杠子', chuuren: '九莲宝灯',
  suuankou_tanki: '四暗刻单骑', kokushi_juusan: '国士无双十三面',
  chuuren_junsei: '纯正九莲宝灯', daisuushii: '大四喜',
  open_riichi: '开立直', double_open_riichi: '双倍开立直', renhou: '人和',
  nagashi_mangan: '流局满贯', daichisei: '大七星', daisharin: '大车轮',
  paarenchan: '八连庄', sashikomi: '包牌'
};

const ruleConfig = {
  guobiao: {
    historyTable: 'guobiao_history_stats',
    fanTable: 'guobiao_fan_stats',
    fanDict: guobiaoFanDict
  },
  riichi: {
    historyTable: 'riichi_history_stats',
    fanTable: 'riichi_fan_stats',
    fanDict: riichiFanDict
  },
  qingque: {
    historyTable: 'qingque_history_stats',
    fanTable: 'qingque_fan_stats',
    fanDict: qingqueFanDict
  },
  classical: {
    historyTable: 'classical_history_stats',
    fanTable: 'classical_fan_stats',
    fanDict: classicalFanDict
  }
};

const HISTORY_FIELDS = new Set([
  'user_id', 'rule', 'mode', 'total_games', 'total_rounds', 'win_count',
  'self_draw_count', 'deal_in_count', 'total_fan_score', 'total_win_turn',
  'total_fangchong_score', 'first_place_count', 'second_place_count',
  'third_place_count', 'fourth_place_count', 'fulu_round_count',
  'created_at', 'updated_at'
]);

// 提取番种字段：将 fan_stats 表行（可能为 null）转为 { 番key: 数量 } 结构，仅保留非零项
function extractFanStats(fanRow) {
  if (!fanRow) {
    return null;
  }
  const fanStats = {};
  for (const [key, value] of Object.entries(fanRow)) {
    if (HISTORY_FIELDS.has(key)) {
      continue;
    }
    if (value !== null && value !== 0) {
      fanStats[key] = value;
    }
  }
  return Object.keys(fanStats).length > 0 ? fanStats : null;
}

// 查询单一规则下的所有 (rule, mode) 统计，并把对应番种表 join 进来
async function queryRuleStats(userId, rule) {
  const cfg = ruleConfig[rule];
  if (!cfg) {
    return [];
  }

  const historyResult = await pool.query(
    `SELECT * FROM ${cfg.historyTable} WHERE user_id = $1 ORDER BY rule, mode`,
    [userId]
  );

  let fanRows = [];
  if (cfg.fanTable) {
    const fanResult = await pool.query(
      `SELECT * FROM ${cfg.fanTable} WHERE user_id = $1`,
      [userId]
    );
    fanRows = fanResult.rows;
  }

  const fanByKey = new Map();
  for (const row of fanRows) {
    fanByKey.set(`${row.rule}|${row.mode}`, row);
  }

  return historyResult.rows.map(row => ({
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
    fourth_place_count: row.fourth_place_count,
    fulu_round_count: row.fulu_round_count,
    fan_stats: extractFanStats(fanByKey.get(`${row.rule}|${row.mode}`))
  }));
}

router.get('/info/:userid', async (req, res) => {
  try {
    const userId = parseInt(req.params.userid);

    if (isNaN(userId)) {
      return res.status(400).json({
        success: false,
        message: '无效的用户ID'
      });
    }

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

    const [guobiaoStats, riichiStats, qingqueStats, classicalStats] = await Promise.all([
      queryRuleStats(userId, 'guobiao'),
      queryRuleStats(userId, 'riichi'),
      queryRuleStats(userId, 'qingque'),
      queryRuleStats(userId, 'classical')
    ]);

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
        // 各规则统计数据；客户端按 tab 切换显示
        guobiao_stats: guobiaoStats,
        riichi_stats: riichiStats,
        qingque_stats: qingqueStats,
        classical_stats: classicalStats,
        // 番种字典随数据一起下发，客户端无需写死
        fan_dict: {
          guobiao: guobiaoFanDict,
          qingque: qingqueFanDict,
          classical: classicalFanDict
        }
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

    // 一次性拉取所有游戏的 record 与玩家记录，避免循环 N+1 查询
    const recordsResult = await pool.query(`
      SELECT game_id, record, created_at
      FROM game_records
      WHERE game_id = ANY($1::varchar[])
    `, [gameIds]);

    const playersResult = await pool.query(`
      SELECT 
        game_id, user_id, username, score, rank, rule, sub_rule, match_type,
        title_used, character_used, profile_used, voice_used
      FROM game_player_records
      WHERE game_id = ANY($1::varchar[])
      ORDER BY game_id DESC, rank
    `, [gameIds]);

    const playersByGame = new Map();
    for (const row of playersResult.rows) {
      if (!playersByGame.has(row.game_id)) {
        playersByGame.set(row.game_id, []);
      }
      playersByGame.get(row.game_id).push({
        user_id: row.user_id,
        username: row.username,
        score: row.score,
        rank: row.rank,
        title_used: row.title_used,
        character_used: row.character_used,
        profile_used: row.profile_used,
        voice_used: row.voice_used
      });
    }

    const recordsByGame = new Map();
    for (const row of recordsResult.rows) {
      recordsByGame.set(row.game_id, row);
    }

    const records = [];
    for (const gameId of gameIds) {
      const gameRecord = recordsByGame.get(gameId);
      if (!gameRecord) {
        continue;
      }
      const players = playersByGame.get(gameId) || [];

      // 从 record 或玩家行中提取规则信息
      let rule = '';
      let subRule = null;
      let matchType = null;
      try {
        const recordData = typeof gameRecord.record === 'string'
          ? JSON.parse(gameRecord.record)
          : gameRecord.record;
        rule = recordData?.rule || '';
        subRule = recordData?.sub_rule || null;
      } catch (_) {
        rule = '';
      }
      const sampleRow = playersResult.rows.find(r => r.game_id === gameId);
      if (sampleRow) {
        if (!rule) {
          rule = sampleRow.rule || '';
        }
        if (!subRule) {
          subRule = sampleRow.sub_rule || null;
        }
        matchType = sampleRow.match_type || null;
      }

      records.push({
        game_id: gameRecord.game_id,
        rule: rule,
        sub_rule: subRule,
        match_type: matchType,
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
