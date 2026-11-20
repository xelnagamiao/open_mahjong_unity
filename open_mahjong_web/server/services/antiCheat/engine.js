'use strict';

const { replayRecord, parseRecord, hash, relation } = require('./replay');
const { evaluateDecisions } = require('./rules');
const { parseTimestamp } = require('./time');

const ENGINE_VERSION = 'replay-anti-cheat-v1';
const CHANNELS = ['ALL', 'MELD', 'RON'];
const DAY_MS = 86400000;

async function* orderedMap(source, concurrency, mapper) {
  const pending = [];
  for await (const input of source) {
    // Attach a rejection handler immediately, including to jobs waiting their turn.
    pending.push(Promise.resolve().then(() => mapper(input)).then(value => ({ value }), error => ({ error })));
    if (pending.length >= concurrency) {
      const next = await pending.shift();
      if (next.error) throw next.error;
      yield next.value;
    }
  }
  for (const job of pending) {
    const next = await job;
    if (next.error) throw next.error;
    yield next.value;
  }
}
const DEFAULT_CHANNELS = {
  ALL: { enabled: true, min_events: 10, min_source_share: 0.6, min_lift: 1.8 },
  MELD: { enabled: true, min_events: 10, min_source_share: 0.6, min_lift: 1.8 },
  RON: { enabled: true, min_events: 4, min_source_share: 0.65, min_lift: 1.8 },
};

function ratio(numerator, denominator, reason = 'no_observed_opportunity') {
  return { numerator, denominator, value: denominator > 0 ? numerator / denominator : null,
    ...(denominator > 0 ? {} : { reason }) };
}
function unavailable(reason) { return { numerator: null, denominator: null, value: null, status: 'unavailable', reason }; }
function mean(values) { return values.length ? values.reduce((a, b) => a + b, 0) / values.length : null; }
function median(values) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}
function stddev(values) {
  if (values.length < 2) return null;
  const m = mean(values);
  return Math.sqrt(values.reduce((n, v) => n + (v - m) ** 2, 0) / (values.length - 1));
}
function moments() { return { count: 0, sum: 0, sum_squares: 0 }; }
function addMoment(stats, value) { stats.count += 1; stats.sum += value; stats.sum_squares += value * value; }
function subtractMoments(total, part) {
  return { count: total.count - part.count, sum: total.sum - part.sum,
    sum_squares: Math.max(0, total.sum_squares - part.sum_squares) };
}
function momentMean(stats) { return stats.count ? stats.sum / stats.count : null; }
function momentStddev(stats) {
  return stats.count > 1 ? Math.sqrt(Math.max(0, (stats.sum_squares - stats.sum ** 2 / stats.count) / (stats.count - 1))) : null;
}
function evidenceOrder(a, b) {
  return (b.family === 'RON') - (a.family === 'RON') || (b.fan || 0) - (a.fan || 0)
    || a.game_id.localeCompare(b.game_id) || a.hand_index - b.hand_index || a.tick_index - b.tick_index;
}
function numericSetting(value, fallback, min = 0) {
  const n = Number(value);
  return value != null && Number.isFinite(n) && n >= min ? n : fallback;
}

function settingsFrom(options = {}) {
  const settings = options.settings || options;
  const asOf = settings.as_of ? new Date(settings.as_of) : new Date();
  if (!Number.isFinite(asOf.getTime())) throw new Error('invalid_as_of');
  const windows = [...new Set((settings.windows_days || [7, 30, 90]).map(Number)
    .filter((n) => Number.isInteger(n) && n > 0 && n <= 3660))].sort((a, b) => a - b);
  if (!windows.length) throw new Error('invalid_windows_days');
  const channels = {};
  for (const name of CHANNELS) channels[name] = { ...DEFAULT_CHANNELS[name], ...(settings.channels?.[name] || {}) };
  return { ...settings, as_of: asOf.toISOString(), as_of_ms: asOf.getTime(), windows_days: windows,
    rules: Array.isArray(settings.rules) ? settings.rules : ['guobiao'], channels,
    min_shared_games: numericSetting(settings.min_shared_games, 4, 1),
    min_shared_days: numericSetting(settings.min_shared_days, 2, 1),
    min_reference_pairs: numericSetting(settings.min_reference_pairs, 10, 1),
    max_evidence_per_pair: Math.min(500, numericSetting(settings.max_evidence_per_pair, 50, 1)) };
}

