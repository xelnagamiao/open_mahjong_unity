const pool = require('../config/database');

const GAME_ID_PATTERN = /^[0-9A-Za-z]{1,16}$/;

function normalizeRecord(value) {
  if (value && typeof value === 'object') return value;
  if (typeof value !== 'string') return null;
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

async function queryPublicGameRecord(gameId, guobiaoOnly) {
  if (!GAME_ID_PATTERN.test(gameId)) return { status: 400, message: '牌谱编号格式不正确' };

  const recordResult = await pool.query(
    'SELECT game_id, record, created_at FROM game_records WHERE game_id = $1 LIMIT 1',
    [gameId]
  );
  if (!recordResult.rowCount) return { status: 404, message: '没有找到这份牌谱' };

  const record = normalizeRecord(recordResult.rows[0].record);
  if (!record) return { status: 422, message: '牌谱数据无法解析' };

  const playersResult = await pool.query(
    `SELECT user_id, username, score, rank, original_player_index,
            title_used, character_used, profile_used, voice_used,
            rule, sub_rule, room_type, match_type
       FROM game_player_records
      WHERE game_id = $1
      ORDER BY original_player_index NULLS LAST, rank`,
    [gameId]
  );
  const players = playersResult.rows;
  const rule = players[0]?.rule || record.game_title?.rule || null;
  if (guobiaoOnly && rule !== 'guobiao') {
    return { status: 400, message: '2D 牌谱阅览目前只支持国标麻将' };
  }

  return {
    status: 200,
    data: {
      game_id: recordResult.rows[0].game_id,
      created_at: recordResult.rows[0].created_at,
      rule,
      sub_rule: players[0]?.sub_rule || record.game_title?.sub_rule || null,
      room_type: players[0]?.room_type || record.game_title?.room_type || null,
      match_type: players[0]?.match_type || null,
      players: players.map((player) => ({
        user_id: Number(player.user_id),
        username: player.username,
        score: Number(player.score) || 0,
        rank: Number(player.rank) || 0,
        original_player_index: Number.isInteger(player.original_player_index)
          ? player.original_player_index
          : null,
        title_used: player.title_used,
        character_used: player.character_used,
        profile_used: player.profile_used,
        voice_used: player.voice_used,
      })),
      record,
    },
  };
}

async function getPublicGameRecord(gameId) {
  return queryPublicGameRecord(gameId, true);
}

async function getPublicUnityGameRecord(gameId) {
  return queryPublicGameRecord(gameId, false);
}

module.exports = { getPublicGameRecord, getPublicUnityGameRecord, GAME_ID_PATTERN };
