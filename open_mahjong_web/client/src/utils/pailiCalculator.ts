export interface PailiOptions {
  mcrSevenPairs?: boolean;
  riichiSevenPairs?: boolean;
  thirteenOrphans?: boolean;
  unrelatedTiles?: boolean;
  combinationDragon?: boolean;
}

export interface PailiRequest {
  handTiles: number[];
  combinations: string[];
  options?: PailiOptions;
}

export interface PailiAccept {
  tile: number;
  remaining: number;
}

export interface PailiShantenResult {
  mode: "shanten";
  shanten: number;
  is_tingpai: boolean;
  accept: PailiAccept[];
  total_accept: number;
}

export interface PailiDiscard {
  discard: number;
  shanten: number;
  accept: PailiAccept[];
  total_accept: number;
}

export interface PailiDiscardResult {
  mode: "discard";
  best_shanten: number;
  discards: PailiDiscard[];
}

export type PailiResult = PailiShantenResult | PailiDiscardResult;

const TILE_IDS = [
  11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25, 26, 27, 28, 29, 31,
  32, 33, 34, 35, 36, 37, 38, 39, 41, 42, 43, 44, 45, 46, 47,
];

const ORPHAN_INDICES = [0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33];
const KNITTED_GROUPS = [
  [0, 3, 6],
  [1, 4, 7],
  [2, 5, 8],
];

function tileToIndex(tile: number): number {
  if (tile >= 11 && tile <= 19) return tile - 11;
  if (tile >= 21 && tile <= 29) return tile - 12;
  if (tile >= 31 && tile <= 39) return tile - 13;
  if (tile >= 41 && tile <= 47) return tile - 14;
  return -1;
}

function handToCounts(hand: number[]): number[] {
  const counts = Array(34).fill(0);
  for (const tile of hand) {
    const index = tileToIndex(tile);
    if (index < 0) throw new Error(`非法牌号: ${tile}`);
    counts[index]++;
  }
  return counts;
}

function meldTileIndices(code: string): number[] {
  if (typeof code !== "string" || code.length < 3) {
    throw new Error(`非法副露格式: ${code}`);
  }
  const kind = code[0];
  const tile = Number.parseInt(code.slice(1), 10);
  const index = tileToIndex(tile);
  if (index < 0) throw new Error(`非法副露牌号: ${code}`);

  if (kind === "s" || kind === "S") {
    if (index >= 27 || tile % 10 < 2 || tile % 10 > 8) {
      throw new Error(`非法顺子格式: ${code}`);
    }
    return [index - 1, index, index + 1];
  }
  if (kind === "k" || kind === "K") return [index, index, index];
  if (kind === "g" || kind === "G") return [index, index, index, index];
  throw new Error(`非法副露格式: ${code}`);
}

interface BlockState {
  melds: number;
  taatsu: number;
  pair: number;
}

const BLOCK_STATE_CACHE = new Map<number, BlockState[]>();

function reduceBlockStates(states: BlockState[]): BlockState[] {
  const unique = new Map<number, BlockState>();
  for (const state of states) {
    unique.set(state.pair * 100 + state.melds * 10 + state.taatsu, state);
  }
  const values = [...unique.values()];
  const maxMelds = values.reduce(
    (maximum, state) => Math.max(maximum, state.melds),
    0,
  );
  const bestTaatsu = [
    Array(maxMelds + 1).fill(-1),
    Array(maxMelds + 1).fill(-1),
  ];
  for (const state of values) {
    bestTaatsu[state.pair][state.melds] = Math.max(
      bestTaatsu[state.pair][state.melds],
      state.taatsu,
    );
  }

  const bestAtHigherMeld = bestTaatsu.map((byMeld) => {
    const higher = Array(byMeld.length).fill(-1);
    let best = -1;
    for (let melds = byMeld.length - 1; melds >= 0; melds--) {
      higher[melds] = best;
      best = Math.max(best, byMeld[melds]);
    }
    return higher;
  });

  return values.filter(
    (state) =>
      bestTaatsu[state.pair][state.melds] === state.taatsu &&
      bestAtHigherMeld[state.pair][state.melds] < state.taatsu,
  );
}

function blockStateCacheKey(source: number[], suited: boolean): number {
  let key = 0;
  for (const count of source) key = key * 5 + count;
  return suited ? key : 2_000_000 + key;
}

