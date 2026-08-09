using System;
using System.Collections.Generic;
using System.Linq;

// 蓝十改只描述与标准国标的差异。拆牌、和牌张明暗转换及基础番种识别
// 全部由 Chinese_Hepai_Check 负责，避免两套国标逻辑长期漂移。
public static class GBhepaiLanshi
{
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> tilesCombination,
        List<string> wayToHepai,
        int getTile,
        bool debug = false)
    {
        return new Lanshi_Hepai_Check(debug).HepaiCheck(
            handList, tilesCombination, wayToHepai, getTile);
    }
}

public sealed class Lanshi_Hepai_Check : Chinese_Hepai_Check
{
    private static readonly Dictionary<string, int> CountModelDict = new Dictionary<string, int>
    {
        { "qixingdui", 100 }, { "sitongshun", 100 }, { "jiulianbaodeng", 100 }, { "sigang", 100 },
        { "dasixi", 72 }, { "qingyaojiu", 72 },
        { "sianke", 48 }, { "shisanyao", 48 },
        { "ziyise", 40 }, { "silianshun", 40 }, { "silianke", 40 },
        { "xiaosixi", 32 }, { "sangang", 32 },
        { "dasanyuan", 24 }, { "shunwang", 24 }, { "santongshun", 24 }, { "shunlian", 24 },
        { "hunyaojiu", 16 }, { "quanda", 16 }, { "quanzhong", 16 }, { "quanxiao", 16 },
        { "quandaiwu", 16 }, { "santongke", 16 }, { "xiaosanyuan", 16 }, { "quanbukao", 16 },
        { "sanfengke", 12 }, { "sananke", 12 }, { "sanlianke", 12 }, { "qingyise", 12 },
        { "sanlianshun", 12 },
        { "sanselianke", 8 }, { "qingquandaiyao", 8 }, { "shuanggang", 8 },
        { "dayuwu", 8 }, { "xiaoyuwu", 8 }, { "qiduizi", 8 }, { "shunhuan", 8 },
        { "shuangjianke", 6 }, { "qinglong", 6 },
        { "miaoshouhuichun", 5 }, { "haidilaoyue", 5 }, { "gangshangkaihua", 5 },
        { "qiangganghe", 5 }, { "tianhe", 5 }, { "dihe", 5 },
        { "hualong", 4 }, { "sansetongshun", 4 },
        { "pengpenghe", 3 }, { "hunquandaiyao", 3 }, { "hunyise", 3 }, { "sanselianshun", 3 },
        { "angang", 2 }, { "shuanganke", 2 }, { "wumenqi", 2 }, { "shuangtongke", 2 },
        { "quanqiuren", 2 }, { "siguiyi", 2 }, { "yibangao", 2 }, { "hejuezhang", 2 },
        { "jianke", 2 }, { "quanfengke", 2 }, { "menfengke", 2 },
        { "menqianqing", 1 }, { "minggang", 1 }, { "duanyao", 1 }, { "xixiangfeng", 1 },
        { "lianliu", 1 }, { "laoshaofu", 1 }, { "yaojiuke", 1 }, { "zimo", 1 }
    };

