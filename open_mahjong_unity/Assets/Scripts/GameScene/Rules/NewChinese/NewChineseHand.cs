using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class NewChineseHand {
    internal const int ExchangeMinimumFan = 15;
    internal static readonly int[] Faces = Enumerable.Range(1, 3).SelectMany(s => Enumerable.Range(1, 9).Select(r => s * 10 + r)).Concat(Enumerable.Range(41, 7)).ToArray();
    internal static readonly Dictionary<string, int> Values = new Dictionary<string, int> {
        {"大四喜之十八罗汉", 1800},
        {"连四杠", 1600},
        {"三龙七对单骑救主", 1400},
        {"七福星", 777},
        {"九宝莲灯之天衣无缝", 499},
        {"十八罗汉", 458},
        {"九宝莲灯之准灯高挂", 299},
        {"清幺九", 299},
        {"连七对", 299},
        {"绿一色", 250},
        {"大四喜", 188},
        {"大四喜混幺九", 208},
        {"大四喜字一色", 238},
        {"国士无双十三面", 233},
        {"四暗刻单骑救主", 258},
        {"四暗刻", 130},
        {"十三幺", 113},
        {"大三元", 150},
        {"字一色", 120},
        {"小四喜", 108},
        {"混幺九", 80},
        {"三杠", 80},
        {"清一色", 50},
        {"暗中一条龙", 50},
        {"小三元", 50},
        {"三风刻", 50},
        {"一色三同顺", 50},
        {"三同刻", 50},
        {"听五张", 30},
        {"三暗刻", 30},
        {"全带幺", 20},
        {"听四张", 25},
        {"五门齐", 20},
        {"一条龙", 20},
        {"混一色", 20},
        {"三色三同顺", 20},
        {"双风刻", 15},
        {"双箭刻", 20},
        {"双杠", 20},
        {"双暗刻", 5},
        {"绝张", 5},
        {"杠上开花", 5},
        {"杠", 5},
        {"四归一", 5},
        {"门清", 5},
        {"听三张", 5},
        {"听六张", 100},
        {"听七张", 100},
        {"听八张", 120},
        {"准听九", 400},
        {"连四对", 25},
        {"连五对", 60},
        {"连六对", 110},
        {"双龙七对", 100},
        {"双龙七对单骑救主", 200},
        {"三龙七对", 400},
        {"八归", 25},
        {"十二归", 120},
        {"龙七对", 20},
        {"杠上杠", 15},
        {"三连杠", 200},
        {"一色四同顺", 150},
        {"精五门", 50}
    };
    private static readonly HashSet<string> Limits = new HashSet<string> {
        "大四喜之十八罗汉",
        "连四杠",
        "三龙七对单骑救主",
        "七福星",
        "九宝莲灯之天衣无缝",
        "十八罗汉",
        "九宝莲灯之准灯高挂",
        "清幺九",
        "连七对",
        "绿一色",
        "大四喜",
        "大四喜混幺九",
        "大四喜字一色",
        "国士无双十三面",
        "四暗刻单骑救主",
        "四暗刻",
        "十三幺",
        "大三元",
        "字一色",
        "小四喜",
        "混幺九",
        "三杠"
    };
    private static readonly HashSet<int> Green = new HashSet<int> {32, 33, 34, 36, 38, 47};
    private static readonly string[] WaitNames = {null, null, null, "听三张", "听四张", "听五张", "听六张", "听七张", "听八张"};
    private static readonly Dictionary<string, NewChineseHand> Cache = new Dictionary<string, NewChineseHand>();
    private readonly int[] before, physical = new int[48], hidden = new int[48];
    private readonly List<Group> exposed = new List<Group>();
    internal readonly HashSet<int> Basic = new HashSet<int>(), Legal = new HashSet<int>();
    internal bool Nine { get; private set; }
    private readonly Dictionary<int, List<Shape>> shapes = new Dictionary<int, List<Shape>>();
    private readonly Dictionary<(int, bool), List<(Shape shape, Dictionary<string, int> fans)>> choices =
        new Dictionary<(int, bool), List<(Shape, Dictionary<string, int>)>>();

    internal sealed class Result {
        public int Points, Fan, Represented, Base;
        public Dictionary<string, int> Fans;
    }

    private sealed class Group {
        internal char Kind;
        internal int Face;
        internal bool Open;
        internal bool Chow => Kind == 's';
        internal bool Kong => Kind == 'g' || Kind == 'G';
        internal int[] Tiles => Chow ? new[]{Face - 1, Face, Face + 1} : Enumerable.Repeat(Face, Kong ? 4 : 3).ToArray();
        internal bool Has(int t) => Chow ? Math.Abs(t - Face) <= 1 : Face == t;
    }

    private sealed class Shape {
        internal string Kind;
        internal int Pair, Win = -1;
        internal Group[] Groups = Array.Empty<Group>();
    }

    private static bool Terminal(int t) => t >= 41 || t % 10 == 1 || t % 10 == 9;
    internal static NewChineseHand Read(IEnumerable<int> hand, IEnumerable<string> codes) {
        int[] before = (hand ?? Array.Empty<int>()).OrderBy(t => t).ToArray();
        string[] melds = (codes ?? Array.Empty<string>()).ToArray();
        string key = string.Join(",", before) + "|" + string.Join(",", melds);
        lock (Cache) {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var result = new NewChineseHand(before, melds);
            if (Cache.Count >= 512) Cache.Clear();
            return Cache[key] = result;
        }
    }

    private NewChineseHand(int[] hand, string[] codes) {
        before = hand;
        if (codes.Length > 4 || hand.Length + codes.Length * 3 != 13 || hand.Any(t => !Faces.Contains(t))) return;
        foreach (int t in hand) { hidden[t]++; physical[t]++; }
        foreach (string code in codes) {
            if (string.IsNullOrEmpty(code) || code.Length != 3 || !"skgG".Contains(code[0]) ||
                !int.TryParse(code.Substring(1), out int t) || !Faces.Contains(t) ||
                code[0] == 's' && (t >= 40 || t % 10 < 2 || t % 10 > 8)) return;
            var group = new Group { Kind = code[0], Face = t, Open = true };
            exposed.Add(group);
            foreach (int face in group.Tiles) physical[face]++;
        }
        if (physical.Any(n => n > 4)) return;
        foreach (int win in Faces) {
            hidden[win]++;
            var complete = new List<Shape>();
            if (codes.Length == 0) {
                if (Faces.Where(Terminal).All(t => hidden[t] > 0) && Faces.Where(t => !Terminal(t)).All(t => hidden[t] == 0))
                    complete.Add(new Shape { Kind = "orphans" });
                if (hidden.All(n => (n & 1) == 0)) complete.Add(new Shape { Kind = "seven_pairs" });
            }
            foreach (int pair in Faces) {
                if (hidden[pair] < 2) continue;
                hidden[pair] -= 2;
                Split(hidden, new List<Group>(exposed), pair, win, complete);
                hidden[pair] += 2;
            }
            hidden[win]--;
            if (complete.Count > 0) {
                shapes[win] = complete;
                if (physical[win] < 4) Basic.Add(win);
            }
        }
        Nine = Enumerable.Range(1, 3).Any(s => Enumerable.Range(1, 9).All(r => shapes.ContainsKey(10 * s + r)));
        bool hasTerminal = Faces.Any(t => physical[t] > 0 && Terminal(t));
        foreach (int tile in Basic) foreach (bool self in new[]{false, true}) {
            var list = new List<(Shape, Dictionary<string, int>)>();
            foreach (var shape in shapes[tile]) {
                var fans = FansFor(shape, tile, self);
                if ((hasTerminal || Nine) && (shape.Kind != "standard" || shape.Groups.Any(g => !g.Chow)) || fans.Keys.Any(Limits.Contains))
                    list.Add((shape, fans));
            }
            if (list.Count > 0) { choices[(tile, self)] = list; Legal.Add(tile); }
        }
    }

    private static void Split(int[] counts, List<Group> groups, int pair, int win, List<Shape> result) {
        int t = 0;
        for (int i = 11; i < counts.Length; i++) if (counts[i] > 0) { t = i; break; }
        if (t == 0) {
            if (groups.Count != 4) return;
            var sets = groups.ToArray();
            if (pair == win) result.Add(new Shape {Kind="standard", Pair=pair, Groups=sets});
            for (int i = 0; i < sets.Length; i++) if (!sets[i].Open && sets[i].Has(win))
                result.Add(new Shape {Kind="standard", Pair=pair, Groups=sets, Win=i});
            return;
        }
        if (counts[t] >= 3) {
            counts[t] -= 3; groups.Add(new Group {Kind='k', Face=t});
            Split(counts, groups, pair, win, result);
            groups.RemoveAt(groups.Count - 1); counts[t] += 3;
        }
        if (t < 40 && t % 10 <= 7 && counts[t+1] > 0 && counts[t+2] > 0) {
            counts[t]--; counts[t+1]--; counts[t+2]--; groups.Add(new Group {Kind='s', Face=t+1});
            Split(counts, groups, pair, win, result);
            groups.RemoveAt(groups.Count - 1); counts[t]++; counts[t+1]++; counts[t+2]++;
        }
    }

    private Dictionary<string, int> FansFor(Shape shape, int tile, bool self) {
        var flags = new Dictionary<string, int>();
        void Add(string name, bool when) { if (when) flags[name] = Values[name]; }
        var faces = Faces.Where(t => physical[t] > 0 || t == tile).ToArray();
        var suits = faces.Where(t => t < 40).Select(t => t / 10).Distinct().ToArray();
        bool honors = faces.Any(t => t >= 41), standard = shape.Kind == "standard", seven = shape.Kind == "seven_pairs";
        var groups = shape.Groups;
        var pungs = new HashSet<int>(groups.Where(g => !g.Chow).Select(g => g.Face));
        var seq = groups.Where(g => g.Chow).Select(g => g.Face).ToArray();
        var darkSeq = groups.Where((g, i) => g.Chow && !g.Open && (self || i != shape.Win)).Select(g => g.Face).ToArray();
        int dark = groups.Where((g, i) => !g.Chow && (!g.Open || g.Kind == 'G') && (self || i != shape.Win)).Count();
        var kongs = new HashSet<int>(exposed.Where(g => g.Kong).Select(g => g.Face));
        bool closed = exposed.All(g => g.Kind == 'G'), allPungs = standard && seq.Length == 0;
        int quads = faces.Count(t => physical[t] + (t == tile ? 1 : 0) == 4 && !kongs.Contains(t));
        int winds = pungs.Count(t => t >= 41 && t <= 44), dragons = pungs.Count(t => t >= 45);
        if (kongs.Count == 4) Add(Enumerable.Range(41, 4).All(kongs.Contains) ? "大四喜之十八罗汉" : "十八罗汉", true);
        Add("三杠", kongs.Count == 3); Add("双杠", kongs.Count == 2); Add("杠", kongs.Count == 1);
        Add("四暗刻单骑救主", dark == 4 && shape.Win < 0); Add("四暗刻", dark == 4 && shape.Win >= 0);
        Add("三暗刻", dark == 3); Add("双暗刻", dark == 2);
        Add("清幺九", faces.All(Terminal) && !honors); Add("混幺九", faces.All(Terminal) && honors && suits.Length > 0);
        Add("字一色", suits.Length == 0); Add("清一色", suits.Length == 1 && !honors);
        Add("混一色", suits.Length == 1 && honors);
        Add("五门齐", suits.Length == 3 && faces.Any(t => t >= 41 && t <= 44) && faces.Any(t => t >= 45));
        Add("绿一色", faces.All(Green.Contains));
        Add("大四喜", winds == 4); Add("大四喜字一色", winds == 4 && shape.Pair >= 41);
        Add("大四喜混幺九", winds == 4 && shape.Pair < 40 && Terminal(shape.Pair));
        Add("小四喜", winds == 3 && shape.Pair >= 41 && shape.Pair <= 44 || seven && Enumerable.Range(41, 4).All(faces.Contains));
        Add("三风刻", winds >= 3); Add("双风刻", winds >= 2); Add("大三元", dragons == 3);
        Add("小三元", dragons == 2 && shape.Pair >= 45 || seven && Enumerable.Range(45, 3).All(faces.Contains));
        Add("双箭刻", dragons >= 2); Add("全带幺", standard && Terminal(shape.Pair) && groups.All(g => g.Tiles.Any(Terminal)));
        Add("一色三同顺", seq.GroupBy(t => t).Any(g => g.Count() >= 3)); Add("一色四同顺", seq.Length == 4 && seq.Distinct().Count() == 1);
        Add("三同刻", Enumerable.Range(1, 9).Any(r => new[]{1,2,3}.All(s => pungs.Contains(s*10+r))));
        Add("三色三同顺", Enumerable.Range(2, 7).Any(r => new[]{1,2,3}.All(s => seq.Contains(s*10+r))));
        bool gates = exposed.Count == 0 && suits.Length == 1 && !honors && Enumerable.Range(1, 9).All(r => {
            int face = 10*suits[0]+r;
            return hidden[face]+(face == tile ? 1 : 0) >= (r == 1 || r == 9 ? 3 : 1);
        });
        Add("门清", closed && standard && !gates && !Nine && dark != 4);
        if (gates) {
            bool exact = Enumerable.Range(1, 9).All(r => hidden[10*suits[0]+r] == (r == 1 || r == 9 ? 3 : 1));
            Add(exact ? "九宝莲灯之天衣无缝" : "九宝莲灯之准灯高挂", true);
        }
        foreach (int s in suits) {
            var line = new[]{s*10+2,s*10+5,s*10+8};
            if (line.All(seq.Contains) || gates && standard && (tile%10 == 1 || tile%10 == 9 || tile%10 == 4 && hidden[tile] == 0)) {
                Add("一条龙", true); Add("暗中一条龙", line.All(darkSeq.Contains) || gates && self);
            }
        }
        if (shape.Kind == "orphans") Add(before.Distinct().Count()==13 ? "国士无双十三面" : "十三幺", true);
        Add("四归一", !seven && quads == 1); Add("八归", !seven && quads == 2); Add("十二归", !seven && quads == 3);
        if (seven) {
            Add("七福星", faces.Length == 7 && suits.Length == 0);
            int run = 0;
            foreach (int s in suits) {
                int current = 0;
                for (int r = 1; r <= 9; r++) {
                    current = faces.Contains(10*s+r) ? current+1 : 0;
                    run = Math.Max(current, run);
                }
            }
            Add("连四对", run == 4); Add("连五对", run == 5); Add("连六对", run == 6); Add("连七对", run == 7);
            Add("龙七对", quads == 1); Add("双龙七对", quads == 2); Add("三龙七对", quads == 3);
            Add("双龙七对单骑救主", quads == 2 && hidden[tile] == 1);
            Add("三龙七对单骑救主", quads == 3 && hidden[tile] == 1);
        }
        if (flags.ContainsKey("五门齐") && flags.ContainsKey("全带幺") && flags.ContainsKey("三色三同顺")) {
            flags.Remove("五门齐");
            flags.Remove("全带幺");
            flags.Remove("三色三同顺");
            flags.Add("精五门", Values["精五门"]);
        }
        if (flags.ContainsKey("大四喜混幺九")) { flags.Remove("大四喜"); flags.Remove("混幺九"); }
        if (flags.ContainsKey("大四喜字一色")) { flags.Remove("大四喜"); flags.Remove("字一色"); }
        if (flags.ContainsKey("绿一色")) flags.Remove("清一色");
        if (flags.ContainsKey("七福星")) flags.Remove("字一色");
        if (flags.ContainsKey("大三元")) {
            if (flags.ContainsKey("字一色")) flags["大三元"] = 230 - Values["字一色"];
            else if (flags.ContainsKey("混幺九")) flags["大三元"] = 200 - Values["混幺九"];
        }
        if (flags.ContainsKey("清一色") && (closed || allPungs || seven))
            flags["清一色"] = allPungs && !faces.Any(Terminal) ? 50 : 80;
        return flags;
    }

    private int BaseFor(Shape shape, int tile, string source) {
        if (shape.Kind == "seven_pairs") return 3;
        if (shape.Kind != "standard") return 0;
        if (shape.Groups.All(g => !g.Chow)) return 2;
        if (shape.Win >= 0) return shape.Groups[shape.Win].Chow && shape.Groups[shape.Win].Face == tile ? 1 : 0;
        return before.Length == 1 && exposed.All(g => g.Kind != 'G') || source == "bao_seen" && tile >= 41 ? 1 : 0;
    }

    private static readonly Dictionary<string,int[,]> Base = new Dictionary<string,int[,]> {
        {"discard", new[,]{{2,1},{4,2},{20,10},{20,10}}}, {"self_draw", new[,]{{4,2},{8,4},{32,16},{32,16}}},
        {"black", new[,]{{9,6},{18,12},{60,40},{60,40}}}, {"bao_draw", new[,]{{8,4},{16,8},{40,20},{40,20}}},
        {"bao_seen", new[,]{{8,4},{24,16},{48,32},{56,32}}}
    };

    internal Result Score(int winning, string source, bool dealer = false, bool payerDealer = false,
                          int chain = 0, bool afterKong = false, int visible = 0,
                          int minimumFan = 0, bool exchangeThree = false) {
        bool self = source != "black" && source != "discard";
        if (exchangeThree) minimumFan = ExchangeMinimumFan;
        Result best = null;
        foreach (int tile in Legal.OrderBy(t => t)) {
            if (source != "bao_draw" && tile != winning || !choices.TryGetValue((tile,self), out var list)) continue;
            foreach (var choice in list) {
                var fans = new Dictionary<string,int>(choice.fans);
                void Add(string name) { fans[name] = Values[name]; }
                if (!fans.ContainsKey("九宝莲灯之准灯高挂")) {
                    if (Nine) Add("准听九");
                    else if (Legal.Count >= 3 && Legal.Count <= 8) Add(WaitNames[Legal.Count]);
                }
                if (visible >= 3 || tile == winning && visible + hidden[tile] == 3 &&
                    choice.shape.Kind != "orphans" && choice.shape.Win < 0) Add("绝张");
                if (afterKong && (source == "self_draw" || source == "bao_draw")) Add("杠上开花");
                if (chain >= 4) Add("连四杠");
                else if (chain == 3) Add("三连杠");
                else if (chain == 2) Add("杠上杠");
                int total=fans.Values.Sum(), grade=BaseFor(choice.shape,tile,source);
                if (total < minimumFan && !(exchangeThree && (grade == 2 || grade == 3))) continue;
                int high=Base[source][grade,0], low=Base[source][grade,1];
                int points = source == "black" ? (dealer || payerDealer ? high : low) + total*(dealer ? 3 : 2)
                    : (dealer ? 3*high : high+2*low) + 3*total;
                if (best == null || points > best.Points || points == best.Points && total > best.Fan)
                    best = new Result { Points=points, Fan=total, Represented=tile, Base=grade, Fans=fans };
            }
        }
        return best;
    }
}
