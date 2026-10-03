/** 蓝十第4版四人规则。牌形、和张归属、无环关联组合均独立于标准国标。 */
import type { HepaiResult } from "./gbHepai";

export const LANSHI_RULE_VERSION = "lanshi-v4-2026";
// 番表顺序也用于同分候选的稳定显示；与服务端及 Unity 的版本一致。
const FAN_TABLE: Array<[string, string, number]> = [
  ["qixingdui", "七星对", 100],
  ["sitongshun", "四同顺", 100],
  ["jiulianbaodeng", "九莲宝灯", 100],
  ["sigang", "四杠", 100],
  ["dasixi", "大四喜", 72],
  ["qingyaojiu", "清幺九", 72],
  ["sianke", "四暗刻", 48],
  ["shisanyao", "十三幺", 48],
  ["ziyise", "字一色", 40],
  ["silianshun", "四连顺", 40],
  ["silianke", "四连刻", 40],
  ["xiaosixi", "小四喜", 32],
  ["sangang", "三杠", 32],
  ["dasanyuan", "大三元", 24],
  ["shunwang", "顺网", 24],
  ["santongshun", "三同顺", 24],
  ["shunlian", "顺链", 24],
  ["hunyaojiu", "混幺九", 16],
  ["quanda", "全大", 16],
  ["quanzhong", "全中", 16],
  ["quanxiao", "全小", 16],
  ["quandaiwu", "全带五", 16],
  ["santongke", "三同刻", 16],
  ["xiaosanyuan", "小三元", 16],
  ["quanbukao", "全不靠", 16],
  ["sanfengke", "三风刻", 12],
  ["sananke", "三暗刻", 12],
  ["sanlianke", "三连刻", 12],
  ["qingyise", "清一色", 12],
  ["sanlianshun", "三连顺", 12],
  ["sanselianke", "三色连刻", 8],
  ["qingquandaiyao", "清全带幺", 8],
  ["shuanggang", "双杠", 8],
  ["dayuwu", "大于五", 8],
  ["xiaoyuwu", "小于五", 8],
  ["qiduizi", "七对", 8],
  ["shunhuan", "顺环", 8],
  ["shuangjianke", "双箭刻", 6],
  ["qinglong", "清龙", 6],
  ["miaoshouhuichun", "妙手回春", 5],
  ["haidilaoyue", "海底捞月", 5],
  ["gangshangkaihua", "杠上开花", 5],
  ["qiangganghe", "抢杠和", 5],
  ["tianhe", "天和", 5],
  ["dihe", "地和", 5],
  ["hualong", "花龙", 4],
  ["sansetongshun", "三色同顺", 4],
  ["pengpenghe", "碰碰和", 3],
  ["hunquandaiyao", "混全带幺", 3],
  ["hunyise", "混一色", 3],
  ["sanselianshun", "三色连顺", 3],
  ["angang", "暗杠", 2],
  ["shuanganke", "双暗刻", 2],
  ["wumenqi", "五门齐", 2],
  ["shuangtongke", "双同刻", 2],
  ["quanqiuren", "全求人", 2],
  ["siguiyi", "四归一", 2],
  ["yibangao", "一般高", 2],
  ["hejuezhang", "和绝张", 2],
  ["jianke", "箭刻", 2],
  ["quanfengke", "圈风刻", 2],
  ["menfengke", "门风刻", 2],
  ["menqianqing", "门前清", 1],
  ["minggang", "明杠", 1],
  ["duanyao", "断幺", 1],
  ["xixiangfeng", "喜相逢", 1],
  ["lianliu", "连六", 1],
  ["laoshaofu", "老少副", 1],
  ["yaojiuke", "幺九刻", 1],
  ["zimo", "自摸", 1],
];
const VALUES = Object.fromEntries(
  FAN_TABLE.map(([key, , value]) => [key, value]),
);
const REPEATABLE = new Set([
  "siguiyi",
  "shuangtongke",
  "yibangao",
  "xixiangfeng",
  "lianliu",
  "laoshaofu",
  "yaojiuke",
]);

