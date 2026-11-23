'use strict';

// One row per played table. This is an inlined query over the two authoritative
// relations, not a database object or a reconstruction of the old record format.
const tableTitles = `(SELECT t.game_id,t.replay_id,t.table_id,t.created_at,
    r.record->'tables'->t.table_id->'payload'->'game_title' AS title
    FROM game_record_tables t JOIN game_replays r ON r.game_id=t.replay_id)`;

function filteredTables(conditions) {
  return conditions.some(condition => condition.includes('gr.title')) ? tableTitles : 'game_record_tables';
}

// Only evidence consumers need the source positions of the removed network
// prompts. Keep the numeric spelling from PostgreSQL (even decimal scale and
// integers outside JS's safe range) for fingerprints and exact-source checks.
function sourceText(tableText) {
  const t = JSON.parse(tableText, (_k,v,c) => typeof v === 'number' ? JSON.rawJSON(c.source) : v);
  const num = v => Number(JSON.isRawJSON(v) ? v.rawJSON : v);
  const p = t.payload;
  for (const k of t.source_omitted_title_fields || []) delete p.game_title[k];
  for (const key of new Set([...Object.keys(t.transport_actions || {}), ...Object.keys(t.source_round_fields || {})])) {
    const rd = p.game_round[key], fields = t.source_round_fields?.[key] || {};
    for (const item of t.transport_actions?.[key] || []) {
      const i = num(item.node) - 1;
      if (!Number.isSafeInteger(i) || i < 0 || i > rd.action_ticks.length) throw new Error('Invalid recorded prompt position');
      rd.action_ticks.splice(i, 0, item.tick);
      rd.action_times_ms?.splice(i, 0, item.time_ms ?? null);
    }
    if (fields.time_count != null) rd.action_times_ms = rd.action_times_ms.concat(fields.time_tail || []).slice(0,num(fields.time_count));
    if (fields.no_action_ticks) delete rd.action_ticks;
  }
  return JSON.stringify(p);
}

module.exports = { tableTitles, filteredTables, sourceText };