function timestamp(value, timezone = null) {
  const iso = parseTimestamp(value, timezone);
  return iso === null ? null : Date.parse(iso);
}
function chinaDate(time) { return new Date(time + 8 * 3600000).toISOString().slice(0, 10); }
function channelFor(event, channel) { return channel === 'ALL' ? ['MELD', 'RON'].includes(event.family) : event.family === channel; }
function blankChannel() {
  return { events: 0, received: 0, discards: 0, eligible_decisions: 0, opportunities: 0,
    realized_opportunities: 0, choices: 0, choice_hits: 0, priority_censored: 0,
    subtypes: { CHI: 0, PENG: 0, GANG: 0, RON: 0 }, matched_decisions: 0 };
}
function playerKey(ruleKey, uid) { return `${ruleKey}\u0000${uid}`; }
function directedKey(ruleKey, donor, recipient) { return `${ruleKey}\u0000${donor}\u0000${recipient}`; }

function eventPriority(event) {
  if (event.family === 'RON') return 6 - ((event.recipient_seat - event.donor_seat + 4) % 4);
  return event.type === 'CHI' ? 1 : 2;
}
function channelPriority(channel, actual, recipientSeat, donorSeat) {
  if ((channel === 'ALL' || channel === 'RON') && actual.RON) return 6 - ((recipientSeat - donorSeat + 4) % 4);
  if (channel !== 'RON') return actual.PENG || actual.GANG ? 2 : actual.CHI ? 1 : 0;
  return 0;
}

function verifyOpportunities(games, calculation) {
  let verified = 0;
  for (const game of games) for (const hand of game.hands) {
    hand.opportunity_valid = hand.reconstructed;
    if (!hand.reconstructed) continue;
    for (const event of hand.events) {
      if (event.family === 'SPECIAL') continue;
      const result = calculation.results.get(event.source_event_id);
      const response = result?.recipients?.find((r) => r.recipient_id === event.recipient_id);
      const helpful = response?.helpful?.[event.type];
      if (Array.isArray(helpful) && !helpful.includes(event.tile)) {
        hand.opportunity_valid = false;
        game.issues.push({ game_id: game.game_id, hand_index: hand.hand_index,
          code: 'recorded_action_legality_mismatch', message: '实际记录的鸣牌/和牌与完整合法机会校验不一致，本副机会统计已隔离' });
        break;
      }
    }
    if (hand.opportunity_valid) verified += 1;
  }
  return verified;
}

function newPair(game, donor, recipient, windowDays) {
  return {
    pair_key: `${windowDays}:${hash([game.rule_key, donor.user_id, recipient.user_id]).slice(0, 24)}`,
    donor_id: donor.user_id, recipient_id: recipient.user_id, donor_name: donor.name, recipient_name: recipient.name,
    rule_key: game.rule_key, rule: game.rule, sub_rule: game.sub_rule, room_type: game.room_type,
    match_type: game.match_type, match_tier: game.match_tier, window_days: windowDays,
    shared_games: 0, shared_hands: 0, shared_days: 0, competitive: game.competitive,
    delivery_supported: game.standard, source_verified_hands: 0,
    channels: Object.fromEntries(CHANNELS.map((c) => [c, blankChannel()])),
    special_events: 0, seat_exposure: { upstream: 0, opposite: 0, downstream: 0 },
    actual_by_seat: { upstream: { ALL: 0, MELD: 0, RON: 0 }, opposite: { ALL: 0, MELD: 0, RON: 0 }, downstream: { ALL: 0, MELD: 0, RON: 0 } },
    _days: new Set(), _donor_scores: moments(), _recipient_scores: moments(), _evidence: [], _evidence_count: 0,
    _ron: { count: 0, direct: 0, gains: 0, max_gain: 0, unknown_payments: 0, fans: moments() },
  };
}