    private static readonly Dictionary<string, string> LanshiFanNames = new Dictionary<string, string>
    {
        { "qixingdui", "七星对" }, { "sitongshun", "四同顺" }, { "jiulianbaodeng", "九莲宝灯" },
        { "sigang", "四杠" }, { "dasixi", "大四喜" }, { "qingyaojiu", "清幺九" },
        { "sianke", "四暗刻" }, { "shisanyao", "十三幺" }, { "ziyise", "字一色" },
        { "silianshun", "四连顺" }, { "silianke", "四连刻" }, { "xiaosixi", "小四喜" },
        { "sangang", "三杠" }, { "dasanyuan", "大三元" }, { "shunwang", "顺网" },
        { "santongshun", "三同顺" }, { "shunlian", "顺链" }, { "hunyaojiu", "混幺九" },
        { "quanda", "全大" }, { "quanzhong", "全中" }, { "quanxiao", "全小" },
        { "quandaiwu", "全带五" }, { "santongke", "三同刻" }, { "xiaosanyuan", "小三元" },
        { "quanbukao", "全不靠" }, { "sanfengke", "三风刻" }, { "sananke", "三暗刻" },
        { "sanlianke", "三连刻" }, { "qingyise", "清一色" }, { "sanlianshun", "三连顺" },
        { "sanselianke", "三色连刻" }, { "qingquandaiyao", "清全带幺" },
        { "shuanggang", "双杠" }, { "dayuwu", "大于五" }, { "xiaoyuwu", "小于五" },
        { "qiduizi", "七对" }, { "shunhuan", "顺环" }, { "shuangjianke", "双箭刻" },
        { "qinglong", "清龙" }, { "miaoshouhuichun", "妙手回春" },
        { "haidilaoyue", "海底捞月" }, { "gangshangkaihua", "杠上开花" },
        { "qiangganghe", "抢杠和" }, { "tianhe", "天和" }, { "dihe", "地和" },
        { "hualong", "花龙" }, { "sansetongshun", "三色同顺" }, { "pengpenghe", "碰碰和" },
        { "hunquandaiyao", "混全带幺" }, { "hunyise", "混一色" },
        { "sanselianshun", "三色连顺" }, { "angang", "暗杠" }, { "shuanganke", "双暗刻" },
        { "wumenqi", "五门齐" }, { "shuangtongke", "双同刻" }, { "quanqiuren", "全求人" },
        { "siguiyi", "四归一" }, { "yibangao", "一般高" }, { "hejuezhang", "和绝张" },
        { "jianke", "箭刻" }, { "quanfengke", "圈风刻" }, { "menfengke", "门风刻" },
        { "menqianqing", "门前清" }, { "minggang", "明杠" }, { "duanyao", "断幺" },
        { "xixiangfeng", "喜相逢" }, { "lianliu", "连六" }, { "laoshaofu", "老少副" },
        { "yaojiuke", "幺九刻" }, { "zimo", "自摸" }
    };

    private static readonly List<string> TableOrder = CountModelDict.Keys.ToList();
    private static readonly HashSet<string> Repeatable = new HashSet<string>
    {
        "siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "laoshaofu", "yaojiuke"
    };
    private static readonly List<string> Occasional = new List<string>
    {
        "miaoshouhuichun", "haidilaoyue", "gangshangkaihua", "qiangganghe", "tianhe", "dihe"
    };
    private static readonly HashSet<string> DirectNames = new HashSet<string>
    {
        "dasixi", "dasanyuan", "jiulianbaodeng", "sigang", "shisanyao", "qingyaojiu",
        "xiaosixi", "xiaosanyuan", "ziyise", "sianke", "sangang", "hunyaojiu", "qiduizi",
        "qingyise", "quanda", "quanzhong", "quanxiao", "quandaiwu", "santongke", "sananke",
        "quanbukao", "dayuwu", "xiaoyuwu", "sanfengke", "miaoshouhuichun", "haidilaoyue",
        "gangshangkaihua", "qiangganghe", "pengpenghe", "hunyise", "wumenqi", "quanqiuren",
        "hejuezhang", "jianke", "quanfengke", "menfengke", "menqianqing", "siguiyi",
        "shuangtongke", "shuanganke", "angang", "duanyao", "yaojiuke", "minggang", "zimo",
        "qixingdui"
    };
    private static readonly HashSet<int> HonorTiles = new HashSet<int> { 41, 42, 43, 44, 45, 46, 47 };
    private static readonly List<HashSet<int>> UnrelatedCases = new List<HashSet<int>>
    {
        new HashSet<int> { 11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47 }
    };

    private sealed class Pattern
    {
        public string Name;
        public HashSet<int> Used;

        public Pattern(string name, IEnumerable<int> used)
        {
            Name = name;
            Used = new HashSet<int>(used);
        }
    }

    private sealed class Edge
    {
        public int Left;
        public int Right;
        public string Name;

