function mapRow(row) {
  return {
    user_id: Number(row.user_id),
    username: row.username || String(row.user_id),
    total_pt_change: Number(row.total_pt_change) || 0,
    games: Number(row.games) || 0,
  };
}

function pickPtLeaders(rows, limit = 10) {
  const players = (rows || []).map(mapRow);
  const gainers = players
    .filter((row) => row.total_pt_change > 0)
    .sort((a, b) => b.total_pt_change - a.total_pt_change || b.games - a.games || a.user_id - b.user_id)
    .slice(0, limit)
    .map((row, index) => ({ ...row, place: index + 1 }));
  const losers = players
    .filter((row) => row.total_pt_change < 0)
    .sort((a, b) => a.total_pt_change - b.total_pt_change || b.games - a.games || a.user_id - b.user_id)
    .slice(0, limit)
    .map((row, index) => ({ ...row, place: index + 1 }));
  return { gainers, losers };
}

module.exports = { pickPtLeaders };
