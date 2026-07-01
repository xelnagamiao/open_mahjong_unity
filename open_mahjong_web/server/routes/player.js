const express = require('express');
const router = express.Router();
const pool = require('../config/database');
const { createWindowLimiter } = require('../middleware/rateLimit');

// 下载专用限流：每 IP 每日最多 10 次（单局 + 批量合并计数），防止无限拉取牌谱
const downloadLimiter = createWindowLimiter({
  windowMs: 86_400_000,
  max: 10,
  keyFn: (req) => `${req.ip || 'unknown'}:download`,
});

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

  // 番种统计按用户合计展示（不按局制分开）：合并该用户所有 mode 的番种行，
  // 挂到首条 history 行上，避免 mode 分开(4/4 vs 4/4_rank)后纯排位用户取不到番种。
  let fanTotal = null;
  if (fanRows.length) {
    const merged = {};
    for (const row of fanRows) {
      const extracted = extractFanStats(row);
      if (!extracted) continue;
      for (const [k, v] of Object.entries(extracted)) {
        merged[k] = (merged[k] || 0) + v;
      }
    }
    if (Object.keys(merged).length > 0) fanTotal = merged;
  }

  return historyResult.rows.map((row, idx) => ({
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
    cuohe_count: row.cuohe_count,
    total_round_score: row.total_round_score,
    fan_stats: idx === 0 ? fanTotal : null
  }));
}

// 解析 user_id：纯数字按 ID，否则按 username 查询。未命中返回 null。
async function resolveUserId(key) {
  const raw = String(key == null ? '' : key).trim();
  if (!raw) return null;
  if (/^\d+$/.test(raw)) {
    const id = parseInt(raw, 10);
    return isNaN(id) ? null : id;
  }
  const r = await pool.query('SELECT user_id FROM users WHERE username = $1 LIMIT 1', [raw]);
  if (r.rows.length === 0) return null;
  return parseInt(r.rows[0].user_id, 10);
}

