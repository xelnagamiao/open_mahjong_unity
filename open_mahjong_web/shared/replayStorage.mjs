// Expand the PostgreSQL representation at the boundary shared by ZIP/HTTP
// exports and browser imports. Fields present in the source are authoritative.
export function expandStoredReplay(record) {
  if (record?.storage_codec !== 'om.single_round_meta.v1' || record.format !== 'open_mahjong.match_record' ||
      record.schema_version !== 2 || record.match_format !== 'single_table') return record
  const table = record.tables?.main
  if (table?.table_id !== 'main' || Object.keys(record.tables).length !== 1 ||
      !table.payload?.game_round || !table.round_meta) {
    throw new Error('单桌牌谱的存储格式与牌桌数据不匹配')
  }

  const metadata = { ...table.round_meta }
  if (Object.keys(metadata).length !== Object.keys(table.payload.game_round).length) {
    throw new Error('单桌牌谱的小局与元数据不匹配')
  }
  for (const [key, round] of Object.entries(table.payload.game_round)) {
    const meta = metadata[key]
    if (!Object.hasOwn(metadata, key) || !meta || !Array.isArray(round?.action_ticks)) {
      throw new Error('单桌牌谱缺少小局或动作数据')
    }
    const expanded = { ...meta }
    if (!Object.hasOwn(meta, 'action_meta')) {
      expanded.action_meta = round.action_ticks.map((_, i) => {
        const time = round.action_times_ms?.[i]
        return {
          actor_original_seat: null,
          occurred_at_ms: Number.isSafeInteger(time) && time >= 0 ? time : null,
          wall_ref: null,
          wall_ref_basis: 'not_applicable',
        }
      })
    }
    if (!Object.hasOwn(meta, 'legacy_node_map')) {
      expanded.legacy_node_map = Array.from({ length: round.action_ticks.length + 1 }, (_, i) => i)
    }
    metadata[key] = expanded
  }
  const expanded = { ...record, tables: { ...record.tables, main: { ...table, round_meta: metadata } } }
  delete expanded.storage_codec
  return expanded
}
