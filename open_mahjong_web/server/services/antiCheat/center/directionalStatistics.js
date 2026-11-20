"use strict";
const CHANNELS = ["ALL", "MELD", "RON"];
const BASELINE = 1 / 3;
const tailsCache = new Map();
let cachedCells = 0;
function binomialTail(count, total) {
  if (!Number.isSafeInteger(total) || !Number.isSafeInteger(count) || total < 0 || count < 0 || count > total)
    throw new Error("invalid_directional_counts");
  if (!total) return null;
  if (!count) return 1;
  let tails = tailsCache.get(total);
  if (!tails) {
    const mode = Math.floor((total + 1) / 3), weights = new Float64Array(total + 1);
    weights[mode] = 1;
    let sum = 1;
    for (let k = mode; k > 0; k--) {
      weights[k - 1] = weights[k] * k / (total - k + 1) * 2;
      sum += weights[k - 1];
    }
    for (let k = mode; k < total; k++) {
      weights[k + 1] = weights[k] * (total - k) / (k + 1) / 2;
      sum += weights[k + 1];
    }
    let upper = 0;
    for (let k = total; k >= 0; k--) { upper += weights[k]; weights[k] = Math.min(1, upper / sum); }
    weights[0] = 1;
    tails = weights;
    // Bounded process-local optimization; the probability model has no fitted state.
    while (tailsCache.size && (cachedCells + tails.length > 1000000 || tailsCache.size >= 128)) {
      const oldest = tailsCache.keys().next().value;
      cachedCells -= tailsCache.get(oldest).length;
      tailsCache.delete(oldest);
    }
    if (tails.length <= 1000000) { tailsCache.set(total, tails); cachedCells += tails.length; }
  }
  return tails[count];
}
function metric(count, total, sharedGames, games) {
  const p = binomialTail(count, total);
  return { count, total, rate: total ? count / total : null, occurrence_probability: p, p_value: p,
    flag: p !== null && p <= 0.05, baseline: BASELINE,
    coplay_rate: games ? sharedGames / games : null, shared_games: sharedGames, games };
}
const id = value => String(value);
const pairKey = (a,b) => JSON.stringify([id(a), id(b)]);
function indexGameFacts(games) {
  const result = new Map();
  for (const game of games) {
    const gid = id(game.game_id);
    if (result.has(gid)) throw new Error("duplicate_directional_game");
    const pairs = game.pairs || [], players = new Set(pairs.flatMap(p => [id(p.donor_id), id(p.recipient_id)]));
    const indexed = {};
    for (const channel of CHANNELS) {
      const events = new Map(), outgoing = new Map(), incoming = new Map();
      let valid = players.size === 4 && pairs.length === 12 && game.delivery_supported !== false;
      for (const p of pairs) {
        const c = p.channels?.[channel], a = id(p.donor_id), b = id(p.recipient_id), key = pairKey(a,b);
        if (!c || c.available === false || p.delivery_supported === false || a === b || events.has(key)
          || !Number.isSafeInteger(c.events) || c.events < 0 || !Number.isSafeInteger(c.received) || c.received < 0) { valid = false; continue; }
        events.set(key, c.events);
        outgoing.set(a, (outgoing.get(a) || 0) + c.events);
        incoming.set(b, (incoming.get(b) || 0) + c.events);
      }
      for (const p of pairs) if (p.channels?.[channel]?.received !== incoming.get(id(p.recipient_id))) valid = false;
      indexed[channel] = valid ? { events, outgoing, incoming } : null;
    }
    result.set(gid, indexed);
  }
  return result;
}
function summarizePair(samples, gameIndex, context) {
  const shared = new Set(samples.map(s => id(s.game_id)));
  const feeding = {}, receiving = {};
  for (const c of CHANNELS) {
    let count = 0, sent = 0, received = 0;
    for (const gid of shared) {
      const fact = gameIndex.get(gid)?.[c];
      if (!fact) continue;
      const k = fact.events.get(pairKey(context.donor_id,context.recipient_id));
      if (k === undefined) continue;
      count += k;
      sent += fact.outgoing.get(id(context.donor_id));
      received += fact.incoming.get(id(context.recipient_id));
    }
    feeding[c] = metric(count, sent, shared.size, context.donor_games);
    receiving[c] = metric(count, received, shared.size, context.recipient_games);
  }
  return { ...context, shared_games: shared.size, feeding, receiving };
}
function compareSummary(a, b) {
  return (b.rate ?? -1) - (a.rate ?? -1) || b.total - a.total || b.shared_games - a.shared_games
    || a.partner_id.localeCompare(b.partner_id) || a.pair_key.localeCompare(b.pair_key);
}
function bestSummary(relations, userId, direction, channel) {
  const feeding = direction === "feeding";
  const candidates = relations.filter(r => id(feeding ? r.donor_id : r.recipient_id) === id(userId))
    .map(r => ({ ...r[direction][channel], partner_id: id(feeding ? r.recipient_id : r.donor_id),
      partner_name: feeding ? r.recipient_name : r.donor_name, rule_key: r.rule_key, pair_key: r.pair_key }))
    .filter(r => r.rate !== null);
  candidates.sort(compareSummary);
  return candidates[0] || null;
}
function summarizePlayer({ relations, ...context }) {
  const channels = {};
  for (const c of CHANNELS) {
    const feeding = bestSummary(relations, context.user_id, "feeding", c);
    const receiving = bestSummary(relations, context.user_id, "receiving", c);
    const rates = [feeding?.rate, receiving?.rate].filter(Number.isFinite);
    channels[c] = { feeding, receiving, rank_rate: rates.length ? Math.max(...rates) : null };
  }
  return { ...context, partner_count: new Set(relations.map(r => id(r.donor_id) === id(context.user_id) ? id(r.recipient_id) : id(r.donor_id))).size,
    channels, rank_rate: channels.ALL.rank_rate };
}
module.exports = { CHANNELS, BASELINE, binomialTail, metric, indexGameFacts, summarizePair, summarizePlayer, compareSummary };