router.get('/info/:key', async (req, res) => {
  try {
    const userId = await resolveUserId(req.params.key);

    if (userId == null) {
      return res.status(404).json({
        success: false,
        message: '用户不存在'
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

// 列表分页硬上限：防止一次拉取过多数据
const LIST_PAGE_MAX = 50;
// 批量下载硬上限：单次 ZIP 最多打包的牌谱数
const DOWNLOAD_MAX_GAMES = 50;
// 每日分析：每 IP 每日 1 次批量拉取牌谱 JSON（≤500 局），用于客户端本地分析
// 按「自然日 4 点」对齐：4 点后 key 翻页，与每日聚合刷新一致
const ANALYZE_MAX_GAMES = 500;
function _analyzeBucketKey(req) {
  const now = new Date();
  // 4 点前归到前一天的分析周期
  const d = new Date(now);
  if (d.getHours() < 4) d.setDate(d.getDate() - 1);
  const ymd = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  return `${req.ip || 'unknown'}:analyze:${ymd}`;
}
const analyzeLimiter = createWindowLimiter({
  windowMs: 86_400_000,
  max: 1,
  keyFn: _analyzeBucketKey,
});

// 将 record.game_title 中的元信息与玩家行字段合并，返回对外展示用的元数据
function buildRecordMeta(gameRecord, playersRows) {
  let rule = '';
  let subRule = null;
  let matchType = null;
  let roomType = null;
  try {
    const recordData = typeof gameRecord.record === 'string'
      ? JSON.parse(gameRecord.record)
      : gameRecord.record;
    const title = recordData?.game_title || {};
    rule = title.rule || recordData?.rule || '';
    subRule = title.sub_rule || null;
    matchType = title.match_type || null;
    roomType = title.room_type || null;
  } catch (_) {
    // record 解析失败时回退到玩家行字段
  }
  const sampleRow = playersRows.find(r => r.game_id === gameRecord.game_id);
  if (sampleRow) {
    if (!rule) rule = sampleRow.rule || '';
    if (!subRule) subRule = sampleRow.sub_rule || null;
    if (!matchType) matchType = sampleRow.match_type || null;
    if (!roomType) roomType = sampleRow.room_type || null;
  }
  return { rule, sub_rule: subRule, match_type: matchType, room_type: roomType };
}

// 局制 game_type → match_type 取值集合
const GAME_TYPE_MATCH_TYPES = {
  dongfeng: ['1/4', '1/4_rank'],
  banzhuang: ['2/4', '2/4_rank'],
  xifeng: ['3/4'],
  quanzhuang: ['4/4', '4/4_rank'],
};

// 场次等级 tier → 条件片段；向 params 追加占位值并返回条件字符串数组
function tierToConditions(tier, params) {
  switch (tier) {
    case 'rank':
      params.push('match');
      return [`gpr.room_type = $${params.length}`];
    case 'custom':
      params.push('custom');
      return [`gpr.room_type = $${params.length}`];
    case 'events':
      params.push('events');
      return [`gpr.room_type = $${params.length}`];
    case 'beginner':
    case 'intermediate':
    case 'advanced':
    case 'mcrpl': {
      params.push('match');
      const c1 = `gpr.room_type = $${params.length}`;
      params.push(tier);
      const c2 = `gpr.match_tier = $${params.length}`;
      return [c1, c2];
    }
    default:
      return null;
  }
}

// 按玩家构建对局筛选 WHERE 片段与参数（用于列表与下载复用）
function buildRecordFilters(userId, query, params) {
  const conditions = ['gpr.user_id = $1'];
  params.push(userId);

  // 场次等级 tier 优先于裸 room_type/match_tier
  if (query.tier) {
    const tierConds = tierToConditions(query.tier, params);
    if (tierConds) conditions.push(...tierConds);
  } else if (query.room_type) {
    conditions.push(`gpr.room_type = $${params.push(query.room_type)}`);
  } else if (query.match_tier) {
    conditions.push(`gpr.match_tier = $${params.push(query.match_tier)}`);
  }

  if (query.rule) {
    conditions.push(`gpr.rule = $${params.push(query.rule)}`);
  }
  if (query.sub_rule) {
    conditions.push(`gpr.sub_rule = $${params.push(query.sub_rule)}`);
  }
  if (query.game_type) {
    const mts = GAME_TYPE_MATCH_TYPES[query.game_type];
    if (mts && mts.length) {
      conditions.push(`gpr.match_type = ANY($${params.push(mts)}::varchar[])`);
    }
  }
  if (query.date_from) {
    conditions.push(`gr.created_at >= $${params.push(query.date_from)}`);
  }
  if (query.date_to) {
    conditions.push(`gr.created_at < $${params.push(query.date_to)}`);
  }
  return conditions;
}

router.get('/records/:key', async (req, res) => {
  try {
    const userId = await resolveUserId(req.params.key);
    if (userId == null) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const offset = Math.max(0, parseInt(req.query.offset) || 0);
    const limit = Math.min(LIST_PAGE_MAX, Math.max(1, parseInt(req.query.limit) || 20));
    const query = {
      rule: req.query.rule || null,
      sub_rule: req.query.sub_rule || null,
      room_type: req.query.room_type || null,
      match_tier: req.query.match_tier || null,
      tier: req.query.tier || null,
      game_type: req.query.game_type || null,
      date_from: req.query.date_from || null,
      date_to: req.query.date_to || null,
    };

    // 1) 取分页后的 game_id 列表（DISTINCT 去重，按时间倒序），并用 COUNT(*) OVER() 取总数
    const pageParams = [];
    const conditions = buildRecordFilters(userId, query, pageParams);
    const pageSql = `
      SELECT game_id, created_at, COUNT(*) OVER() AS total
      FROM (
        SELECT DISTINCT gpr.game_id, gr.created_at
        FROM game_player_records gpr
        JOIN game_records gr ON gr.game_id = gpr.game_id
        WHERE ${conditions.join(' AND ')}
      ) sub
      ORDER BY created_at DESC
      LIMIT $${pageParams.length + 1} OFFSET $${pageParams.length + 2}
    `;
    pageParams.push(limit, offset);
    const pageResult = await pool.query(pageSql, pageParams);

    const total = pageResult.rows.length > 0 ? parseInt(pageResult.rows[0].total) : 0;
    const gameIds = pageResult.rows.map(r => r.game_id);
    const createdAtByGame = new Map(pageResult.rows.map(r => [r.game_id, r.created_at]));

    // 2) 该用户历史中可用筛选维度（room_type/match_tier/event_id/rule），供前端场次下拉
    const filterParams = [userId];
    const filterSql = `
      SELECT DISTINCT room_type, match_tier, event_id, rule
      FROM game_player_records
      WHERE user_id = $1
      ORDER BY room_type, match_tier, event_id, rule
    `;
    const filterResult = await pool.query(filterSql, filterParams);
    const filters = filterResult.rows.map(r => ({
      room_type: r.room_type,
      match_tier: r.match_tier,
      event_id: r.event_id,
      rule: r.rule,
    }));

    if (gameIds.length === 0) {
      return res.json({ success: true, data: { total, items: [], filters } });
    }

    // 3) 取本页对局的 record 元数据与玩家行（不含完整 record，降低负载）
    const recordsResult = await pool.query(
      `SELECT game_id, record FROM game_records WHERE game_id = ANY($1::varchar[])`,
      [gameIds]
    );
    const playersResult = await pool.query(
      `SELECT game_id, user_id, username, score, rank, rule, sub_rule, match_type, room_type,
              match_tier, event_id,
              title_used, character_used, profile_used, voice_used
       FROM game_player_records
       WHERE game_id = ANY($1::varchar[])
       ORDER BY game_id, rank`,
      [gameIds]
    );

    const playersByGame = new Map();
    for (const row of playersResult.rows) {
      if (!playersByGame.has(row.game_id)) playersByGame.set(row.game_id, []);
      playersByGame.get(row.game_id).push({
        user_id: row.user_id,
        username: row.username,
        score: row.score,
        rank: row.rank,
        title_used: row.title_used,
        character_used: row.character_used,
        profile_used: row.profile_used,
        voice_used: row.voice_used,
      });
    }
    const recordsByGame = new Map(recordsResult.rows.map(r => [r.game_id, r]));

    const items = [];
    for (const gameId of gameIds) {
      const gameRecord = recordsByGame.get(gameId);
      if (!gameRecord) continue;
      const playersRows = playersResult.rows.filter(r => r.game_id === gameId);
      const meta = buildRecordMeta(gameRecord, playersRows);
      items.push({
        game_id: gameId,
        created_at: createdAtByGame.get(gameId),
        rule: meta.rule,
        sub_rule: meta.sub_rule,
        match_type: meta.match_type,
        room_type: meta.room_type,
        match_tier: playersRows[0]?.match_tier || null,
        event_id: playersRows[0]?.event_id || null,
        players: playersByGame.get(gameId) || [],
      });
    }

    res.json({ success: true, data: { total, items, filters } });
  } catch (error) {
    console.error('获取对局记录错误:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// 单局牌谱下载：直接返回原始 record JSON
router.get('/record/:gameId', downloadLimiter, async (req, res) => {
  try {
    const gameId = String(req.params.gameId || '').trim();
    if (!gameId) {
      return res.status(400).json({ success: false, message: '无效的 game_id' });
    }
    const result = await pool.query(
      `SELECT record FROM game_records WHERE game_id = $1`,
      [gameId]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ success: false, message: '牌谱不存在' });
    }
    const raw = result.rows[0].record;
    const body = typeof raw === 'string' ? raw : JSON.stringify(raw);
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    res.setHeader('Content-Disposition', `attachment; filename="${gameId}.json"`);
    res.send(body);
  } catch (error) {
    console.error('单局牌谱下载错误:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// 批量牌谱下载：按筛选/指定 game_ids 流式打包 ZIP，每局一个原始 record JSON
router.post('/records/download', downloadLimiter, async (req, res) => {
  try {
    const userId = parseInt(req.body?.user_id);
    if (isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户ID' });
    }
    const query = {
      rule: req.body.rule || null,
      sub_rule: req.body.sub_rule || null,
      room_type: req.body.room_type || null,
      match_tier: req.body.match_tier || null,
      tier: req.body.tier || null,
      game_type: req.body.game_type || null,
      date_from: req.body.date_from || null,
      date_to: req.body.date_to || null,
    };
    const requestedIds = Array.isArray(req.body.game_ids)
      ? req.body.game_ids.map(String).filter(Boolean)
      : [];

    let gameIds = [];
    if (requestedIds.length > 0) {
      // 指定 ID 模式：仅保留属于该用户的，防越权
      const idResult = await pool.query(
        `SELECT DISTINCT game_id FROM game_player_records
         WHERE user_id = $1 AND game_id = ANY($2::varchar[])`,
        [userId, requestedIds]
      );
      gameIds = idResult.rows.map(r => r.game_id);
      if (gameIds.length > DOWNLOAD_MAX_GAMES) {
        return res.status(400).json({
          success: false,
          message: `单次最多下载 ${DOWNLOAD_MAX_GAMES} 局，当前选中 ${gameIds.length} 局，请减少选择`,
        });
      }
    } else {
      // 筛选模式：取全部命中 game_id（仅 ID，开销低），超出上限提示缩小范围
      const params = [];
      const conditions = buildRecordFilters(userId, query, params);
      const sql = `
        SELECT game_id FROM (
          SELECT DISTINCT gpr.game_id, gr.created_at
          FROM game_player_records gpr
          JOIN game_records gr ON gr.game_id = gpr.game_id
          WHERE ${conditions.join(' AND ')}
        ) sub
        ORDER BY created_at DESC
      `;
      const idResult = await pool.query(sql, params);
      gameIds = idResult.rows.map(r => r.game_id);
      if (gameIds.length > DOWNLOAD_MAX_GAMES) {
        return res.status(400).json({
          success: false,
          message: `单次最多下载 ${DOWNLOAD_MAX_GAMES} 局，当前筛选命中 ${gameIds.length} 局，请缩小时间范围或场次`,
        });
      }
    }

    if (gameIds.length === 0) {
      return res.status(404).json({ success: false, message: '没有匹配的牌谱' });
    }

    const recordsResult = await pool.query(
      `SELECT game_id, record FROM game_records WHERE game_id = ANY($1::varchar[])`,
      [gameIds]
    );
    const byGame = new Map(recordsResult.rows.map(r => [r.game_id, r.record]));

    const archiver = require('archiver');
    res.setHeader('Content-Type', 'application/zip');
    res.setHeader('Content-Disposition', `attachment; filename="player_${userId}_records.zip"`);
    const archive = archiver('zip', { zlib: { level: 5 } });
    archive.on('error', (err) => {
      console.error('zip 打包错误:', err);
      if (!res.headersSent) {
        res.status(500).json({ success: false, message: '打包失败' });
      } else {
        res.end();
      }
    });
    archive.pipe(res);
    for (const gameId of gameIds) {
      const raw = byGame.get(gameId);
      if (raw === undefined || raw === null) continue;
      const body = typeof raw === 'string' ? raw : JSON.stringify(raw);
      archive.append(body, { name: `${gameId}.json` });
    }
    await archive.finalize();
  } catch (error) {
    console.error('批量牌谱下载错误:', error);
    if (!res.headersSent) {
      res.status(500).json({ success: false, message: '服务器内部错误' });
    }
  }
});

// ===== 公开排行榜（前 N 名，快捷查询用） =====
const { RANK_NAME_TO_INDEX, LEADERBOARD_MIN_USER_ID } = require('../utils/rankNames');

router.get('/leaderboard', async (req, res) => {
  try {
    const limit = Math.min(20, Math.max(1, parseInt(req.query.limit, 10) || 10));
    const result = await pool.query(
      `SELECT r.user_id, r.guobiao_rank, r.guobiao_score, u.username
       FROM rank_data r
       JOIN users u ON r.user_id = u.user_id
       WHERE r.user_id > $1 AND r.guobiao_rank != '10级'`,
      [LEADERBOARD_MIN_USER_ID]
    );
    const entries = result.rows.map((row) => ({
      user_id: row.user_id,
      username: row.username || '',
      guobiao_rank: row.guobiao_rank,
      guobiao_score: parseFloat(row.guobiao_score),
      _idx: RANK_NAME_TO_INDEX[row.guobiao_rank] ?? 0,
    }));
    entries.sort((a, b) => b._idx - a._idx || b.guobiao_score - a.guobiao_score || a.user_id - b.user_id);
    const sliced = entries.slice(0, limit).map((e, i) => ({
      rank_position: i + 1,
      user_id: e.user_id,
      username: e.username,
      guobiao_rank: e.guobiao_rank,
    }));
    res.json({ success: true, data: sliced });
  } catch (err) {
    console.error('public leaderboard:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// ===== 热点查询：记录手动输入 + 一周内 Top N =====
async function ensureSearchLogTable() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS player_search_log (
      id BIGSERIAL PRIMARY KEY,
      key TEXT NOT NULL,
      user_id BIGINT,
      username TEXT,
      ip TEXT,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    )
  `);
}

router.post('/search-log', async (req, res) => {
  try {
    const key = String(req.body?.key ?? '').trim();
    if (!key) return res.status(400).json({ success: false, message: '缺少查询关键词' });
    await ensureSearchLogTable();
    const userId = req.body?.user_id ? parseInt(req.body.user_id, 10) : null;
    const username = req.body?.username ? String(req.body.username).slice(0, 64) : null;
    await pool.query(
      `INSERT INTO player_search_log (key, user_id, username, ip) VALUES ($1, $2, $3, $4)`,
      [key.slice(0, 64), Number.isFinite(userId) ? userId : null, username, req.ip || null]
    );
    res.json({ success: true });
  } catch (err) {
    console.error('search-log:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/hot', async (req, res) => {
  try {
    const limit = Math.min(20, Math.max(1, parseInt(req.query.limit, 10) || 10));
    await ensureSearchLogTable();
    // 一周内按 key 计数 Top N，附带最近一次命中的 user_id/username
    const result = await pool.query(
      `SELECT key, COUNT(*) AS cnt,
              (array_agg(user_id ORDER BY created_at DESC))[1] AS user_id,
              (array_agg(username ORDER BY created_at DESC))[1] AS username
       FROM player_search_log
       WHERE created_at >= NOW() - INTERVAL '7 days'
       GROUP BY key
       ORDER BY cnt DESC, key
       LIMIT $1`,
      [limit]
    );
    // 缺 username 时从 users 表补全（历史记录可能只存了 uid）
    const needLookup = [];
    const items = result.rows.map((r) => {
      const userId = r.user_id ? parseInt(r.user_id, 10) : null;
      let username = r.username || null;
      if (!username) {
        const lookupId = userId || (/^\d+$/.test(String(r.key)) ? parseInt(r.key, 10) : null);
        if (lookupId) needLookup.push(lookupId);
      }
      return {
        key: r.key,
        count: parseInt(r.cnt, 10),
        user_id: userId,
        username,
        _lookupId: !username ? (userId || (/^\d+$/.test(String(r.key)) ? parseInt(r.key, 10) : null)) : null,
      };
    });
    if (needLookup.length) {
      const uniq = [...new Set(needLookup)];
      const nameRows = await pool.query(
        'SELECT user_id, username FROM users WHERE user_id = ANY($1::bigint[])',
        [uniq]
      );
      const nameMap = new Map(nameRows.rows.map((row) => [parseInt(row.user_id, 10), row.username]));
      for (const item of items) {
        if (!item.username && item._lookupId) {
          item.username = nameMap.get(item._lookupId) || null;
        }
        delete item._lookupId;
      }
    } else {
      for (const item of items) delete item._lookupId;
    }
    res.json({ success: true, data: items });
  } catch (err) {
    console.error('hot:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// ===== 每日分析：批量拉取牌谱 JSON（≤500 局），客户端本地分析 =====
router.post('/records/batch-json', analyzeLimiter, async (req, res) => {
  try {
    const userId = parseInt(req.body?.user_id);
    if (isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户ID' });
    }
    const query = {
      rule: req.body.rule || null,
      sub_rule: req.body.sub_rule || null,
      room_type: req.body.room_type || null,
      match_tier: req.body.match_tier || null,
      tier: req.body.tier || null,
      game_type: req.body.game_type || null,
      date_from: req.body.date_from || null,
      date_to: req.body.date_to || null,
    };
    const params = [];
    const conditions = buildRecordFilters(userId, query, params);
    const sql = `
      SELECT game_id FROM (
        SELECT DISTINCT gpr.game_id, gr.created_at
        FROM game_player_records gpr
        JOIN game_records gr ON gr.game_id = gpr.game_id
        WHERE ${conditions.join(' AND ')}
      ) sub
      ORDER BY created_at DESC
      LIMIT $${params.length + 1}
    `;
    params.push(ANALYZE_MAX_GAMES);
    const idResult = await pool.query(sql, params);
    const gameIds = idResult.rows.map(r => r.game_id);
    if (gameIds.length === 0) {
      return res.json({ success: true, data: { items: [], total: 0, cap: ANALYZE_MAX_GAMES } });
    }
    const recordsResult = await pool.query(
      `SELECT game_id, record FROM game_records WHERE game_id = ANY($1::varchar[])`,
      [gameIds]
    );
    const items = recordsResult.rows.map(r => ({ game_id: r.game_id, record: r.record }));
    res.json({ success: true, data: { items, total: items.length, cap: ANALYZE_MAX_GAMES } });
  } catch (error) {
    console.error('batch-json:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