function blockStates(source: number[], suited: boolean): BlockState[] {
  const cacheKey = blockStateCacheKey(source, suited);
  const cached = BLOCK_STATE_CACHE.get(cacheKey);
  if (cached) return cached;

  const first = source.findIndex((count) => count > 0);
  if (first < 0) {
    const empty = [{ melds: 0, taatsu: 0, pair: 0 }];
    BLOCK_STATE_CACHE.set(cacheKey, empty);
    return empty;
  }

  const collect = (
    remove: number[],
    addition: Partial<BlockState>,
    states: BlockState[],
  ) => {
    const remaining = [...source];
    for (const index of remove) remaining[index]--;
    for (const state of blockStates(remaining, suited)) {
      const next = {
        melds: state.melds + (addition.melds ?? 0),
        taatsu: state.taatsu + (addition.taatsu ?? 0),
        pair: state.pair + (addition.pair ?? 0),
      };
      if (next.pair <= 1) states.push(next);
    }
  };

  const states: BlockState[] = [];
  collect([first], {}, states);
  if (source[first] >= 3) collect([first, first, first], { melds: 1 }, states);
  if (source[first] >= 2) {
    collect([first, first], { pair: 1 }, states);
    collect([first, first], { taatsu: 1 }, states);
  }
  if (suited && first <= 6 && source[first + 1] > 0 && source[first + 2] > 0) {
    collect([first, first + 1, first + 2], { melds: 1 }, states);
  }
  if (suited && first <= 7 && source[first + 1] > 0) {
    collect([first, first + 1], { taatsu: 1 }, states);
  }
  if (suited && first <= 6 && source[first + 2] > 0) {
    collect([first, first + 2], { taatsu: 1 }, states);
  }

  const reduced = reduceBlockStates(states);
  BLOCK_STATE_CACHE.set(cacheKey, reduced);
  return reduced;
}

function normalShanten(source: number[], openMelds: number): number {
  const blocks = [
    blockStates(source.slice(0, 9), true),
    blockStates(source.slice(9, 18), true),
    blockStates(source.slice(18, 27), true),
    blockStates(source.slice(27), false),
  ];
  let combined: BlockState[] = [{ melds: 0, taatsu: 0, pair: 0 }];
  for (const block of blocks) {
    const next: BlockState[] = [];
    for (const left of combined) {
      for (const right of block) {
        const pair = left.pair + right.pair;
        const melds = left.melds + right.melds;
        if (pair > 1 || melds + openMelds > 4) continue;
        next.push({
          melds,
          taatsu: left.taatsu + right.taatsu,
          pair,
        });
      }
    }
    combined = reduceBlockStates(next);
  }

  let best = 8;
  for (const state of combined) {
    const taatsu = Math.min(state.taatsu, 4 - openMelds - state.melds);
    best = Math.min(
      best,
      8 - 2 * (openMelds + state.melds) - taatsu - state.pair,
    );
  }
  return best;
}

function mcrSevenPairsShanten(counts: number[]): number {
  const pairs = counts.reduce(
    (total, count) => total + Math.floor(count / 2),
    0,
  );
  return 6 - Math.min(7, pairs);
}

function riichiSevenPairsShanten(counts: number[]): number {
  const pairs = counts.filter((count) => count >= 2).length;
  const unique = counts.filter((count) => count > 0).length;
  return 6 - pairs + Math.max(0, 7 - unique);
}

function thirteenOrphansShanten(counts: number[]): number {
  let unique = 0;
  let hasPair = false;
  for (const index of ORPHAN_INDICES) {
    if (counts[index] > 0) unique++;
    if (counts[index] > 1) hasPair = true;
  }
  return 13 - unique - Number(hasPair);
}

function knittedPatterns(): number[][] {
  const patterns: number[][] = [];
  for (let first = 0; first < 3; first++) {
    for (let second = 0; second < 3; second++) {
      if (second === first) continue;
      const third = 3 - first - second;
      patterns.push([
        ...KNITTED_GROUPS[first],
        ...KNITTED_GROUPS[second].map((index) => index + 9),
        ...KNITTED_GROUPS[third].map((index) => index + 18),
      ]);
    }
  }
  return patterns;
}

const KNITTED_PATTERNS = knittedPatterns();