        public Edge(int left, int right, string name)
        {
            Left = left;
            Right = right;
            Name = name;
        }
    }

    public Lanshi_Hepai_Check(bool debug = false) : base(debug, CountModelDict) { }

    protected override bool QD_check(PlayerTiles playerTiles, List<PlayerTiles> playerTilesList)
    {
        var counts = playerTiles.hand_tiles.GroupBy(tile => tile).ToDictionary(group => group.Key, group => group.Count());
        if (counts.Count != 7 || counts.Values.Any(count => count != 2))
            return false;

        var candidate = playerTiles.DeepCopy();
        candidate.complete_step = 14;
        candidate.fan_list.Add(new HashSet<int>(counts.Keys).SetEquals(HonorTiles) ? "qixingdui" : "qiduizi");
        playerTilesList.Add(candidate);
        return false;
    }

    protected override bool QBK_check(PlayerTiles playerTiles, List<PlayerTiles> playerTilesList)
    {
        var hand = new HashSet<int>(playerTiles.hand_tiles);
        if (playerTiles.hand_tiles.Count != 14 || hand.Count != 14 || !UnrelatedCases.Any(rule => hand.IsSubsetOf(rule)))
            return false;

        var candidate = playerTiles.DeepCopy();
        candidate.complete_step = 14;
        candidate.fan_list.Add("quanbukao");
        playerTilesList.Add(candidate);
        return false;
    }

    private static Tuple<int, int> Sequence(string token)
    {
        int tile = int.Parse(token.Substring(1));
        return Tuple.Create(tile / 10, tile % 10 - 1);
    }

    private static Tuple<int, int> Triplet(string token)
    {
        int tile = int.Parse(token.Substring(1));
        return Tuple.Create(tile / 10, tile % 10);
    }

    private static bool Same(Tuple<int, int> left, Tuple<int, int> right)
    {
        return left.Item1 == right.Item1 && left.Item2 == right.Item2;
    }

    private static string LowRelation(Tuple<int, int> left, Tuple<int, int> right)
    {
        if (Same(left, right))
            return "yibangao";
        if (left.Item2 == right.Item2 && left.Item1 != right.Item1)
            return "xixiangfeng";
        if (left.Item1 == right.Item1 && Math.Abs(left.Item2 - right.Item2) == 3)
            return "lianliu";
        if (left.Item1 == right.Item1 && new HashSet<int> { left.Item2, right.Item2 }.SetEquals(new[] { 1, 7 }))
            return "laoshaofu";
        return null;
    }

    private static int FindSequence(List<Tuple<int, int>> sequences, int suit, int rank)
    {
        return sequences.FindIndex(sequence => sequence.Item1 == suit && sequence.Item2 == rank);
    }