/** 结算和牌谱使用蓝十自己的番值，不以标准国标番表解释存储的番名。 */
export function lanshiFanPoints(label: string): number | null {
  if (label === "错和") return 0;
  const match = String(label ?? "").trim().match(/^(.+?)(?:\s*[*×xX]\s*(\d+))?$/);
  if (!match) return null;
  const entry = FAN_TABLE.find(([, name]) => name === match[1].trim());
  const count = match[2] == null ? 1 : Number(match[2]);
  if (!entry || count < 1 || count > 4 || (count > 1 && !REPEATABLE.has(entry[0]))) return null;
  return entry[2] * count;
}
const OCCASIONAL = new Set([
  "miaoshouhuichun",
  "haidilaoyue",
  "gangshangkaihua",
  "qiangganghe",
  "tianhe",
  "dihe",
]);
const TILES = [1, 2, 3]
  .flatMap((s) => Array.from({ length: 9 }, (_, r) => s * 10 + r + 1))
  .concat([41, 42, 43, 44, 45, 46, 47]);
const VALID = new Set(TILES);
const HONORS = new Set([41, 42, 43, 44, 45, 46, 47]);
const TERMINALS = new Set([11, 19, 21, 29, 31, 39]);
const ORPHANS = new Set([...HONORS, ...TERMINALS]);
type Meld = { kind: string; tile: number };
type Shape = { name: string; groups: Meld[] };
type Relation = { fan: string; nodes: number[] };
type Scored = HepaiResult & {
  keys: string[];
  raw: number;
  order: number[];
  identity: string;
};
const count = <T>(items: T[]) => {
  const result = new Map<T, number>();
  for (const item of items) result.set(item, (result.get(item) ?? 0) + 1);
  return result;
};
const subset = <T>(items: Iterable<T>, set: Set<T>) =>
  [...items].every((t) => set.has(t));
const sequence = (g: Meld) => "sS".includes(g.kind);
const triplet = (g: Meld) => "kKgG".includes(g.kind);
const expand = (g: Meld): number[] =>
  sequence(g)
    ? [g.tile - 1, g.tile, g.tile + 1]
    : Array("gG".includes(g.kind) ? 4 : g.kind === "q" ? 2 : 3).fill(g.tile);
const compare = <T extends number | string>(a: T[], b: T[]) => {
  for (let i = 0; i < Math.min(a.length, b.length); i++) {
    if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
  }
  return a.length - b.length;
};

function validate(
  hand: number[],
  tokens: string[],
  win: number,
): Meld[] | null {
  if (
    !Array.isArray(hand) ||
    !Array.isArray(tokens) ||
    tokens.length > 4 ||
    hand.length !== 14 - 3 * tokens.length ||
    !hand.includes(win) ||
    hand.some((t) => !VALID.has(t))
  )
    return null;
  const physical = [...hand],
    fixed: Meld[] = [];
  for (const token of tokens) {
    if (typeof token !== "string" || !/^[skgG][1-4][0-9]$/.test(token))
      return null;
    const group = { kind: token[0], tile: Number(token.slice(1)) };
    if (
      !VALID.has(group.tile) ||
      (group.kind === "s" &&
        (group.tile >= 40 || group.tile % 10 < 2 || group.tile % 10 > 8))
    )
      return null;
    fixed.push(group);
    physical.push(...expand(group));
  }
  return [...count(physical).values()].some((n) => n > 4) ? null : fixed;
}

function* closedGroups(counts: number[]): Generator<Meld[]> {
  const first = TILES.find((t) => counts[t] > 0);
  if (first === undefined) {
    yield [];
    return;
  }
  const choices: Meld[] = [];
  if (counts[first] >= 3) choices.push({ kind: "K", tile: first });
  if (
    first < 40 &&
    first % 10 <= 7 &&
    counts[first + 1] > 0 &&
    counts[first + 2] > 0
  )
    choices.push({ kind: "S", tile: first + 1 });
  for (const group of choices) {
    for (const tile of expand(group)) counts[tile]--;
    for (const tail of closedGroups(counts)) yield [group, ...tail];
    for (const tile of expand(group)) counts[tile]++;
  }
}

