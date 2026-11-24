// One-time conversion of the Vue replay and analytics caches. Hashes, UUIDs and
// board origins follow server/records/format.py and wall_alignment.py.
import { canonical, decoded, digest, identity, pairedId, requireData } from './browserRecordJson.js'
import { origins, boardDefinition } from './browserRecordOrigins.js'
import { expandStoredReplay } from '../../../shared/replayStorage.mjs'
export { parseReplayJSON, stringifyReplayJSON } from './browserRecordJson.js'

const FORMAT = 'open_mahjong.match_record'
const pairedRules = new Set(['sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate'])
const clone = (value) => structuredClone(value)

function catalogFor(entries, matchId, layout) {
  const groups = new Map()
  for (const { raw } of entries)
    for (const rd of Object.values(raw.game_round)) {
      const n = rd.current_round ?? rd.round_index
      requireData(Number.isInteger(n) && n > 0, '复式副号')
      if (!groups.has(n)) groups.set(n, [])
      groups.get(n).push({ rd, title: raw.game_title })
    }
  const boards = []
  for (const [n, group] of [...groups].sort(([a], [b]) => a - b)) {
    const ids = new Set(group.map((x) => x.rd.board_instance_id).filter(Boolean))
    const attempts = new Set(group.map((x) => x.rd.board_attempt).filter((v) => v != null))
    const defs = group.map(({ rd, title }) => boardDefinition(title, rd, n)).filter(Boolean)
    requireData(
      ids.size <= 1 &&
        attempts.size <= 1 &&
        [...attempts].every((v) => Number.isInteger(v) && v >= 1) &&
        new Set(defs.map(digest)).size <= 1,
      '复式副号或牌墙冲突',
    )
    const instance =
      [...ids][0] ||
      (group.length === 2 && defs.length === 2 ? identity('legacy-effective-board', matchId, n) : null)
    if (instance)
      boards.push({
        board_instance_id: instance,
        board_no: n,
        attempt: [...attempts][0] ?? null,
        wall_layout: layout,
        status: group.length === 2 ? 'paired' : 'playing',
        wall_definition: defs[0] ?? null,
      })
  }
  return boards
}

function buildTable({ side, raw, id, players }, matchId, boards) {
  requireData(raw.game_title && raw.game_round && !Array.isArray(raw.game_round), '缺少原始局信息')
  const payload = clone(raw),
    paired = side !== 'main'
  const table = {
    table_id: side,
    table_record_id: id,
    record_state: 'complete',
    payload_codec: 'om.action_ticks.v1',
    payload,
    players: clone(players ?? raw.players_settings ?? []),
    round_meta: {},
    source_sha256: digest(raw),
  }
  for (const [key, round] of Object.entries(raw.game_round)) {
    const index = Number(key.replace(/^round_index_/, ''))
    requireData(
      /^round_index_\d+$/.test(key) &&
        Number.isInteger(index) &&
        index > 0 &&
        (round.round_index == null || round.round_index === index),
      '局序号',
    )
    const ticks = round.action_ticks || [],
      times = round.action_times_ms || [],
      nodeMap = [0],
      asks = []
    const rd = payload.game_round[key]
    rd.action_ticks = []
    if ('action_times_ms' in rd) rd.action_times_ms = []
    ticks.forEach((tick, i) => {
      requireData(Array.isArray(tick) && typeof tick[0] === 'string', '动作节点')
      if (['ask_hand', 'ask_other'].includes(tick[0])) {
        const ask = { node: i + 1, tick }
        if (i < times.length) ask.time_ms = times[i]
        asks.push(ask)
      } else {
        rd.action_ticks.push(tick)
        if ('action_times_ms' in rd) rd.action_times_ms.push(times[i] ?? null)
      }
      nodeMap.push(rd.action_ticks.length)
    })
    if (asks.length) (table.transport_actions ??= {})[key] = asks
    const extra = {}
    if (!('action_ticks' in round)) extra.no_action_ticks = true
    if ('action_times_ms' in round && times.length !== ticks.length) {
      extra.time_count = times.length
      if (times.length > ticks.length) extra.time_tail = times.slice(ticks.length)
    }
    if (Object.keys(extra).length) (table.source_round_fields ??= {})[key] = extra
    const board = boards.find((b) => b.board_no === (rd.current_round ?? index))
    const instance = rd.board_instance_id || board?.board_instance_id || null
    const meta = {
      table_round_id: rd.table_round_id || identity('round', matchId, side, instance || key),
      board_instance_id: instance,
      round_index: index,
      legacy_node_map: nodeMap,
    }
    const basis = paired ? 'unavailable' : 'not_applicable'
    Object.assign(
      meta,
      paired && instance
        ? origins(rd, instance, board.wall_layout, board.wall_definition)
        : {
            initial_anchors: [],
            action_meta: rd.action_ticks.map(() => ({
              actor_original_seat: null,
              occurred_at_ms: null,
              wall_ref: null,
              wall_ref_basis: basis,
            })),
            alignment_status: basis,
            alignment_reason: paired ? 'board_instance_unverified' : null,
          },
    )
    meta.action_meta.forEach((m, i) => {
      const t = rd.action_times_ms?.[i]
      if (Number.isInteger(t) && t >= 0) m.occurred_at_ms = t
    })
    table.round_meta[key] = meta
  }
  return table
}

