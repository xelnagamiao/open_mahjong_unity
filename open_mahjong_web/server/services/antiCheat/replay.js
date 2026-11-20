'use strict';

// Read-only reconstruction. No database, clock, network or live game dependency.
const { createHash } = require('node:crypto');

const MELD_CODES = new Set(['cl', 'cm', 'cr', 'p', 'g']);
const HU_CODES = new Set(['hu_self', 'hu_first', 'hu_second', 'hu_third']);
const WIN_DISTANCE = { hu_first: 1, hu_second: 2, hu_third: 3 };
const TILE_TYPES = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25, 26, 27, 28, 29,
  31, 32, 33, 34, 35, 36, 37, 38, 39, 41, 42, 43, 44, 45, 46, 47];

function integer(value) {
  if (value === null || value === undefined || value === '' || typeof value === 'boolean') return null;
  const n = Number(value);
  return Number.isSafeInteger(n) ? n : null;
}

function tile(value) {
  const n = integer(value);
  return TILE_TYPES.includes(n) || (n >= 51 && n <= 58) ? n : null;
}

function stableStringify(value) {
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(',')}]`;
  if (value && typeof value === 'object') return `{${Object.keys(value).sort()
    .filter((key) => value[key] !== undefined)
    .map((key) => `${JSON.stringify(key)}:${stableStringify(value[key])}`).join(',')}}`;
  return JSON.stringify(value) ?? 'null';
}

function hash(value) {
  return createHash('sha256').update(stableStringify(value)).digest('hex');
}

function inferGuobiaoSeats(currentRound) {
  if (!Number.isInteger(currentRound) || currentRound < 1 || currentRound > 16) return null;
  let seats = [0, 1, 2, 3];
  for (let r = 2; r <= currentRound; r += 1) {
    seats = seats.map((s) => (s + 3) % 4);
    if (r === 5) seats = [1, 0, 3, 2];
    if (r === 9) seats = [3, 2, 0, 1];
    if (r === 13) seats = [2, 3, 1, 0];
  }
  return seats;
}

function resolveSeats(round, standard, handIndex, count = 4) {
  if (Array.isArray(round.seats)) {
    const seats = round.seats.map(integer);
    if (seats.length === count && new Set(seats).size === count
      && seats.every((s) => s !== null && s >= 0 && s < count)) return { seats, inferred: false };
    return { seats: null, inferred: false };
  }
  if (standard) return { seats: inferGuobiaoSeats(integer(round.current_round) ?? handIndex), inferred: true };
  return { seats: null, inferred: false };
}

function relation(donor, recipient, count = 4) {
  const distance = (recipient - donor + count) % count;
  return distance === 1 ? 'upstream' : distance === count - 1 ? 'downstream' : 'opposite';
}

function parseRecord(row) {
  if (row?.metadata_conflict) return { error: 'metadata_conflict' };
  const raw = row && Object.prototype.hasOwnProperty.call(row, 'record') ? row.record : row;
  let record = raw;
  if (typeof raw === 'string') {
    try { record = JSON.parse(raw); } catch (_) { return { error: 'invalid_json' }; }
  }
  if (!record || typeof record !== 'object' || Array.isArray(record)
    || !record.game_title || typeof record.game_title !== 'object'
    || !record.game_round || typeof record.game_round !== 'object') return { error: 'invalid_record_shape' };
  const title = record.game_title;
  const count = integer(title.player_count) ?? (title.p3_uid != null ? 4 : title.p2_uid != null ? 3 : 0);
  if (![3, 4].includes(count)) return { error: 'unsupported_player_count' };
  const players = Array.from({ length: count }, (_, i) => {
    const uid = integer(title[`p${i}_uid`]);
    return uid === null ? null : { user_id: String(uid), name: String(title[`p${i}_name`] ?? uid), original_index: i };
  });
  if (players.some((p) => p === null) || new Set(players.map((p) => p.user_id)).size !== count) {
    return { error: 'invalid_player_identity' };
  }
  const rule = String(title.rule || row?.rule || 'unknown');
  const subRule = String(title.sub_rule || row?.sub_rule || (rule === 'guobiao' ? 'guobiao/standard' : 'unknown'));
  const mode = String(row?.match_type ?? title.match_type ?? title.match_queue_type ?? 'unknown');
  const room = String(title.room_type || row?.room_type || 'unknown');
  const cooperative = Boolean(title.is_doubles || title.team_by_player_index || title.duplicate_key
    || title.duplicate_wall_type || title.duplicate_walls || title.is_player_set_random_seed
    || /doubles|duplicate|team|双打|团队|复式/i.test(`${subRule} ${mode}`));
  const standard = rule === 'guobiao' && ['', 'guobiao', 'guobiao/standard'].includes(subRule)
    && count === 4 && !cooperative;
  const limit = integer(title.hepai_limit) ?? 8;
  const tier = String(title.match_tier ?? row?.match_tier ?? 'unknown');
  const ruleKey = [rule, subRule, count, room, mode, tier,
    `limit=${limit}`, `flowers=${title.use_flowers !== false}`,
    `cuohe=${Boolean(title.open_cuohe)}`, `opening=${Boolean(title.tian_di_ren_he)}`].join('|');
  const recordHash = hash(record);
  const gameId = String(row?.game_id ?? record.game_id ?? title.game_id ?? `sha256:${recordHash}`);
  return {
    record, title, players, count, rule, sub_rule: subRule, room_type: room, match_type: mode,
    match_tier: tier, rule_key: ruleKey, game_id: gameId, content_hash: recordHash,
    standard, cooperative, competitive: standard && room === 'match', hepai_limit: limit,
    created_at: row?.created_at ?? title.end_time ?? title.start_time ?? null,
    created_at_timezone: row?.created_at_timezone ?? null,
    metadata_players: Array.isArray(row?.players) ? row.players : [],
  };
}

function removeOne(hand, value) {
  const index = hand.indexOf(value);
  if (index < 0) return false;
  hand.splice(index, 1);
  return true;
}

function parseChanges(raw, count) {
  if (typeof raw === 'string') { try { raw = JSON.parse(raw); } catch (_) { return null; } }
  if (!Array.isArray(raw) || raw.length !== count) return null;
  const values = raw.map((v) => (v === null || v === '' ? NaN : Number(v)));
  return values.every(Number.isFinite) && Math.abs(values.reduce((a, b) => a + b, 0)) < 1e-8 ? values : null;
}

function paymentsFromChanges(changes, seatUsers) {
  const positives = changes.map((n, i) => ({ amount: n, user_id: seatUsers[i] })).filter((p) => p.amount > 0);
  const negatives = changes.map((n, i) => ({ amount: -n, user_id: seatUsers[i] })).filter((p) => p.amount > 0);
  if (positives.length === 1) return negatives.map((p) => ({ payer_id: p.user_id, payee_id: positives[0].user_id, amount: p.amount }));
  if (negatives.length === 1) return positives.map((p) => ({ payer_id: negatives[0].user_id, payee_id: p.user_id, amount: p.amount }));
  return positives.length || negatives.length ? null : [];
}

function flowerFan(fans) {
  return fans.reduce((n, value) => {
    const text = String(value);
    if (!text.includes('花牌')) return n;
    const match = text.match(/花牌\s*[*×x]\s*(\d+)/);
    return n + (match ? Number(match[1]) : 1);
  }, 0);
}

function combo(action, value) {
  return action === 'cl' ? `s${value - 1}` : action === 'cm' ? `s${value}`
    : action === 'cr' ? `s${value + 1}` : action === 'p' ? `k${value}` : `g${value}`;
}

function expandCombo(value) {
  const n = Number(value.slice(1));
  if (value[0] === 's') return [n - 1, n, n + 1];
  return Array(value[0].toLowerCase() === 'g' ? 4 : 3).fill(n);
}

function reconstructHand(game, key, round, handIndex) {
  const issues = [];
  const issue = (code, message) => issues.push({ game_id: game.game_id, hand_index: handIndex, code, message });
  const ticks = round.action_ticks;
  const { seats, inferred } = resolveSeats(round, game.standard, handIndex, game.count);
  const result = { hand_index: handIndex, round_key: key, completed: false, reconstructed: false,
    score_verified: false, seats, inferred_seats: inferred, issues, events: [], decisions: [],
    exposures: [], score_events: [], score_deltas: Object.fromEntries(game.players.map((p) => [p.user_id, 0])),
    first_cut_tick: null, source_verified: true };
  if (!Array.isArray(ticks) || !ticks.every((t) => Array.isArray(t) && typeof t[0] === 'string')) {
    issue('invalid_ticks', '动作序列格式不完整'); return result;
  }
  if (!seats) { issue('invalid_seats', '无法确定本局原始座位到当前座位的映射'); return result; }
  if (!ticks.some((t) => t[0] === 'end')) { issue('unfinished_hand', '没有小局结束标记，跳过该局统计'); return result; }
  result.completed = true;
  const seatUsers = Array(game.count);
  seats.forEach((s, original) => { seatUsers[s] = game.players[original].user_id; });
  result.seat_users = seatUsers;
  if (!game.standard) { issue('unsupported_rule', '精细动作与机会重建仅支持四人标准国标个人对局'); return result; }
  const validSeat = (s) => Number.isInteger(s) && s >= 0 && s < 4;
  const dealer = integer(round.dealer_index) ?? 0;
  let actor = integer(round.start_player_index) ?? dealer;
  if (!validSeat(dealer) || !validSeat(actor)) { issue('invalid_actor', '起手行动者不合法'); return result; }
  const hands = Array.from({ length: 4 }, (_, s) => Array.isArray(round[`p${s}_tiles`]) ? [...round[`p${s}_tiles`]] : []);
  const melds = Array.from({ length: 4 }, () => []);
  const flowers = Array.from({ length: 4 }, () => []);
  const discards = Array.from({ length: 4 }, () => []);
  const cutCounts = [0, 0, 0, 0];
  const blocked = [false, false, false, false];
  const wall = Array.isArray(round.tiles_list) ? [...round.tiles_list] : [];
  let reconstructable = true;
  let scoringValid = true;
  let lastSource = null;
  let firstCut = true;
  let seenReset = false;
  let tailMode = 'double';
  let terminalSeen = false;
  const invalidate = (code, message) => { if (!issues.some((i) => i.code === code)) issue(code, message); reconstructable = false; };
  if (hands.some((h, s) => h.length !== (s === dealer ? 14 : 13) || h.some((t) => tile(t) === null))) {
    invalidate('invalid_initial_hands', '起手牌数量或牌种不符合标准国标，机会指标不可用');
  }
  const initialCounts = new Map();
  for (const t of [...hands.flat(), ...wall]) initialCounts.set(t, (initialCounts.get(t) || 0) + 1);
  if (!Array.isArray(round.tiles_list) || wall.some((t) => tile(t) === null)
    || TILE_TYPES.some((t) => initialCounts.get(t) !== 4)
    || Array.from({ length: 8 }, (_, i) => 51 + i).some((t) =>
      (initialCounts.get(t) || 0) !== (game.title.use_flowers !== false ? 1 : 0))
    || [...initialCounts].some(([t, n]) => n > (t >= 51 ? 1 : 4))) {
    invalidate('invalid_initial_wall', '起手牌加剩余牌山不能还原标准牌集，机会指标不可用');
  }
  const consume = (s, t) => { if (!removeOne(hands[s], t)) invalidate('missing_hand_tile', '动作使用了当时手牌中不存在的牌'); };
  const publicCounts = () => {
    const counts = {};
    for (const d of discards) for (const t of d) counts[t] = (counts[t] || 0) + 1;
    // Match authoritative check_hepai: public pungs/chows; concealed kongs are not disclosed.
    for (const ms of melds) for (const m of ms) {
      if (m[0] !== 'k' && m[0] !== 's') continue;
      for (const t of expandCombo(m)) counts[t] = (counts[t] || 0) + 1;
    }
    return counts;
  };
  const snapshot = (index, value, kind = 'discard') => ({
    id: `${game.game_id}:${handIndex}:${index}`, game_id: game.game_id, hand_index: handIndex,
    tick_index: index, donor_id: seatUsers[actor], donor_seat: actor, kind,
    actual_tile: value, candidates: [...new Set(hands[actor])].sort((a, b) => a - b),
    hands: hands.map((h) => [...h]), melds: melds.map((m) => [...m]),
    flowers: flowers.map((f) => f.length), public_counts: publicCounts(),
    remaining_tiles: wall.length, current_round: integer(round.current_round) ?? handIndex,
    dealer_index: dealer, cut_counts: [...cutCounts], blocked: [...blocked],
    hepai_limit: game.hepai_limit, tian_di_ren_he: Boolean(game.title.tian_di_ren_he), seat_users: [...seatUsers],
  });

  for (let index = 0; index < ticks.length; index += 1) {
    const tick = ticks[index];
    const code = tick[0];
    if (code === 'end') break;
    if (terminalSeen) {
      invalidate('actions_after_terminal', '正常和牌或流局后仍有非结束动作');
      result.source_verified = false;
    }
    if (code === 'reset') {
      const next = integer(tick[1]);
      if (!validSeat(next)) { invalidate('invalid_actor', 'reset 行动者无效'); result.source_verified = false; }
      else actor = next;
      seenReset = true; continue;
    }
    if (code === 'liuju') { terminalSeen = true; continue; }
    if (['ask_hand', 'ask_other', 'ca'].includes(code)) continue;
    if (['d', 'gd', 'bd'].includes(code)) {
      const explicit = integer(tick[2]);
      if (explicit !== null && validSeat(explicit)) actor = explicit;
      else if (code === 'd') actor = (actor + 1) % 4;
      const value = tile(tick[1]);
      if (value === null) { invalidate('invalid_tile', '摸牌牌种无效'); continue; }
      const offset = code === 'd' ? 0 : (wall.length <= 1 || tailMode === 'single' ? wall.length - 1 : wall.length - 2);
      if (offset < 0 || wall[offset] !== value) invalidate('wall_draw_mismatch', '记录摸牌与剩余牌山顺序不一致');
      if (offset >= 0) wall.splice(offset, 1);
      if (code !== 'd') tailMode = tailMode === 'double' ? 'single' : 'double';
      hands[actor].push(value);
      lastSource = null;
      continue;
    }
    if (code === 'bh') {
      const next = integer(tick[2]);
      if (next !== null && validSeat(next)) actor = next;
      else if (next !== null) { invalidate('invalid_actor', '补花行动者无效'); continue; }
      const value = tile(tick[1]);
      if (value === null || value < 51) { invalidate('invalid_flower', '补花牌种无效'); continue; }
      consume(actor, value); flowers[actor].push(value); continue;
    }
    if (code === 'c') {
      // Old standard records omitted the reset after the opening flower pass.
      if (firstCut && !seenReset) actor = dealer;
      firstCut = false;
      if (result.first_cut_tick === null) result.first_cut_tick = index;
      const value = tile(tick[1]);
      if (value === null) { invalidate('invalid_tile', '弃牌牌种无效'); result.source_verified = false; continue; }
      if (hands[actor].length + melds[actor].length * 3 !== 14) invalidate('invalid_hand_size', '弃牌前手牌结构数量不一致');
      const decision = snapshot(index, value);
      result.decisions.push(decision);
      for (let recipient = 0; recipient < 4; recipient += 1) if (recipient !== actor) {
        result.exposures.push({ donor_id: seatUsers[actor], recipient_id: seatUsers[recipient],
          donor_seat: actor, recipient_seat: recipient, seat_relation: relation(actor, recipient),
          hand_index: handIndex, tick_index: index });
      }
      consume(actor, value); discards[actor].push(value); cutCounts[actor] += 1;
      lastSource = { kind: 'discard', actor, tile: value, tick_index: index, decision_id: decision.id, consumed: false };
      continue;
    }
    if (MELD_CODES.has(code)) {
      const recipient = integer(tick[2]);
      const value = tile(tick[1]);
      if (!validSeat(recipient) || value === null) { invalidate('invalid_claim', '鸣牌行动者或牌种无效'); continue; }
      const source = lastSource;
      const sourceValid = source && source.kind === 'discard' && !source.consumed
        && source.tile === value && source.actor !== recipient
        && (!code.startsWith('c') || (source.actor + 1) % 4 === recipient);
      if (!sourceValid) { invalidate('invalid_claim_source', '鸣牌无法连接到合法的来源弃牌'); result.source_verified = false; }
      const count = code === 'g' ? 3 : 2;
      const expected = code === 'cl' ? [value - 2, value - 1] : code === 'cm' ? [value - 1, value + 1]
        : code === 'cr' ? [value + 1, value + 2] : Array(count).fill(value);
      const explicit = tick.slice(3, 3 + count);
      const taken = explicit.length === count ? explicit : expected;
      if (taken.some((t) => tile(t) === null)
        || [...taken].sort((a, b) => a - b).join(',') !== [...expected].sort((a, b) => a - b).join(',')) {
        invalidate('invalid_claim_tiles', '鸣牌手牌来源与牌型不一致');
      }
      taken.forEach((t) => consume(recipient, t));
      melds[recipient].push(combo(code, value));
      if (sourceValid) {
        removeOne(discards[source.actor], value);
        source.consumed = true;
        result.events.push({ game_id: game.game_id, hand_index: handIndex, tick_index: index,
          source_tick_index: source.tick_index, source_event_id: source.decision_id,
          type: code.startsWith('c') ? 'CHI' : code === 'p' ? 'PENG' : 'GANG', family: 'MELD',
          donor_id: seatUsers[source.actor], recipient_id: seatUsers[recipient],
          donor_seat: source.actor, recipient_seat: recipient, seat_relation: relation(source.actor, recipient),
          tile: value, fan: null, payments: [], source_kind: 'discard', reconstruction_status: 'recorded',
          description: `第 ${handIndex} 副第 ${index} 个 tick：${seatUsers[source.actor]} 的弃牌 ${value} 被 ${seatUsers[recipient]} ${code.startsWith('c') ? '吃' : code === 'p' ? '碰' : '明杠'}。`,
        });
      }
      actor = recipient;
      continue;
    }
    if (code === 'ag' || code === 'jg') {
      const value = tile(tick[1]);
      if (value === null) { invalidate('invalid_kong', '杠牌牌种无效'); continue; }
      if (code === 'ag') {
        for (let n = 0; n < 4; n += 1) consume(actor, value);
        melds[actor].push(`G${value}`);
        lastSource = null;
      } else {
        const s = snapshot(index, value, 'added_kong');
        result.decisions.push(s);
        consume(actor, value);
        const existing = melds[actor].indexOf(`k${value}`);
        if (existing < 0) invalidate('missing_original_pung', '加杠没有对应的原碰');
        else melds[actor][existing] = `g${value}`;
        lastSource = { kind: 'added_kong', actor, tile: value, tick_index: index, decision_id: s.id, consumed: false };
      }
      continue;
    }
    if (HU_CODES.has(code)) {
      const winner = integer(tick[1]);
      if (!validSeat(winner)) { invalidate('invalid_winner', '和牌行动者无效'); scoringValid = false; continue; }
      const fans = Array.isArray(tick[3]) ? tick[3].map(String) : [];
      const wrong = fans.some((f) => /错和|诈和/.test(f));
      const fan = typeof tick[2] === 'number' && Number.isFinite(tick[2]) ? tick[2] : null;
      const changes = parseChanges(tick[4], 4);
      let payments = null;
      if (changes) {
        for (let s = 0; s < 4; s += 1) result.score_deltas[seatUsers[s]] += changes[s];
        payments = paymentsFromChanges(changes, seatUsers);
        result.score_events.push({ game_id: game.game_id, hand_index: handIndex, tick_index: index,
          type: wrong ? 'WRONG_WIN' : code === 'hu_self' ? 'TSUMO' : 'RON',
          winner_id: seatUsers[winner], changes: Object.fromEntries(changes.map((v, s) => [seatUsers[s], v])), payments });
      } else { scoringValid = false; issue('invalid_score_changes', '和牌分变缺失或不守恒'); }
      if (wrong) { blocked[winner] = true; continue; }
      terminalSeen = true;
      if (code === 'hu_self') continue;
      const donor = (winner - WIN_DISTANCE[code] + 4) % 4;
      if (!lastSource || lastSource.actor !== donor || lastSource.consumed) {
        issue('invalid_ron_source', '点和来源与此前弃牌/杠牌窗口不一致'); result.source_verified = false; continue;
      }
      const explicitTile = tile(tick[5]);
      if (explicitTile !== null && explicitTile !== lastSource.tile) {
        issue('invalid_ron_tile', '和牌张与来源动作不一致'); continue;
      }
      if (fan === null || fan - flowerFan(fans) < game.hepai_limit) {
        issue('unqualified_recorded_win', '记录和牌未达到非花牌起和门槛，不计合法递送'); continue;
      }
      const special = lastSource.kind !== 'discard';
      if (changes) {
        const expected = Array.from({ length: 4 }, (_, s) => s === winner ? fan + 24 : -(8 + (s === donor ? fan : 0)));
        if (expected.some((v, s) => Math.abs(v - changes[s]) > 1e-8)) {
          scoringValid = false;
          issue('unexpected_mcr_settlement', '实际点和付款与标准国标公式不一致，保留原始分变但标记待核对');
        }
      }
      result.events.push({ game_id: game.game_id, hand_index: handIndex, tick_index: index,
        source_tick_index: lastSource.tick_index, source_event_id: lastSource.decision_id,
        type: special ? 'ROB_KONG' : 'RON', family: special ? 'SPECIAL' : 'RON',
        donor_id: seatUsers[donor], recipient_id: seatUsers[winner], donor_seat: donor, recipient_seat: winner,
        seat_relation: relation(donor, winner), tile: lastSource.tile, fan, qualifying_fan: fan - flowerFan(fans),
        payments, score_changes: changes ? Object.fromEntries(changes.map((v, s) => [seatUsers[s], v])) : null,
        source_kind: lastSource.kind, reconstruction_status: 'recorded',
        description: `第 ${handIndex} 副第 ${index} 个 tick：${seatUsers[donor]} ${special ? '加杠' : '弃牌'} ${lastSource.tile}，${seatUsers[winner]} ${special ? '抢杠和' : '点和'} ${fan} 番。`,
      });
      continue;
    }
    invalidate('unknown_action', `不支持动作 ${String(code).slice(0, 40)}，机会重建停止`);
  }
  if (!terminalSeen) {
    invalidate('missing_terminal_result', '小局缺少正常和牌或流局结果，不能把缺失结算当零分');
    scoringValid = false;
    result.source_verified = false;
  }
  result.reconstructed = reconstructable;
  result.score_verified = scoringValid;
  if (!reconstructable) result.decisions = [];
  else for (const event of result.events) event.reconstruction_status = 'reconstructed';
  return result;
}

function replayRecord(row) {
  const game = parseRecord(row);
  if (game.error) return game;
  const entries = Object.entries(game.record.game_round).sort((a, b) =>
    (integer(a[1]?.round_index) ?? Number(a[0].match(/\d+$/)?.[0]) ?? 0)
    - (integer(b[1]?.round_index) ?? Number(b[0].match(/\d+$/)?.[0]) ?? 0));
  const hands = entries.map(([key, round], index) => reconstructHand(game, key,
    round && typeof round === 'object' ? round : {}, integer(round?.round_index) ?? index + 1));
  const scoreDeltas = Object.fromEntries(game.players.map((p) => [p.user_id, 0]));
  for (const hand of hands) for (const [uid, score] of Object.entries(hand.score_deltas)) scoreDeltas[uid] += score;
  game.hands = hands;
  game.issues = hands.flatMap((h) => h.issues);
  game.score_deltas = scoreDeltas;
  game.scores_available = game.standard && hands.some((h) => h.completed) && hands.every((h) => h.completed && h.score_verified);
  // Metadata is a useful cross-check, never an excuse to silently repair mismatched replay scores.
  if (game.scores_available) for (const p of game.metadata_players) {
    const uid = String(p.user_id);
    if (scoreDeltas[uid] !== undefined && p.score != null && Number.isFinite(Number(p.score))
      && Math.abs(Number(p.score) - scoreDeltas[uid]) > 1e-8) {
      game.issues.push({ game_id: game.game_id, code: 'metadata_score_mismatch', message: '牌谱累计分变与逐玩家结算不一致' });
      game.scores_available = false;
    }
  }
  return game;
}

module.exports = { parseRecord, replayRecord, reconstructHand, inferGuobiaoSeats, resolveSeats,
  relation, integer, tile, hash, stableStringify, paymentsFromChanges, TILE_TYPES };