function* decompose(
  hand: number[],
  fixed: Meld[],
  win: number,
  selfDraw: boolean,
): Generator<Shape> {
  const kinds = new Set(hand),
    counts = Array(48).fill(0);
  for (const tile of hand) counts[tile]++;
  if (!fixed.length) {
    if (kinds.size === 7 && [...kinds].every((t) => counts[t] === 2))
      yield {
        name: subset(kinds, HONORS) ? "qixingdui" : "qiduizi",
        groups: [],
      };
    if (kinds.size === 13 && subset(kinds, ORPHANS))
      yield { name: "shisanyao", groups: [] };
    if (kinds.size === 14) {
      let unrelated = false;
      for (const a of [1, 2, 3])
        for (const b of [1, 2, 3])
          if (a !== b) {
            const offsets = [a, b, 6 - a - b];
            if (
              [...kinds].every(
                (t) =>
                  t >= 40 ||
                  ((t % 10) - offsets[Math.floor(t / 10) - 1]) % 3 === 0,
              )
            )
              unrelated = true;
          }
      if (unrelated) yield { name: "quanbukao", groups: [] };
    }
  }
  for (const pair of TILES) {
    if (counts[pair] < 2) continue;
    counts[pair] -= 2;
    for (const groups of closedGroups(counts)) {
      const closed = [...groups, { kind: "q", tile: pair }];
      for (let i = 0; i < closed.length; i++)
        if (expand(closed[i]).includes(win)) {
          const completed = closed.map((g, j) =>
            !selfDraw && i === j && g.kind === "K" ? { ...g, kind: "k" } : g,
          );
          yield { name: "standard", groups: [...fixed, ...completed] };
        }
    }
    counts[pair] += 2;
  }
}

function lowRelation(a: number[], b: number[]): string | null {
  if (a[0] === b[0] && a[1] === b[1]) return "yibangao";
  if (a[0] !== b[0] && a[1] === b[1]) return "xixiangfeng";
  if (a[0] === b[0] && Math.abs(a[1] - b[1]) === 3) return "lianliu";
  if (a[0] === b[0] && Math.min(a[1], b[1]) === 1 && Math.max(a[1], b[1]) === 7)
    return "laoshaofu";
  return null;
}

function relationCandidates(groups: Meld[]): Relation[] {
  const result: Relation[] = [];
  for (let size = 2; size <= 4; size++)
    for (let mask = 1; mask < 1 << groups.length; mask++) {
      const nodes = groups
        .map((_, i) => i)
        .filter((i) => (mask & (1 << i)) !== 0);
      if (nodes.length !== size) continue;
      const selected = nodes.map((i) => groups[i]),
        seq = selected.every(sequence);
      if (!seq && !selected.every((g) => triplet(g) && g.tile < 40)) continue;
      const values = selected.map((g) => [
        Math.floor(g.tile / 10),
        (g.tile % 10) - Number(seq),
      ]);
      const suits = new Set(values.map((v) => v[0])),
        ranks = values.map((v) => v[1]).sort((a, b) => a - b);
      const same = suits.size === 1,
        all = suits.size === 3,
        consecutive = ranks.every((v, i) => v === ranks[0] + i);
      const add = (fan: string | null) => {
        if (fan) result.push({ fan, nodes });
      };
      if (size === 2)
        add(
          seq
            ? lowRelation(values[0], values[1])
            : suits.size === 2 && ranks[0] === ranks[1]
              ? "shuangtongke"
              : null,
        );
      else if (size === 3) {
        if (same && consecutive) add(seq ? "sanlianshun" : "sanlianke");
        if (all && consecutive) add(seq ? "sanselianshun" : "sanselianke");
        if (new Set(ranks).size === 1) {
          if (all) add(seq ? "sansetongshun" : "santongke");
          else if (seq && same) add("santongshun");
        }
        if (seq && compare(ranks, [1, 4, 7]) === 0) {
          if (same) add("qinglong");
          else if (all) add("hualong");
        }
      } else {
        if (same && consecutive) add(seq ? "silianshun" : "silianke");
        if (seq) {
          const frequencies = count(values.map((v) => v.join(",")));
          if (frequencies.size === 1) add("sitongshun");
          if (same && compare(ranks, [1, 3, 5, 7]) === 0) add("shunlian");
          if (
            frequencies.size === 2 &&
            [...frequencies.values()].every((n) => n === 2)
          ) {
            const distinct = [...frequencies.keys()].map((v) =>
              v.split(",").map(Number),
            );
            if (
              ["xixiangfeng", "lianliu", "laoshaofu"].includes(
                lowRelation(distinct[0], distinct[1]) ?? "",
              )
            )
              add("shunwang");
          }
          if (suits.size === 2 && frequencies.size === 4) {
            const bySuit = [...suits].map((s) =>
              values
                .filter((v) => v[0] === s)
                .map((v) => v[1])
                .sort((a, b) => a - b),
            );
            if (
              bySuit[0].length === 2 &&
              compare(bySuit[0], bySuit[1]) === 0 &&
              (bySuit[0][1] - bySuit[0][0] === 3 ||
                compare(bySuit[0], [1, 7]) === 0)
            )
              add("shunhuan");
          }
        }
      }
    }
  return result;
}