function addDecision(pair, decision, response, events) {
  const actual = Object.fromEntries(Object.entries(response.helpful || {})
    .map(([kind, choices]) => [kind, Array.isArray(choices) && choices.includes(decision.actual_tile)]));
  const ownEvents = events.filter((event) => event.recipient_id === pair.recipient_id);
  const otherEvents = events.filter((event) => event.recipient_id !== pair.recipient_id);
  for (const channel of CHANNELS) {
    const helpful = response.helpful?.[channel];
    if (!Array.isArray(helpful)) continue;
    const c = pair.channels[channel];
    c.eligible_decisions += 1;
    if (helpful.includes(decision.actual_tile)) {
      c.opportunities += 1;
      if (ownEvents.some((event) => channelFor(event, channel))) c.realized_opportunities += 1;
      else if (otherEvents.some((e) => eventPriority(e)
        > channelPriority(channel, actual, response.recipient_seat, response.donor_seat))) c.priority_censored += 1;
    }
    if (helpful.length > 0 && helpful.length < decision.candidates.length) {
      c.choices += 1;
      if (helpful.includes(decision.actual_tile)) c.choice_hits += 1;
    }
  }
  for (const event of ownEvents) {
    event.legal_choices = decision.candidates;
    event.helpful_choices = response.helpful[event.family] || [];
    if (event.family === 'RON') {
      event.calculated_fan = response.fan_values?.[String(event.tile)]?.fan ?? null;
      event.qualifying_fan = response.fan_values?.[String(event.tile)]?.qualifying_fan ?? event.qualifying_fan;
    }
  }
}

function createWindowState(windowDays) {
  return { windowDays, playerTotals: new Map(), pairMap: new Map(), selected_games: 0 };
}

function accumulateGame(state, game, calculation, settings) {
    const { windowDays, playerTotals, pairMap } = state;
    const start = settings.as_of_ms - windowDays * DAY_MS;
    if (game.time_ms === null || game.time_ms < start || game.time_ms > settings.as_of_ms) return;
    const completedHands = game.hands.filter((h) => h.completed && h.seats);
    if (!completedHands.length) return;
    state.selected_games += 1;
    for (const player of game.players) {
      const key = playerKey(game.rule_key, player.user_id);
      if (!playerTotals.has(key)) playerTotals.set(key, { user_id: player.user_id, name: player.name,
        rule_key: game.rule_key, games: 0, hands: 0, scores: moments() });
      const totals = playerTotals.get(key);
      totals.games += 1;
      totals.hands += completedHands.length;
      if (game.scores_available) addMoment(totals.scores, game.score_deltas[player.user_id]);
    }
    for (const donor of game.players) for (const recipient of game.players) {
      if (donor.user_id === recipient.user_id) continue;
      const key = directedKey(game.rule_key, donor.user_id, recipient.user_id);
      if (!pairMap.has(key)) pairMap.set(key, newPair(game, donor, recipient, windowDays));
      const pair = pairMap.get(key);
      pair.shared_games += 1;
      pair._days.add(chinaDate(game.time_ms));
      if (game.scores_available) {
        addMoment(pair._donor_scores, game.score_deltas[donor.user_id]);
        addMoment(pair._recipient_scores, game.score_deltas[recipient.user_id]);
      }
      for (const hand of completedHands) {
        pair.shared_hands += 1;
        const donorSeat = hand.seats[donor.original_index];
        const recipientSeat = hand.seats[recipient.original_index];
        pair.seat_exposure[relation(donorSeat, recipientSeat, game.count)] += 1;
        // Incomplete source attribution changes source denominators. Exclude the entire hand from that family.
        if (!hand.source_verified) continue;
        if (game.standard) pair.source_verified_hands += 1;
        const received = hand.events.filter((e) => e.recipient_id === recipient.user_id);
        const own = received.filter((e) => e.donor_id === donor.user_id);
        const exposure = hand.exposures.filter((e) => e.donor_id === donor.user_id && e.recipient_id === recipient.user_id).length;
        for (const channel of CHANNELS) {
          const c = pair.channels[channel];
          const events = own.filter((e) => channelFor(e, channel));
          c.events += events.length;
          c.received += received.filter((e) => channelFor(e, channel)).length;
          c.discards += exposure;
          for (const event of events) {
            c.subtypes[event.type] = (c.subtypes[event.type] || 0) + 1;
            pair.actual_by_seat[event.seat_relation][channel] += 1;
          }
        }
        pair.special_events += own.filter((e) => e.family === 'SPECIAL').length;
        pair._evidence.push(...own);
        pair._evidence_count += own.length;
        pair._evidence.sort(evidenceOrder);
        pair._evidence.length = Math.min(pair._evidence.length, settings.max_evidence_per_pair);
        for (const event of own.filter((e) => e.family === 'RON')) {
          pair._ron.count += 1;
          if (Number.isFinite(event.fan)) addMoment(pair._ron.fans, event.fan);
          if (!Array.isArray(event.payments) || !event.score_changes) { pair._ron.unknown_payments += 1; continue; }
          pair._ron.direct += event.payments.filter((p) => p.payer_id === pair.donor_id && p.payee_id === pair.recipient_id)
            .reduce((sum, p) => sum + p.amount, 0);
          const gain = event.score_changes[pair.recipient_id];
          pair._ron.gains += gain;
          pair._ron.max_gain = Math.max(pair._ron.max_gain, gain);
        }
        if (hand.opportunity_valid) for (const decision of hand.decisions) {
          if (decision.donor_id !== donor.user_id || decision.kind !== 'discard') continue;
          const result = calculation.results.get(decision.id);
          const response = result?.recipients?.find((r) => r.recipient_id === recipient.user_id);
          if (!response) continue;
          addDecision(pair, decision, response, hand.events.filter((e) => e.source_event_id === decision.id));
        }
      }
    }
}