function unrelatedShanten(counts: number[]): number {
  let best = 13;
  for (const pattern of KNITTED_PATTERNS) {
    let matched = 0;
    for (const index of pattern) matched += Number(counts[index] > 0);
    for (let index = 27; index < 34; index++)
      matched += Number(counts[index] > 0);
    best = Math.min(best, 13 - matched);
  }
  return best;
}

function pairDeficit(count: number): number {
  return Math.max(0, 2 - count);
}

function bestPairDeficitOutside(
  available: number[],
  deficitCounts: number[],
  first: number,
  second = -1,
  third = -1,
): number {
  for (let deficit = 0; deficit <= 2; deficit++) {
    let excluded = 0;
    if (first >= 0 && pairDeficit(available[first]) === deficit) excluded++;
    if (second >= 0 && pairDeficit(available[second]) === deficit) excluded++;
    if (third >= 0 && pairDeficit(available[third]) === deficit) excluded++;
    if (deficitCounts[deficit] > excluded) return deficit;
  }
  return Number.POSITIVE_INFINITY;
}

function combinationDragonShanten(
  counts: number[],
  openMelds: number,
  upperBound: number,
): number {
  if (openMelds > 1) return upperBound;
  let best = upperBound;

  // Allocate the nine knitted tiles first, then find the cheapest pair and
  // meld directly instead of enumerating every pair-meld Cartesian product.
  for (const pattern of KNITTED_PATTERNS) {
    const available = [...counts];
    let patternDeficit = 0;
    for (const index of pattern) {
      if (available[index] > 0) available[index]--;
      else patternDeficit++;
    }

    if (patternDeficit - 1 >= best) continue;

    const pairDeficitCounts = [0, 0, 0];
    for (const count of available) pairDeficitCounts[pairDeficit(count)]++;

    if (openMelds === 1) {
      const remainderDeficit = pairDeficitCounts.findIndex(
        (count) => count > 0,
      );
      best = Math.min(best, patternDeficit + remainderDeficit - 1);
      continue;
    }

    let remainderDeficit = Number.POSITIVE_INFINITY;
    for (let index = 0; index < 34; index++) {
      const meldDeficit = Math.max(0, 3 - available[index]);
      const pair = bestPairDeficitOutside(
        available,
        pairDeficitCounts,
        index,
      );
      remainderDeficit = Math.min(remainderDeficit, meldDeficit + pair);
    }
    for (let suit = 0; suit < 3; suit++) {
      for (let rank = 0; rank <= 6; rank++) {
        const first = suit * 9 + rank;
        const second = first + 1;
        const third = first + 2;
        const meldDeficit =
          Math.max(0, 1 - available[first]) +
          Math.max(0, 1 - available[second]) +
          Math.max(0, 1 - available[third]);
        let pair = bestPairDeficitOutside(
          available,
          pairDeficitCounts,
          first,
          second,
          third,
        );
        for (const index of [first, second, third]) {
          pair = Math.min(
            pair,
            Math.max(0, 3 - available[index]) -
              Math.max(0, 1 - available[index]),
          );
        }
        remainderDeficit = Math.min(remainderDeficit, meldDeficit + pair);
      }
    }
    best = Math.min(best, patternDeficit + remainderDeficit - 1);
    if (best === -1) break;
  }
  return best;
}

function calculateShanten(
  counts: number[],
  openMelds: number,
  options: Required<PailiOptions>,
): number {
  let shanten = normalShanten(counts, openMelds);
  if (openMelds === 0) {
    if (options.thirteenOrphans)
      shanten = Math.min(shanten, thirteenOrphansShanten(counts));
    if (options.mcrSevenPairs)
      shanten = Math.min(shanten, mcrSevenPairsShanten(counts));
    if (options.riichiSevenPairs)
      shanten = Math.min(shanten, riichiSevenPairsShanten(counts));
    if (options.unrelatedTiles) {
      shanten = Math.min(shanten, unrelatedShanten(counts));
    }
  }
  if (options.combinationDragon) {
    shanten = combinationDragonShanten(counts, openMelds, shanten);
  }
  return shanten;
}

function resolvePailiOptions(options?: PailiOptions): Required<PailiOptions> {
  const resolved: Required<PailiOptions> = {
    mcrSevenPairs: options?.mcrSevenPairs !== false,
    riichiSevenPairs: options?.riichiSevenPairs === true,
    thirteenOrphans: options?.thirteenOrphans !== false,
    unrelatedTiles: options?.unrelatedTiles !== false,
    combinationDragon: options?.combinationDragon !== false,
  };
  if (resolved.mcrSevenPairs && resolved.riichiSevenPairs) {
    throw new Error("国标七对与日麻七对不能同时开启");
  }
  return resolved;
}