function visitRelations(groups: Meld[], visit: (fans: string[]) => void) {
  const candidates = relationCandidates(groups);
  function search(start: number, components: number[], fans: string[]) {
    visit(fans);
    for (let i = start; i < candidates.length; i++) {
      const r = candidates[i],
        roots = new Set(r.nodes.map((n) => components[n]));
      if (roots.size !== r.nodes.length) continue;
      const root = Math.min(...roots);
      search(
        i + 1,
        components.map((n) => (roots.has(n) ? root : n)),
        [...fans, r.fan],
      );
    }
  }
  search(
    0,
    groups.map((_, i) => i),
    [],
  );
}

function tileFans(hand: number[], fixed: Meld[], shape: string): string[] {
  const tiles = [...hand, ...fixed.flatMap(expand)],
    kinds = new Set(tiles);
  const numeric = [...kinds].filter((t) => t < 40),
    suits = new Set(numeric.map((t) => Math.floor(t / 10)));
  const hasHonors = numeric.length !== kinds.size,
    fans: string[] = [];
  if (suits.size === 1) fans.push(hasHonors ? "hunyise" : "qingyise");
  if (
    suits.size === 3 &&
    [...kinds].some((t) => t >= 41 && t <= 44) &&
    [...kinds].some((t) => t >= 45) &&
    !["shisanyao", "quanbukao"].includes(shape)
  )
    fans.push("wumenqi");
  if (!hasHonors && numeric.every((t) => t % 10 >= 2 && t % 10 <= 8))
    fans.push("duanyao");
  if (!hasHonors) {
    const ranks = numeric.map((t) => t % 10);
    if (ranks.every((r) => r >= 7)) fans.push("quanda");
    else if (ranks.every((r) => r >= 6)) fans.push("dayuwu");
    if (ranks.every((r) => r <= 3)) fans.push("quanxiao");
    else if (ranks.every((r) => r <= 4)) fans.push("xiaoyuwu");
    if (ranks.every((r) => r >= 4 && r <= 6)) fans.push("quanzhong");
  }
  const kongs = new Set(
    fixed.filter((g) => "gG".includes(g.kind)).map((g) => g.tile),
  );
  for (const [t, n] of count(tiles))
    if (n === 4 && !kongs.has(t)) fans.push("siguiyi");
  return fans;
}