function finishWindow(state, settings) {
  const { windowDays, pairMap, playerTotals } = state;
  const pairs = [...pairMap.values()];
  for (const pair of pairs) {
    pair.shared_days = pair._days.size;
    const donorTotals = playerTotals.get(playerKey(pair.rule_key, pair.donor_id));
    const recipientTotals = playerTotals.get(playerKey(pair.rule_key, pair.recipient_id));
    pair.donor_coplay_rate = ratio(pair.shared_games, donorTotals.games);
    pair.recipient_coplay_rate = ratio(pair.shared_games, recipientTotals.games);
    pair.coplay_jaccard = ratio(pair.shared_games, donorTotals.games + recipientTotals.games - pair.shared_games);
    pair.source_coverage = ratio(pair.source_verified_hands, pair.shared_hands);
    const donorWithout = subtractMoments(donorTotals.scores, pair._donor_scores);
    const recipientWithout = subtractMoments(recipientTotals.scores, pair._recipient_scores);
    const direct = pair._ron.direct;
    const donorMean = momentMean(pair._donor_scores);
    const recipientMean = momentMean(pair._recipient_scores);
    const donorOther = momentMean(donorWithout);
    const recipientOther = momentMean(recipientWithout);
    const sumGain = pair._ron.gains;
    const allPaymentsKnown = pair.delivery_supported && pair.source_verified_hands > 0 && pair._ron.unknown_payments === 0;
    pair.score = {
      unit: 'recorded_mcr_points_per_game', comparison_status: 'descriptive_only',
      donor_mean_with: donorMean, donor_mean_without: donorOther, donor_games_without: donorWithout.count,
      donor_difference: donorMean !== null && donorOther !== null ? donorMean - donorOther : null,
      recipient_mean_with: recipientMean, recipient_mean_without: recipientOther, recipient_games_without: recipientWithout.count,
      recipient_difference: recipientMean !== null && recipientOther !== null ? recipientMean - recipientOther : null,
      scored_shared_games: pair._recipient_scores.count,
      combined_mean_with: donorMean !== null && recipientMean !== null ? donorMean + recipientMean : null,
      recipient_without_stddev: momentStddev(recipientWithout),
      direct_paid_to_recipient: allPaymentsKnown ? direct : null,
      recipient_ron_gain: allPaymentsKnown ? sumGain : null,
      others_paid_on_ron: allPaymentsKnown ? sumGain - direct : null,
      ron_fan_mean: momentMean(pair._ron.fans),
      largest_ron_share: sumGain > 0 ? pair._ron.max_gain / sumGain : null,
      adjusted_difference: { value: null, status: 'unavailable',
        reason: '未建立经校准的赛前实力与同类对手模型；当前差异是同规则池的原始描述，不是因果提升' },
      comparison_reason: recipientWithout.count ? '同规则池内与该玩家同桌/不同桌的实际整场牌分' : '该窗口没有该玩家不在场的可比较比赛',
    };
    pair.evidence = pair._evidence;
    pair.evidence_count = pair._evidence_count;
    pair.evidence_truncated = pair._evidence_count > pair.evidence.length;
    pair.seat_bucket = Math.floor((pair.seat_exposure.upstream / Math.max(1, pair.shared_hands)) * 4);
  }

  // Keep raw reference counts immutable while the public channel objects are finalized.
  // Pre-filter sparse pairs once per channel. With thousands of one-off encounters,
  // most pairs never belong to any reference set; do not scan them for every target.
  const referenceGroups = new Map();
  for (const p of pairs) {
    if (p.shared_games < settings.min_shared_games || !p.delivery_supported || p.source_verified_hands === 0) continue;
    for (const channel of CHANNELS) {
      const c = p.channels[channel];
      if (c.received < numericSetting(settings.channels[channel].min_events, DEFAULT_CHANNELS[channel].min_events)) continue;
      const key = `${p.rule_key}\u0000${p.seat_bucket}\u0000${channel}`;
      if (!referenceGroups.has(key)) referenceGroups.set(key, []);
      referenceGroups.get(key).push({ donor_id: p.donor_id, recipient_id: p.recipient_id,
        dyad: [p.donor_id, p.recipient_id].sort().join(':'), share: c.events / c.received });
    }
  }
  const referenceCache = new Map();
  const findings = [];
  for (const pair of pairs) {
    for (const channel of CHANNELS) {
      const counts = pair.channels[channel];
      const config = settings.channels[channel];
      const share = ratio(counts.events, counts.received, 'no_recipient_events_in_shared_hands');
      const referenceKey = `${pair.rule_key}\u0000${pair.seat_bucket}\u0000${channel}`;
      const cacheKey = `${referenceKey}\u0000${[pair.donor_id, pair.recipient_id].sort().join(':')}`;
      let referenceResult = referenceCache.get(cacheKey);
      if (!referenceResult) {
        const reference = (referenceGroups.get(referenceKey) || []).filter((p) =>
          p.donor_id !== pair.donor_id && p.donor_id !== pair.recipient_id
          && p.recipient_id !== pair.donor_id && p.recipient_id !== pair.recipient_id);
        const dyads = new Set(reference.map((p) => p.dyad)).size;
        referenceResult = { count: dyads,
          baseline: dyads >= settings.min_reference_pairs ? median(reference.map((p) => p.share)) : null };
        referenceCache.set(cacheKey, referenceResult);
      }
      const referenceDyads = referenceResult.count;
      const enoughReference = referenceDyads >= settings.min_reference_pairs;
      const baseline = referenceResult.baseline;
      const lift = baseline > 0 && share.value !== null ? share.value / baseline : null;
      const reasons = [];
      if (!config.enabled) reasons.push('该通道已关闭');
      if (!pair.competitive) reasons.push('非标准国标个人排位，仅显示描述统计');
      if (Number(pair.donor_id) <= 10 || Number(pair.recipient_id) <= 10) reasons.push('包含机器人账号，仅显示描述统计');
      if (pair.shared_games < settings.min_shared_games || pair.shared_days < settings.min_shared_days) reasons.push('共同场数或独立日期尚不足观察条件');
      if (counts.events < numericSetting(config.min_events, DEFAULT_CHANNELS[channel].min_events)) reasons.push('实际事件数不足');
      if (share.value === null || share.value < numericSetting(config.min_source_share, DEFAULT_CHANNELS[channel].min_source_share)) reasons.push('来源集中度未达观察条件');
      const baseEligible = reasons.length === 0;
      if (!enoughReference) reasons.push('相同规则及座次暴露层参考不足，仅作观察，不使用三分之一基准');
      else if (baseline === 0) reasons.push('经验参考中位数为零，不能输出无限倍异常');
      else if (lift < numericSetting(config.min_lift, DEFAULT_CHANNELS[channel].min_lift)) reasons.push('相对经验参考的提升未达观察条件');
      const flag = baseEligible && (!enoughReference || baseline === 0
        || lift >= numericSetting(config.min_lift, DEFAULT_CHANNELS[channel].min_lift));
      const status = flag && enoughReference && baseline > 0 ? 'review' : 'observe';
      if (flag) reasons.push(status === 'review' ? '进入牌谱复核队列；不是作弊认定' : '来源集中且缺乏充分参考，保留观察线索');
      const hasOpportunities = counts.eligible_decisions > 0;
      pair.channels[channel] = {
        events: counts.events,
        source_share: share,
        discard_rate: ratio(counts.events, counts.discards, 'no_verified_discard_exposure'),
        opportunity_creation_rate: hasOpportunities ? ratio(counts.opportunities, counts.eligible_decisions)
          : unavailable('没有完整重建且通过对应合法性校验的决策'),
        opportunity_realization_rate: hasOpportunities ? ratio(counts.realized_opportunities, counts.opportunities)
          : unavailable('没有完整重建且通过对应合法性校验的机会'),
        choice_hit_rate: hasOpportunities ? ratio(counts.choice_hits, counts.choices, 'no_mixed_helpful_and_nonhelpful_choices')
          : unavailable('无法恢复全部合法备选和足番条件'),
        opportunity_coverage: ratio(counts.eligible_decisions, counts.discards),
        priority_censored_opportunities: counts.priority_censored,
        subtypes: counts.subtypes,
        baseline_share: baseline, lift, reference_pairs: referenceDyads,
        baseline_method: '同规则池、上家暴露比例分层，排除待查两人后的经验来源占比中位数；未校准为作弊概率',
        confidence_interval: { value: null, status: 'unavailable', reason: '尚未验证分块置信区间与多重比较方法，不输出虚假精确区间' },
        flag, reason: reasons.join('；'), reasons, evidence_count: counts.events,
      };
      if (!pair.delivery_supported || pair.source_verified_hands === 0) {
        const reason = !pair.delivery_supported ? '当前玩法未实现可靠的递送规则适配，只保留同桌统计'
          : '没有通过来源动作完整性校验的小局，不能把未知写成零次递送';
        Object.assign(pair.channels[channel], {
          events: null, status: 'unavailable', source_share: unavailable(reason), discard_rate: unavailable(reason),
          opportunity_creation_rate: unavailable(reason), opportunity_realization_rate: unavailable(reason),
          choice_hit_rate: unavailable(reason), baseline_share: null, lift: null, flag: false, reason, reasons: [reason],
        });
      }
      if (flag) findings.push({ id: hash([ENGINE_VERSION, pair.pair_key, channel]).slice(0, 28),
        pair_key: pair.pair_key, channel, rule_key: pair.rule_key, window_days: windowDays,
        donor_id: pair.donor_id, recipient_id: pair.recipient_id,
        donor_name: pair.donor_name, recipient_name: pair.recipient_name,
        shared_games: pair.shared_games, events: counts.events, source_share: share,
        baseline_share: baseline, lift, status, reasons, evidence_count: counts.events });
    }
    pair.flag_all = pair.channels.ALL.flag;
    pair.flag_meld = pair.channels.MELD.flag;
    pair.flag_ron = pair.channels.RON.flag;
    for (const key of Object.keys(pair)) if (key.startsWith('_')) delete pair[key];
  }
  return { pairs, findings, playerTotals, selected_games: state.selected_games };
}

