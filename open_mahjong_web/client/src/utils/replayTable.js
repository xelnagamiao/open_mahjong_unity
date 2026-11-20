// Rendering consumes one table. The cached/downloaded document always keeps all tables.
export function replayTableView(detail, { tableId, userId, sourceActions = false } = {}) {
  const body = detail?.record ?? detail
  if (body?.format !== 'open_mahjong.match_record') return detail
  const tables = ['main', 'open', 'closed'].map(k => body.tables[k]).filter(t => t?.payload)
  const uid = Number(userId)
  const hasPlayer = t => t.players.some(p => Number(p.user_id) === uid)
    || Object.entries(t.payload.game_title).some(([k, v]) => /^p\d+_uid$/.test(k) && Number(v) === uid)
  const preferred = tables.find(t => t.table_id === detail.selected_table_id)
  const table = tables.find(t => t.table_id === tableId)
    || (uid > 0 && preferred && hasPlayer(preferred) && preferred)
    || (uid > 0 && tables.find(hasPlayer)) || preferred || tables[0]
  if (!table || (tableId && table.table_id !== tableId)) throw new Error('这份牌谱中没有可读取的指定牌桌')
  const record = { ...table.payload, game_title: { ...table.payload.game_title } }
  for (const p of table.players) {
    if (p.original_player_index != null && p.username != null) record.game_title[`p${p.original_player_index}_name`] = p.username
  }
  if (sourceActions && (table.transport_actions || table.source_round_fields)) {
    record.game_round = { ...record.game_round }
    const keys = new Set([...Object.keys(table.transport_actions || {}), ...Object.keys(table.source_round_fields || {})])
    for (const key of keys) {
      const round = record.game_round[key]
      const restored = { ...round, action_ticks: [...round.action_ticks] }
      if (round.action_times_ms) restored.action_times_ms = [...round.action_times_ms]
      for (const action of table.transport_actions?.[key] || []) {
        restored.action_ticks.splice(action.node - 1, 0, action.tick)
        restored.action_times_ms?.splice(action.node - 1, 0, action.time_ms ?? null)
      }
      const fields = table.source_round_fields?.[key] || {}
      if (fields.time_count != null) restored.action_times_ms = restored.action_times_ms.concat(fields.time_tail || []).slice(0, fields.time_count)
      if (fields.no_action_ticks) delete restored.action_ticks
      record.game_round[key] = restored
    }
  }
  return { ...detail, game_id: detail.game_id ?? table.table_record_id, rule: body.rule,
    sub_rule: body.sub_rule, selected_table_id: table.table_id, table_record_id: table.table_record_id,
    players: table.players, record, round_meta: table.round_meta }
}

export function replayLinkNode(raw, meta, canonical) {
  const node = Math.max(0, Math.floor(raw))
  const map = meta?.legacy_node_map
  return canonical || !map?.length ? node : map[Math.min(node, map.length - 1)]
}
