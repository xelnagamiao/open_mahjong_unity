import profile from '../../../shared/washizuDoubles.json' with { type: 'json' }

export function washizuDoublesSummary(detail) {
  const title = detail?.record?.game_title
  if (title?.sub_rule !== profile.sub_rule) return null
  const teams = title.team_by_player_index
  const roles = title.team_role_by_player_index
  if (!Array.isArray(teams) || teams.length !== 4 || !Array.isArray(roles) || roles.length !== 4) return null
  const identities = teams.map((team, i) => [0, 1].includes(team)
    && ['captain', 'assistant'].includes(roles[i]) ? 2 * team + (roles[i] === 'assistant' ? 1 : 0) : -1)
  if (identities.includes(-1) || new Set(identities).size !== 4) return null
  return {
    players: identities.map((role, index) => ({ role: profile.roles[role], name: String(title[`p${index}_name`] || `座位${index + 1}`) })),
    result: detail.record.doubles_result || title.doubles_result || null,
  }
}

export function captainResultReason(result) {
  if (result?.reason === 'captain_bust') return result.winner_team_id == null ? '双方主将同时击飞且同分，和局' : '主将击飞，按击飞时点数判定'
  return result?.winner_team_id == null ? '主将同分，和局' : '按主将最终点数判定'
}