function buildWindow(games, calculation, settings, windowDays) {
  const state = createWindowState(windowDays);
  for (const game of games) accumulateGame(state, game, calculation, settings);
  return finishWindow(state, settings);
}

async function analyzeRecords(rows, options = {}) {
  if (!Array.isArray(rows) && typeof rows !== 'function') throw new TypeError('records_must_be_an_array_or_reopenable_source');
  const source = typeof rows === 'function' ? rows : () => rows;
  let inputCount = 0;
  const settings = settingsFrom(options);
  const unique = new Map();
  const conflicts = new Set();
  const metadataBlocked = new Set();
  const quality = { duplicate_records: 0, conflicting_records: 0, invalid_records: 0,
    unsupported_records: 0, missing_timestamp_records: 0, out_of_window_records: 0,
    completed_hands: 0, reconstructed_hands: 0, opportunity_verified_hands: 0, scored_hands: 0,
    issues: [], calculator_errors: [] };
  for await (const row of source()) {
    inputCount += 1;
    if (inputCount % 100 === 0 && options.onProgress) await options.onProgress({ stage: 'loading', processed: inputCount, total: options.record_count ?? null, message: '正在索引快照，原始牌谱逐场释放' });
    if (row?.metadata_conflict && row.game_id != null) metadataBlocked.add(String(row.game_id));
    const parsed = parseRecord(row);
    if (parsed.error) {
      quality.invalid_records += 1;
      quality.issues.push({ game_id: String(row?.game_id ?? ''), code: parsed.error, message: '输入牌谱格式或身份无效' });
      continue;
    }
    if (!settings.rules.includes(parsed.rule)) { quality.unsupported_records += 1; continue; }
    const old = unique.get(parsed.game_id);
    if (old) {
      if (old === parsed.content_hash) quality.duplicate_records += 1;
      else {
        conflicts.add(parsed.game_id);
        quality.conflicting_records += 1;
        quality.issues.push({ game_id: parsed.game_id, code: 'conflicting_duplicate', message: '同 game_id 有不同内容且缺乏可靠版本顺序，整场隔离' });
      }
    } else unique.set(parsed.game_id, parsed.content_hash);
  }
  const gameSummaries = [];
  const windowStates = settings.windows_days.map(createWindowState);
  const calculatorErrors = new Set();
  let pythonFailed = false;
  let processed = 0;
  const progress = typeof options.onProgress === 'function' ? options.onProgress : null;
  const oldest = settings.as_of_ms - Math.max(...settings.windows_days) * DAY_MS;
  const eligibleUniqueCount = [...unique.keys()].filter((id) => !conflicts.has(id) && !metadataBlocked.has(id)).length;
  const visited = new Set();
  async function* eligibleGames() {
   for await (const row of source()) {
    const parsed = parseRecord(row);
    const id = parsed.game_id;
    if (parsed.error || !unique.has(id) || visited.has(id) || conflicts.has(id) || metadataBlocked.has(id)) continue;
    visited.add(id);
    const input = { row, parsed };
    const time = timestamp(input.parsed.created_at, input.parsed.created_at_timezone);
    if (time === null) {
      quality.missing_timestamp_records += 1;
      quality.issues.push({ game_id: id, code: 'unknown_timestamp_zone', message: '时间缺失或未注明时区，不能可靠纳入滚动窗口' });
      continue;
    }
    if (time < oldest || time > settings.as_of_ms) { quality.out_of_window_records += 1; continue; }
    const game = replayRecord(input.row);
    game.time_ms = time;
    if (!game.standard) quality.unsupported_records += 1;
    yield game;
   }
  }
  const concurrency = Math.max(1, Math.min(8, Number(options.analysis_concurrency) || 1));
  const analyzed = orderedMap(eligibleGames(), concurrency, async game => {
    const decisions = game.hands.flatMap((h) => h.decisions);
    const calculation = await evaluateDecisions(decisions, { ...settings, ...options, python_failed: pythonFailed });
    return { game, calculation };
  });
  for await (const { game, calculation } of analyzed) {
    const id = game.game_id;
    const time = game.time_ms;
    quality.completed_hands += game.hands.filter((h) => h.completed).length;
    quality.reconstructed_hands += game.hands.filter((h) => h.reconstructed).length;
    quality.scored_hands += game.hands.filter((h) => h.score_verified).length;
    if (calculation.errors.some((reason) => reason.startsWith('mcr_calculator_'))) pythonFailed = true;
    for (const reason of calculation.errors) calculatorErrors.add(reason);
    quality.opportunity_verified_hands += verifyOpportunities([game], calculation);
    for (const state of windowStates) accumulateGame(state, game, calculation, settings);
    quality.issues.push(...game.issues);
    gameSummaries.push({ game_id: game.game_id, content_hash: game.content_hash, created_at: new Date(time).toISOString(),
      rule: game.rule, sub_rule: game.sub_rule, rule_key: game.rule_key, player_ids: game.players.map((p) => p.user_id),
      completed_hands: game.hands.filter((h) => h.completed).length,
      reconstructed_hands: game.hands.filter((h) => h.reconstructed).length,
      scores_available: game.scores_available, score_deltas: game.scores_available ? game.score_deltas : null,
      issues: game.issues.map(({ code, hand_index, message }) => ({ code, hand_index, message })) });
    // Drop private snapshots and response maps before loading the next game. Kept evidence contains
    // only a bounded list of actual/candidate tile IDs, never full state or future wall contents.
    for (const hand of game.hands) hand.decisions.length = 0;
    calculation.results.clear();
    processed += 1;
    if (progress && (processed % 10 === 0 || processed === eligibleUniqueCount)) {
      await progress({ stage: 'analyzing', processed, total: eligibleUniqueCount,
        message: `已分析 ${processed} 场，完成足番机会校验并释放单场快照` });
    }
  }
  quality.calculator_errors = [...calculatorErrors];
  gameSummaries.sort((a, b) => a.created_at.localeCompare(b.created_at) || a.game_id.localeCompare(b.game_id));
  const pairs = [];
  const findings = [];
  const players = new Map();
  for (const state of windowStates) {
    const days = state.windowDays;
    const window = finishWindow(state, settings);
    pairs.push(...window.pairs);
    findings.push(...window.findings);
    for (const totals of window.playerTotals.values()) {
      if (!players.has(totals.user_id)) players.set(totals.user_id, { user_id: totals.user_id, name: totals.name, windows: [] });
      players.get(totals.user_id).windows.push({ window_days: days, rule_key: totals.rule_key,
        games: totals.games, hands: totals.hands, scored_games: totals.scores.count, mean_score: momentMean(totals.scores) });
    }
  }
  findings.sort((a, b) => (a.status === 'review' ? 0 : 1) - (b.status === 'review' ? 0 : 1)
    || (a.channel === 'RON' ? 0 : 1) - (b.channel === 'RON' ? 0 : 1)
    || b.events - a.events || a.id.localeCompare(b.id));
  pairs.sort((a, b) => Number(b.flag_ron) - Number(a.flag_ron) || b.channels.RON.events - a.channels.RON.events
    || b.channels.ALL.events - a.channels.ALL.events || a.pair_key.localeCompare(b.pair_key));
  const availableOpportunities = pairs.some((p) => p.channels.RON.opportunity_creation_rate.denominator > 0);
  const capabilities = [
    { key: 'directed_delivery', label: '有向 ALL / MELD / RON 事实', status: 'available', reason: '逐副座次与来源动作归因，三条独立观察通道；抢杠单列' },
    { key: 'qualified_mcr_opportunities', label: '标准国标足番机会', status: availableOpportunities ? 'available' : 'unavailable',
      reason: availableOpportunities ? '完整牌张重建通过后，复用仓库原听牌和计番器；非花牌门槛、风位、末张与绝张已纳入'
        : quality.calculator_errors[0] || '本次输入没有通过完整重建和足番机会验证的决策' },
    { key: 'empirical_reference', label: '经验参考', status: 'partial', reason: '规则池与座次暴露分层、排除待查两人；不足参考时仅观察，未校准为显著性或作弊概率' },
    { key: 'score_comparison', label: '同桌成绩与真实支付', status: 'available', reason: '逐局分变和同规则池原始成绩差异；未做赛前实力的因果调整' },
    { key: 'adjusted_score_model', label: '实力调整成绩模型', status: 'unavailable', reason: '尚无经校准的赛前实力和共同支持域模型' },
    { key: 'meld_value_and_sacrifice', label: '过渡张牌效与自身牺牲', status: 'unavailable', reason: '当前计算可鸣牌与够番和牌集合；完整牌效、名次价值与牺牲模型未实现' },
    { key: 'hidden_information_and_groups', label: '隐藏听口模型与多人团伙', status: 'unavailable', reason: '真实等待仅用作足番机会标签；尚未校准隐藏信息选择模型和多人图模型' },
    { key: 'manual_pass_and_timing', label: '主动过牌与响应时间', status: 'unavailable', reason: '普通历史牌谱未记录主动放弃原因、提示送达、托管来源和秒级响应时间' },
    { key: 'statistical_confidence', label: '校准置信区间与误报保证', status: 'unavailable', reason: '当前是可解释观察排序；未声称作弊概率、显著性或真人检出率' },
  ];
  return {
    engine_version: ENGINE_VERSION,
    summary: { input_records: inputCount, unique_games: eligibleUniqueCount,
      analyzed_games: gameSummaries.filter((g) => g.completed_hands > 0).length,
      analyzed_hands: quality.completed_hands, player_count: players.size, pair_count: pairs.length,
      finding_count: findings.length, as_of: settings.as_of, windows_days: settings.windows_days,
      channel_findings: Object.fromEntries(CHANNELS.map((c) => [c, findings.filter((f) => f.channel === c).length])),
      warning: '观察与复核候选不是作弊认定；重叠窗口不是独立证据' },
    players: [...players.values()].sort((a, b) => a.user_id.localeCompare(b.user_id, undefined, { numeric: true })),
    pairs, findings, capabilities, quality,
    games: gameSummaries,
  };
}

module.exports = { analyzeRecords, ENGINE_VERSION, ratio, settingsFrom, buildWindow, verifyOpportunities,
  createWindowState, accumulateGame };