function restore(table) {
  const raw = clone(table.payload)
  for (const key of table.source_omitted_title_fields || []) delete raw.game_title[key]
  for (const [key, rd] of Object.entries(raw.game_round)) {
    for (const ask of table.transport_actions?.[key] || []) {
      rd.action_ticks.splice(ask.node - 1, 0, ask.tick)
      rd.action_times_ms?.splice(ask.node - 1, 0, ask.time_ms ?? null)
    }
    const fields = table.source_round_fields?.[key] || {}
    if (fields.time_count != null)
      rd.action_times_ms = rd.action_times_ms.concat(fields.time_tail || []).slice(0, fields.time_count)
    if (fields.no_action_ticks) delete rd.action_ticks
  }
  return raw
}

function buildBody(entries, matchId) {
  entries = [...entries].sort((a, b) => Number(a.side === 'closed') - Number(b.side === 'closed'))
  const first = entries.find((e) => e.side === 'open') || entries[0]
  const paired = first.side !== 'main',
    title = first.raw.game_title
  const expected = paired ? ['open', 'closed'] : ['main']
  const sync = new Set(),
    teams = new Map()
  for (const { raw, side } of entries) {
    const other = raw.game_title
    requireData(
      other.rule === title.rule &&
        other.sub_rule === title.sub_rule &&
        canonical(other.duplicate_profile) === canonical(title.duplicate_profile),
      '两桌规则不一致',
    )
    if (paired) requireData(other.duplicate_match_id === matchId && other.duplicate_table === side, '复式身份')
    for (const value of [other.duplicate_sync_mode, raw.duplicate_match?.sync_mode])
      if (paired && value != null) sync.add(value)
    for (const [i, team] of (other.team_by_player_index || []).entries()) {
      requireData(i < 4 && Number.isInteger(team), '队伍信息')
      if (!teams.has(team)) teams.set(team, { team_id: team, members: [] })
      teams
        .get(team)
        .members.push({ table_id: side, original_player_index: i, user_id: other['p' + i + '_uid'] ?? null })
    }
  }
  requireData(sync.size <= 1 && [...sync].every((v) => typeof v === 'boolean'), '同步模式')
  const boards = paired
    ? catalogFor(entries, matchId, title.sub_rule === 'sichuan/xueliu_doubles_duplicate' ? 'xueliu' : 'standard')
    : []
  const tables = {}
  for (const side of expected) {
    const entry = entries.find((e) => e.side === side)
    tables[side] = entry
      ? buildTable(entry, matchId, boards)
      : {
          table_id: side,
          table_record_id: null,
          record_state: 'missing',
          payload: null,
          round_meta: {},
          players: [],
        }
  }
  return {
    format: FORMAT,
    schema_version: 2,
    match_id: matchId,
    record_revision: 1,
    match_format: paired ? 'paired_tables_2v2' : 'single_table',
    completeness: entries.length === expected.length ? 'complete' : 'partial',
    rule: title.rule ?? null,
    sub_rule: title.sub_rule ?? null,
    rule_profile: title.duplicate_profile || { version: title.rule_version ?? null },
    sync_mode: [...sync][0] ?? null,
    teams: [...teams.values()].sort((a, b) => a.team_id - b.team_id),
    board_catalog: boards,
    tables,
    settlements: first.raw.duplicate_match?.boards || [],
    result: first.raw.doubles_result || first.raw.match_result || null,
    provenance: {
      converter: 'match-record-v2.1',
      sources: entries
        .map(({ side, id }) => ({ table_id: side, game_id: id, sha256: tables[side].source_sha256 }))
        .sort((a, b) => a.table_id.localeCompare(b.table_id)),
    },
  }
}

export function standardBody(row) {
  const record = decoded(row.record)
  return record?.format === FORMAT ? record : decoded(row.match_record)
}

