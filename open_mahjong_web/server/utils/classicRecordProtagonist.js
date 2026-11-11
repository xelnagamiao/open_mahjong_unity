const { shareInteger } = require('./recordShareParse');

function recordObject(value) {
  if (typeof value !== 'string') return value || {};
  try { return JSON.parse(value); } catch { return {}; }
}

function recordPlayers(recordValue, players) {
  const title = recordObject(recordValue).game_title || {};
  const count = title.player_count === 3 || title.rule === 'malaysia' ? 3 : 4;
  return Array.from({ length: count }, (_, original) => {
    const uid = shareInteger(title[`p${original}_uid`]);
    const player = players.find(p => p.original_player_index === original)
      || (uid > 0 ? players.find(p => p.user_id === uid) : null);
    return {
      ...player,
      original_player_index: original,
      user_id: uid ?? player?.user_id ?? 0,
      username: player?.username || title[`p${original}_name`] || `玩家 ${original + 1}`,
    };
  });
}

// 旧目录没有视角时，按节点已执行的最后一个玩家动作补齐；seats 是开局席位 -> 本局座位。
function inferOriginalPlayer(item, recordValue, count) {
  const record = recordObject(recordValue);
  const rounds = Object.values(record.game_round || {}).sort((a, b) => a.round_index - b.round_index);
  const round = rounds.find(r => Number(r.round_index) === item.round) || rounds[(item.round || 1) - 1] || rounds[0];
  if (!round) return 0;
  let current = shareInteger(round.start_player_index, 0, count - 1) ?? 0;
  let actor = current;
  const ticks = (round.action_ticks || []).slice(0, item.node ?? 0);
  for (const tick of ticks) {
    const action = tick[0];
    if (action === 'reset') { current = shareInteger(tick[1], 0, count - 1) ?? current; continue; }
    if (['bh', 'bd', 'cl', 'cm', 'cr', 'p', 'g'].includes(action)) {
      actor = shareInteger(tick[2], 0, count - 1) ?? current;
      current = actor;
    } else if (['hu_self', 'hu_first', 'hu_second', 'hu_third', 'hu_riichi', 'riichi', 'rk'].includes(action)) {
      actor = shareInteger(tick[1], 0, count - 1) ?? current;
    } else if (['c', 'd', 'gd', 'ag', 'jg'].includes(action)) {
      if (tick[2] === 'dw') current = shareInteger(tick[5], 0, count - 1) ?? current;
      actor = current;
      if (action === 'c') current = (current + 1) % count;
    }
  }
  const original = Array.isArray(round.seats) ? round.seats.indexOf(actor) : actor;
  return original >= 0 && original < count ? original : 0;
}

function resolveProtagonist(item, record, players, strict = false) {
  const index = shareInteger(item.protagonist_player_index, 0, players.length - 1);
  const uid = shareInteger(item.protagonist_user_id);
  let selected = index != null ? players.find(p => p.original_player_index === index) : null;
  if (selected && uid != null && selected.user_id !== uid) selected = null;
  if (!selected && uid > 0) {
    const matches = players.filter(p => p.user_id === uid);
    if (matches.length === 1) selected = matches[0];
  }
  if (!selected && strict && (item.protagonist_player_index != null || item.protagonist_user_id != null)) {
    throw Object.assign(new Error('主角必须是这份牌谱中的玩家'), { status: 400 });
  }
  return selected || players[inferOriginalPlayer(item, record, players.length)] || null;
}

module.exports = { recordPlayers, resolveProtagonist, inferOriginalPlayer };