function standardFans(
  hand: number[],
  fixed: Meld[],
  groups: Meld[],
  way: Set<string>,
  selfDraw: boolean,
): string[] {
  const pair = groups.find((g) => g.kind === "q")!.tile,
    melds = groups.filter((g) => g.kind !== "q"),
    trips = melds.filter(triplet);
  const winds = trips.map((g) => g.tile).filter((t) => t >= 41 && t <= 44),
    dragons = trips.map((g) => g.tile).filter((t) => t >= 45);
  const covered = new Set<number>(),
    fans: string[] = [];
  if (winds.length >= 3) {
    fans.push(
      winds.length === 4
        ? "dasixi"
        : pair >= 41 && pair <= 44
          ? "xiaosixi"
          : "sanfengke",
    );
    winds.forEach((t) => covered.add(t));
  }
  if (dragons.length) {
    fans.push(
      dragons.length === 3
        ? "dasanyuan"
        : dragons.length === 2
          ? pair >= 45
            ? "xiaosanyuan"
            : "shuangjianke"
          : "jianke",
    );
    dragons.forEach((t) => covered.add(t));
  }
  if (winds.length !== 4)
    for (const tile of winds) {
      if (way.has("场风" + "东南西北"[tile - 41])) {
        fans.push("quanfengke");
        covered.add(tile);
      }
      if (way.has("自风" + "东南西北"[tile - 41])) {
        fans.push("menfengke");
        covered.add(tile);
      }
    }
  for (const g of trips)
    if (ORPHANS.has(g.tile) && !covered.has(g.tile)) fans.push("yaojiuke");
  if (trips.length === 4) {
    fans.push("pengpenghe");
    const kinds = [pair, ...trips.map((g) => g.tile)];
    if (subset(kinds, HONORS)) fans.push("ziyise");
    else if (subset(kinds, TERMINALS)) fans.push("qingyaojiu");
    else if (subset(kinds, ORPHANS)) fans.push("hunyaojiu");
  }
  const concealed = trips.filter((g) => "KG".includes(g.kind)).length,
    kongs = fixed.filter((g) => "gG".includes(g.kind));
  if (concealed >= 2)
    fans.push(["", "", "shuanganke", "sananke", "sianke"][concealed]);
  if (kongs.length >= 2)
    fans.push(["", "", "shuanggang", "sangang", "sigang"][kongs.length]);
  else if (kongs.length)
    fans.push(kongs[0].kind === "G" ? "angang" : "minggang");
  if (groups.every((g) => expand(g).some((t) => t < 40 && t % 10 === 5)))
    fans.push("quandaiwu");
  if (groups.every((g) => expand(g).some((t) => ORPHANS.has(t))))
    fans.push(
      groups.some((g) => g.tile >= 40) ? "hunquandaiyao" : "qingquandaiyao",
    );
  if (
    !selfDraw &&
    fixed.length === 4 &&
    fixed.every((g) => "skg".includes(g.kind))
  )
    fans.push("quanqiuren");
  if (
    !fixed.length &&
    hand.every((t) => t < 40 && Math.floor(t / 10) === Math.floor(hand[0] / 10))
  ) {
    const required = [1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9];
    const ranks = count(hand.map((t) => t % 10));
    if (
      way.has("庄家起手") &&
      selfDraw &&
      [...count(required)].every(([r, n]) => (ranks.get(r) ?? 0) >= n)
    )
      fans.push("jiulianbaodeng");
  }
  return fans;
}

function exclude(fans: string[]): string[] {
  const high = FAN_TABLE.find(
    ([key, , value]) => value === 100 && fans.includes(key),
  );
  if (high) return [high[0]];
  const excluded = new Set<string>();
  const rules: Record<string, string[]> = {
    dasixi: ["pengpenghe"],
    qingyaojiu: ["pengpenghe", "yaojiuke"],
    sianke: ["pengpenghe", "menqianqing"],
    ziyise: ["pengpenghe", "yaojiuke"],
    silianke: ["pengpenghe"],
    hunyaojiu: ["pengpenghe", "yaojiuke"],
    quanzhong: ["duanyao"],
    quandaiwu: ["duanyao"],
  };
  for (const key of fans)
    for (const lower of rules[key] ?? []) excluded.add(lower);
  return fans.filter((key) => !excluded.has(key));
}