function validateAndCountVisible(
  hand: number[],
  combinations: string[],
): { concealed: number[]; visible: number[] } {
  const concealed = handToCounts(hand);
  const visible = [...concealed];
  for (const code of combinations) {
    for (const index of meldTileIndices(code)) visible[index]++;
  }
  const overflow = visible.findIndex((count) => count > 4);
  if (overflow >= 0) throw new Error(`牌 ${TILE_IDS[overflow]} 超过 4 张`);

  const effectiveTiles = hand.length + combinations.length * 3;
  if (effectiveTiles !== 13 && effectiveTiles !== 14) {
    throw new Error("手牌总数应为 13 或 14（副露按 3 张计算）");
  }
  return { concealed, visible };
}

function buildAccept(
  counts: number[],
  visible: number[],
  discardIndex: number,
  baseShanten: number,
  getShanten: (counts: number[]) => number,
): PailiAccept[] {
  const accept: PailiAccept[] = [];
  for (let index = 0; index < TILE_IDS.length; index++) {
    const remaining =
      4 - visible[index] + Number(index === discardIndex);
    if (remaining <= 0) continue;
    counts[index]++;
    const shanten = getShanten(counts);
    counts[index]--;
    if (shanten < baseShanten)
      accept.push({ tile: TILE_IDS[index], remaining });
  }
  return accept;
}

/**
 * 只算向听，不算进张。
 * 13 张为当前向听，14 张为最佳切牌后向听。默认与牌理页相同（国标七对/十三幺/不靠/组合龙）。
 */
export function calculatePailiShanten(
  handTiles: number[],
  combinations: string[] = [],
  options?: PailiOptions,
): number {
  const { concealed } = validateAndCountVisible(handTiles, combinations);
  const resolved = resolvePailiOptions(options);
  const openMelds = combinations.length;
  if (handTiles.length + openMelds * 3 === 13) {
    return calculateShanten(concealed, openMelds, resolved);
  }
  let best = Number.POSITIVE_INFINITY;
  for (let index = 0; index < concealed.length; index++) {
    if (concealed[index] === 0) continue;
    concealed[index]--;
    const shanten = calculateShanten(concealed, openMelds, resolved);
    concealed[index]++;
    if (shanten < best) best = shanten;
  }
  return Number.isFinite(best) ? best : 0;
}

export function calculatePaili(request: PailiRequest): PailiResult {
  const hand = [...request.handTiles];
  const combinations = [...request.combinations];
  const { concealed, visible } = validateAndCountVisible(hand, combinations);
  const options = resolvePailiOptions(request.options);
  const shantenCache = new Map<string, number>();
  const getShanten = (counts: number[]) => {
    const key = counts.join("");
    const cached = shantenCache.get(key);
    if (cached != null) return cached;
    const shanten = calculateShanten(counts, combinations.length, options);
    shantenCache.set(key, shanten);
    return shanten;
  };

  if (hand.length + combinations.length * 3 === 13) {
    const shanten = getShanten(concealed);
    const accept = buildAccept(
      concealed,
      visible,
      -1,
      shanten,
      getShanten,
    );
    return {
      mode: "shanten",
      shanten,
      is_tingpai: shanten === 0,
      accept,
      total_accept: accept.reduce((total, item) => total + item.remaining, 0),
    };
  }

  const discards: PailiDiscard[] = [];
  for (let discardIndex = 0; discardIndex < concealed.length; discardIndex++) {
    if (concealed[discardIndex] === 0) continue;
    concealed[discardIndex]--;
    const shanten = getShanten(concealed);
    const accept = buildAccept(
      concealed,
      visible,
      discardIndex,
      shanten,
      getShanten,
    );
    concealed[discardIndex]++;
    discards.push({
      discard: TILE_IDS[discardIndex],
      shanten,
      accept,
      total_accept: accept.reduce((total, item) => total + item.remaining, 0),
    });
  }

  discards.sort(
    (left, right) =>
      left.shanten - right.shanten ||
      right.total_accept - left.total_accept ||
      left.discard - right.discard,
  );
  return {
    mode: "discard",
    best_shanten: discards[0]?.shanten ?? 0,
    discards,
  };
}
