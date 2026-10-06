const METRIC_KEYS = ['win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
  'total_win_turn', 'total_fangchong_score', 'fulu_round_count', 'cuohe_count', 'total_round_score'];
const RANK_KEYS = ['first_place_count', 'second_place_count', 'third_place_count', 'fourth_place_count'];
const GAME_TYPES = { 1: 'dongfeng', 2: 'banzhuang', 3: 'xifeng', 4: 'quanzhuang' };
const GAME_LABELS = { dongfeng: '东风战', banzhuang: '半庄战', xifeng: '东西战', quanzhuang: '全庄战' };

function querySql(withMetrics, exclude, filterSubRules) {
  return `WITH players AS (
      SELECT p.game_id,p.room_type,p.match_type AS mode,
             CASE WHEN p.room_type='match' THEN p.match_tier ELSE NULL END AS match_tier,
             CASE WHEN p.rule='sichuan' AND COALESCE(NULLIF(p.sub_rule,''),g.record->'game_title'->>'sub_rule')='sichuan/xueliu_exchange'
               THEN 'sichuan_xueliu_exchange'
               WHEN p.rule='sichuan' AND COALESCE(NULLIF(p.sub_rule,''),g.record->'game_title'->>'sub_rule')='sichuan/xueliu'
               THEN 'sichuan_xueliu'
               WHEN p.rule IN ('jiandan','nanque') OR (p.rule='zhongyong' AND p.sub_rule='zhongyong/nanque') THEN 'nanque'
               WHEN p.rule='guangdong' AND COALESCE(NULLIF(p.sub_rule,''),NULLIF(g.record->'game_title'->>'sub_rule',''),'guangdong/tuidao_mil2024')='guangdong/tuidao_mil2024' THEN 'guangdong_tuidao'
               WHEN p.rule='guobiao' AND p.sub_rule='guobiao/sanma' THEN 'guobiao_sanma'
               WHEN p.rule='shanghai' AND COALESCE(NULLIF(p.sub_rule,''),g.record->'game_title'->>'sub_rule')='shanghai/qinghunpeng' THEN 'shanghai_qinghunpeng'
               ELSE p.rule END AS rule,p.rank,
             ${withMetrics ? 'm.game_id IS NOT NULL' : 'FALSE'} AS has_metrics,
             ${withMetrics ? 'm.total_rounds' : 'NULL::int AS total_rounds'},
             ${METRIC_KEYS.map(key => withMetrics ? `m.${key}` : `NULL::int AS ${key}`).join(',')}
      FROM game_player_records p JOIN game_records g USING(game_id)
      ${withMetrics ? `LEFT JOIN (SELECT DISTINCT ON (game_id,user_id) * FROM game_player_metrics
        WHERE ${exclude ? 'NOT (rule=ANY($1::text[]))' : 'rule=ANY($1::text[])'} ORDER BY game_id,user_id,id DESC) m
        ON m.game_id=p.game_id AND m.user_id=p.user_id AND m.rule=p.rule` : ''}
      WHERE ${exclude ? 'NOT (p.rule=ANY($1::text[]))' : 'p.rule=ANY($1::text[])'}
        ${filterSubRules ? 'AND p.sub_rule=ANY($2::text[])' : ''}
        AND p.room_type IN ('match','custom','events')
    ), games AS (
      SELECT game_id,rule,room_type,mode,match_tier,MAX(total_rounds) AS total_rounds,
             COUNT(*)::int AS player_game_count,BOOL_AND(has_metrics) AS details_available,
             ${METRIC_KEYS.map(key => `COALESCE(SUM(${key}),0) AS ${key}`).join(',')},
             ${RANK_KEYS.map((key, i) => `COUNT(*) FILTER (WHERE rank=${i + 1}) AS ${key}`).join(',')}
      FROM players GROUP BY game_id,rule,room_type,mode,match_tier
    )
    SELECT rule,room_type,mode,match_tier,COUNT(*)::int AS total_games,
           COALESCE(SUM(total_rounds),0) AS total_rounds,SUM(player_game_count) AS player_game_count,
           BOOL_AND(details_available) AS details_available,
           ${[...METRIC_KEYS, ...RANK_KEYS].map(key => `SUM(${key}) AS ${key}`).join(',')}
    FROM games GROUP BY rule,room_type,mode,match_tier ORDER BY rule,room_type,mode,match_tier`;
}

/** 没有累计表的规则读取牌谱参与者和场次明细；血战／血流按子规则隔离。 */
async function queryRecordHomeStats(db, rules, { exclude = false, subRules = [] } = {}) {
  const params = subRules.length ? [rules, subRules] : [rules];
  let result;
  try {
    result = await db.query(querySql(true, exclude, subRules.length > 0), params);
  } catch (error) {
    if (error.code !== '42P01') throw error;
    result = await db.query(querySql(false, exclude, subRules.length > 0), params);
  }
  return result.rows.map(row => {
    const gameType = GAME_TYPES[Number.parseInt(row.mode, 10)] || null;
    const out = { ...row, source: 'record_metrics', game_type: gameType,
      game_type_label: GAME_LABELS[gameType] || row.mode };
    for (const key of ['total_games', 'total_rounds', 'player_game_count', ...METRIC_KEYS, ...RANK_KEYS]) out[key] = Number(row[key]) || 0;
    return out;
  });
}

module.exports = { queryRecordHomeStats };