function identify(row) {
  requireData(row?.game_id && row.record, '缺少牌谱编号或内容')
  const record = decoded(row.record)
  const isArchive = record.format === FORMAT
  const body = expandStoredReplay(isArchive ? record : decoded(row.match_record))
  if (body) {
    requireData(body.format === FORMAT && body.schema_version === 2 && body.match_id && body.tables, '未知格式')
    const paired = body.match_format === 'paired_tables_2v2'
    const sides = paired ? ['closed', 'open'] : ['main']
    requireData(
      (paired || body.match_format === 'single_table') && Object.keys(body.tables).sort().join() === sides.join(),
      '牌桌集合不完整',
    )
    for (const key of sides) {
      const table = body.tables[key]
      requireData(
        table.table_id === key &&
          (table.payload
            ? table.table_record_id && table.payload.game_title && table.payload.game_round && table.round_meta
            : table.record_state === 'missing'),
        '已发布桌信息不完整',
      )
    }
    const side =
      Object.keys(body.tables).find((k) => body.tables[k].table_record_id === row.game_id) ||
      row.selected_table_id ||
      (paired ? 'open' : 'main')
    return {
      id: paired ? pairedId(body.match_id) : body.tables.main.table_record_id,
      matchId: body.match_id,
      side,
      body,
      raw: isArchive ? null : record,
    }
  }
  const title = record.game_title
  requireData(title && record.game_round, '未知本地牌谱')
  const paired = pairedRules.has(title.sub_rule)
  if (paired)
    requireData(
      typeof title.duplicate_match_id === 'string' && ['open', 'closed'].includes(title.duplicate_table),
      '缺少复式身份',
    )
  return {
    id: paired ? pairedId(title.duplicate_match_id) : String(row.game_id),
    matchId: paired ? title.duplicate_match_id : identity('single', String(row.game_id)),
    side: paired ? title.duplicate_table : 'main',
    raw: record,
  }
}

export function replayKey(row) {
  return identify(row).id
}

function cursorMap(table) {
  const round_map = {},
    node_map = {}
  for (const [k, m] of Object.entries(table.round_meta)) {
    round_map[k] = m.table_round_id
    node_map[k] = m.legacy_node_map
  }
  return { round_map, node_map }
}

// All input rows belong to the same canonical match. A previously saved normalized
// body may coexist with a raw source carrying the original transport-only actions.
export function mergeReplayRows(rows) {
  const identified = rows.map((row) => ({ row, ...identify(row) }))
  const { id, matchId } = identified[0]
  const sources = new Map(),
    published = new Map(),
    aliases = new Map()
  let base = null
  for (const item of identified) {
    requireData(item.id === id && item.matchId === matchId, '编号冲突')
    const { row, body, raw, side } = item
    if (body) {
      if (
        !base ||
        Object.values(body.tables).filter((t) => t.payload).length >=
          Object.values(base.tables).filter((t) => t.payload).length
      )
        base = body
      for (const [k, t] of Object.entries(body.tables))
        if (t.payload) {
          const old = published.get(k)
          requireData(
            !old || (old.table_record_id === t.table_record_id && old.source_sha256 === t.source_sha256),
            '已保存的两份正文冲突',
          )
          if (old) requireData(canonical(cursorMap(old)) === canonical(cursorMap(t)), '同一来源的旧游标不一致')
          published.set(k, t)
        }
    }
    if (raw) {
      const sourceId = body?.tables[side]?.table_record_id || row.game_id
      const previous = sources.get(side)
      requireData(
        !previous || (sourceId === previous.id && digest(raw) === digest(previous.raw)),
        '同一桌有冲突的原始数据',
      )
      sources.set(side, {
        side,
        id: sourceId,
        raw,
        players: row.players?.filter((p) => !p.table_id || p.table_id === side),
      })
    }
    if (row.game_id !== id) {
      const { record, match_record, players, game_id, selected_table_id, legacy_cursor_map, ...metadata } = row
      aliases.set(row.game_id, {
        game_id: row.game_id,
        replay_id: id,
        table_id: side,
        rank: row.rank ?? null,
        metadata,
      })
    }
  }
  const unavailable = new Set()
  for (const [side, table] of published) {
    if (sources.has(side)) {
      requireData(digest(sources.get(side).raw) === table.source_sha256, '原始内容与校验值不符')
    } else {
      const raw = restore(table)
      // Old archives sometimes retained the source checksum but omitted the
      // original transport actions; never claim this projection is the original.
      if (table.source_sha256 !== digest(raw)) unavailable.add(side)
      for (const key of table.source_omitted_title_fields || [])
        raw.game_title[key] = table.payload.game_title[key]
      sources.set(side, { side, id: table.table_record_id, raw, players: table.players })
    }
  }
  requireData(sources.size > 0, '两桌都没有可读内容')
  const fullyPublished = base && [...sources.keys()].every((k) => base.tables[k]?.payload)
  const body = fullyPublished ? clone(base) : buildBody([...sources.values()], matchId)
  for (const [side, table] of published) {
    const source = sources.get(side)
    const retained = clone(table)
    if (unavailable.has(side)) retained.original_source_unavailable = true
    if (!fullyPublished) {
      for (const [key, metadata] of Object.entries(body.tables[side].round_meta)) {
        const old = retained.round_meta[key]
        if (metadata.alignment_status === 'exact' && old.alignment_status !== 'exact') {
          requireData(canonical(metadata.legacy_node_map) === canonical(old.legacy_node_map), '原动作位置已改变')
          Object.assign(old, { ...metadata, table_round_id: old.table_round_id })
        }
      }
    }
    if (identified.some((i) => i.raw && i.side === side)) {
      delete retained.original_source_unavailable
      const omitted = ['duplicate_match_id', 'duplicate_table'].filter(
        (k) => !Object.hasOwn(source.raw.game_title, k) && Object.hasOwn(retained.payload.game_title, k),
      )
      if (omitted.length) retained.source_omitted_title_fields = omitted
      const calculated = buildTable(source, matchId, body.board_catalog)
      Object.assign(retained, {
        transport_actions: calculated.transport_actions,
        source_round_fields: calculated.source_round_fields,
      })
      for (const k of ['transport_actions', 'source_round_fields']) if (!retained[k]) delete retained[k]
      requireData(digest(restore(retained)) === table.source_sha256, '无法还原原始动作')
    }
    body.tables[side] = retained
    const provenance = body.provenance?.sources?.find((entry) => entry.table_id === side)
    if (provenance) provenance.sha256 = retained.source_sha256
  }
  const selected = identified.at(-1)
  const chosen = body.tables[selected.side]?.payload
    ? selected.side
    : Object.keys(body.tables).find((k) => body.tables[k].payload)
  const primary = Object.assign({}, ...rows)
  delete primary.match_record
  const players = []
  for (const [k, t] of Object.entries(body.tables))
    if (t.payload) {
      if (k !== 'main') {
        const title = t.payload.game_title
        for (let i = 0; i < 4; i++) {
          const uid = title['p' + i + '_uid']
          if (uid == null || t.players.some((p) => p.original_player_index === i)) continue
          const known = t.players.find((p) => p.user_id === uid && p.original_player_index == null)
          if (known) known.original_player_index = i
          else
            t.players.push({ user_id: uid, original_player_index: i, username: title['p' + i + '_name'] ?? null })
        }
      }
      players.push(...t.players.map((p) => ({ ...p, table_id: k })))
      if (t.table_record_id !== id && !aliases.has(t.table_record_id))
        aliases.set(t.table_record_id, { game_id: t.table_record_id, replay_id: id, table_id: k })
    }
  Object.assign(primary, {
    game_id: id,
    record: body,
    selected_table_id: chosen,
    players,
    rule: body.rule,
    sub_rule: body.sub_rule,
  })
  delete primary.legacy_cursor_map
  return { row: primary, aliases: [...aliases.values()] }
}

