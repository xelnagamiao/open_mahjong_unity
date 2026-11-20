'use strict';

// Synthetic fixtures only. These are not observations about actual accounts.
const { TILE_TYPES } = require('./replay');

const USERS = [101, 102, 103, 104];
function title() {
  return { rule: 'guobiao', sub_rule: 'guobiao/standard', room_type: 'match', player_count: 4,
    hepai_limit: 8, match_tier: 'intermediate', max_round: 1, open_cuohe: true,
    ...Object.fromEntries(USERS.flatMap((id, i) => [[`p${i}_uid`, id], [`p${i}_name`, `模拟玩家${id}`]])) };
}

function simpleRecord(id, donorOriginal = 0, family = 'RON', seats = [0, 1, 2, 3], date = '2026-11-10T10:00:00Z') {
  const donor = seats[donorOriginal];
  const recipient = seats[1];
  const distance = (recipient - donor + 4) % 4;
  const changes = Array.from({ length: 4 }, (_, s) => s === recipient ? 32 : s === donor ? -16 : -8);
  const ticks = [['reset', donor], ['c', 17, 'F']];
  if (family === 'RON') ticks.push([`hu_${['self', 'first', 'second', 'third'][distance]}`, recipient, 8, ['三色三同顺'], changes, 17]);
  else ticks.push(['p', 17, recipient, 17, 17], ['c', 19, 'F'], ['liuju']);
  ticks.push(['end']);
  return { game_id: id, created_at: date, match_type: 'quanzhuang', record: {
    game_title: title(), game_round: { round_index_1: {
      round_index: 1, current_round: 1, dealer_index: 0, start_player_index: donor, seats, action_ticks: ticks,
    } },
  } };
}

function concentrationFixture() {
  const rows = [];
  for (const [donor, n] of [[0, 33], [2, 51], [3, 51]]) for (let i = 0; i < n; i += 1) {
    rows.push(simpleRecord(`sim-meld-${donor}-${i}`, donor, 'MELD', undefined,
      `2026-11-${String(1 + (i % 10)).padStart(2, '0')}T10:00:00Z`));
  }
  for (const [donor, n] of [[0, 12], [2, 2], [3, 1]]) for (let i = 0; i < n; i += 1) {
    rows.push(simpleRecord(`sim-ron-${donor}-${i}`, donor, 'RON', undefined,
      `2026-11-${String(1 + (i % 10)).padStart(2, '0')}T12:00:00Z`));
  }
  return rows;
}

function physicalDrawRecord({ id = 'physical-draw', recipientHand, firstDiscard = 46 } = {}) {
  const recipient = recipientHand || [11, 12, 13, 24, 25, 26, 35, 36, 37, 45, 45, 45, 46];
  const deck = TILE_TYPES.flatMap((t) => Array(4).fill(t));
  const take = (t) => {
    const i = deck.indexOf(t);
    if (i < 0) throw new Error(`overfull fixture tile ${t}`);
    return deck.splice(i, 1)[0];
  };
  const hands = [[], recipient.map(take), [], []];
  hands[0].push(take(firstDiscard));
  while (hands[0].length < 14) hands[0].push(deck.shift());
  for (const seat of [2, 3]) while (hands[seat].length < 13) hands[seat].push(deck.shift());
  const wall = [...deck];
  const ticks = [['reset', 0], ['c', firstDiscard, 'F']];
  // Replay the entire remaining wall by normal draws and immediate discards.
  for (const value of wall) ticks.push(['d', value], ['c', value, 'T']);
  ticks.push(['liuju'], ['end']);
  return { game_id: id, created_at: '2026-11-10T12:00:00Z', match_type: 'quanzhuang', record: {
    game_title: { ...title(), use_flowers: false },
    game_round: { round_index_1: {
      round_index: 1, current_round: 1, dealer_index: 0, start_player_index: 0, seats: [0, 1, 2, 3],
      ...Object.fromEntries(hands.map((hand, seat) => [`p${seat}_tiles`, hand])), tiles_list: wall, action_ticks: ticks,
    } },
  } };
}

function physicalWinningRecord(id = 'physical-win') {
  const row = physicalDrawRecord({ id, recipientHand: [11, 11, 12, 12, 21, 21, 22, 22, 31, 31, 32, 32, 33], firstDiscard: 33 });
  row.record.game_round.round_index_1.action_ticks = [['reset', 0], ['c', 33, 'F'],
    ['hu_first', 1, 48, ['七对', '全小'], [-56, 72, -8, -8], 33], ['end']];
  return row;
}

module.exports = { simpleRecord, concentrationFixture, physicalDrawRecord, physicalWinningRecord, title, USERS };