export function hepaiCheckLanshi(
  hand: number[],
  tokens: string[],
  ways: string[],
  win: number,
): HepaiResult {
  const fixed = validate(hand, tokens, win);
  if (!fixed) return { fan: 0, fanNames: [] };
  const way = new Set(ways ?? []);
  if (way.has("last_deal")) way.add("妙手回春");
  if (way.has("last_cut")) way.add("海底捞月");
  const selfDraw = ["自摸", "妙手回春", "杠上开花", "天和"].some((w) =>
    way.has(w),
  );
  const occasional = FAN_TABLE.find(
    ([key, name]) => OCCASIONAL.has(key) && way.has(name),
  )?.[0];
  const common = selfDraw ? ["zimo"] : [];
  if (way.has("和绝张") || way.has("抢杠和")) common.push("hejuezhang");
  let best: Scored | null = null;
  for (const shape of decompose(hand, fixed, win, selfDraw)) {
    const fans = [...common, ...tileFans(hand, fixed, shape.name)];
    if (shape.name !== "standard") fans.push(shape.name);
    else {
      if (!fixed.some((g) => "skg".includes(g.kind))) fans.push("menqianqing");
      fans.push(...standardFans(hand, fixed, shape.groups, way, selfDraw));
      if (
        !fixed.length &&
        hand.every(
          (t) => t < 40 && Math.floor(t / 10) === Math.floor(hand[0] / 10),
        )
      ) {
        const before = [...hand];
        before.splice(before.indexOf(win), 1);
        if (
          compare(
            before.map((t) => t % 10).sort((a, b) => a - b),
            [1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9],
          ) === 0
        )
          fans.push("jiulianbaodeng");
      }
    }
    let bestShape: Scored | null = null;
    visitRelations(
      shape.groups.filter((g) => g.kind !== "q"),
      (relations) => {
        const regular = exclude([...fans, ...relations]),
          raw = regular.reduce((sum, f) => sum + VALUES[f], 0);
        const final = occasional && raw < 5 ? [occasional] : regular,
          counts = count(final);
        const scored: Scored = {
          fan: Math.min(
            100,
            final.reduce((sum, f) => sum + VALUES[f], 0),
          ),
          fanNames: [],
          keys: [],
          raw,
          order: [],
          identity: shape.groups.map((g) => g.kind + g.tile).join("\u0001"),
        };
        FAN_TABLE.forEach(([key, name], index) => {
          const n = counts.get(key) ?? 0;
          if (n) {
            const suffix = REPEATABLE.has(key) ? `*${n}` : "";
            scored.keys.push(key + suffix);
            scored.fanNames.push(name + suffix);
            scored.order.push(...Array(n).fill(index));
          }
        });
        if (
          !bestShape ||
          scored.fan > bestShape.fan ||
          (scored.fan === bestShape.fan &&
            (scored.raw > bestShape.raw ||
              (scored.raw === bestShape.raw &&
                compare(scored.order, bestShape.order) < 0)))
        )
          bestShape = scored;
      },
    );
    const selected = bestShape as Scored | null;
    if (
      selected &&
      (!best ||
        selected.fan > best.fan ||
        (selected.fan === best.fan &&
          (compare(selected.keys, best.keys) < 0 ||
            (compare(selected.keys, best.keys) === 0 &&
              selected.identity < best.identity))))
    )
      best = selected;
  }
  return best
    ? { fan: best.fan, fanNames: best.fanNames }
    : { fan: 0, fanNames: [] };
}

export function tingpaiCheckLanshi(hand: number[], tokens: string[]): number[] {
  if (
    !Array.isArray(hand) ||
    !Array.isArray(tokens) ||
    hand.length + 3 * tokens.length !== 13
  )
    return [];
  return TILES.filter((tile) => {
    const complete = [...hand, tile],
      fixed = validate(complete, tokens, tile);
    return (
      fixed !== null && !decompose(complete, fixed, tile, true).next().done
    );
  });
}