export function selectCachedReplay(row, alias = null) {
  if (!row) return null
  const side = alias?.table_id || row.selected_table_id
  const record = expandStoredReplay(row.record)
  const table = record.tables[side]
  return {
    ...row,
    ...alias?.metadata,
    record,
    selected_table_id: side,
    source_game_id: alias?.game_id || row.game_id,
    rank: alias && Object.hasOwn(alias, 'rank') ? alias.rank : row.rank,
    legacy_cursor_map: table && (alias || table.table_record_id === row.game_id) ? cursorMap(table) : null,
  }
}

// Only identity metadata is kept while scanning a legacy cache. The caller
// then loads at most the sources for one replay at a time.
export function legacyRowIdentity(row) {
  const raw = decoded(row.record)
  const archive = raw?.format === FORMAT ? raw : decoded(row.match_record)
  const needsArchive =
    !archive &&
    pairedRules.has(raw?.game_title?.sub_rule) &&
    !(raw.game_title.duplicate_match_id && raw.game_title.duplicate_table)
  const sourceKeys = Object.values(archive?.tables || {})
    .filter((t) => t.payload && t.table_record_id)
    .map((t) => t.table_record_id)
  return { id: needsArchive ? null : replayKey(row), sourceKeys }
}

export function attachArchivedTable(row, group) {
  if (legacyRowIdentity(row).id) return row
  for (const candidate of group) {
    const body =
      decoded(candidate.record)?.format === FORMAT ? decoded(candidate.record) : decoded(candidate.match_record)
    const selected = Object.entries(body?.tables || {}).find(([, t]) => t.table_record_id === row.game_id)
    if (selected) {
      const title = decoded(row.record).game_title
      requireData(
        (!title.duplicate_match_id || title.duplicate_match_id === body.match_id) &&
          (!title.duplicate_table || title.duplicate_table === selected[0]),
        '已发布桌身份不符',
      )
      return { ...row, match_record: body }
    }
  }
  throw new Error('缺少可核验的复式来源 ' + row.game_id)
}
