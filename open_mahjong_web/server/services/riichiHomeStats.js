const { RIICHI_STATS_VERSION, BASE_KEYS, DETAIL_KEYS } = require('./riichiStats');

const GAME_TYPES = { 1: 'dongfeng', 2: 'banzhuang', 3: 'xifeng', 4: 'quanzhuang' };
const GAME_LABELS = { dongfeng: '东风战', banzhuang: '半庄战', xifeng: '东西战', quanzhuang: '全庄战' };
const RANK_KEYS = ['first_place_count', 'second_place_count', 'third_place_count', 'fourth_place_count'];
const sum = key => `COALESCE(SUM((stats->>'${key}')::bigint),0) AS ${key}`;

function querySql(withSummaries) {
  const details = DETAIL_KEYS.map(key => `'${key}',COALESCE(SUM((stats->'riichi_details'->>'${key}')::bigint),0)`).join(',');
  return `
    WITH players AS (
      SELECT p.game_id,p.room_type,p.match_type,p.sub_rule,p.match_tier,p.rank,
             ${withSummaries ? 's.stats' : 'NULL::jsonb AS stats'},
             (COALESCE(p.sub_rule,'')='riichi/sanma'
               OR COALESCE(p.match_type,'') ~ '^[0-9]+/3(_rank)?$'
               OR COALESCE(p.match_type,'') LIKE '%_sanma%') AS sanma
      FROM game_player_records p JOIN game_records g USING(game_id)
      ${withSummaries ? `LEFT JOIN riichi_player_game_stats s ON s.game_id=p.game_id AND s.user_id=p.user_id AND s.version=${RIICHI_STATS_VERSION}` : ''}
      WHERE p.rule='riichi' AND p.room_type IN ('match','custom','events')
    ), samples AS (
      SELECT *,CASE WHEN match_type ~ '^[0-9]+/(3|4)(_sanma)?(_rank)?$'
          THEN split_part(match_type,'/',1)||'/4'||CASE WHEN sanma THEN '_sanma' ELSE '' END
            ||CASE WHEN room_type='match' THEN '_rank' ELSE '' END
          ELSE match_type END AS mode,
          CASE WHEN sanma THEN 3 ELSE 4 END AS player_count,
          CASE WHEN room_type='match' THEN match_tier ELSE NULL END AS tier
      FROM players
    )
    SELECT room_type,mode,tier AS match_tier,player_count,
           COUNT(DISTINCT game_id)::int AS total_games,
           COUNT(*)::int AS player_game_count,
           COUNT(stats)::int AS analyzed_player_games,
           (COUNT(stats)=COUNT(*)) AS details_available,
           ${BASE_KEYS.filter(key => key !== 'total_games' && !RANK_KEYS.includes(key)).map(sum).join(',')},
           ${RANK_KEYS.map((key, index) => `COUNT(*) FILTER (WHERE rank=${index + 1} AND rank<=player_count)::int AS ${key}`).join(',')},
           jsonb_build_object(${details}) AS riichi_details
    FROM samples GROUP BY room_type,mode,tier,player_count
    ORDER BY room_type,mode,tier,player_count
  `;
}

/** 首页日麻：对局按 game_id 去重，行为指标直接合计每位玩家的小局与结果。 */
async function queryRiichiHomeStats(db) {
  let result;
  try {
    result = await db.query(querySql(true));
  } catch (error) {
    if (error.code !== '42P01') throw error;
    // 未升级摘要表的数据库仍可展示实际对局和顺位，行为指标暂不可用。
    result = await db.query(querySql(false));
  }
  return result.rows.map(row => {
    const gameType = GAME_TYPES[Number.parseInt(row.mode, 10)] || null;
    const out = { ...row, rule: Number(row.player_count) === 3 ? 'riichi_sanma' : 'riichi', source: 'riichi_summary',
      game_type: gameType, game_type_label: GAME_LABELS[gameType] || row.mode,
      riichi_details: row.riichi_details };
    for (const key of [...BASE_KEYS, 'player_game_count', 'analyzed_player_games', 'player_count']) {
      out[key] = Number(row[key]) || 0;
    }
    return out;
  });
}

module.exports = { queryRiichiHomeStats };