    private static List<string> BestLowSequenceFans(List<Tuple<int, int>> sequences, HashSet<int> occupied)
    {
        var edges = new List<Edge>();
        for (int left = 0; left < sequences.Count; left++)
        {
            for (int right = left + 1; right < sequences.Count; right++)
            {
                if (occupied.Contains(left) && occupied.Contains(right))
                    continue;
                string relation = LowRelation(sequences[left], sequences[right]);
                if (relation != null)
                    edges.Add(new Edge(left, right, relation));
            }
        }
        edges = edges.OrderByDescending(edge => CountModelDict[edge.Name])
            .ThenBy(edge => TableOrder.IndexOf(edge.Name)).ToList();

        int bestScore = -1;
        var best = new List<string>();
        int combinations = 1 << edges.Count;
        for (int mask = 0; mask < combinations; mask++)
        {
            var parent = Enumerable.Range(0, sequences.Count).ToArray();
            Func<int, int> root = null;
            root = node => parent[node] == node ? node : (parent[node] = root(parent[node]));
            int score = 0;
            bool valid = true;
            var names = new List<string>();
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                if ((mask & (1 << edgeIndex)) == 0)
                    continue;
                Edge edge = edges[edgeIndex];
                int leftRoot = root(edge.Left);
                int rightRoot = root(edge.Right);
                if (leftRoot == rightRoot)
                {
                    valid = false;
                    break;
                }
                parent[leftRoot] = rightRoot;
                names.Add(edge.Name);
                score += CountModelDict[edge.Name];
            }
            if (valid && score > bestScore)
            {
                bestScore = score;
                best = names;
            }
        }
        return best;
    }

    private static List<string> SequenceFans(List<string> tokens)
    {
        var sequences = tokens.Where(token => token.Length > 0 && (token[0] == 's' || token[0] == 'S'))
            .Select(Sequence).ToList();
        if (sequences.Count == 0)
            return new List<string>();

        var candidates = new List<Pattern>();
        Action<string, IEnumerable<int>> add = (name, used) => candidates.Add(new Pattern(name, used));

        foreach (var group in sequences.Select((value, index) => new { value, index })
                     .GroupBy(item => item.value.Item1 * 10 + item.value.Item2))
        {
            var positions = group.Select(item => item.index).ToList();
            if (positions.Count >= 4)
                add("sitongshun", positions.Take(4));
            else if (positions.Count >= 3)
                add("santongshun", positions.Take(3));
        }

        for (int suit = 1; suit <= 3; suit++)
        {
            for (int start = 1; start <= 4; start++)
            {
                var used = Enumerable.Range(0, 4).Select(step => FindSequence(sequences, suit, start + step)).ToList();
                if (used.All(index => index >= 0)) add("silianshun", used);
            }
            for (int start = 1; start <= 5; start++)
            {
                var used = Enumerable.Range(0, 3).Select(step => FindSequence(sequences, suit, start + step)).ToList();
                if (used.All(index => index >= 0)) add("sanlianshun", used);
            }
            var chain = new[] { 1, 3, 5, 7 }.Select(rank => FindSequence(sequences, suit, rank)).ToList();
            if (chain.All(index => index >= 0)) add("shunlian", chain);
            var dragon = new[] { 1, 4, 7 }.Select(rank => FindSequence(sequences, suit, rank)).ToList();
            if (dragon.All(index => index >= 0)) add("qinglong", dragon);
        }

        if (sequences.Count == 4)
        {
            var relationPairs = new List<Tuple<int, int, string, string>>();
            for (int left = 0; left < 4; left++)
            {
                for (int right = left + 1; right < 4; right++)
                {
                    string relation = LowRelation(sequences[left], sequences[right]);
                    if (relation == "xixiangfeng" || relation == "lianliu" || relation == "laoshaofu")
                    {
                        var values = new[]
                        {
                            sequences[left].Item1 * 10 + sequences[left].Item2,
                            sequences[right].Item1 * 10 + sequences[right].Item2
                        };
                        Array.Sort(values);
                        relationPairs.Add(Tuple.Create(left, right, relation, values[0] + ":" + values[1]));
                    }
                }
            }
            bool shunwang = relationPairs.Any(first => relationPairs.Any(second =>
                !ReferenceEquals(first, second) && first.Item3 == second.Item3 && first.Item4 == second.Item4 &&
                first.Item1 != second.Item1 && first.Item1 != second.Item2 &&
                first.Item2 != second.Item1 && first.Item2 != second.Item2));
            if (shunwang) add("shunwang", Enumerable.Range(0, 4));

            var grouped = sequences.GroupBy(sequence => sequence.Item1)
                .Select(group => group.Select(sequence => sequence.Item2).OrderBy(rank => rank).ToList()).ToList();
            if (grouped.Count == 2 && grouped.All(group => group.Count == 2) && grouped[0].SequenceEqual(grouped[1]) &&
                (Math.Abs(grouped[0][0] - grouped[0][1]) == 3 || new HashSet<int>(grouped[0]).SetEquals(new[] { 1, 7 })))
                add("shunhuan", Enumerable.Range(0, 4));
        }

        for (int rank = 1; rank <= 7; rank++)
        {
            var same = Enumerable.Range(1, 3).Select(suit => FindSequence(sequences, suit, rank)).ToList();
            if (same.All(index => index >= 0)) add("sansetongshun", same);
        }
        foreach (var suitOrder in Permutations(new[] { 1, 2, 3 }))
        {
            var dragon = new[] { 1, 4, 7 }.Select((rank, index) => FindSequence(sequences, suitOrder[index], rank)).ToList();
            if (dragon.All(index => index >= 0)) add("hualong", dragon);
            for (int rank = 1; rank <= 5; rank++)
            {
                var used = Enumerable.Range(0, 3).Select(offset => FindSequence(sequences, suitOrder[offset], rank + offset)).ToList();
                if (used.All(index => index >= 0)) add("sanselianshun", used);
            }
        }

        if (candidates.Count == 0)
            return BestLowSequenceFans(sequences, new HashSet<int>());

        Pattern main = candidates.OrderByDescending(candidate => CountModelDict[candidate.Name])
            .ThenByDescending(candidate => candidate.Used.Count)
            .ThenBy(candidate => TableOrder.IndexOf(candidate.Name)).First();
        var result = new List<string> { main.Name };
        if (main.Used.Count == 3)
            result.AddRange(BestLowSequenceFans(sequences, main.Used).Take(1));
        return result;
    }

    private static IEnumerable<int[]> Permutations(int[] values)
    {
        foreach (int first in values)
            foreach (int second in values.Where(value => value != first))
                foreach (int third in values.Where(value => value != first && value != second))
                    yield return new[] { first, second, third };
    }

    private static List<string> ExtraTripletFans(List<string> tokens)
    {
        var trips = tokens.Where(token => token.Length > 0 && "kKgG".Contains(token[0]))
            .Select(Triplet).ToList();
        var fans = new List<string>();
        for (int suit = 1; suit <= 3; suit++)
        {
            var ranks = new HashSet<int>(trips.Where(trip => trip.Item1 == suit).Select(trip => trip.Item2));
            if (Enumerable.Range(1, 6).Any(start => Enumerable.Range(0, 4).All(step => ranks.Contains(start + step))))
                fans.Add("silianke");
            else if (Enumerable.Range(1, 7).Any(start => Enumerable.Range(0, 3).All(step => ranks.Contains(start + step))))
                fans.Add("sanlianke");
        }
        if (!fans.Any(name => name == "silianke" || name == "sanlianke"))
        {
            bool found = Permutations(new[] { 1, 2, 3 }).Any(order =>
                Enumerable.Range(1, 7).Any(rank => Enumerable.Range(0, 3)
                    .All(offset => trips.Any(trip => trip.Item1 == order[offset] && trip.Item2 == rank + offset))));
            if (found) fans.Add("sanselianke");
        }
        return fans;
    }

    private static List<string> NormaliseRawFans(PlayerTiles playerTiles, List<string> wayToHepai)
    {
        var raw = playerTiles.fan_list;
        var fans = raw.Where(DirectNames.Contains).ToList();
        if (raw.Contains("buqiuren"))
            fans.AddRange(new[] { "menqianqing", "zimo" });

        var tokens = playerTiles.combination_list.Where(token => token.Length > 0 && "sSkKgGq".Contains(token[0])).ToList();
        fans.AddRange(SequenceFans(tokens));
        fans.AddRange(ExtraTripletFans(tokens));

        if (raw.Contains("quandaiyao"))
            fans.Add(tokens.Any(token => int.Parse(token.Substring(1)) >= 40) ? "hunquandaiyao" : "qingquandaiyao");
        if (raw.Any(name => name == "shuangangang" || name == "shuangminggang" || name == "mingangang"))
            fans.Add("shuanggang");
        if (wayToHepai.Contains("天和")) fans.Add("tianhe");
        if (wayToHepai.Contains("地和")) fans.Add("dihe");
        return fans;
    }

    private static void RemoveOnce(List<string> fans, IEnumerable<string> names)
    {
        foreach (string name in names)
            fans.Remove(name);
    }

    private static List<string> ApplyExclusions(List<string> fans, List<string> wayToHepai)
    {
        string hundred = TableOrder.FirstOrDefault(name => fans.Contains(name) && CountModelDict[name] == 100);
        if (hundred != null)
            return new List<string> { hundred };

        var rules = new Dictionary<string, List<string>>
        {
            { "dasixi", new List<string> { "xiaosixi", "sanfengke", "pengpenghe", "quanfengke", "menfengke", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "qingyaojiu", new List<string> { "pengpenghe", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "sianke", new List<string> { "sananke", "shuanganke", "pengpenghe", "menqianqing" } },
            { "shisanyao", new List<string> { "hunyaojiu", "wumenqi", "menqianqing" } },
            { "ziyise", new List<string> { "pengpenghe", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "silianke", new List<string> { "sanlianke", "santongshun", "pengpenghe" } },
            { "xiaosixi", new List<string> { "sanfengke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "sangang", new List<string> { "shuanggang", "angang", "minggang" } },
            { "dasanyuan", new List<string> { "xiaosanyuan", "shuangjianke", "jianke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "shunwang", new List<string> { "qiduizi", "yibangao", "xixiangfeng", "lianliu", "laoshaofu" } },
            { "santongshun", new List<string> { "sanlianke", "yibangao" } },
            { "hunyaojiu", new List<string> { "qiduizi", "pengpenghe", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "quanda", new List<string> { "dayuwu" } }, { "quanzhong", new List<string> { "duanyao" } },
            { "quanxiao", new List<string> { "xiaoyuwu" } }, { "quandaiwu", new List<string> { "duanyao" } },
            { "santongke", new List<string> { "shuangtongke" } },
            { "xiaosanyuan", new List<string> { "shuangjianke", "jianke", "yaojiuke", "yaojiuke" } },
            { "quanbukao", new List<string> { "wumenqi", "menqianqing" } },
            { "sanfengke", new List<string> { "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "sananke", new List<string> { "shuanganke" } }, { "sanlianke", new List<string> { "santongshun" } },
            { "shuanggang", new List<string> { "angang", "minggang" } }, { "qiduizi", new List<string> { "menqianqing" } },
            { "shuangjianke", new List<string> { "jianke", "yaojiuke", "yaojiuke" } },
            { "jianke", new List<string> { "yaojiuke" } }
        };

        var result = new List<string>(fans);
        foreach (string fan in fans)
            if (rules.ContainsKey(fan)) RemoveOnce(result, rules[fan]);

        int windRemovals = (fans.Contains("quanfengke") ? 1 : 0) + (fans.Contains("menfengke") ? 1 : 0);
        if (windRemovals == 2 && wayToHepai.Contains("门风圈风相同")) windRemovals = 1;
        RemoveOnce(result, Enumerable.Repeat("yaojiuke", windRemovals));

        string occasional = Occasional.FirstOrDefault(result.Contains);
        if (occasional != null)
        {
            var regular = result.Where(name => !Occasional.Contains(name)).ToList();
            int regularScore = regular.Sum(name => CountModelDict[name]);
            return regularScore >= 5 ? regular : new List<string> { occasional };
        }
        return result;
    }

    private static Tuple<int, List<string>> Score(List<string> fans)
    {
        int score = 0;
        var output = new List<string>();
        foreach (string name in TableOrder)
        {
            int count = fans.Count(fan => fan == name);
            if (count == 0) continue;
            if (Repeatable.Contains(name))
            {
                score += count * CountModelDict[name];
                output.Add($"{LanshiFanNames[name]}*{count}");
            }
            else
            {
                score += CountModelDict[name];
                output.Add(LanshiFanNames[name]);
            }
        }
        return Tuple.Create(Math.Min(score, 100), output);
    }

    protected override Tuple<int, List<string>> FanCountOutput(
        PlayerTiles playerTiles,
        string combinationStr,
        bool zimoOrNot,
        List<string> wayToHepai)
    {
        return Score(ApplyExclusions(NormaliseRawFans(playerTiles, wayToHepai), wayToHepai));
    }
}
